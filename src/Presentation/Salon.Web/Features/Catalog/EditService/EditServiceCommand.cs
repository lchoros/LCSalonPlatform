using Salon.Core.Catalog;
using Salon.Core.Cqrs;

namespace Salon.Web.Features.Catalog.EditService;

/// <summary>
/// Replaces every editable field of one service (ADR 0013) and returns false when the salon has no such service. Bound
/// straight from the admin form, with <see cref="ServiceId"/> taken from the route; <see cref="EditServiceValidator"/> and
/// <see cref="EditServiceDatabaseValidator"/> hold the rules, the handler normalises the text and publishes the events.
/// </summary>
/// <param name="DurationMinutes">Total calendar time in whole minutes, a multiple of five.</param>
/// <param name="CategoryId">Null leaves the service uncategorised.</param>
/// <param name="BufferAfterMinutes">Clean-up time after the service; 0–<see cref="ServiceRules.MaxBufferAfterMinutes"/>, step 5.</param>
/// <param name="ProcessingMinutes">Time the client waits inside the service; 0–<see cref="ServiceRules.MaxProcessingMinutes"/>, step 5.</param>
/// <param name="ProcessingStartsAfterMinutes">Offset of the processing window; ignored (stored as 0) without processing time.</param>
/// <param name="IsActive">False hides the service from the public list and publishes <c>ServiceDeactivated</c>.</param>
/// <param name="DepositPercent">Deposit for this service as a whole percentage of the price (ADR 0023); blank follows the salon's default, 0 means none.</param>
/// <param name="VariantGroup">The title this service shares with its sibling variants (ADR 0032); set together with <paramref name="VariantLabel"/> or not at all.</param>
/// <param name="VariantLabel">What tells this variant from its siblings.</param>
public sealed record EditServiceCommand(
    Guid ServiceId,
    string Name,
    string? Description,
    int DurationMinutes,
    decimal Price,
    Guid? CategoryId,
    int BufferAfterMinutes,
    int ProcessingMinutes,
    int ProcessingStartsAfterMinutes,
    bool IsActive,
    int? DepositPercent = null,
    string? VariantGroup = null,
    string? VariantLabel = null) : ICommand<bool>
{
    /// <summary>The form for an existing service, as the GET renders it.</summary>
    public static EditServiceCommand From(Service service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return new EditServiceCommand(
            service.Id,
            service.Name,
            service.Description,
            (int)service.Duration.TotalMinutes,
            service.Price,
            service.CategoryId,
            service.BufferAfterMinutes,
            service.ProcessingMinutes,
            service.ProcessingStartsAfterMinutes,
            service.IsActive,
            service.DepositPercent is { } percent ? (int)decimal.Round(percent) : null,
            service.VariantGroup,
            service.VariantLabel);
    }
}