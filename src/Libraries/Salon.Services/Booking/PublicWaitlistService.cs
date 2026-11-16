using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Salon.Core.Booking;
using Salon.Core.Clients;
using Salon.Core.Messaging;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Appointments;
using Salon.Services.Clients;
using Salon.Services.Messaging;
using Salon.Services.Notifications;
using Salon.Services.Settings;
using Salon.Services.Tenancy;

namespace Salon.Services.Booking;

/// <summary>What the public "tell me when a time comes up" form collects (ADR 0055). Text is trimmed and normalised by the service.</summary>
/// <param name="StaffMemberId">Null when anyone will do.</param>
/// <param name="NotBefore">No use offering a day before this one; null means as soon as possible.</param>
/// <param name="NotAfter">No use offering a day after this one; null means any time.</param>
public sealed record PublicWaitlistRequest(
    Guid ServiceId,
    Guid? StaffMemberId,
    string FirstName,
    string LastName,
    string Phone,
    string? Email,
    LocalDate? NotBefore,
    LocalDate? NotAfter,
    string? Note)
{
    /// <summary>Keeps the phone and email out of any log line that prints the request.</summary>
    public override string ToString() => $"{nameof(PublicWaitlistRequest)} {{ ServiceId = {ServiceId} }}";
}

/// <summary>Either a verification to complete, or, with SMS verification off, the client is on the list already.</summary>
public sealed record PublicWaitlistStart(Guid? VerificationId)
{
    public bool RequiresVerification => VerificationId is not null;
}

public enum PublicWaitlistOutcome
{
    /// <summary>The client is on the list (or was already and kept their place).</summary>
    Joined = 1,
    WrongCode = 2,
    Expired = 3,
    TooManyAttempts = 4,
    NotFound = 5,

    /// <summary>The service or the person is no longer offered, or the days no longer make sense: the client starts again.</summary>
    NoLongerPossible = 6,
}

public sealed record PublicWaitlistVerification(PublicWaitlistOutcome Outcome, int AttemptsLeft = 0, Guid? ServiceId = null);

/// <summary>
/// The client-facing way onto the salon's waitlist (ADR 0055): somebody who finds no time that suits leaves their name
/// and number, proves the number is theirs with the same SMS code a booking uses, and the desk finds them on its list
/// (ADR 0041). Only when the owner has switched it on (<see cref="BookingSettings.PublicWaitlist"/>).
/// </summary>
public interface IPublicWaitlistService
{
    /// <summary>Whether the salon offers the form at all.</summary>
    Task<bool> IsOfferedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the request, then stores a <see cref="PhoneVerification"/> that carries it and sends the code, or, with
    /// SMS verification off, puts the client on the list at once. Throws <see cref="ArgumentException"/> for bad input,
    /// <see cref="InvalidOperationException"/> when the form is switched off or the service is not offered, and
    /// <see cref="PhoneVerificationRateLimitedException"/> after too many codes to one number in an hour.
    /// </summary>
    Task<PublicWaitlistStart> StartAsync(PublicWaitlistRequest request, CancellationToken cancellationToken = default);

