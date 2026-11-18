using FluentValidation;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Appointments;
using Salon.Core.Clients;
using Salon.Core.Cqrs;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Booking;
using Salon.Services.Clients;
using Salon.Services.Localization;
using Salon.Services.Settings;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.Booking.JoinWaitlist;

/// <summary>
/// What the public waitlist form says it is for (ADR 0055): the service, and the person when one was chosen. Null when
/// the salon has not switched the form on, or does not offer the service. A query: no transaction, tenant required.
/// </summary>
public sealed record WaitlistFormQuery(Guid ServiceId, Guid? StaffMemberId) : IQuery<WaitlistFormSummary?>;

/// <param name="StaffName">The person asked for; null when anyone will do.</param>
/// <param name="Today">Today in the salon's zone: the first day a client can ask for.</param>
/// <param name="RequireSmsVerification">Whether a code is sent; the button says so.</param>
public sealed record WaitlistFormSummary(Guid ServiceId, string ServiceName, Guid? StaffMemberId, string? StaffName, LocalDate Today, bool RequireSmsVerification);

/// <summary>
/// The form as posted (ADR 0055): who is asking, and optionally between which days and a remark. Sends the SMS code,
/// or, when the salon has switched verification off, puts the client on the list at once. Bound straight from the
/// form; <see cref="JoinWaitlistValidator"/> holds the field rules and <see cref="JoinWaitlistPhoneValidator"/> the
/// tenant-aware phone rule.
/// </summary>
/// <param name="ServiceId">From the route.</param>
/// <param name="NotBefore">ISO date, or empty for "as soon as possible".</param>
/// <param name="NotAfter">ISO date, or empty for "any time".</param>
public sealed record JoinWaitlistCommand(
    Guid ServiceId,
    Guid? StaffMemberId,
    string? FirstName,
    string? LastName,
    string? Phone,
    string? Email,
    string? NotBefore,
    string? NotAfter,
    string? Note) : ICommand<PublicWaitlistStart>
{
    /// <summary>Keeps the phone and email out of any log line that prints the request.</summary>
    public override string ToString() => $"{nameof(JoinWaitlistCommand)} {{ ServiceId = {ServiceId} }}";

    /// <summary>An ISO date as typed; null for an empty field. An unreadable one is the validator's to refuse.</summary>
    public static LocalDate? Day(string? text)
        => !string.IsNullOrWhiteSpace(text) && LocalDatePattern.Iso.Parse(text.Trim()) is { Success: true } parsed ? parsed.Value : null;

    public static bool IsDayOrEmpty(string? text)
        => string.IsNullOrWhiteSpace(text) || LocalDatePattern.Iso.Parse(text.Trim()).Success;
}

/// <summary>What the verify page shows; null when the id is not a waitlist request of this salon.</summary>
public sealed record PendingWaitlistQuery(Guid VerificationId) : IQuery<PendingVerification?>;

/// <summary>The six-digit code for a waitlist request (ADR 0055).</summary>
public sealed record VerifyWaitlistCodeCommand(Guid VerificationId, string? Code) : ICommand<PublicWaitlistVerification>;

/// <summary>Field rules that need no tenant: names, email shape, the two days and the remark. Messages come from resource keys.</summary>
public sealed class JoinWaitlistValidator : AbstractValidator<JoinWaitlistCommand>
{
    public JoinWaitlistValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.FirstName)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage(_ => localizer["Booking.Public.Validation.FirstNameRequired"])
            .Must(name => name is null || name.Trim().Length <= Client.NameMaxLength)
            .WithMessage(_ => localizer.Format("Booking.Public.Validation.NameTooLong", Client.NameMaxLength));

        RuleFor(c => c.LastName)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage(_ => localizer["Booking.Public.Validation.LastNameRequired"])
            .Must(name => name is null || name.Trim().Length <= Client.NameMaxLength)
            .WithMessage(_ => localizer.Format("Booking.Public.Validation.NameTooLong", Client.NameMaxLength));

        RuleFor(c => c.Phone)
            .Must(phone => !string.IsNullOrWhiteSpace(phone))
            .WithMessage(_ => localizer["Booking.Public.Validation.PhoneRequired"]);

        RuleFor(c => c.Email)
            .Must(email => EmailAddress.TryNormalise(email, out _))
            .WithMessage(_ => localizer["Booking.Public.Validation.EmailInvalid"]);

        RuleFor(c => c.NotBefore)
            .Must(JoinWaitlistCommand.IsDayOrEmpty)
            .WithMessage(_ => localizer["Booking.Waitlist.Validation.Day"]);

        RuleFor(c => c.NotAfter)
            .Cascade(CascadeMode.Stop)
            .Must(JoinWaitlistCommand.IsDayOrEmpty)
            .WithMessage(_ => localizer["Booking.Waitlist.Validation.Day"])
            .Must((command, last) => JoinWaitlistCommand.Day(command.NotBefore) is not { } first || JoinWaitlistCommand.Day(last) is not { } until || until >= first)
            .WithMessage(_ => localizer["Booking.Waitlist.Validation.DaysInOrder"]);

        RuleFor(c => c.Note)
            .Must(note => note is null || note.Trim().Length <= WaitlistEntry.NoteMaxLength)
            .WithMessage(_ => localizer.Format("Booking.Waitlist.Validation.NoteTooLong", WaitlistEntry.NoteMaxLength));
    }
}

