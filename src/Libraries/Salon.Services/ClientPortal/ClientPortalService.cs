using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Booking;
using Salon.Core.Clients;
using Salon.Core.Messaging;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Booking;
using Salon.Services.Clients;
using Salon.Services.Messaging;
using Salon.Services.Notifications;
using Salon.Services.Tenancy;

namespace Salon.Services.ClientPortal;

/// <summary>
/// ADR 0083. The code is made, hashed and checked by <see cref="PhoneVerificationRules"/>, the rules a booking uses;
/// nothing about codes is written here a second time. Log lines carry ids, outcomes and counts, never the number,
/// a name or the code.
/// </summary>
internal sealed class ClientPortalService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IMessageTemplateService templates,
    ISmsSender sms,
    IClock clock,
    ILogger<ClientPortalService> logger) : IClientPortalService
{
    /// <summary>More visits ahead than this are not listed; nobody has them.</summary>
    private const int MaxVisits = 50;

    public async Task<Guid> RequestCodeAsync(string? phone, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("The client's pages belong to a salon, but no tenant is set in the current scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");

        if (!PhoneNumber.TryNormalise(phone, tenant.Currency, out var number) || number is null)
        {
            throw new ArgumentException("The phone number is not valid; use the country code for numbers from abroad.", nameof(phone));
        }

        // The limit a booking has, counted together with bookings and for every number alike: a refusal says nothing
        // about whether the salon knows it.
        var now = clock.GetCurrentInstant();
        var recent = await db.PhoneVerifications
            .CountAsync(v => v.PhoneE164 == number && v.CreatedAt > now.Minus(PhoneVerificationRules.CodeRateWindow), cancellationToken)
            .ConfigureAwait(false);
        if (recent >= PhoneVerificationRules.MaxCodesPerPhone)
        {
            logger.LogWarning("Client portal code refused for tenant {TenantId}: the phone already received {Count} codes in the last hour", tenant.Id, recent);
            throw new PhoneVerificationRateLimitedException();
        }

        // Both paths do the same work: a row, a rendered text. A number without a card gets a row that no six digits
        // can prove, and its text is simply not handed to the provider.
        var known = await CardsWith(number).AnyAsync(cancellationToken).ConfigureAwait(false);
        var code = PhoneVerificationRules.GenerateCode();
        var salt = PhoneVerificationRules.GenerateSalt();
        var pending = new PhoneVerification
        {
            TenantId = tenant.Id,
            Purpose = PhoneVerificationPurpose.ClientPortal,
            PhoneE164 = number,
            CodeSalt = salt,
            CodeHash = PhoneVerificationRules.Hash(salt, known ? code : PhoneVerificationRules.GenerateUnmatchableSecret()),
            ServiceId = null,
            // Nothing is being booked; the column holds the moment of asking.
            Start = now,
            FirstName = string.Empty,
            LastName = string.Empty,
            CreatedAt = now,
            ExpiresAt = now.Plus(PhoneVerificationRules.Lifetime),
        };
        db.PhoneVerifications.Add(pending);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var rendered = await templates.RenderAsync(MessageTemplateCatalog.BookingCode, MessageChannel.Sms, tenant.Culture, BookingSmsTokens.Code(tenant.Name, code), cancellationToken).ConfigureAwait(false);
        if (known)
        {
            await sms.SendAsync(new SmsMessage(number, rendered.Body), cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation("Client portal verification {VerificationId} created for tenant {TenantId}; code handed to a provider: {Sent}", pending.Id, tenant.Id, known);
        return pending.Id;
    }

    public async Task<ClientPortalPendingCode?> FindPendingAsync(Guid verificationId, CancellationToken cancellationToken = default)
        => await db.PhoneVerifications.AsNoTracking()
            .Where(v => v.Id == verificationId && v.Purpose == PhoneVerificationPurpose.ClientPortal)
            .Select(v => new ClientPortalPendingCode(v.Id, v.PhoneE164))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<ClientPortalSignIn> VerifyAsync(Guid verificationId, string? code, CancellationToken cancellationToken = default)
    {
        // A row made for a booking or the waitlist is not a sign-in: here it does not exist.
        var row = await db.PhoneVerifications
            .FirstOrDefaultAsync(v => v.Id == verificationId && v.Purpose == PhoneVerificationPurpose.ClientPortal, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return new ClientPortalSignIn(ClientPortalSignInOutcome.NotFound);
        }

        var outcome = PhoneVerificationRules.Verify(row, code, clock.GetCurrentInstant());
        // The attempt that was counted, or the stamp that spends the row: a code opens the door once.
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (outcome != PhoneVerificationOutcome.Verified)
        {
            logger.LogInformation("Client portal verification {VerificationId} refused: {Outcome}", row.Id, outcome);
            return new ClientPortalSignIn(ClientPortalSignInOutcome.Refused);
        }

        var phone = row.PhoneE164;
        var clientId = await CardsWith(phone)
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (clientId is null)
        {
            // The card went between asking and proving (erased, merged, renumbered): the same answer as a wrong code.
            logger.LogInformation("Client portal verification {VerificationId} proven, but no card carries the number any more", row.Id);
            return new ClientPortalSignIn(ClientPortalSignInOutcome.Refused);
        }

        logger.LogInformation("Client {ClientId} signed in to the client portal of tenant {TenantId} with verification {VerificationId}", clientId, row.TenantId, row.Id);
        return new ClientPortalSignIn(ClientPortalSignInOutcome.SignedIn, clientId);
    }

    public async Task<IReadOnlyList<ClientPortalVisit>?> GetUpcomingVisitsAsync(Guid clientId, Instant signedInAt, CancellationToken cancellationToken = default)
    {
        var card = await db.Clients.AsNoTracking()
            .Where(c => c.Id == clientId && c.ErasedAt == null && c.MergedIntoClientId == null)
            .Select(c => new { c.Phone, c.UpdatedAt })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (card?.Phone is not { } phone || card.UpdatedAt > signedInAt)
        {
            return null;
        }

        var now = clock.GetCurrentInstant();
        var holding = AppointmentStateMachine.Active.ToArray();
        // Every card with the proven number, not only the one the session hangs on: today a number is on one card per
        // salon (ADR 0011), and the day a family may share one, their visits are here without this query changing.
        var cards = CardsWith(phone).Select(c => c.Id);
        var rows = await db.Appointments.AsNoTracking()
            .Where(a => cards.Contains(a.ClientId) && holding.Contains(a.Status) && a.Start > now)
            .OrderBy(a => a.Start)
            .ThenBy(a => a.Id)
            .Take(MaxVisits)
            .Select(a => new
            {
                a.Id,
                a.Start,
                a.End,
                a.PublicToken,
                ServiceName = db.Services.Where(s => s.Id == a.ServiceId).Select(s => s.Name).FirstOrDefault(),
                StaffName = db.StaffMembers.Where(s => s.Id == a.StaffMemberId).Select(s => s.DisplayName).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(r => new ClientPortalVisit(r.Id, r.ServiceName ?? string.Empty, r.StaffName ?? string.Empty, r.Start, r.End, r.PublicToken))];
    }

    /// <summary>The cards a number opens: the ones that carry it and are still a person the salon serves.</summary>
    private IQueryable<Client> CardsWith(string phone)
        => db.Clients.AsNoTracking().Where(c => c.Phone == phone && c.ErasedAt == null && c.MergedIntoClientId == null);
}