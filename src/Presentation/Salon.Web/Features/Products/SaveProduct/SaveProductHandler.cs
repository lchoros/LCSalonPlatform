using Microsoft.EntityFrameworkCore;
using Salon.Core.Cqrs;
using Salon.Core.Products;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Products;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.Products.SaveProduct;

/// <summary>
/// Runs inside the pipeline's transaction (ADR 0003): a new product and its opening count commit together or not at
/// all. The price is in the tenant's currency. The shelf's total is never set here; an opening count goes through
/// <see cref="IStockService"/> like every other change, so it has its line in the history.
/// </summary>
public sealed class SaveProductHandler(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IStockService stock) : IRequestHandler<SaveProductCommand, Guid?>
{
    public async Task<Guid?> HandleAsync(SaveProductCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Id is { } id)
        {
            var existing = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (existing is null)
            {
                return null;
            }

            Apply(existing, request);
            await db.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        // The tenant guard has already refused tenant-less requests; this only narrows the type.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(SaveProductCommand)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");

        var product = new Product { Name = string.Empty, Currency = tenant.Currency };
        Apply(product, request);
        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);

        if (request.OpeningStock is > 0)
        {
            await stock.RecordAsync(product.Id, StockMovementKind.Counted, request.OpeningStock.Value, null, request.ByUserId, cancellationToken);
        }

        return product.Id;
    }

    private static void Apply(Product product, SaveProductCommand request)
    {
        product.Name = ProductRules.NormalizeName(request.Name);
        product.Brand = ProductRules.NormalizeOptional(request.Brand);
        product.Code = ProductRules.NormalizeOptional(request.Code);
        product.RetailPrice = request.RetailPrice;
        product.CostPrice = request.CostPrice;
        product.LowStockAt = request.LowStockAt;
        product.IsActive = request.IsActive;
    }
}

/// <summary>Reads through the tenant filter, so another salon's id finds nothing.</summary>
public sealed class GetProductForEditHandler(SalonDbContext db) : IRequestHandler<GetProductForEditQuery, Product?>
{
    public async Task<Product?> HandleAsync(GetProductForEditQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);
    }
}