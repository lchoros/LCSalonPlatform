using FluentValidation;
using Salon.Core.Tenancy;
using Salon.Services.Clients;
using Salon.Services.Localization;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.Clients.EditClient;

/// <summary>
/// Whether the phone resolves to one stored shape depends on the salon's country (a UK "07…" is fine in London and
/// meaningless in Warsaw), so this rule reads the tenant and is kept apart from <see cref="EditClientValidator"/>,
/// which stays testable without a tenant.
/// </summary>
public sealed class EditClientPhoneValidator : AbstractValidator<EditClientCommand>
{
    public EditClientPhoneValidator(ITenantContext tenantContext, ITenantLookup tenantLookup, ILocalizationService localizer)
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
                return PhoneNumber.TryNormalise(phone, tenant?.Currency, out _);
            })
            .WithMessage(_ => localizer["Clients.Edit.Validation.PhoneInvalid"]);
    }
}