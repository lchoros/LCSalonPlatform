using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Reports;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Localization;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Products.ExportProducts;

/// <summary>
/// Every product of the salon with what is on the shelf now (ADR 0047), by name. A query: no transaction, tenant
/// required.
/// </summary>
public sealed record ExportProductsQuery : IQuery<StockList>;

/// <param name="Day">Today in the salon's zone: the day the counts are of.</param>
public sealed record StockList(LocalDate Day, IReadOnlyList<StockListLine> Lines);

/// <param name="Price">What a client pays for one; null for a product the salon only uses itself.</param>
public sealed record StockListLine(string Name, string? Brand, string? Code, decimal? Price, string Currency, int OnHand, int? LowStockAt, bool InUse);

/// <summary>Reads through the tenant filter only: a salon has tens or a few hundred products.</summary>
public sealed class ExportProductsHandler(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IClock clock) : IRequestHandler<ExportProductsQuery, StockList>
{
    public async Task<StockList> HandleAsync(ExportProductsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The tenant guard has already refused tenant-less requests; this only narrows the type.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(ExportProductsQuery)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc;

        var products = await db.Products.AsNoTracking().ToListAsync(cancellationToken);
        var lines = products
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.Id)
            .Select(p => new StockListLine(p.Name, p.Brand, p.Code, p.RetailPrice, p.Currency, p.StockOnHand, p.LowStockAt, p.IsActive))
            .ToArray();
        return new StockList(clock.GetCurrentInstant().InZone(zone).Date, lines);
    }
}

/// <summary>
/// <c>GET /admin/products/stock.csv</c> (ADR 0047): the salon's product list with today's counts as a CSV file, for a
/// stocktake on paper, a reorder or the accountant. Tenant only, behind <see cref="Permissions.CatalogManage"/> like
/// the import (ADR 0045), whose headings these are: the file can be read back by the import, here or in another salon.
/// Products the salon stopped using are in the file too, marked so.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.CatalogManage)]
[Route("admin/products")]
public sealed class ExportProductsController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    private static readonly string[] Columns = ["Name", "Brand", "Code", "Price", "Currency", "InStock", "LowStockAt", "InUse"];

    [HttpGet("stock.csv")]
    public async Task<IActionResult> Stock(CancellationToken cancellationToken)
    {
        var list = await dispatcher.SendAsync(new ExportProductsQuery(), cancellationToken);
        var culture = CultureInfo.CurrentCulture;
        var headers = Columns.Select(column => localizer[$"Products.Export.Column.{column}"]).ToArray();
        var rows = list.Lines.Select(line => (IReadOnlyList<CsvValue>)
        [
            CsvValue.Text(line.Name),
            CsvValue.Text(line.Brand),
            CsvValue.Text(line.Code),
            // No thousands separator: a number, not a label. Empty for a product that is not for sale.
            CsvValue.Number(line.Price?.ToString("0.00", culture.NumberFormat) ?? string.Empty),
            CsvValue.Text(line.Currency),
            CsvValue.Number(line.OnHand.ToString(CultureInfo.InvariantCulture)),
            CsvValue.Number(line.LowStockAt?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
            CsvValue.Text(localizer[line.InUse ? "Products.Export.Yes" : "Products.Export.No"]),
        ]);

        return CsvDownload($"stock-{LocalDatePattern.Iso.Format(list.Day)}.csv", headers, rows);
    }
}