/// <summary>
/// Whether a number resolves depends on the salon's country, so this rule reads the tenant and is kept apart from
/// <see cref="JoinWaitlistValidator"/>. A blank phone is the other validator's message. The last day must not be past
/// on the salon's calendar either, which also needs the tenant.
/// </summary>
public sealed class JoinWaitlistTenantValidator : AbstractValidator<JoinWaitlistCommand>
{
    public JoinWaitlistTenantValidator(ITenantContext tenantContext, ITenantLookup tenantLookup, IClock clock, ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(tenantLookup);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.Phone)
            .MustAsync(async (phone, cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(phone))
                {
                    return true;
                }

                var tenant = tenantContext.TenantId is { } tenantId ? await tenantLookup.FindByIdAsync(tenantId, cancellationToken) : null;
                return PhoneNumber.TryNormalise(phone, tenant?.Currency, out var normalised) && normalised is not null;
            })
            .WithMessage(_ => localizer["Booking.Public.Validation.PhoneInvalid"]);

        RuleFor(c => c.NotAfter)
            .MustAsync(async (last, cancellationToken) =>
            {
                if (JoinWaitlistCommand.Day(last) is not { } until)
                {
                    return true;
                }

                var tenant = tenantContext.TenantId is { } tenantId ? await tenantLookup.FindByIdAsync(tenantId, cancellationToken) : null;
                var zone = tenant is null ? DateTimeZone.Utc : DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc;
                return until >= clock.GetCurrentInstant().InZone(zone).Date;
            })
            .WithMessage(_ => localizer["Booking.Waitlist.Validation.DayPast"]);
    }
}

/// <summary>Reads through the tenant filter only.</summary>
public sealed class WaitlistFormHandler(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    ISettingService settings,
    IClock clock) : IRequestHandler<WaitlistFormQuery, WaitlistFormSummary?>
{
    public async Task<WaitlistFormSummary?> HandleAsync(WaitlistFormQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var rules = await settings.LoadAsync<BookingSettings>(cancellationToken);
        if (!rules.PublicWaitlist)
        {
            return null;
        }

        var service = await db.Services.AsNoTracking().Where(s => s.Id == request.ServiceId && s.IsActive).Select(s => new { s.Id, s.Name }).FirstOrDefaultAsync(cancellationToken);
        if (service is null)
        {
            return null;
        }

        // An unknown or unbookable person is simply "anyone": the form still works.
        var staff = request.StaffMemberId is { } staffId
            ? await db.StaffMembers.AsNoTracking().Where(s => s.Id == staffId && s.IsActive && s.IsBookable).Select(s => new { s.Id, s.DisplayName }).FirstOrDefaultAsync(cancellationToken)
            : null;

        // The tenant guard has already refused tenant-less requests; this only narrows the type.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(WaitlistFormQuery)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc;
        return new WaitlistFormSummary(service.Id, service.Name, staff?.Id, staff?.DisplayName, clock.GetCurrentInstant().InZone(zone).Date, rules.RequireSmsVerification);
    }
}

/// <summary>Hands the request to <see cref="IPublicWaitlistService"/>, which owns the flow.</summary>
public sealed class JoinWaitlistHandler(IPublicWaitlistService waitlist) : IRequestHandler<JoinWaitlistCommand, PublicWaitlistStart>
{
    public Task<PublicWaitlistStart> HandleAsync(JoinWaitlistCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return waitlist.StartAsync(
            new PublicWaitlistRequest(
                request.ServiceId,
                request.StaffMemberId,
                request.FirstName ?? string.Empty,
                request.LastName ?? string.Empty,
                request.Phone ?? string.Empty,
                request.Email,
                JoinWaitlistCommand.Day(request.NotBefore),
                JoinWaitlistCommand.Day(request.NotAfter),
                request.Note),
            cancellationToken);
    }
}

public sealed class PendingWaitlistHandler(IPublicWaitlistService waitlist) : IRequestHandler<PendingWaitlistQuery, PendingVerification?>
{
    public Task<PendingVerification?> HandleAsync(PendingWaitlistQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return waitlist.GetVerificationAsync(request.VerificationId, cancellationToken);
    }
}

public sealed class VerifyWaitlistCodeHandler(IPublicWaitlistService waitlist) : IRequestHandler<VerifyWaitlistCodeCommand, PublicWaitlistVerification>
{
    public Task<PublicWaitlistVerification> HandleAsync(VerifyWaitlistCodeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return waitlist.VerifyAndJoinAsync(request.VerificationId, request.Code, cancellationToken);
    }
}