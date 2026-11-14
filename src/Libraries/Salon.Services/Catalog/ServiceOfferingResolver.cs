using Microsoft.EntityFrameworkCore;
using Salon.Core.Catalog;
using Salon.Data;

namespace Salon.Services.Catalog;

/// <summary>
/// Loads the service and its override rows through the tenant filter and lets the pure <see cref="Resolve"/> and
/// <see cref="PriceSpanFor"/> decide (ADR 0018). The static halves are unit-tested; the loading half is covered by the
/// integration tests.
/// </summary>
internal sealed class ServiceOfferingResolver(SalonDbContext db) : IServiceOfferingResolver
{
    public async Task<ServiceOffering> ResolveAsync(Guid serviceId, Guid staffMemberId, CancellationToken cancellationToken = default)
        => (await ResolveAsync(serviceId, [staffMemberId], cancellationToken).ConfigureAwait(false))[staffMemberId];

    public async Task<IReadOnlyDictionary<Guid, ServiceOffering>> ResolveAsync(Guid serviceId, IReadOnlyCollection<Guid> staffMemberIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(staffMemberIds);

        var service = await db.Services.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == serviceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Service {serviceId} does not exist in this salon.");
        var ids = staffMemberIds.Distinct().ToArray();
        var overrides = ids.Length == 0
            ? []
            : await db.StaffServiceOverrides.AsNoTracking()
                .Where(o => o.ServiceId == serviceId && ids.Contains(o.StaffMemberId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        var byStaff = overrides.ToDictionary(o => o.StaffMemberId);

        return ids.ToDictionary(id => id, id => Resolve(service, id, byStaff.GetValueOrDefault(id)));
    }

    public async Task<IReadOnlyDictionary<Guid, PriceSpan>> PriceSpansAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceIds);
        var ids = serviceIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, PriceSpan>();
        }

        var services = await db.Services.AsNoTracking().Where(s => ids.Contains(s.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var staffIds = await db.StaffMembers.AsNoTracking()
            .Where(s => s.IsActive && s.IsBookable)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var overrides = await db.StaffServiceOverrides.AsNoTracking()
            .Where(o => ids.Contains(o.ServiceId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return services.ToDictionary(s => s.Id, s => PriceSpanFor(s, staffIds, overrides));
    }

    /// <summary>The pure rule: the override (which must be for this very pair) wins field by field over the service.</summary>
    public static ServiceOffering Resolve(Service service, Guid staffMemberId, StaffServiceOverride? staffOverride)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (staffOverride is not null && (staffOverride.ServiceId != service.Id || staffOverride.StaffMemberId != staffMemberId))
        {
            throw new ArgumentException("The override belongs to another staff member or service.", nameof(staffOverride));
        }

        return new ServiceOffering(
            service.Id,
            staffMemberId,
            ServiceRules.IsOffered(staffOverride),
            ServiceRules.EffectiveDuration(service, staffOverride),
            ServiceRules.EffectivePrice(service, staffOverride),
            service.Currency,
            staffOverride is not null);
    }

    /// <summary>
    /// Min and max effective price over <paramref name="staffMemberIds"/> who offer the service (people without a row pay the
    /// default); the service price alone when nobody in the list offers it. Rows for other services or people are ignored.
    /// </summary>
    public static PriceSpan PriceSpanFor(Service service, IReadOnlyCollection<Guid> staffMemberIds, IEnumerable<StaffServiceOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(staffMemberIds);
        ArgumentNullException.ThrowIfNull(overrides);

        var byStaff = overrides.Where(o => o.ServiceId == service.Id).ToDictionary(o => o.StaffMemberId);
        var prices = staffMemberIds
            .Select(id => byStaff.GetValueOrDefault(id))
            .Where(ServiceRules.IsOffered)
            .Select(o => ServiceRules.EffectivePrice(service, o))
            .ToList();

        return prices.Count == 0 ? new PriceSpan(service.Price, service.Price) : new PriceSpan(prices.Min(), prices.Max());
    }
}