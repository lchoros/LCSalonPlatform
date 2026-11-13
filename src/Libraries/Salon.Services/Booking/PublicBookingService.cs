using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NodaTime;

using Salon.Core.Appointments;
using Salon.Core.Booking;
using Salon.Core.Clients;
using Salon.Core.Domain;
using Salon.Core.Messaging;
using Salon.Core.Payments;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Appointments;
using Salon.Services.Availability;
using Salon.Services.Clients;
using Salon.Services.Messaging;
using Salon.Services.Notifications;
using Salon.Services.Settings;
using Salon.Services.Tenancy;

namespace Salon.Services.Booking;

/// <summary>
/// ADR 0014. The code is generated here, hashed into the row and handed to the SMS sender; it is never logged and never
/// returned. Every read goes through the tenant filter, so a verification id or a token from another salon simply does
/// not exist here.
/// </summary>
internal sealed class PublicBookingService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    ISettingService settings,
    IAvailabilityService availability,
    IBookingService booking,
    IClientService clients,
    IMessageTemplateService templates,
    ISmsSender sms,
    IClock clock,
    ILogger<PublicBookingService> logger) : IPublicBookingService
{
    public async Task<PublicBookingStart> StartAsync(PublicBookingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenant = await RequireTenantAsync(cancellationToken).ConfigureAwait(false);

        if (!ClientName.TryNormalise(request.FirstName, out var firstName) || !ClientName.TryNormalise(request.LastName, out var lastName))
        {
            throw new ArgumentException($"First and last name are required and can have at most {Client.NameMaxLength} characters.", nameof(request));
        }

        if (!PhoneNumber.TryNormalise(request.Phone, tenant.Currency, out var phone) || phone is null)
        {
            throw new ArgumentException("The phone number is not valid; use the country code for numbers from abroad.", nameof(request));
        }

        if (!EmailAddress.TryNormalise(request.Email, out var email))
        {
            throw new ArgumentException($"The email must look like name@domain and have at most {Client.EmailMaxLength} characters.", nameof(request));
        }

        var wording = request.MarketingConsent ? RequireWording(request.ConsentWording) : null;
        if (!await db.Services.AnyAsync(s => s.Id == request.ServiceId && s.IsActive, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Service {request.ServiceId} does not exist or is not active in this salon.");
        }

        var pending = new PhoneVerification
        {
            TenantId = tenant.Id,
            PhoneE164 = phone,
            CodeSalt = string.Empty,
            CodeHash = string.Empty,
            ServiceId = request.ServiceId,
            StaffMemberId = request.StaffMemberId,
            Start = request.Start,
            FirstName = firstName!,
            LastName = lastName!,
            Email = email,
            MarketingConsent = request.MarketingConsent,
            ConsentWording = wording,
        };

        var rules = await settings.LoadAsync<BookingSettings>(cancellationToken).ConfigureAwait(false);
        if (!rules.RequireSmsVerification)
        {
            var appointment = await BookPendingAsync(pending, cancellationToken).ConfigureAwait(false);
            return new PublicBookingStart(null, appointment.Id);
        }

        var now = clock.GetCurrentInstant();
        var recent = await db.PhoneVerifications
            .CountAsync(v => v.PhoneE164 == phone && v.CreatedAt > now.Minus(PhoneVerificationRules.CodeRateWindow), cancellationToken)
            .ConfigureAwait(false);
        if (recent >= PhoneVerificationRules.MaxCodesPerPhone)
        {
            logger.LogWarning("Verification code refused for tenant {TenantId}: the phone already received {Count} codes in the last hour", tenant.Id, recent);
            throw new PhoneVerificationRateLimitedException();
        }

        var code = PhoneVerificationRules.GenerateCode();
        pending.CodeSalt = PhoneVerificationRules.GenerateSalt();
        pending.CodeHash = PhoneVerificationRules.Hash(pending.CodeSalt, code);
        pending.CreatedAt = now;
        pending.ExpiresAt = now.Plus(PhoneVerificationRules.Lifetime);
        db.PhoneVerifications.Add(pending);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var rendered = await templates.RenderAsync(MessageTemplateCatalog.BookingCode, MessageChannel.Sms, tenant.Culture, BookingSmsTokens.Code(tenant.Name, code), cancellationToken).ConfigureAwait(false);
        await sms.SendAsync(new SmsMessage(phone, rendered.Body), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Verification {VerificationId} created for tenant {TenantId}; code handed to {Provider}", pending.Id, tenant.Id, sms.SystemName);

        return new PublicBookingStart(pending.Id, null);
    }

    public async Task<PendingVerification?> GetVerificationAsync(Guid verificationId, CancellationToken cancellationToken = default)
    {
        var row = await db.PhoneVerifications.AsNoTracking().FirstOrDefaultAsync(v => v.Id == verificationId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var now = clock.GetCurrentInstant();
        var usable = row.VerifiedAt is null && !PhoneVerificationRules.IsDead(row) && !PhoneVerificationRules.IsExpired(row, now);
        return new PendingVerification(row.Id, row.PhoneE164, row.ServiceId, row.AppointmentId, usable, PhoneVerificationRules.AttemptsLeft(row));
    }

    public async Task<PublicBookingVerification> VerifyAndBookAsync(Guid verificationId, string? code, CancellationToken cancellationToken = default)
    {
        var row = await db.PhoneVerifications.FirstOrDefaultAsync(v => v.Id == verificationId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return new PublicBookingVerification(PublicBookingVerificationOutcome.NotFound);
        }

        if (row.AppointmentId is { } already)
        {
            // A repeated POST (back button, double tap) lands on the same confirmation instead of booking twice.
            return new PublicBookingVerification(PublicBookingVerificationOutcome.Booked, already, ServiceId: row.ServiceId);
        }

        var outcome = PhoneVerificationRules.Verify(row, code, clock.GetCurrentInstant());
        switch (outcome)
        {
            case PhoneVerificationOutcome.WrongCode:
            case PhoneVerificationOutcome.TooManyAttempts:
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return new PublicBookingVerification(
                    outcome == PhoneVerificationOutcome.WrongCode ? PublicBookingVerificationOutcome.WrongCode : PublicBookingVerificationOutcome.TooManyAttempts,
                    AttemptsLeft: PhoneVerificationRules.AttemptsLeft(row),
                    ServiceId: row.ServiceId);
            case PhoneVerificationOutcome.Expired:
                return new PublicBookingVerification(PublicBookingVerificationOutcome.Expired, ServiceId: row.ServiceId);
            case PhoneVerificationOutcome.AlreadyVerified:
                // Verified earlier but the booking failed (slot taken); the code has been spent, the client starts again.
                return new PublicBookingVerification(PublicBookingVerificationOutcome.SlotTaken, ServiceId: row.ServiceId);
            case PhoneVerificationOutcome.Verified:
                break;
            default:
                throw new InvalidOperationException($"Unexpected verification outcome {outcome}.");
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var appointment = await BookPendingAsync(row, cancellationToken).ConfigureAwait(false);
            row.AppointmentId = appointment.Id;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new PublicBookingVerification(PublicBookingVerificationOutcome.Booked, appointment.Id, ServiceId: row.ServiceId);
        }
        catch (SlotNoLongerAvailableException ex)
        {
            logger.LogInformation(ex, "Verification {VerificationId} passed but the slot was gone", row.Id);
            return new PublicBookingVerification(PublicBookingVerificationOutcome.SlotTaken, ServiceId: row.ServiceId);
        }
    }

    public Task<PublicAppointment?> FindAsync(Guid appointmentId, CancellationToken cancellationToken = default)
        => ProjectAsync(db.Appointments.Where(a => a.Id == appointmentId && a.Source == AppointmentSource.Online && a.PublicToken != null), cancellationToken);

    public Task<PublicAppointment?> FindByTokenAsync(string? publicToken, CancellationToken cancellationToken = default)
        => PublicBookingToken.LooksValid(publicToken)
            ? ProjectAsync(db.Appointments.Where(a => a.PublicToken == publicToken), cancellationToken)
            : Task.FromResult<PublicAppointment?>(null);

    public async Task<PublicCancellation> CancelByTokenAsync(string? publicToken, CancellationToken cancellationToken = default)
    {
        var appointment = await FindByTokenAsync(publicToken, cancellationToken).ConfigureAwait(false);
        if (appointment is null)
        {
            return new PublicCancellation(PublicCancellationOutcome.NotFound);
        }

        if (!appointment.IsActive)
        {
            return new PublicCancellation(PublicCancellationOutcome.AlreadyCancelled, appointment);
        }

        if (appointment.Start <= clock.GetCurrentInstant())
        {
            return new PublicCancellation(PublicCancellationOutcome.TooLate, appointment);
        }

        var cancelled = await booking.CancelAsync(appointment.Id, byClient: true, reason: null, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new PublicCancellation(PublicCancellationOutcome.Cancelled, appointment with { Status = cancelled.Status });
    }

    public async Task<PublicReschedule> RescheduleByTokenAsync(string? publicToken, Guid? staffMemberId, Instant newStart, CancellationToken cancellationToken = default)
    {
        var appointment = await FindByTokenAsync(publicToken, cancellationToken).ConfigureAwait(false);
        if (appointment is null)
        {
            return new PublicReschedule(PublicRescheduleOutcome.NotFound);
        }

        if (!appointment.IsActive)
        {
            return new PublicReschedule(PublicRescheduleOutcome.NotActive, appointment);
        }

        var now = clock.GetCurrentInstant();
        if (appointment.Start <= now)
        {
            return new PublicReschedule(PublicRescheduleOutcome.TooLate, appointment);
        }

        // The time it already has (a double submit, or the back button): nothing to do, and no second SMS.
        if (newStart == appointment.Start && (staffMemberId is null || staffMemberId == appointment.StaffMemberId))
        {
            return new PublicReschedule(PublicRescheduleOutcome.Moved, appointment);
        }

        // Inside the notice period of the salon's cancellation policy (ADR 0022) the salon is asked instead.
        var policy = await settings.LoadAsync<CancellationPolicySettings>(cancellationToken).ConfigureAwait(false);
        if (CancellationPolicy.IsInsideNotice(appointment.Start, now, policy.FreeChangeHours))
        {
            return new PublicReschedule(PublicRescheduleOutcome.TooLate, appointment);
        }

        try
        {
            var staff = staffMemberId;
            if (staff is null)
            {
                var zone = await ZoneAsync(cancellationToken).ConfigureAwait(false);
                var day = newStart.InZone(zone).Date;
                var slots = await availability.GetSlotsAsync(appointment.ServiceId, null, day, day, appointment.Id, cancellationToken).ConfigureAwait(false);
                staff = slots.FirstOrDefault(s => s.Start == newStart)?.StaffMemberId ?? throw new SlotNoLongerAvailableException();
            }

            await booking.MoveAsync(appointment.Id, staff.Value, newStart, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SlotNoLongerAvailableException or InvalidOperationException)
        {
            // Taken meanwhile, or the person stopped being bookable for this service: the client picks again.
            logger.LogInformation("Reschedule of appointment {AppointmentId} refused: {Reason}", appointment.Id, ex.GetType().Name);
            return new PublicReschedule(PublicRescheduleOutcome.SlotTaken, appointment);
        }

        return new PublicReschedule(PublicRescheduleOutcome.Moved, await FindByTokenAsync(publicToken, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Finds or creates the client by phone, picks the person for "anyone", books as Online. Throws <see cref="SlotNoLongerAvailableException"/>.</summary>
    private async Task<Appointment> BookPendingAsync(PhoneVerification pending, CancellationToken cancellationToken)
    {
        var clientId = await FindOrCreateClientAsync(pending, cancellationToken).ConfigureAwait(false);

        var staffMemberId = pending.StaffMemberId;
        if (staffMemberId is null)
        {
            var zone = await ZoneAsync(cancellationToken).ConfigureAwait(false);
            var day = pending.Start.InZone(zone).Date;
            var slots = await availability.GetSlotsAsync(pending.ServiceId, null, day, day, cancellationToken).ConfigureAwait(false);
            staffMemberId = slots.FirstOrDefault(s => s.Start == pending.Start)?.StaffMemberId
                ?? throw new SlotNoLongerAvailableException();
        }

        return await booking.BookAsync(
            new BookAppointmentRequest(clientId, staffMemberId.Value, pending.ServiceId, pending.Start, AppointmentSource.Online),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Guid> FindOrCreateClientAsync(PhoneVerification pending, CancellationToken cancellationToken)
    {
        var existing = await clients.FindByPhoneAsync(pending.PhoneE164, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (pending.MarketingConsent)
            {
                var card = await db.Clients.AsNoTracking().Where(c => c.Id == existing.Id).Select(c => new { c.MarketingConsent }).FirstAsync(cancellationToken).ConfigureAwait(false);
                if (!card.MarketingConsent)
                {
                    await clients.SetMarketingConsentAsync(existing.Id, granted: true, pending.ConsentWording, cancellationToken).ConfigureAwait(false);
                }
            }

            return existing.Id;
        }

        // Another card may already own the email (a family sharing one address); the phone decides, the email is then left off.
        var email = pending.Email;
        if (email is not null && await db.Clients.AnyAsync(c => c.Email == email, cancellationToken).ConfigureAwait(false))
        {
            logger.LogInformation("Email of the new online client is already on another card in tenant {TenantId}; stored without it", pending.TenantId);
            email = null;
        }

        return await clients.CreateAsync(
            new CreateClientRequest(pending.FirstName, pending.LastName, pending.PhoneE164, email, MarketingConsent: pending.MarketingConsent, MarketingConsentWording: pending.ConsentWording),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<PublicAppointment?> ProjectAsync(IQueryable<Appointment> query, CancellationToken cancellationToken)
    {
        var row = await query.AsNoTracking()
            .Select(a => new
            {
                a.Id,
                a.ServiceId,
                a.StaffMemberId,
                a.Start,
                a.End,
                a.PriceAmount,
                a.Currency,
                a.DepositAmount,
                a.Status,
                a.PublicToken,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (row?.PublicToken is null)
        {
            return null;
        }

        var serviceName = await db.Services.AsNoTracking().Where(s => s.Id == row.ServiceId).Select(s => s.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        var staffName = await db.StaffMembers.AsNoTracking().Where(s => s.Id == row.StaffMemberId).Select(s => s.DisplayName).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        return new PublicAppointment(row.Id, row.ServiceId, serviceName, row.StaffMemberId, staffName, row.Start, row.End, new Money(row.PriceAmount, row.Currency), row.Status, row.PublicToken, row.DepositAmount > 0m ? new Money(row.DepositAmount, row.Currency) : null);
    }

    private async Task<TenantInfo> RequireTenantAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("Online booking is per salon, but no tenant is set in the current scope.");
        return await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");
    }

    private async Task<DateTimeZone> ZoneAsync(CancellationToken cancellationToken)
    {
        var tenant = await RequireTenantAsync(cancellationToken).ConfigureAwait(false);
        return DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc;
    }

    private static string RequireWording(string? wording)
    {
        if (string.IsNullOrWhiteSpace(wording))
        {
            throw new ArgumentException("Marketing consent needs the wording the client agreed to.", nameof(wording));
        }

        var trimmed = wording.Trim();
        return trimmed.Length <= Client.MarketingConsentWordingMaxLength
            ? trimmed
            : throw new ArgumentException($"Consent wording can have at most {Client.MarketingConsentWordingMaxLength} characters.", nameof(wording));
    }
}