using Salon.Core.Locations;

namespace Salon.Services.Locations;

/// <summary>
/// Where the salon is and how to reach it (ADR 0027). A salon starts with one location, its main one; clients see it
/// when they book and in their emails. Nothing here is personal data: it is the business's public address.
/// </summary>
public interface ILocationService
{
    /// <summary>The salon's main location, or null while the salon has not said where it is.</summary>
    Task<SalonLocation?> GetMainAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the main location or replaces its details. Text is trimmed and blank optional fields become null.
    /// Throws <see cref="ArgumentException"/> when a required line is blank, something is too long or the phone number
    /// is not one (<see cref="LocationRules.IsPhone"/>).
    /// </summary>
    Task<SalonLocation> SaveMainAsync(LocationDetails details, CancellationToken cancellationToken = default);
}

/// <summary>What the salon enters about a location.</summary>
public sealed record LocationDetails(string? Name, string? AddressLine1, string? AddressLine2, string? City, string? Postcode, string? Phone, string? Email);

/// <param name="Address">On one line, in the order the salon's country writes it.</param>
public sealed record SalonLocation(
    Guid Id,
    string? Name,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string Postcode,
    string? Phone,
    string? Email,
    string Address)
{
    /// <summary>Opens the address in the reader's maps app.</summary>
    public string MapUrl => LocationRules.MapUrl(Address);

    /// <summary>The phone number as a <c>tel:</c> address; null without a number.</summary>
    public string? PhoneLink => LocationRules.Dialable(Phone) is { Length: > 0 } dialable ? "tel:" + dialable : null;
}