using Salon.Core.Catalog;
using Salon.Core.Cqrs;

namespace Salon.Web.Features.Catalog.CreateService;

/// <summary>
/// Adds one service to the current salon's catalogue and returns its id. Bound straight from the admin form;
/// <see cref="CreateServiceValidator"/> holds the rules, the handler trims the text and takes the currency from the tenant.
/// The timing fields (ADR 0013) are optional so a form that posts only the M0 fields still binds.
/// </summary>
/// <param name="Name">Shown to clients; unique per salon.</param>
/// <param name="Description">Optional, shown under the name on the public list.</param>
/// <param name="DurationMinutes">Total calendar time in whole minutes, a multiple of five.</param>
/// <param name="Price">In the salon's currency, at most two decimals.</param>
/// <param name="CategoryId">Null leaves the service uncategorised.</param>
/// <param name="BufferAfterMinutes">Clean-up time after the service; 0–<see cref="ServiceRules.MaxBufferAfterMinutes"/>, step 5.</param>
/// <param name="ProcessingMinutes">Time the client waits inside the service; 0–<see cref="ServiceRules.MaxProcessingMinutes"/>, step 5.</param>
/// <param name="ProcessingStartsAfterMinutes">Offset of the processing window; ignored (stored as 0) without processing time.</param>
/// <param name="DepositPercent">Deposit for this service as a whole percentage of the price (ADR 0023); blank follows the salon's default, 0 means none.</param>
/// <param name="VariantGroup">The title this service shares with its sibling variants (ADR 0032); set together with <paramref name="VariantLabel"/> or not at all.</param>
/// <param name="VariantLabel">What tells this variant from its siblings.</param>
public sealed record CreateServiceCommand(
    string Name,
    string? Description,
    int DurationMinutes,
    decimal Price,
    Guid? CategoryId = null,
    int BufferAfterMinutes = 0,
    int ProcessingMinutes = 0,
    int ProcessingStartsAfterMinutes = 0,
    int? DepositPercent = null,
    string? VariantGroup = null,
    string? VariantLabel = null) : ICommand<Guid>;