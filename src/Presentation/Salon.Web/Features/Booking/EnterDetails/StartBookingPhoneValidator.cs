using FluentValidation;

using Salon.Core.Tenancy;
using Salon.Services.Clients;
using Salon.Services.Localization;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.Booking.EnterDetails;

/// <summary>
/// Whether a number resolves depends on the salon's country (a UK "07…" is fine in London and meaningless in Warsaw), so
/// this rule reads the tenant and is kept apart from <see cref="StartBookingValidator"/>. A blank phone is the other
/// validator's message, not this one's.
/// </summary>
public sealed class StartBookingPhoneValidator : AbstractValidator<StartBookingCommand>
{
    public StartBookingPhoneValidator(ITenantContext tenantContext, ITenantLookup tenantLookup, ILocalizationService localizer)
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