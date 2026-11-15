using Microsoft.EntityFrameworkCore;
using Salon.Core.Catalog;
using Salon.Core.Cqrs;
using Salon.Core.Domain;
using Salon.Core.Paging;
using Salon.Data;

namespace Salon.Web.Features.Booking.ChooseService;

/// <summary>
/// Reads through the tenant filter only; inactive services are not bookable and stay out. Filtering, grouping into
/// entries and paging happen in memory with <see cref="ServiceCatalogue"/>: a salon has tens of services, not thousands.
/// </summary>
public sealed class ChooseServiceHandler(SalonDbContext db) : IRequestHandler<ChooseServiceQuery, ServiceChoice>
{
    /// <summary>The number of accent colours the stylesheet defines (<c>.accent-1</c> to <c>.accent-5</c>).</summary>
    public const int Accents = 5;

    public async Task<ServiceChoice> HandleAsync(ChooseServiceQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var services = await db.Services.AsNoTracking().Where(s => s.IsActive).ToListAsync(cancellationToken);
        var categories = await db.ServiceCategories.AsNoTracking().ToListAsync(cancellationToken);

        // Categories in use, in display order; each keeps its colour whatever the filter, so a tile and its cards match.
        var inUse = ServiceGrouping.Group(services, categories)
            .Where(g => g.Category is not null)
            .Select((g, index) => new BookableCategory(g.Category!.Id, g.Category.Name, (index % Accents) + 1))
            .ToList();
        var accents = inUse.ToDictionary(c => c.Id, c => c.Accent);

        var categoryId = request.CategoryId is { } wanted && accents.ContainsKey(wanted) ? wanted : (Guid?)null;
        var search = string.IsNullOrWhiteSpace(request.Search) ? null : string.Join(' ', ServiceCatalogue.Words(request.Search));
        var entries = ServiceCatalogue.Entries(services, categories, categoryId, search)
            .Select(e => new BookableEntry(
                e.Category?.Name,
                e.Category is { } category ? accents[category.Id] : 0,
                e.Title,
                e.IsVariantGroup,
                e.Services.Select(s => new BookableService(s.Id, e.IsVariantGroup ? s.VariantLabel ?? s.Name : s.Name, s.Description, s.Duration, new Money(s.Price, s.Currency))).ToList(),
                new Money(e.MinPrice, e.Services[0].Currency),
                new Money(e.MaxPrice, e.Services[0].Currency)))
            .ToList();

        return new ServiceChoice(Pages.Cut(entries, request.Page, ServiceChoice.PageSize), inUse, categoryId, search, services.Count);
    }
}