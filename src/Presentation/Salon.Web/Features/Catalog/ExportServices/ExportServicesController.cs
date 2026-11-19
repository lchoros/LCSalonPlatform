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

namespace Salon.Web.Features.Catalog.ExportServices;

/// <summary>
/// Every service of the salon as the price list reads (ADR 0066): under its heading, in the salon's order. A query:
/// no transaction, tenant required.
/// </summary>
public sealed record ExportServicesQuery : IQuery<PriceList>;

/// <param name="Day">Today in the salon's zone: the day the prices are of.</param>
public sealed record PriceList(LocalDate Day, IReadOnlyList<PriceListLine> Lines);

/// <param name="Category">The heading the service is listed under; null for a service without one.</param>
/// <param name="Name">The variant group for a variant ("Keratin"), the full name otherwise: with <paramref name="Variant"/> it reads back as the same service (ADR 0073).</param>
/// <param name="Variant">What tells a variant from its siblings ("Long hair"); null for a service without variants.</param>
/// <param name="DepositPercent">The service's own deposit; null follows the salon's rule.</param>
public sealed record PriceListLine(string? Category, string Name, string? Variant, int Minutes, decimal Price, decimal? DepositPercent, bool InUse);

/// <summary>Reads through the tenant filter only: a salon has tens or a few hundred services.</summary>
public sealed class ExportServicesHandler(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IClock clock) : IRequestHandler<ExportServicesQuery, PriceList>
{
    public async Task<PriceList> HandleAsync(ExportServicesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The tenant guard has already refused tenant-less requests; this only narrows the type.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(ExportServicesQuery)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc;

        var categories = await db.ServiceCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => new { c.Name, c.SortOrder }, cancellationToken);
        var services = await db.Services.AsNoTracking().ToListAsync(cancellationToken);

        // The salon's own order: headings as it sorted them, services without a heading last, then the service's place.
        var lines = services
            .Select(s => new { Service = s, Category = s.CategoryId is { } id ? categories.GetValueOrDefault(id) : null })
            .OrderBy(x => x.Category is null)
            .ThenBy(x => x.Category?.SortOrder)
            .ThenBy(x => x.Category?.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Service.SortOrder)
            .ThenBy(x => x.Service.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Service.Id)
            .Select(x => new PriceListLine(x.Category?.Name, x.Service.VariantGroup ?? x.Service.Name, x.Service.VariantLabel, (int)x.Service.Duration.TotalMinutes, x.Service.Price, x.Service.DepositPercent, x.Service.IsActive))
            .ToArray();
        return new PriceList(clock.GetCurrentInstant().InZone(zone).Date, lines);
    }
}

/// <summary>
/// <c>GET /admin/services/price-list.csv</c> (ADR 0066): the salon's price list as a CSV file. Tenant only, behind
/// <see cref="Permissions.CatalogManage"/> like the import, whose headings these are: the owner changes times and
/// prices in a spreadsheet and reads the file back, here or in another salon. Services the salon stopped using are
/// in the file too, marked so.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.CatalogManage)]
[Route("admin/services")]
public sealed class ExportServicesController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    private static readonly string[] Columns = ["Category", "Service", "Variant", "Minutes", "Price", "Deposit", "InUse"];

    [HttpGet("price-list.csv")]
    public async Task<IActionResult> Download(CancellationToken cancellationToken)
    {
        var list = await dispatcher.SendAsync(new ExportServicesQuery(), cancellationToken);
        var culture = CultureInfo.CurrentCulture;
        var headers = Columns.Select(column => localizer[$"Catalog.Export.Column.{column}"]).ToArray();
        var rows = list.Lines.Select(line => (IReadOnlyList<CsvValue>)
        [
            CsvValue.Text(line.Category),
            CsvValue.Text(line.Name),
            CsvValue.Text(line.Variant),
            CsvValue.Number(line.Minutes.ToString(CultureInfo.InvariantCulture)),
            // No thousands separator: a number, not a label.
            CsvValue.Number(line.Price.ToString("0.00", culture.NumberFormat)),
            // Empty for a service that follows the salon's rule, so reading the file back leaves it following.
            CsvValue.Number(line.DepositPercent?.ToString("0.##", culture.NumberFormat) ?? string.Empty),
            CsvValue.Text(localizer[line.InUse ? "Catalog.Export.Yes" : "Catalog.Export.No"]),
        ]);

        return CsvDownload($"price-list-{LocalDatePattern.Iso.Format(list.Day)}.csv", headers, rows);
    }
}