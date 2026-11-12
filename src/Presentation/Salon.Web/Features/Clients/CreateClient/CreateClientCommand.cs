using Salon.Core.Cqrs;

namespace Salon.Web.Features.Clients.CreateClient;

/// <summary>
/// Adds one client to the current salon and returns its id. Bound straight from the admin form; <see cref="CreateClientValidator"/>
/// holds the field rules, <see cref="CreateClientPhoneValidator"/> the tenant-aware phone rule, and the client service
/// normalises, encrypts the health notes and refuses duplicates.
/// </summary>
/// <param name="Phone">As typed; local numbers resolve with the salon's country.</param>
/// <param name="HealthNotes">Plain text for this request only; stored encrypted (ADR 0011).</param>
/// <param name="LastPatchTestOn">ISO date (<c>yyyy-MM-dd</c>, what a date input posts) or blank.</param>
/// <param name="MarketingConsent">The tick; the wording recorded is the label shown next to it.</param>
public sealed record CreateClientCommand(
    string FirstName,
    string LastName,
    string? Phone,
    string? Email,
    string? Notes,
    string? HealthNotes,
    string? LastPatchTestOn,
    bool MarketingConsent) : ICommand<Guid>
{
    /// <summary>Keeps contact details and health notes out of any log line that prints the request.</summary>
    public override string ToString() => $"{nameof(CreateClientCommand)} {{ FirstName = {FirstName}, LastName = {LastName} }}";
}