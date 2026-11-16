using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Npgsql;
using Salon.Core.Appointments;
using Salon.Core.Appointments.Events;
using Salon.Core.Events;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Availability;
using Salon.Services.Tenancy;

namespace Salon.Services.Appointments;

/// <summary>
/// Reads and writes through the tenant filter. "Today" is the salon's own day. Health notes are never read (ADR 0011);
/// the phone number is, because calling the client is what the list is for.
/// </summary>
internal sealed class WaitlistService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IClock clock,
    ILogger<WaitlistService> logger) : IWaitlistService
{
    public async Task<int> CountWaitingAsync(CancellationToken cancellationToken = default)
    {
        var today = await TodayAsync(cancellationToken).ConfigureAwait(false);
        return await Waiting(today).CountAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WaitlistItem>> ListWaitingAsync(CancellationToken cancellationToken = default)
    {
        var zone = await TenantZone.ResolveAsync(tenantContext, tenantLookup, cancellationToken).ConfigureAwait(false);
        var today = clock.GetCurrentInstant().InZone(zone).Date;
        var entries = await Waiting(today).AsNoTracking()
            .OrderBy(w => w.CreatedAt).ThenBy(w => w.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var clientIds = entries.Select(e => e.ClientId).Distinct().ToArray();
        var clients = await db.Clients.AsNoTracking()
            .Where(c => clientIds.Contains(c.Id))
            .Select(c => new { c.Id, c.FirstName, c.LastName, c.Phone })
            .ToDictionaryAsync(c => c.Id, cancellationToken).ConfigureAwait(false);
        var serviceIds = entries.Select(e => e.ServiceId).Distinct().ToArray();
        var services = await db.Services.AsNoTracking()
            .Where(s => serviceIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken).ConfigureAwait(false);
        var staffIds = entries.Where(e => e.StaffMemberId is not null).Select(e => e.StaffMemberId!.Value).Distinct().ToArray();
        var staff = await db.StaffMembers.AsNoTracking()
            .Where(s => staffIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.DisplayName, cancellationToken).ConfigureAwait(false);

        return entries
            .Select(e =>
            {
                clients.TryGetValue(e.ClientId, out var client);
                return new WaitlistItem(
                    e.Id,
                    e.ClientId,
                    client is null ? string.Empty : $"{client.FirstName} {client.LastName}",
                    client?.Phone,
                    e.ServiceId,
                    services.GetValueOrDefault(e.ServiceId) ?? string.Empty,
                    e.StaffMemberId,
                    e.StaffMemberId is { } staffId ? staff.GetValueOrDefault(staffId) : null,
                    e.NotBefore,
                    e.NotAfter,
                    e.Note,
                    WaitlistRules.BookingDay(e.NotBefore, today),
                    e.CreatedAt.InZone(zone).Date)
                {
                    AskedOnline = e.CreatedByUserId is null,
                    LastToldOn = e.LastToldAt is { } told ? told.InZone(zone).Date : null,
                };
            })
            .ToArray();
    }

    public async Task<WaitlistAddOutcome> AddAsync(WaitlistRequest request, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var today = await TodayAsync(cancellationToken).ConfigureAwait(false);
        if (!WaitlistRules.DaysFit(request.NotBefore, request.NotAfter) || !WaitlistRules.IsStillPossible(request.NotAfter, today))
        {
            return WaitlistAddOutcome.DaysInvalid;
        }

        if (!WaitlistRules.NoteFits(request.Note))
        {
            return WaitlistAddOutcome.NoteTooLong;
        }

        if (!await db.Clients.AnyAsync(c => c.Id == request.ClientId && c.IsActive, cancellationToken).ConfigureAwait(false))
        {
            return WaitlistAddOutcome.ClientUnknown;
        }

        if (!await db.Services.AnyAsync(s => s.Id == request.ServiceId && s.IsActive, cancellationToken).ConfigureAwait(false))
        {
            return WaitlistAddOutcome.ServiceUnknown;
        }

        if (request.StaffMemberId is { } staffId
            && !await db.StaffMembers.AnyAsync(s => s.Id == staffId && s.IsActive && s.IsBookable, cancellationToken).ConfigureAwait(false))
        {
            return WaitlistAddOutcome.StaffUnknown;
        }

        // Two desks adding the same wish at once: the unique index lets one row in, the second try finds it.
        for (var attempt = 1; ; attempt++)
        {
            var entry = await db.WaitlistEntries
                .FirstOrDefaultAsync(w => w.ClientId == request.ClientId && w.ServiceId == request.ServiceId && w.ClosedAt == null, cancellationToken).ConfigureAwait(false);
            var isNew = entry is null;
            if (entry is null)
            {
                entry = new WaitlistEntry
                {
                    ClientId = request.ClientId,
                    ServiceId = request.ServiceId,
                    CreatedAt = clock.GetCurrentInstant(),
                    CreatedByUserId = byUserId,
                };
                db.WaitlistEntries.Add(entry);
            }

            entry.StaffMemberId = request.StaffMemberId;
            entry.NotBefore = request.NotBefore;
            entry.NotAfter = request.NotAfter;
            entry.Note = WaitlistRules.NormalizeNote(request.Note);
            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Waitlist entry {EntryId} {Action} by user {UserId}", entry.Id, isNew ? "added" : "updated", byUserId);
                return isNew ? WaitlistAddOutcome.Added : WaitlistAddOutcome.Updated;
            }
            catch (DbUpdateException ex) when (attempt == 1 && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.Entry(entry).State = EntityState.Detached;
            }
        }
    }

    public async Task<bool> RemoveAsync(Guid entryId, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        var entry = await db.WaitlistEntries.FirstOrDefaultAsync(w => w.Id == entryId && w.ClosedAt == null, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return false;
        }

        entry.ClosedAt = clock.GetCurrentInstant();
        entry.Outcome = WaitlistOutcome.Removed;
        entry.ClosedByUserId = byUserId;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Waitlist entry {EntryId} removed by user {UserId}", entryId, byUserId);
        return true;
    }

    public async Task<int> CloseForBookingAsync(Guid clientId, Guid serviceId, Guid appointmentId, CancellationToken cancellationToken = default)
    {
        var entries = await db.WaitlistEntries
            .Where(w => w.ClientId == clientId && w.ServiceId == serviceId && w.ClosedAt == null)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (entries.Count == 0)
        {
            return 0;
        }

        var now = clock.GetCurrentInstant();
        foreach (var entry in entries)
        {
            entry.ClosedAt = now;
            entry.Outcome = WaitlistOutcome.Booked;
            entry.AppointmentId = appointmentId;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Appointment {AppointmentId} ended the wait of {Count} waitlist entries", appointmentId, entries.Count);
        return entries.Count;
    }

    /// <summary>Not closed and not past its last day.</summary>
    private IQueryable<WaitlistEntry> Waiting(LocalDate today)
        => db.WaitlistEntries.Where(w => w.ClosedAt == null && (w.NotAfter == null || w.NotAfter >= today));

    private async Task<LocalDate> TodayAsync(CancellationToken cancellationToken)
    {
        var zone = await TenantZone.ResolveAsync(tenantContext, tenantLookup, cancellationToken).ConfigureAwait(false);
        return clock.GetCurrentInstant().InZone(zone).Date;
    }
}

/// <summary>
/// A booking ends the wait (ADR 0041): when a visit is booked, online or at the desk, the client's waiting entries for
/// that service are closed as booked. A consumer of its own, so the outbox retries it without repeating a message to
/// the client (ADR 0007, ADR 0025). Runs in the publishing tenant's scope.
/// </summary>
public sealed class WaitlistBookedConsumer(IWaitlistService waitlist) : IConsumer<AppointmentBooked>
{
    public async Task HandleAsync(AppointmentBooked domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        await waitlist.CloseForBookingAsync(domainEvent.ClientId, domainEvent.ServiceId, domainEvent.AppointmentId, cancellationToken).ConfigureAwait(false);
    }
}