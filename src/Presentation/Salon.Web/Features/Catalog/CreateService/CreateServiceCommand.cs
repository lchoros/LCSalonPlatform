using Salon.Core.Cqrs;

namespace Salon.Web.Features.Catalog.CreateService;

/// <summary>
/// Adds one service to the current salon's catalogue and returns its id. Bound straight from the admin form;
/// <see cref="CreateServiceValidator"/> holds the rules, the handler trims the text and takes the currency from the tenant.
/// </summary>
/// <param name="Name">Shown to clients; unique per salon.</param>
/// <param name="Description">Optional, shown under the name on the public list.</param>
/// <param name="DurationMinutes">Calendar time in whole minutes, a multiple of five.</param>
/// <param name="Price">In the salon's currency, at most two decimals.</param>
public sealed record CreateServiceCommand(string Name, string? Description, int DurationMinutes, decimal Price) : ICommand<Guid>;