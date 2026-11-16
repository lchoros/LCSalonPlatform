using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Appointments.Events;
using Salon.Core.Events;
using Salon.Core.Messaging;
using Salon.Core.Notifications;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Availability;
using Salon.Services.Formatting;
using Salon.Services.Messaging;
using Salon.Services.Notifications;
using Salon.Services.Settings;
using Salon.Services.Tenancy;

namespace Salon.Services.Appointments;

public enum WaitlistOfferOutcome
{
    /// <summary>The first client waiting for this time was sent a text.</summary>
    Told = 1,

    /// <summary>The salon has not switched the message on.</summary>
    SwitchedOff = 2,

    /// <summary>The visit is unknown, is active again, or its time is too close or already past to be booked online.</summary>
    NothingToOffer = 3,

    /// <summary>It is the salon's quiet hours; nobody is texted at night. The desk has the list in the morning.</summary>
    QuietHours = 4,

    /// <summary>The freed time cannot be booked as it stands (the rota changed, or something else took it).</summary>
    NotBookable = 5,

    /// <summary>Nobody is waiting for this service on this day with this person who can be told now.</summary>
    NobodyWaiting = 6,
}

/// <summary>
/// Offers the time of a cancelled visit to the first client waiting for it (ADR 0056): one text, to one person, with a
/// link to book. Nothing is held for them: whoever books first has it, and the desk still has the whole list.
/// </summary>
public interface IWaitlistOfferService
{
    Task<WaitlistOfferOutcome> OfferFreedTimeAsync(Guid appointmentId, Instant now, CancellationToken cancellationToken = default);
}

internal sealed class WaitlistOfferService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    ISettingService settings,
    IAvailabilityService availability,
    IMessageTemplateService templates,
    IPublicUrlBuilder urls,
    ISmsSender sms,
    ILogger<WaitlistOfferService> logger) : IWaitlistOfferService
{
    public async Task<WaitlistOfferOutcome> OfferFreedTimeAsync(Guid appointmentId, Instant now, CancellationToken cancellationToken = default)
    {
        var rules = await settings.LoadAsync<BookingSettings>(cancellationToken).ConfigureAwait(false);
        if (!rules.TellWaitlistWhenFreed)
        {
            return WaitlistOfferOutcome.SwitchedOff;
        }

        var visit = await db.Appointments.AsNoTracking()
            .Where(a => a.Id == appointmentId)
            .Select(a => new { a.ClientId, a.ServiceId, a.StaffMemberId, a.Start, a.Status })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        // A time that cannot be booked online any more (too close, or gone) is not worth a text.
        if (visit is null || AppointmentStateMachine.IsActive(visit.Status) || visit.Start < now + Duration.FromMinutes(rules.MinNoticeMinutes))
        {
            return WaitlistOfferOutcome.NothingToOffer;
        }

        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("The waitlist belongs to a salon, but no tenant is set in the current scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc;
        var notification = await settings.LoadAsync<NotificationSettings>(cancellationToken).ConfigureAwait(false);
        if (ReminderRules.IsQuiet(now, notification.QuietHoursStartMinute, notification.QuietHoursEndMinute, zone))
        {
            return WaitlistOfferOutcome.QuietHours;
        }

        // The message must be true: the same time with the same person has to be on the booking page right now.
        var day = visit.Start.InZone(zone).Date;
        var slots = await availability.GetSlotsAsync(visit.ServiceId, visit.StaffMemberId, day, day, cancellationToken).ConfigureAwait(false);
        if (!slots.Any(s => s.Start == visit.Start))
        {
            return WaitlistOfferOutcome.NotBookable;
        }

        var today = now.InZone(zone).Date;
        var waiting = await db.WaitlistEntries
            .Where(e => e.ClosedAt == null && e.ServiceId == visit.ServiceId && e.ClientId != visit.ClientId)
            .OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var candidates = waiting
            .Where(e => WaitlistRules.IsWaiting(e.ClosedAt, e.NotAfter, today))
            .Where(e => WaitlistRules.Wants(e.NotBefore, e.NotAfter, e.StaffMemberId, day, visit.StaffMemberId))
            .Where(e => WaitlistRules.CanBeTold(e.LastToldAt, now))
            .ToList();
        if (candidates.Count == 0)
        {
            return WaitlistOfferOutcome.NobodyWaiting;
        }

        var clientIds = candidates.Select(e => e.ClientId).Distinct().ToArray();
        var clients = await db.Clients.AsNoTracking()
            .Where(c => clientIds.Contains(c.Id) && c.IsActive && c.Phone != null)
            .ToDictionaryAsync(c => c.Id, c => new { c.FirstName, Phone = c.Phone! }, cancellationToken).ConfigureAwait(false);

        // The one who asked first and can be reached by text.
        var first = candidates.FirstOrDefault(e => clients.ContainsKey(e.ClientId));
        if (first is null)
        {
            return WaitlistOfferOutcome.NobodyWaiting;
        }

        var serviceName = await db.Services.AsNoTracking().Where(s => s.Id == visit.ServiceId).Select(s => s.Name).FirstAsync(cancellationToken).ConfigureAwait(false);
        var culture = CultureInfo.GetCultureInfo(tenant.Culture);
        var link = urls.SalonUrl(tenant.Slug, $"/book/{visit.ServiceId}?date={day.ToIso()}");
        var tokens = MessageTokens.Empty
            .Add("SalonName", tenant.Name)
            .Add("ClientFirstName", clients[first.ClientId].FirstName)
            .Add("ServiceName", serviceName)
            .Add("AppointmentStart", BookingSmsTokens.FormatStart(visit.Start, zone, culture))
            .Add("BookingUrl", link);

        // Marked before the text is handed over (as for reminders, ADR 0020): at worst a message is missed, never repeated.
        first.LastToldAt = now;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var rendered = await templates.RenderAsync(MessageTemplateCatalog.WaitlistTimeFreed, MessageChannel.Sms, tenant.Culture, tokens, cancellationToken).ConfigureAwait(false);
        await sms.SendAsync(new SmsMessage(clients[first.ClientId].Phone, rendered.Body), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Waitlist entry {EntryId} of tenant {TenantId} told about the freed time of appointment {AppointmentId}; text handed to {Provider}", first.Id, tenantId, appointmentId, sms.SystemName);
        return WaitlistOfferOutcome.Told;
    }
}

/// <summary>
/// When a visit is cancelled, by the client or by the salon, its time is offered to the first client waiting for it
/// (ADR 0056). A consumer of its own, so the outbox retries it alone and the message to the client who cancelled does
/// not depend on it (ADR 0025).
/// </summary>
public sealed class WaitlistTimeFreedConsumer(IWaitlistOfferService offers, ILogger<WaitlistTimeFreedConsumer> logger) : IConsumer<AppointmentCancelled>
{
    public async Task HandleAsync(AppointmentCancelled domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var outcome = await offers.OfferFreedTimeAsync(domainEvent.AppointmentId, domainEvent.At, cancellationToken).ConfigureAwait(false);
        logger.LogDebug("Freed time of appointment {AppointmentId}: {Outcome}", domainEvent.AppointmentId, outcome);
    }
}