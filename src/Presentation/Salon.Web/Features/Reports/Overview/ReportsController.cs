using Microsoft.AspNetCore.Mvc;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Reports;
using Salon.Services.Reports;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Reports.Overview;

/// <summary>
/// <c>/admin/reports?period=this-month</c> or <c>?from=YYYY-MM-DD&amp;to=YYYY-MM-DD</c> (ADR 0026): takings, visits,
/// no-shows, top services, staff utilisation and completed visits nobody took payment for. Read only. Tenant only,
/// behind <see cref="Permissions.ReportsView"/> (Owner and Manager) and <see cref="Permissions.ClientsView"/> (the
/// unpaid list names clients). An address that cannot be read shows this month rather than failing a bookmark.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.ReportsView)]
[RequirePermission(Permissions.ClientsView)]
[Route("admin/reports")]
public sealed class ReportsController(IDispatcher dispatcher) : SalonControllerBase
{
    public const string Path = "/admin/reports";

    /// <summary>The till of one day; linked by address because a slice never references another slice.</summary>
    public const string TillPath = "/admin/payments/cash-up";

    public const string CheckoutPath = "/admin/payments/checkout/";

    /// <summary>The period's money lines as a CSV file (ADR 0042); the ExportMoney slice owns the route.</summary>
    public static string MoneyExportPath(ReportPeriod period)
        => $"{Path}/money.csv?from={LocalDatePattern.Iso.Format(period.From)}&to={LocalDatePattern.Iso.Format(period.To)}";

    /// <summary>The period's visits as a CSV file (ADR 0047); the ExportVisits slice owns the route.</summary>
    public static string VisitsExportPath(ReportPeriod period)
        => $"{Path}/visits.csv?from={LocalDatePattern.Iso.Format(period.From)}&to={LocalDatePattern.Iso.Format(period.To)}";

    private static readonly Dictionary<string, ReportPreset> Presets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["this-week"] = ReportPreset.ThisWeek,
        ["last-week"] = ReportPreset.LastWeek,
        ["this-month"] = ReportPreset.ThisMonth,
        ["last-month"] = ReportPreset.LastMonth,
    };

    [HttpGet("")]
    public async Task<IActionResult> Index(string? period, string? from, string? to, CancellationToken cancellationToken)
    {
        ReportPreset? preset = period is { Length: > 0 } && Presets.TryGetValue(period.Trim(), out var known) ? known : null;
        var report = await dispatcher.SendAsync(new ReportQuery(preset, ParseDate(from), ParseDate(to)), cancellationToken);
        return View(report);
    }

    /// <summary>The address of a one-tap period.</summary>
    public static string PresetPath(ReportPreset preset) => Path + "?period=" + Presets.First(p => p.Value == preset).Key;

    private static LocalDate? ParseDate(string? date)
        => date is { Length: > 0 } && LocalDatePattern.Iso.Parse(date.Trim()) is { Success: true } parsed ? parsed.Value : null;
}