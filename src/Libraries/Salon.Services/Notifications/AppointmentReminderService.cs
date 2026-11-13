using System.Globalization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NodaTime;

using Npgsql;

using Salon.Core.Appointments;
using Salon.Core.Messaging;
using Salon.Core.Notifications;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Messaging;
using Salon.Services.Settings;
using Salon.Services.Tenancy;

namespace Salon.Services.Notifications;

/// <summary>Sends the appointment reminders that are due for the salon in scope (ADR 0020).</summary>
public interface IAppointmentReminderService
{
    /// <summary>
    /// Sends every reminder due at <paramref name="now"/> and returns how many SMS were handed to the provider. Safe to
    /// call as often as wanted and from several workers: each reminder is claimed by a unique row before it is sent.
    /// </summary>
    Task<int> SendDueAsync(Instant now, CancellationToken cancellationToken = default);
}

internal sealed class AppointmentReminderService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    ISettingService settings,
    IMessageTemplateService templates,
    IPublicUrlBuilder urls,
    ISmsSender sms,
    ILogger<AppointmentReminderService> logger) : IAppointmentReminderService
{
    public async Task<int> SendDueAsync(Instant now, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("Reminders are sent per salon; a tenant must be in scope.");
        var notification = await settings.LoadAsync<NotificationSettings>(cancellationToken).ConfigureAwait(false);
        var plan = ReminderRules.Plan(notification).OrderBy(p => p.Lead).ToList();
        if (plan.Count == 0)
        {
            return 0;
        }

        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc;

        // A reminder is never sent before start minus its lead time, so nothing further away than the longest lead can be due.
        var from = now + ReminderRules.MinimumNotice;
        var to = now + plan[^1].Lead;
        var active = AppointmentStateMachine.Active.ToArray();
        var appointments = await db.Appointments.AsNoTracking()
            .Where(a => active.Contains(a.Status) && a.Start >= from && a.Start <= to)
            .OrderBy(a => a.Start)
            .Select(a => new DueAppointment(a.Id, a.ClientId, a.ServiceId, a.StaffMemberId, a.Start, a.CreatedAt, a.PublicToken))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (appointments.Count == 0)
        {
            return 0;
        }

        var ids = appointments.Select(a => a.Id).ToArray();
        var handled = (await db.AppointmentReminders.AsNoTracking()
                .Where(r => ids.Contains(r.AppointmentId))
                .Select(r => new { r.AppointmentId, r.Kind, r.AppointmentStart })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(r => (r.AppointmentId, r.Kind, r.AppointmentStart))
            .ToHashSet();
        // A move is a history row whose status did not change (ADR 0016); the salon has just told the client the new time.
        var movedAt = (await db.AppointmentStatusChanges.AsNoTracking()
                .Where(c => ids.Contains(c.AppointmentId) && c.From == c.To)
                .GroupBy(c => c.AppointmentId)
                .Select(g => new { AppointmentId = g.Key, At = g.Max(c => c.At) })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(m => m.AppointmentId, m => m.At);

        var sent = 0;
        foreach (var appointment in appointments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bookedAt = movedAt.TryGetValue(appointment.Id, out var moved) && moved > appointment.CreatedAt ? moved : appointment.CreatedAt;
            var due = plan
                .Where(p => !handled.Contains((appointment.Id, p.Kind, appointment.Start)))
                .Where(p => ReminderRules.IsDue(
                    ReminderRules.SendAt(appointment.Start, p.Lead, notification.QuietHoursStartMinute, notification.QuietHoursEndMinute, zone),
                    now,
                    appointment.Start,
                    bookedAt))
                .Select(p => p.Kind)
                .ToList();
            if (due.Count == 0)
            {
                continue;
            }

            // Two reminders due in one pass (the task was down, or quiet hours pushed them together) are one message:
            // the one closest to the visit is sent, the others are recorded so they never go out later.
            if (await SendOneAsync(tenant, zone, appointment, due[0], now, cancellationToken).ConfigureAwait(false))
            {
                sent++;
            }

            foreach (var superseded in due.Skip(1))
            {
                await ClaimAsync(tenantId, appointment, superseded, ReminderOutcome.Superseded, now, cancellationToken).ConfigureAwait(false);
            }
        }

        return sent;
    }

    private async Task<bool> SendOneAsync(TenantInfo tenant, DateTimeZone zone, DueAppointment appointment, ReminderKind kind, Instant now, CancellationToken cancellationToken)
    {
        var claim = await ClaimAsync(tenant.Id, appointment, kind, ReminderOutcome.Pending, now, cancellationToken).ConfigureAwait(false);
        if (claim is null)
        {
            return false;
        }

        var client = await db.Clients.AsNoTracking()
            .Where(c => c.Id == appointment.ClientId)
            .Select(c => new { c.FirstName, c.Phone })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(client?.Phone))
        {
            await FinishAsync(claim, ReminderOutcome.NoPhone, cancellationToken).ConfigureAwait(false);
            return false;
        }

        try
        {
            var serviceName = await db.Services.AsNoTracking().Where(s => s.Id == appointment.ServiceId).Select(s => s.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
            var staffName = await db.StaffMembers.AsNoTracking().Where(s => s.Id == appointment.StaffMemberId).Select(s => s.DisplayName).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
            // Admin bookings have no public page yet (ADR 0014); their link leads to the salon's booking page instead.
            var link = appointment.PublicToken is { } token ? urls.CancelBookingUrl(tenant.Slug, token) : urls.BookingUrl(tenant.Slug);
            var tokens = BookingSmsTokens.Confirmation(tenant.Name, client.FirstName, serviceName, staffName, appointment.Start, zone, CultureInfo.GetCultureInfo(tenant.Culture), link);
            var rendered = await templates.RenderAsync(MessageTemplateCatalog.BookingReminder, MessageChannel.Sms, tenant.Culture, tokens, cancellationToken).ConfigureAwait(false);
            if (!rendered.IsComplete)
            {
                logger.LogWarning("Reminder template for tenant {TenantId} has unresolved tokens: {Tokens}", tenant.Id, string.Join(", ", rendered.MissingTokens));
            }

            await sms.SendAsync(new SmsMessage(client.Phone, rendered.Body), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not retried: the claim stays, so the client can at worst miss this reminder, never get it twice.
            logger.LogError(ex, "Reminder {Kind} for appointment {AppointmentId} could not be sent", kind, appointment.Id);
            await FinishAsync(claim, ReminderOutcome.Failed, cancellationToken).ConfigureAwait(false);
            return false;
        }

        await FinishAsync(claim, ReminderOutcome.Sent, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Reminder {Kind} for appointment {AppointmentId} handed to {Provider}", kind, appointment.Id, sms.SystemName);
        return true;
    }

    /// <summary>Writes the unique row for this reminder; null when another pass or worker already holds it.</summary>
    private async Task<AppointmentReminder?> ClaimAsync(Guid tenantId, DueAppointment appointment, ReminderKind kind, ReminderOutcome outcome, Instant now, CancellationToken cancellationToken)
    {
        var claim = new AppointmentReminder
        {
            TenantId = tenantId,
            AppointmentId = appointment.Id,
            Kind = kind,
            AppointmentStart = appointment.Start,
            Outcome = outcome,
            CreatedAt = now,
        };
        db.AppointmentReminders.Add(claim);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return claim;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(claim).State = EntityState.Detached;
            return null;
        }
    }

    private async Task FinishAsync(AppointmentReminder claim, ReminderOutcome outcome, CancellationToken cancellationToken)
    {
        claim.Outcome = outcome;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record DueAppointment(Guid Id, Guid ClientId, Guid ServiceId, Guid StaffMemberId, Instant Start, Instant CreatedAt, string? PublicToken);
}