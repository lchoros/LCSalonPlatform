using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Npgsql;
using Salon.Core.Appointments;
using Salon.Core.Appointments.Events;
using Salon.Core.Booking;
using Salon.Core.Domain;
using Salon.Core.Events;
using Salon.Core.Messaging;
using Salon.Core.Notifications;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Availability;
using Salon.Services.Catalog;
using Salon.Services.Formatting;
using Salon.Services.Messaging;
using Salon.Services.Notifications;
using Salon.Services.Settings;
using Salon.Services.Tenancy;

namespace Salon.Services.Appointments;

public enum WaitlistOfferOutcome
{
    /// <summary>The first client waiting for this time was sent a text; the time is kept for them when the salon holds freed times.</summary>
    Told = 1,

    /// <summary>The salon has not switched the message on.</summary>
    SwitchedOff = 2,

    /// <summary>The visit is unknown, is active again, or its time is too close or already past to be booked online.</summary>
    NothingToOffer = 3,

    /// <summary>It is the salon's quiet hours; nobody is texted at night. The desk has the list in the morning.</summary>
    QuietHours = 4,

    /// <summary>The freed time cannot be booked as it stands (the rota changed, something else took it, or it is held already).</summary>
    NotBookable = 5,

    /// <summary>Nobody is waiting for this service on this day with this person who can be told now.</summary>
    NobodyWaiting = 6,
}

/// <summary>A time kept for a waiting client, as the page behind their link shows it (ADR 0065).</summary>
/// <param name="Start">In the salon's zone.</param>
/// <param name="HeldUntil">In the salon's zone.</param>
/// <param name="VisitToken">The public token of the visit booked from this offer; null until it is taken.</param>
public sealed record HeldTime(
    WaitlistOfferState State,
    Guid ServiceId,
    string ServiceName,
    string StaffName,
    ZonedDateTime Start,
    Money Price,
    ZonedDateTime HeldUntil,
    string ClientFirstName,
    string? VisitToken);

public enum TakeHeldTimeOutcome
{
    /// <summary>The visit is booked.</summary>
    Booked = 1,

    /// <summary>The same link again: the visit was booked the first time.</summary>
    AlreadyBooked = 2,

    /// <summary>This salon has no such link.</summary>
    NotFound = 3,

    /// <summary>The time is no longer kept for the client: the hold ran out or the desk let it go.</summary>
    Over = 4,

    /// <summary>The time was kept but could not be booked (the rota changed, or the desk put a visit there).</summary>
    Gone = 5,
}

/// <param name="VisitToken">The public token of the booked visit, for its own page (ADR 0014).</param>
public sealed record TakeHeldTimeResult(TakeHeldTimeOutcome Outcome, string? VisitToken);

/// <summary>
/// Offers the time of a cancelled visit to the first client waiting for it (ADR 0056): one text, to one person. When
/// the salon holds freed times (ADR 0065) the time is kept off the booking pages for that person for a while, their
/// link books it with one tap, and when they do not take it the clock passes it on to the next in line.
/// </summary>
public interface IWaitlistOfferService
{
    Task<WaitlistOfferOutcome> OfferFreedTimeAsync(Guid appointmentId, Instant now, CancellationToken cancellationToken = default);

    /// <summary>The offer behind a client's link; null when this salon has no such link.</summary>
    Task<HeldTime?> FindAsync(string? token, CancellationToken cancellationToken = default);

    /// <summary>Books the held time for the client it is held for. The link is the proof: it went to their phone.</summary>
    Task<TakeHeldTimeResult> TakeAsync(string? token, CancellationToken cancellationToken = default);

