using Salon.Core.Cqrs;

namespace Salon.Web.Features.Clients.EditClient;

/// <summary>
/// Rewrites one client of the current salon: details, health notes and marketing consent. Returns false when the salon has
/// no such client. Bound straight from the admin form (the id comes from the route); <see cref="EditClientValidator"/>
/// holds the field rules, <see cref="EditClientPhoneValidator"/> the tenant-aware phone rule, and the client service
/// normalises, encrypts the health notes and refuses duplicates.
/// </summary>
/// <param name="Phone">As typed; local numbers resolve with the salon's country.</param>
/// <param name="HealthNotes">Plain text for this request only; stored encrypted (ADR 0011).</param>
/// <param name="LastPatchTestOn">ISO date (<c>yyyy-MM-dd</c>, what a date input posts) or blank.</param>
/// <param name="MarketingConsent">The tick; the wording recorded on a new grant is the label shown next to it.</param>
/// <param name="DepositPercent">This client's own deposit as a whole percentage (ADR 0023); blank follows the service and the salon, 0 never asks.</param>
public sealed record EditClientCommand(
    Guid Id,
    string FirstName,
    string LastName,
    string? Phone,
    string? Email,
    string? Notes,
    string? HealthNotes,
    string? LastPatchTestOn,
    bool MarketingConsent,
    int? DepositPercent = null) : ICommand<bool>
{
    /// <summary>Keeps contact details and health notes out of any log line that prints the request.</summary>
    public override string ToString() => $"{nameof(EditClientCommand)} {{ Id = {Id} }}";
}