    /// <summary>What the verify page shows; null when the id is not a waitlist request of this salon.</summary>
    Task<PendingVerification?> GetVerificationAsync(Guid verificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the code and, when it is right, finds or creates the client by the verified number and puts them on the
    /// list. A wrong code counts an attempt. The right code sent again is still "joined": the list keeps one entry.
    /// </summary>
    Task<PublicWaitlistVerification> VerifyAndJoinAsync(Guid verificationId, string? code, CancellationToken cancellationToken = default);
}

internal sealed class PublicWaitlistService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    ISettingService settings,
    IWaitlistService waitlist,
    IClientService clients,
    IMessageTemplateService templates,
    ISmsSender sms,
    IClock clock,
    ILogger<PublicWaitlistService> logger) : IPublicWaitlistService
{
    public async Task<bool> IsOfferedAsync(CancellationToken cancellationToken = default)
        => (await settings.LoadAsync<BookingSettings>(cancellationToken).ConfigureAwait(false)).PublicWaitlist;

    public async Task<PublicWaitlistStart> StartAsync(PublicWaitlistRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("The waitlist belongs to a salon, but no tenant is set in the current scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");
        var rules = await settings.LoadAsync<BookingSettings>(cancellationToken).ConfigureAwait(false);
        if (!rules.PublicWaitlist)
        {
            throw new InvalidOperationException("This salon does not take waitlist requests online.");
        }

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

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is { Length: > Core.Appointments.WaitlistEntry.NoteMaxLength })
        {
            throw new ArgumentException($"The note can have at most {Core.Appointments.WaitlistEntry.NoteMaxLength} characters.", nameof(request));
        }

        if (request.NotBefore is { } first && request.NotAfter is { } last && last < first)
        {
            throw new ArgumentException("The last day is before the first.", nameof(request));
        }

        if (!await db.Services.AnyAsync(s => s.Id == request.ServiceId && s.IsActive, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Service {request.ServiceId} does not exist or is not active in this salon.");
        }

        var now = clock.GetCurrentInstant();
        var pending = new PhoneVerification
        {
            TenantId = tenant.Id,
            Purpose = PhoneVerificationPurpose.Waitlist,
            PhoneE164 = phone,
            CodeSalt = string.Empty,
            CodeHash = string.Empty,
            ServiceId = request.ServiceId,
            StaffMemberId = request.StaffMemberId,
            // There is no time to book; the column holds the moment of asking.
            Start = now,
            FirstName = firstName!,
            LastName = lastName!,
            Email = email,
            WaitNotBefore = request.NotBefore,
            WaitNotAfter = request.NotAfter,
            WaitNote = note,
        };

        if (!rules.RequireSmsVerification)
        {
            var outcome = await JoinAsync(pending, cancellationToken).ConfigureAwait(false);
            return outcome == PublicWaitlistOutcome.Joined
                ? new PublicWaitlistStart(null)
                : throw new InvalidOperationException("The waitlist request can no longer be taken as it was asked.");
        }

        // The same limit a booking has, and counted together with bookings: codes to one number in an hour.
        var recent = await db.PhoneVerifications
            .CountAsync(v => v.PhoneE164 == phone && v.CreatedAt > now.Minus(PhoneVerificationRules.CodeRateWindow), cancellationToken)
            .ConfigureAwait(false);
        if (recent >= PhoneVerificationRules.MaxCodesPerPhone)
        {
            logger.LogWarning("Waitlist code refused for tenant {TenantId}: the phone already received {Count} codes in the last hour", tenant.Id, recent);
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
        logger.LogInformation("Waitlist verification {VerificationId} created for tenant {TenantId}; code handed to {Provider}", pending.Id, tenant.Id, sms.SystemName);
        return new PublicWaitlistStart(pending.Id);
    }

    public async Task<PendingVerification?> GetVerificationAsync(Guid verificationId, CancellationToken cancellationToken = default)
    {
        var row = await db.PhoneVerifications.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == verificationId && v.Purpose == PhoneVerificationPurpose.Waitlist, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var now = clock.GetCurrentInstant();
        var usable = row.VerifiedAt is null && !PhoneVerificationRules.IsDead(row) && !PhoneVerificationRules.IsExpired(row, now);
        return new PendingVerification(row.Id, row.PhoneE164, row.ServiceId, null, usable, PhoneVerificationRules.AttemptsLeft(row));
    }

    public async Task<PublicWaitlistVerification> VerifyAndJoinAsync(Guid verificationId, string? code, CancellationToken cancellationToken = default)
    {
        var row = await db.PhoneVerifications
            .FirstOrDefaultAsync(v => v.Id == verificationId && v.Purpose == PhoneVerificationPurpose.Waitlist, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return new PublicWaitlistVerification(PublicWaitlistOutcome.NotFound);
        }

        var outcome = PhoneVerificationRules.Verify(row, code, clock.GetCurrentInstant());
        switch (outcome)
        {
            case PhoneVerificationOutcome.WrongCode:
            case PhoneVerificationOutcome.TooManyAttempts:
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return new PublicWaitlistVerification(
                    outcome == PhoneVerificationOutcome.WrongCode ? PublicWaitlistOutcome.WrongCode : PublicWaitlistOutcome.TooManyAttempts,
                    PhoneVerificationRules.AttemptsLeft(row),
                    row.ServiceId);
            case PhoneVerificationOutcome.Expired:
                return new PublicWaitlistVerification(PublicWaitlistOutcome.Expired, ServiceId: row.ServiceId);
            case PhoneVerificationOutcome.AlreadyVerified:
            case PhoneVerificationOutcome.Verified:
                // The number is proven. Joining again (a second tap, a reload) keeps the one entry and its place.
                break;
            default:
                throw new InvalidOperationException($"Unexpected verification outcome {outcome}.");
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new PublicWaitlistVerification(await JoinAsync(row, cancellationToken).ConfigureAwait(false), ServiceId: row.ServiceId);
    }

    /// <summary>Finds or creates the client by the number and puts them on the desk's list (ADR 0041).</summary>
    private async Task<PublicWaitlistOutcome> JoinAsync(PhoneVerification pending, CancellationToken cancellationToken)
    {
        var existing = await clients.FindByPhoneAsync(pending.PhoneE164, cancellationToken).ConfigureAwait(false);
        Guid clientId;
        if (existing is not null)
        {
            clientId = existing.Id;
        }
        else
        {
            // Another card may already own the email (a family sharing one address); the phone decides, the email is then left off.
            var email = pending.Email;
            if (email is not null && await db.Clients.AnyAsync(c => c.Email == email, cancellationToken).ConfigureAwait(false))
            {
                email = null;
            }

            clientId = await clients.CreateAsync(new CreateClientRequest(pending.FirstName, pending.LastName, pending.PhoneE164, email), cancellationToken).ConfigureAwait(false);
        }

        var added = await waitlist.AddAsync(
            new WaitlistRequest(clientId, pending.ServiceId, pending.StaffMemberId, pending.WaitNotBefore, pending.WaitNotAfter, pending.WaitNote),
            byUserId: null,
            cancellationToken).ConfigureAwait(false);
        if (added is WaitlistAddOutcome.Added or WaitlistAddOutcome.Updated)
        {
            logger.LogInformation("Client {ClientId} joined the waitlist online for service {ServiceId} in tenant {TenantId}: {Outcome}", clientId, pending.ServiceId, pending.TenantId, added);
            return PublicWaitlistOutcome.Joined;
        }

        logger.LogInformation("Online waitlist request for service {ServiceId} in tenant {TenantId} could not be taken: {Outcome}", pending.ServiceId, pending.TenantId, added);
        return PublicWaitlistOutcome.NoLongerPossible;
    }
}