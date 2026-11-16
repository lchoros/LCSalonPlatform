using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Settings.YourData;

/// <summary>What the salon has in the system and the years it has visits or money in (ADR 0051). A query: no transaction, tenant required.</summary>
public sealed record YourDataQuery : IQuery<YourDataPage>;

/// <param name="Clients">Client cards, merged pointers left out.</param>
/// <param name="Years">Every year from the salon's first visit or payment to this one, newest first.</param>
public sealed record YourDataPage(int Clients, int Visits, int MoneyLines, int Products, IReadOnlyList<int> Years)
{
    /// <summary>A salon rarely needs more than these; older years are still reachable from the reports with dates.</summary>
    public const int MaxYears = 10;
}

/// <summary>Reads through the tenant filter only: four counts and two earliest moments.</summary>
public sealed class YourDataHandler(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IClock clock) : IRequestHandler<YourDataQuery, YourDataPage>
{
    public async Task<YourDataPage> HandleAsync(YourDataQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The tenant guard has already refused tenant-less requests; this only narrows the type.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(YourDataQuery)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc;
        var thisYear = clock.GetCurrentInstant().InZone(zone).Year;

        var clients = await db.Clients.CountAsync(c => c.MergedIntoClientId == null, cancellationToken);
        var visits = await db.Appointments.CountAsync(cancellationToken);
        var moneyLines = await db.ClientAccountEntries.CountAsync(e => e.Method != null, cancellationToken);
        var products = await db.Products.CountAsync(cancellationToken);
        var firstVisit = await db.Appointments.OrderBy(a => a.Start).Select(a => (Instant?)a.Start).FirstOrDefaultAsync(cancellationToken);
        var firstMoney = await db.ClientAccountEntries.OrderBy(e => e.At).Select(e => (Instant?)e.At).FirstOrDefaultAsync(cancellationToken);

        var firstYear = new[] { firstVisit, firstMoney }
            .Where(moment => moment is not null)
            .Select(moment => moment!.Value.InZone(zone).Year)
            .DefaultIfEmpty(thisYear)
            .Min();
        var years = Enumerable.Range(0, YourDataPage.MaxYears)
            .Select(back => thisYear - back)
            .Where(year => year >= firstYear)
            .ToArray();
        return new YourDataPage(clients, visits, moneyLines, products, years);
    }
}

/// <summary>
/// <c>/admin/settings/your-data</c> (ADR 0051): everything the salon has typed in, as files, in one place. The salon
/// owns its data; this page is where the owner takes it. Owner only (<see cref="Permissions.SettingsManage"/>). The
/// files themselves belong to other slices and are linked by address.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.SettingsManage)]
[Route("admin/settings/your-data")]
public sealed class YourDataController(IDispatcher dispatcher) : SalonControllerBase
{
    /// <summary>The client list; the ExportClients slice owns the route.</summary>
    public const string ClientsFilePath = "/admin/clients/export.csv";

    /// <summary>The product list with today's counts; the ExportProducts slice owns the route.</summary>
    public const string StockFilePath = "/admin/products/stock.csv";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(await dispatcher.SendAsync(new YourDataQuery(), cancellationToken));

    /// <summary>The visits of one year; the ExportVisits slice owns the route.</summary>
    public static string VisitsFilePath(int year) => $"/admin/reports/visits.csv?from={year:0000}-01-01&to={year:0000}-12-31";

    /// <summary>The money lines of one year; the ExportMoney slice owns the route.</summary>
    public static string MoneyFilePath(int year) => $"/admin/reports/money.csv?from={year:0000}-01-01&to={year:0000}-12-31";
}