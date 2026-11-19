using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Npgsql;
using Salon.Core.Appointments;
using Salon.Core.Appointments.Events;
using Salon.Core.Booking;
using Salon.Core.Events;
using Salon.Core.Import;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Catalog;
using Salon.Services.Settings;
using Salon.Services.Tenancy;

namespace Salon.Services.Appointments.Import;

/// <summary>Words the import writes on the visits it adds (ADR 0067).</summary>
public static class AppointmentImportDefaults
{
    /// <summary>The reason on the first line of an imported visit's history: where it came from.</summary>
    public const string Reason = "Imported";
}

internal sealed class AppointmentImportService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    ISettingService settings,
    IServiceOfferingResolver offerings,
    IEventPublisher publisher,
    IClock clock,
    ILogger<AppointmentImportService> logger) : IAppointmentImportService
{
    public async Task<LocalDate> FirstDayAsync(CancellationToken cancellationToken = default)
    {
        var (_, zone) = await SalonAsync(cancellationToken).ConfigureAwait(false);
        return clock.GetCurrentInstant().InZone(zone).Date;
    }

    public async Task<AppointmentImportPreview> PreviewAsync(AppointmentImportParseResult parsed, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (!parsed.IsSuccess)
        {
            throw new ArgumentException("A refused file has nothing to preview.", nameof(parsed));
        }

        var (tenant, zone) = await SalonAsync(cancellationToken).ConfigureAwait(false);
        var context = await ContextAsync(parsed.Rows, tenant, zone, cancellationToken).ConfigureAwait(false);
        var rows = AppointmentImportClassifier.Classify(parsed.Rows, context);
        var readAt = context.Now.InZone(zone);

        return new AppointmentImportPreview(
            tenant.Id,
            fileName.Trim(),
            tenant.Currency,
            readAt,
            await RemindersFromAsync(readAt, cancellationToken).ConfigureAwait(false),
            rows,
            parsed.PastCount,
            parsed.PastDays ?? [],
            Unrecognised(rows, AppointmentImportProblem.UnknownService, r => r.Service, context.Services.Where(s => s.InUse).Select(s => s.Name)),
            Unrecognised(rows, AppointmentImportProblem.UnknownStaff, r => r.Staff, context.Staff.Where(s => s.Bookable).Select(s => s.Name)),
            Statuses(rows));
    }

    public async Task<AppointmentImportResult> CommitAsync(AppointmentImportPreview preview, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);

        var (tenant, zone) = await SalonAsync(cancellationToken).ConfigureAwait(false);
        if (preview.TenantId != tenant.Id)
        {
            throw new TenantViolationException("The diary import preview belongs to another tenant.");
        }

        // The diary, the clients, the services and the people as they are now, through the tenant filter: no id of the
        // cached preview is trusted, and a visit booked since the preview is a clash now.
        var source = preview.Rows.Select(r => r.Row).ToList();
        var context = await ContextAsync(source, tenant, zone, cancellationToken).ConfigureAwait(false);
        var rows = AppointmentImportClassifier.Classify(source, context);
        var now = context.Now;

        // Every visit, its first history line and its event are added, then written in one save: the database's rule
        // against two visits of one person at one time refuses the whole file or none of it. One batch id marks them
        // all, so a file read in by mistake can be taken out again (ADR 0074).
        var batchId = Guid.CreateVersion7();
        var added = new List<Appointment>();
        foreach (var row in rows.Where(r => r.Status == AppointmentImportStatus.New))
        {
            var match = row.Match;
            var appointment = new Appointment
            {
                TenantId = tenant.Id,
                ClientId = match.ClientId!.Value,
                StaffMemberId = match.StaffId!.Value,
                ServiceId = match.ServiceId!.Value,
                Start = match.Start!.Value,
                End = match.End!.Value,
                Status = AppointmentStatus.Booked,
                Source = AppointmentSource.Import,
                ImportBatchId = batchId,
                PriceAmount = match.Price!.Value,

                // No deposit is asked of a visit the old system booked; money paid there is not recorded here.
                DepositAmount = 0m,
                Currency = tenant.Currency,
                DurationMinutes = match.Minutes!.Value,
                CreatedAt = now,
                CreatedByUserId = byUserId,

                // The client's own link, so the reminder lets them change or cancel the visit as any other (ADR 0029).
                PublicToken = PublicBookingToken.Generate(),
            };
            db.Appointments.Add(appointment);
            db.AppointmentStatusChanges.Add(new AppointmentStatusChange
            {
                TenantId = tenant.Id,
                AppointmentId = appointment.Id,
                From = null,
                To = AppointmentStatus.Booked,
                At = now,
                ByUserId = byUserId,
                Reason = AppointmentImportDefaults.Reason,
            });
            publisher.Queue(new AppointmentBooked(appointment.Id, tenant.Id, appointment.ClientId, appointment.StaffMemberId, appointment.ServiceId, appointment.Start, appointment.End, AppointmentSource.Import));
            added.Add(appointment);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation })
        {
            // Thrown on, so the command's transaction rolls back: nothing of the file is written.
            logger.LogInformation(ex, "Diary import for tenant {TenantId} refused by the database: a visit was booked into one of its times since the check", tenant.Id);
            throw new AppointmentImportClashException("A visit of the file clashes with one written since the file was checked; nothing was written.", ex);
        }

        var readAt = now.InZone(zone);
        var remindersFrom = await RemindersFromAsync(readAt, cancellationToken).ConfigureAwait(false);
        var result = new AppointmentImportResult(
            preview.FileName,
            added.Count,
            rows.Count(r => r.Status == AppointmentImportStatus.AlreadyThere),
            rows.Count(r => r.Status is AppointmentImportStatus.Cancelled or AppointmentImportStatus.CancelledSince),
            preview.PastCount + rows.Count(r => r.Status == AppointmentImportStatus.Past),
            remindersFrom,
            remindersFrom is { } from ? added.Count(a => a.Start < from.ToInstant()) : 0,
            [.. rows.Where(r => r.Status is AppointmentImportStatus.Invalid or AppointmentImportStatus.CancelledSince)]);

        // Counts and ids only: the file holds names, numbers and emails, and its name is typed text.
        logger.LogInformation(
            "Diary import for tenant {TenantId} by user {UserId}: {Added} visits added, {AlreadyThere} already there, {Cancelled} cancelled in the old system, {NotBrought} not brought over",
            tenant.Id, byUserId ?? Guid.Empty, result.Added, result.AlreadyThere, result.Cancelled, result.NotBrought.Count);
        return result;
    }

    /// <summary>Everything the rows are classified against, read through the tenant filter.</summary>
    public async Task<IReadOnlyList<DiaryImportBatch>> BatchesAsync(CancellationToken cancellationToken = default)
    {
        var batches = await db.Appointments.AsNoTracking()
            .Where(a => a.ImportBatchId != null)
            .GroupBy(a => a.ImportBatchId!.Value)
            .Select(g => new { Id = g.Key, ReadAt = g.Min(a => a.CreatedAt), Brought = g.Count() })
            .OrderByDescending(b => b.ReadAt)
            .Take(10)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = batches.Select(b => b.Id).ToArray();
        var untouched = await db.Appointments.AsNoTracking()
            .Where(a => a.ImportBatchId != null && ids.Contains(a.ImportBatchId.Value))
            .Where(Unworn())
            .GroupBy(a => a.ImportBatchId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken).ConfigureAwait(false);
        return [.. batches.Select(b => new DiaryImportBatch(b.Id, b.ReadAt, b.Brought, untouched.GetValueOrDefault(b.Id)))];
    }

    public async Task<DiaryImportUndo?> UndoAsync(Guid batchId, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        var brought = await db.Appointments.CountAsync(a => a.ImportBatchId == batchId, cancellationToken).ConfigureAwait(false);
        if (brought == 0)
        {
            return null;
        }

        // One statement that checks again as it deletes: a visit is removed only while it is still exactly as the
        // file left it - booked, never moved (a move writes a second history line), no money, no products. History
        // and reminder rows go with it (cascade); a payment racing this very statement makes the database refuse and
        // the command roll back, so nothing is half done.
        var removed = await db.Appointments
            .Where(a => a.ImportBatchId == batchId)
            .Where(Unworn())
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        // Counts and ids only (the log rule): never a client's name or number.
        logger.LogInformation(
            "Diary import {BatchId} undone by user {UserId}: {Removed} visits removed, {Kept} kept",
            batchId, byUserId ?? Guid.Empty, removed, brought - removed);
        return new DiaryImportUndo(removed, brought - removed);
    }

    /// <summary>
    /// Still exactly as the file left it: booked, never moved or marked (a move writes a second history line), no
    /// money, no products. Only such a visit may an undo remove.
    /// </summary>
    private System.Linq.Expressions.Expression<Func<Appointment, bool>> Unworn()
        => a => a.Status == AppointmentStatus.Booked
            && !db.ClientAccountEntries.Any(e => e.AppointmentId == a.Id)
            && !db.VisitProductLines.Any(l => l.AppointmentId == a.Id)
            && db.AppointmentStatusChanges.Count(c => c.AppointmentId == a.Id) == 1;

    private async Task<AppointmentImportContext> ContextAsync(IReadOnlyList<ImportedAppointmentRow> rows, TenantInfo tenant, DateTimeZone zone, CancellationToken cancellationToken)
    {
        var now = clock.GetCurrentInstant();
        var firstDay = now.InZone(zone).Date;
        var fromStartOfToday = zone.AtStartOfDay(firstDay).ToInstant();

        var clients = await db.Clients.AsNoTracking()
            .Where(c => c.IsActive && c.MergedIntoClientId == null && c.ErasedAt == null)
            .Select(c => new ImportClient(c.Id, c.FirstName, c.LastName, c.Phone, c.Email))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var services = await db.Services.AsNoTracking()
            .Select(s => new ImportService(s.Id, s.Name, s.IsActive))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var staff = await db.StaffMembers.AsNoTracking()
            .OrderBy(s => s.SortOrder).ThenBy(s => s.DisplayName).ThenBy(s => s.Id)
            .Select(s => new ImportStaff(s.Id, s.DisplayName, s.IsActive && s.IsBookable))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // What each person takes and charges, for the services the file names only.
        var named = rows.Select(r => ImportNames.Key(r.Service)).Where(k => k.Length > 0).ToHashSet(StringComparer.Ordinal);
        var staffIds = staff.Where(s => s.Bookable).Select(s => s.Id).ToArray();
        var priced = new Dictionary<(Guid ServiceId, Guid StaffId), ImportOffering>();
        foreach (var service in services.Where(s => s.InUse && named.Contains(ImportNames.Key(s.Name))))
        {
            var offered = await offerings.ResolveAsync(service.Id, staffIds, cancellationToken).ConfigureAwait(false);
            foreach (var (staffId, offering) in offered)
            {
                priced[(service.Id, staffId)] = new ImportOffering(offering.Offers, (int)offering.Duration.TotalMinutes, offering.Price);
            }
        }

        var active = AppointmentStateMachine.Active.ToArray();
        var diary = await db.Appointments.AsNoTracking()
            .Where(a => active.Contains(a.Status) && a.End > fromStartOfToday)
            .Select(a => new DiaryVisit(a.Id, a.StaffMemberId, a.ClientId, a.Start, a.End))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var imported = (await db.Appointments.AsNoTracking()
                .Where(a => a.Source == AppointmentSource.Import && a.Start >= fromStartOfToday)
                .Select(a => new { a.Id, a.ClientId, a.ServiceId, a.Start, a.Status })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(a => new ImportedVisit(a.Id, a.ClientId, a.ServiceId, a.Start.InZone(zone).Date, AppointmentStateMachine.IsActive(a.Status)))
            .ToList();
        var held = await db.WaitlistOffers.AsNoTracking()
            .Where(o => o.EndedAt == null && o.HeldUntil > now)
            .Select(o => new HeldSlot(o.StaffMemberId, o.Start, o.End))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new AppointmentImportContext(zone, now, tenant.Currency, clients, services, staff, priced, diary, imported, held);
    }

    /// <summary>
    /// Until when the old system must keep reminding: a reminder of ours goes out only when its time is after the
    /// import, so a visit starting within the longest lead may miss it. Null when the salon sends no reminders.
    /// </summary>
    private async Task<ZonedDateTime?> RemindersFromAsync(ZonedDateTime readAt, CancellationToken cancellationToken)
    {
        var notifications = await settings.LoadAsync<NotificationSettings>(cancellationToken).ConfigureAwait(false);
        var lead = Math.Max(notifications.FirstReminderHours, notifications.SecondReminderHours);
        return notifications.RemindersEnabled && lead > 0 ? readAt.Plus(Duration.FromHours(lead)) : null;
    }

    /// <summary>
    /// The distinct values of a column the salon does not have, most rows first, each with the nearest name it has:
    /// one find-and-replace in the spreadsheet fixes every row that carries it.
    /// </summary>
    private static IReadOnlyList<UnrecognisedValue> Unrecognised(
        IReadOnlyList<ClassifiedAppointmentRow> rows,
        AppointmentImportProblem problem,
        Func<ImportedAppointmentRow, string?> value,
        IEnumerable<string> names)
    {
        var known = names.ToList();
        return [.. rows
            .Where(r => r.Problems.Contains(problem) && !string.IsNullOrWhiteSpace(value(r.Row)))
            .GroupBy(r => ImportNames.Key(value(r.Row)), StringComparer.Ordinal)
            .Select(g => new UnrecognisedValue(value(g.First().Row)!.Trim(), g.Count(), Suggest(value(g.First().Row), known)))
            .OrderByDescending(v => v.Rows)
            .ThenBy(v => v.Value, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>A name the salon has that the value is close to: the same letters, or one inside the other; null when none or several.</summary>
    private static string? Suggest(string? value, IReadOnlyList<string> names)
    {
        var loose = ImportNames.Loose(value);
        if (loose.Length == 0)
        {
            return null;
        }

        var same = names.Where(n => ImportNames.Loose(n) == loose).ToList();
        if (same.Count == 1)
        {
            return same[0];
        }

        var inside = names.Where(n => ImportNames.Loose(n) is { Length: > 2 } other && (other.Contains(loose, StringComparison.Ordinal) || loose.Contains(other, StringComparison.Ordinal))).ToList();
        return inside.Count == 1 ? inside[0] : null;
    }

    /// <summary>Every status word of the file with how it was read, most rows first.</summary>
    private static IReadOnlyList<StatusWord> Statuses(IReadOnlyList<ClassifiedAppointmentRow> rows)
        => [.. rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Row.Status))
            .GroupBy(r => r.Row.Status!.Trim().ToLowerInvariant(), StringComparer.Ordinal)
            .Select(g => new StatusWord(g.First().Row.Status!.Trim(), AppointmentImportStatuses.Read(g.Key), g.Count()))
            .OrderByDescending(s => s.Rows)
            .ThenBy(s => s.Word, StringComparer.CurrentCultureIgnoreCase)];

    private async Task<(TenantInfo Tenant, DateTimeZone Zone)> SalonAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("A diary import belongs to a salon, but no tenant is set in the current scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");
        return (tenant, DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc);
    }
}