using Salon.Api.Contracts.Booking;
using Salon.Core.Appointments;
using Salon.Core.Catalog;
using Salon.Services.Catalog;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.Api.Booking.ListServices;

/// <summary>The catalogue's entries as the app's list: one item per service, the options of one service sharing their group's title.</summary>
public static class ServiceListMapper
{
    /// <param name="severalServices">Whether a booking here may hold more than one service (<c>IVisitBasket.IsOpen</c>, ADR 0095).</param>
    public static ServiceList Map(TenantInfo tenant, bool severalServices, IReadOnlyList<CatalogueEntry> entries, IReadOnlyDictionary<Guid, PriceSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(spans);
        var salon = new SalonInfo(tenant.Name, tenant.Culture, tenant.Currency, tenant.TimeZoneId, severalServices, severalServices ? VisitRules.MaxServices : 1);
        return new ServiceList(salon, [.. entries.SelectMany(e => e.Services.Select(s => Item(e, s, spans)))]);
    }

    private static ServiceItem Item(CatalogueEntry entry, Service service, IReadOnlyDictionary<Guid, PriceSpan> spans)
    {
        var span = spans.GetValueOrDefault(service.Id, new PriceSpan(service.Price, service.Price));
        return new ServiceItem(
            service.Id,
            service.Name,
            service.Description,
            (int)service.Duration.TotalMinutes,
            span.Min,
            span.IsRange,
            entry.Category?.Name,
            entry.IsVariantGroup ? entry.Title : null,
            entry.IsVariantGroup ? service.VariantLabel : null,
            service.IsPopular);
    }
}