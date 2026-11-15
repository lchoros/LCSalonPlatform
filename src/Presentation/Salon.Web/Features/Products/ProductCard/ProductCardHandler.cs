using Microsoft.EntityFrameworkCore;
using NodaTime;
using Salon.Core.Cqrs;
using Salon.Core.Paging;
using Salon.Core.Products;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.Products.ProductCard;

/// <summary>
/// Reads through the tenant filter only: the product, a count of its lines, the lines of one page and the names of
/// the people who added them. Times are shown in the salon's zone.
/// </summary>
public sealed class ProductCardHandler(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup) : IRequestHandler<ProductCardQuery, ProductCard?>
{
    public async Task<ProductCard?> HandleAsync(ProductCardQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);
        if (product is null)
        {
            return null;
        }

        // The tenant guard has already refused tenant-less requests; this only narrows the type.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(ProductCardQuery)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
        var zone = DateTimeZoneProviders.Tzdb[tenant.TimeZoneId];

        var lines = db.StockMovements.AsNoTracking().Where(m => m.ProductId == product.Id);
        var total = await lines.CountAsync(cancellationToken);
        var number = Pages.Clamp(request.Page, total, ProductCard.PageSize);
        var rows = await lines
            .OrderByDescending(m => m.At).ThenByDescending(m => m.Id)
            .Skip((number - 1) * ProductCard.PageSize)
            .Take(ProductCard.PageSize)
            .ToListAsync(cancellationToken);

        var userIds = rows.Where(m => m.ByUserId is not null).Select(m => m.ByUserId!.Value).Distinct().ToArray();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        var history = rows
            .Select(m => new StockLine(
                m.Id,
                m.Kind,
                m.Quantity,
                m.OnHandAfter,
                m.Note,
                m.At.InZone(zone).LocalDateTime,
                m.ByUserId is { } userId && users.TryGetValue(userId, out var name) ? name : null,
                m.Kind == StockMovementKind.Counted && m.AppointmentId is not null))
            .ToList();

        return new ProductCard(
            product.Id,
            product.Name,
            product.Brand,
            product.Code,
            product.Price,
            product.StockOnHand,
            product.LowStockAt,
            product.IsActive && StockRules.IsLow(product.StockOnHand, product.LowStockAt),
            product.IsActive,
            new Paged<StockLine>(history, number, ProductCard.PageSize, total));
    }
}