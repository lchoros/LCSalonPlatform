using NodaTime;

namespace Salon.Services.Catalog;

/// <summary>
/// Answers "what does this person offer for this service" (ADR 0018) for the current tenant: the service defaults unless a
/// <see cref="Core.Catalog.StaffServiceOverride"/> says otherwise. The availability engine takes the duration per person,
/// the booking service snapshots duration and price from it, and both booking pages show it. Everything reads through the
/// tenant filter, so another salon's overrides never shape the answer.
/// </summary>
public interface IServiceOfferingResolver
{
    /// <summary>The effective offering of one staff member. Throws <see cref="InvalidOperationException"/> when the salon has no such service.</summary>
    Task<ServiceOffering> ResolveAsync(Guid serviceId, Guid staffMemberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The effective offering of each of <paramref name="staffMemberIds"/>, keyed by staff member (people without an
    /// override get the service defaults). Throws <see cref="InvalidOperationException"/> when the salon has no such service.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, ServiceOffering>> ResolveAsync(Guid serviceId, IReadOnlyCollection<Guid> staffMemberIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// For each known service id: the cheapest and dearest price among the active, bookable staff who offer it, or the
    /// service price when nobody has an override (or there is no staff). Ids the salon does not have are left out.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, PriceSpan>> PriceSpansAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken = default);
}

/// <summary>What one person offers for one service: the effective values, whether they offer it at all, and whether a row changed the defaults.</summary>
public sealed record ServiceOffering(Guid ServiceId, Guid StaffMemberId, bool Offers, Duration Duration, decimal Price, string Currency, bool HasOverride);

/// <summary>The cheapest and dearest effective price of a service across the staff who offer it; equal when there is one price.</summary>
public readonly record struct PriceSpan(decimal Min, decimal Max)
{
    /// <summary>True when the public list should say "from <see cref="Min"/>".</summary>
    public bool IsRange => Min != Max;
}
