using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Salon.Core.Import;
using Salon.Core.Products;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Clients.Import;
using Salon.Services.Tenancy;

namespace Salon.Services.Products.Import;

internal sealed class ProductImportService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IStockService stock,
    ILogger<ProductImportService> logger) : IProductImportService
{
    public async Task<ProductImportPreview> PreviewAsync(ProductImportParseResult parsed, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (!parsed.IsSuccess)
        {
            throw new ArgumentException($"A file refused with {parsed.Error} has nothing to preview.", nameof(parsed));
        }

        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("Previewing a product import requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
        var rows = ProductImportClassifier.Classify(parsed.Rows, await ExistingNamesAsync(cancellationToken).ConfigureAwait(false));
        return new ProductImportPreview(tenantId, fileName.Trim(), tenant.Currency, rows);
    }

    public async Task<ProductImportResult> CommitAsync(ProductImportPreview preview, string stockNote, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentException.ThrowIfNullOrWhiteSpace(stockNote);

        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("Committing a product import requires a tenant in scope.");
        if (preview.TenantId != tenantId)
        {
            throw new TenantViolationException("The import preview belongs to another tenant.");
        }

        // Read the names again: a product added by hand since the preview must not be added twice.
        var taken = await ExistingNamesAsync(cancellationToken).ConfigureAwait(false);
        var duplicates = preview.DuplicateCount;
        var added = new List<(Product Product, int Stock)>();
        foreach (var row in preview.Rows.Where(r => r.Status == ImportRowStatus.New).Select(r => r.Row))
        {
            if (!taken.Add(ProductImportClassifier.NameKey(row.Name)))
            {
                logger.LogInformation("Import row {SourceRow} of {FileName} skipped: the product already exists", row.SourceRow, preview.FileName);
                duplicates++;
                continue;
            }

            var product = new Product
            {
                Name = ProductRules.NormalizeName(row.Name),
                Brand = ProductRules.NormalizeOptional(row.Brand),
                Code = ProductRules.NormalizeOptional(row.Code),
                RetailPrice = row.Price,
                CostPrice = row.Cost,
                Currency = preview.Currency,
                LowStockAt = row.LowStockAt,
            };
            db.Products.Add(product);
            added.Add((product, row.Stock ?? 0));
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The shelf's total is never set directly (ADR 0036): an opening count is a line of the history like any other.
        var note = stockNote.Length <= StockMovement.NoteMaxLength ? stockNote : stockNote[..StockMovement.NoteMaxLength].TrimEnd();
        var units = 0;
        foreach (var (product, opening) in added.Where(a => a.Stock > 0))
        {
            var counted = await stock.RecordAsync(product.Id, StockMovementKind.Counted, opening, note, byUserId, cancellationToken).ConfigureAwait(false);
            if (!counted.IsRecorded)
            {
                // The caller's transaction takes the products back with it; nothing is half imported.
                throw new InvalidOperationException($"The opening count of imported product {product.Id} was not recorded: {counted.Outcome}.");
            }

            units += opening;
        }

        logger.LogInformation("Imported {Created} products from {FileName}; {Duplicates} duplicates, {Invalid} invalid, {Units} units counted in, by user {UserId}", added.Count, preview.FileName, duplicates, preview.InvalidCount, units, byUserId);
        return new ProductImportResult(preview.FileName, added.Count, duplicates, preview.InvalidCount, units);
    }

    /// <summary>Every product name of the salon, active or not: an inactive product still owns its name.</summary>
    private async Task<HashSet<string>> ExistingNamesAsync(CancellationToken cancellationToken)
    {
        var names = await db.Products.AsNoTracking().Select(p => p.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        return names.Select(ProductImportClassifier.NameKey).ToHashSet(StringComparer.Ordinal);
    }
}