    /// <summary>The desk lets a held time go: it is on the booking pages again and nobody else is told. False when no such offer stands.</summary>
    Task<bool> ReleaseAsync(Guid offerId, Guid? byUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the offers whose hold ran out or whose client stopped waiting, and offers each time to the next client in
    /// line. Returns how many clients were told.
    /// </summary>
    Task<int> PassOnDueAsync(Instant now, CancellationToken cancellationToken = default);
}

internal sealed class WaitlistOfferService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    ISettingService settings,
    IAvailabilityService availability,
    IServiceOfferingResolver offerings,
    IBookingService booking,
    IMessageTemplateService templates,
    IPublicUrlBuilder urls,
    ISmsSender sms,
    IClock clock,
    ILogger<WaitlistOfferService> logger) : IWaitlistOfferService
{
    private const string OneStandingPerTime = "ux_waitlist_offers_one_standing_per_time";

    public async Task<WaitlistOfferOutcome> OfferFreedTimeAsync(Guid appointmentId, Instant now, CancellationToken cancellationToken = default)
    {
        var visit = await db.Appointments.AsNoTracking()
            .Where(a => a.Id == appointmentId)
            .Select(a => new { a.ClientId, a.ServiceId, a.StaffMemberId, a.Start, a.Status })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (visit is null || AppointmentStateMachine.IsActive(visit.Status))
        {
            return WaitlistOfferOutcome.NothingToOffer;
        }

        return await OfferAsync(new FreedTime(visit.ServiceId, visit.StaffMemberId, visit.Start, visit.ClientId, appointmentId), now, cancellationToken).ConfigureAwait(false);
    }

    public async Task<HeldTime?> FindAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (!PublicBookingToken.LooksValid(token))
        {
            return null;
        }

        var offer = await db.WaitlistOffers.AsNoTracking().FirstOrDefaultAsync(o => o.Token == token, cancellationToken).ConfigureAwait(false);
        if (offer is null)
        {
            return null;
        }

        var (_, zone) = await SalonAsync(cancellationToken).ConfigureAwait(false);
        var firstName = await (
            from e in db.WaitlistEntries.AsNoTracking()
            join c in db.Clients.AsNoTracking() on e.ClientId equals c.Id
            where e.Id == offer.WaitlistEntryId
            select c.FirstName).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        var serviceName = await db.Services.AsNoTracking().Where(s => s.Id == offer.ServiceId).Select(s => s.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        var staffName = await db.StaffMembers.AsNoTracking().Where(s => s.Id == offer.StaffMemberId).Select(s => s.DisplayName).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        var offering = await offerings.ResolveAsync(offer.ServiceId, offer.StaffMemberId, cancellationToken).ConfigureAwait(false);
        var visitToken = offer.AppointmentId is { } appointmentId
            ? await db.Appointments.AsNoTracking().Where(a => a.Id == appointmentId).Select(a => a.PublicToken).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;
        return new HeldTime(
            WaitlistHoldRules.State(offer.EndedAt, offer.Outcome, offer.HeldUntil, clock.GetCurrentInstant()),
            offer.ServiceId,
            serviceName,
            staffName,
            offer.Start.InZone(zone),
            new Money(offering.Price, offering.Currency),
            offer.HeldUntil.InZone(zone),
            firstName,
            visitToken);
    }

    public async Task<TakeHeldTimeResult> TakeAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (!PublicBookingToken.LooksValid(token))
        {
            return new TakeHeldTimeResult(TakeHeldTimeOutcome.NotFound, null);
        }

        // Its own transaction when the caller has none: the offer is taken and the visit is booked, or neither.
        var own = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false) : null;
        try
        {
            var now = clock.GetCurrentInstant();

            // Taken in one statement: a second tap, or the clock passing the time on, finds the offer no longer standing.
            var taken = await db.WaitlistOffers
                .Where(o => o.Token == token && o.EndedAt == null && o.HeldUntil > now)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.EndedAt, now).SetProperty(o => o.Outcome, WaitlistOfferEnd.Taken), cancellationToken).ConfigureAwait(false);
            var offer = await db.WaitlistOffers.AsNoTracking().FirstOrDefaultAsync(o => o.Token == token, cancellationToken).ConfigureAwait(false);
            if (offer is null)
            {
                return new TakeHeldTimeResult(TakeHeldTimeOutcome.NotFound, null);
            }

            if (taken == 0)
            {
                if (offer.Outcome != WaitlistOfferEnd.Taken || offer.AppointmentId is not { } bookedId)
                {
                    return new TakeHeldTimeResult(TakeHeldTimeOutcome.Over, null);
                }

                var booked = await db.Appointments.AsNoTracking().Where(a => a.Id == bookedId).Select(a => a.PublicToken).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return new TakeHeldTimeResult(TakeHeldTimeOutcome.AlreadyBooked, booked);
            }

            var clientId = await db.WaitlistEntries.AsNoTracking().Where(e => e.Id == offer.WaitlistEntryId).Select(e => e.ClientId).FirstAsync(cancellationToken).ConfigureAwait(false);
            Appointment appointment;
            try
            {
                // The same rules as any booking (ADR 0012): the offer no longer stands, so its own time is free again.
                appointment = await booking.BookAsync(new BookAppointmentRequest(clientId, offer.StaffMemberId, offer.ServiceId, offer.Start, AppointmentSource.Online), cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                // The rota changed, the service or the person is no longer bookable, or the desk put a visit there.
                await db.WaitlistOffers.Where(o => o.Id == offer.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.Outcome, WaitlistOfferEnd.Gone), cancellationToken).ConfigureAwait(false);
                if (own is not null)
                {
                    await own.CommitAsync(cancellationToken).ConfigureAwait(false);
                }

                logger.LogInformation(ex, "Waitlist offer {OfferId} could not be taken: its time cannot be booked any more", offer.Id);
                return new TakeHeldTimeResult(TakeHeldTimeOutcome.Gone, null);
            }

            await db.WaitlistOffers.Where(o => o.Id == offer.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.AppointmentId, appointment.Id), cancellationToken).ConfigureAwait(false);
            if (own is not null)
            {
                await own.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            logger.LogInformation("Waitlist offer {OfferId} taken: appointment {AppointmentId} booked from the client's link", offer.Id, appointment.Id);
            return new TakeHeldTimeResult(TakeHeldTimeOutcome.Booked, appointment.PublicToken);
        }
        finally
        {
            if (own is not null)
            {
                await own.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<bool> ReleaseAsync(Guid offerId, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetCurrentInstant();
        var released = await db.WaitlistOffers
            .Where(o => o.Id == offerId && o.EndedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.EndedAt, now).SetProperty(o => o.Outcome, WaitlistOfferEnd.Released).SetProperty(o => o.EndedByUserId, byUserId), cancellationToken).ConfigureAwait(false);
        if (released > 0)
        {
            logger.LogInformation("Waitlist offer {OfferId} released by user {UserId}", offerId, byUserId ?? Guid.Empty);
        }

        return released > 0;
    }

    public async Task<int> PassOnDueAsync(Instant now, CancellationToken cancellationToken = default)
    {
        var due = await (
            from o in db.WaitlistOffers.AsNoTracking()
            join e in db.WaitlistEntries.AsNoTracking() on o.WaitlistEntryId equals e.Id
            where o.EndedAt == null && (o.HeldUntil <= now || e.ClosedAt != null)
            orderby o.HeldUntil, o.Id
            select new { o.Id, o.ServiceId, o.StaffMemberId, o.Start, o.FreedByAppointmentId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var told = 0;
        foreach (var offer in due)
        {
            // Ended in one statement: the client tapping at this very moment, or a second run, finds it ended.
            var ended = await db.WaitlistOffers
                .Where(o => o.Id == offer.Id && o.EndedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.EndedAt, now).SetProperty(o => o.Outcome, WaitlistOfferEnd.NotTaken), cancellationToken).ConfigureAwait(false);
            if (ended == 0)
            {
                continue;
            }

            // The client whose visit freed the time is still not offered it back.
            var freedBy = offer.FreedByAppointmentId is { } freedById
                ? await db.Appointments.AsNoTracking().Where(a => a.Id == freedById).Select(a => (Guid?)a.ClientId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                : null;
            var outcome = await OfferAsync(new FreedTime(offer.ServiceId, offer.StaffMemberId, offer.Start, freedBy, offer.FreedByAppointmentId), now, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Waitlist offer {OfferId} was not taken; passing its time on: {Outcome}", offer.Id, outcome);
            if (outcome == WaitlistOfferOutcome.Told)
            {
                told++;
            }
        }

        return told;
    }

    /// <summary>Tells the first client waiting for <paramref name="time"/>, and keeps it for them when the salon holds freed times.</summary>
    private async Task<WaitlistOfferOutcome> OfferAsync(FreedTime time, Instant now, CancellationToken cancellationToken)
    {
        var rules = await settings.LoadAsync<BookingSettings>(cancellationToken).ConfigureAwait(false);
        if (!rules.TellWaitlistWhenFreed)
        {
            return WaitlistOfferOutcome.SwitchedOff;
        }

        // A time that cannot be booked online any more (too close, or gone) is not worth a text.
        if (time.Start < now + Duration.FromMinutes(rules.MinNoticeMinutes))
        {
            return WaitlistOfferOutcome.NothingToOffer;
        }

        var (tenant, zone) = await SalonAsync(cancellationToken).ConfigureAwait(false);
        var notification = await settings.LoadAsync<NotificationSettings>(cancellationToken).ConfigureAwait(false);
        if (ReminderRules.IsQuiet(now, notification.QuietHoursStartMinute, notification.QuietHoursEndMinute, zone))
        {
            return WaitlistOfferOutcome.QuietHours;
        }

        // The message must be true: the same time with the same person has to be on the booking page right now. A time
        // already held for somebody is not on it.
        var day = time.Start.InZone(zone).Date;
        var slots = await availability.GetSlotsAsync(time.ServiceId, time.StaffMemberId, day, day, cancellationToken).ConfigureAwait(false);
        if (!slots.Any(s => s.Start == time.Start))
        {
            return WaitlistOfferOutcome.NotBookable;
        }

        var today = now.InZone(zone).Date;
        var waiting = await db.WaitlistEntries.AsNoTracking()
            .Where(e => e.ClosedAt == null && e.ServiceId == time.ServiceId && (time.NotClientId == null || e.ClientId != time.NotClientId))
            .OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var candidates = waiting
            .Where(e => WaitlistRules.IsWaiting(e.ClosedAt, e.NotAfter, today))
            .Where(e => WaitlistRules.Wants(e.NotBefore, e.NotAfter, e.StaffMemberId, day, time.StaffMemberId))
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
        var holds = rules.HoldFreedTimeMinutes > 0;
        var offering = await offerings.ResolveAsync(time.ServiceId, time.StaffMemberId, cancellationToken).ConfigureAwait(false);
        var toldBefore = now - WaitlistRules.TellAgainAfter;

        // The one who asked first and can be reached by text; the next when somebody else got to them first.
        foreach (var entry in candidates.Where(e => clients.ContainsKey(e.ClientId)))
        {
            // Marked in one statement before the text is handed over (as for reminders, ADR 0020): at worst a message
            // is missed, never repeated, and two cancellations at the same moment do not text one person twice.
            var marked = await db.WaitlistEntries
                .Where(e => e.Id == entry.Id && e.ClosedAt == null && (e.LastToldAt == null || e.LastToldAt <= toldBefore))
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.LastToldAt, now), cancellationToken).ConfigureAwait(false);
            if (marked == 0)
            {
                continue;
            }

            WaitlistOffer? offer = null;
            if (holds)
            {
                offer = new WaitlistOffer
                {
                    TenantId = tenant.Id,
                    WaitlistEntryId = entry.Id,
                    ServiceId = time.ServiceId,
                    StaffMemberId = time.StaffMemberId,
                    Start = time.Start,
                    End = time.Start + offering.Duration,
                    Token = PublicBookingToken.Generate(),
                    OfferedAt = now,
                    HeldUntil = WaitlistHoldRules.HeldUntil(now, rules.HoldFreedTimeMinutes, time.Start, rules.MinNoticeMinutes),
                    FreedByAppointmentId = time.FreedByAppointmentId,
                };
                db.WaitlistOffers.Add(offer);
                try
                {
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } refused)
                {
                    // Another request got there first: this time is held already, or this client holds another time.
                    db.Entry(offer).State = EntityState.Detached;
                    await db.WaitlistEntries.Where(e => e.Id == entry.Id && e.LastToldAt == now)
                        .ExecuteUpdateAsync(s => s.SetProperty(e => e.LastToldAt, entry.LastToldAt), cancellationToken).ConfigureAwait(false);
                    if (refused.ConstraintName == OneStandingPerTime)
                    {
                        return WaitlistOfferOutcome.NotBookable;
                    }

                    continue;
                }
            }

            var serviceName = await db.Services.AsNoTracking().Where(s => s.Id == time.ServiceId).Select(s => s.Name).FirstAsync(cancellationToken).ConfigureAwait(false);
            var culture = CultureInfo.GetCultureInfo(tenant.Culture);
            var tokens = MessageTokens.Empty
                .Add("SalonName", tenant.Name)
                .Add("ClientFirstName", clients[entry.ClientId].FirstName)
                .Add("ServiceName", serviceName)
                .Add("AppointmentStart", BookingSmsTokens.FormatStart(time.Start, zone, culture));
            string template;
            if (offer is null)
            {
                template = MessageTemplateCatalog.WaitlistTimeFreed;
                tokens = tokens.Add("BookingUrl", urls.SalonUrl(tenant.Slug, $"/book/{time.ServiceId}?date={day.ToIso()}"));
            }
            else
            {
                template = MessageTemplateCatalog.WaitlistTimeHeld;
                tokens = tokens
                    .Add("HeldUntil", HeldUntilText(offer.HeldUntil, now, zone, culture))
                    .Add("BookingUrl", urls.SalonUrl(tenant.Slug, $"/w/{offer.Token}"));
            }

            var rendered = await templates.RenderAsync(template, MessageChannel.Sms, tenant.Culture, tokens, cancellationToken).ConfigureAwait(false);
            await sms.SendAsync(new SmsMessage(clients[entry.ClientId].Phone, rendered.Body), cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Waitlist entry {EntryId} of tenant {TenantId} told about a freed time (held: {Held}); text handed to {Provider}", entry.Id, tenant.Id, offer is not null, sms.SystemName);
            return WaitlistOfferOutcome.Told;
        }

        return WaitlistOfferOutcome.NobodyWaiting;
    }

    /// <summary>"15:30" when the hold ends today, the day with it otherwise.</summary>
    private static string HeldUntilText(Instant heldUntil, Instant now, DateTimeZone zone, CultureInfo culture)
    {
        var until = heldUntil.InZone(zone);
        return until.Date == now.InZone(zone).Date ? until.TimeOfDay.ToShortTime(culture) : BookingSmsTokens.FormatStart(heldUntil, zone, culture);
    }

    private async Task<(TenantInfo Tenant, DateTimeZone Zone)> SalonAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("The waitlist belongs to a salon, but no tenant is set in the current scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");
        return (tenant, DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc);
    }

    /// <summary>A time that became free: which service, with whom, when, and whose visit freed it.</summary>
    /// <param name="NotClientId">The client who gave the time up; they are not offered it back.</param>
    private sealed record FreedTime(Guid ServiceId, Guid StaffMemberId, Instant Start, Guid? NotClientId, Guid? FreedByAppointmentId);
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