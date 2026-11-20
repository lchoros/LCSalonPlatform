using FluentValidation;
using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Services.ClientPortal;
using Salon.Services.Clients;
using Salon.Services.Localization;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.ClientPortal.RequestCode;

/// <summary>
/// The first step of "My bookings" (ADR 0083): a mobile number. The answer is the id of a verification row whether
/// or not the salon knows the number; a text message goes out only when it does.
/// </summary>
public sealed record RequestCodeCommand(string? Phone) : ICommand<Guid>
{
    /// <summary>Keeps the phone out of any log line that prints the request.</summary>
    public override string ToString() => nameof(RequestCodeCommand);
}

/// <summary>The messages are the booking form's own, so the two read alike.</summary>
public sealed class RequestCodeValidator : AbstractValidator<RequestCodeCommand>
{
    public RequestCodeValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.Phone)
            .Must(phone => !string.IsNullOrWhiteSpace(phone))
            .WithMessage(_ => localizer["Booking.Public.Validation.PhoneRequired"]);
    }
}

/// <summary>
/// Whether a number can be read depends on the salon's country, so this rule looks at the tenant. It says nothing
/// about whether the salon knows the number. A blank phone is the other validator's message.
/// </summary>
public sealed class RequestCodePhoneValidator : AbstractValidator<RequestCodeCommand>
{
    public RequestCodePhoneValidator(ITenantContext tenantContext, ITenantLookup tenantLookup, ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(tenantLookup);
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
    }
}

/// <summary>Runs inside the pipeline's transaction: the row is kept only when the text message was handed over.</summary>
public sealed class RequestCodeHandler(IClientPortalService portal) : IRequestHandler<RequestCodeCommand, Guid>
{
    public Task<Guid> HandleAsync(RequestCodeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return portal.RequestCodeAsync(request.Phone, cancellationToken);
    }
}