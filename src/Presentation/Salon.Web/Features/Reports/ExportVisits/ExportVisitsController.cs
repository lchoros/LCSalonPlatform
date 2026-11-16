using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Reports;
using Salon.Services.Localization;
using Salon.Services.Reports;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Reports.ExportVisits;

/// <summary>
/// The visits of a period (ADR 0047), one row per visit, whatever became of it. A query: no transaction, tenant
/// required.
/// </summary>
public sealed record ExportVisitsQuery(LocalDate? From, LocalDate? To) : IQuery<VisitJournal>;

public sealed class ExportVisitsHandler(IReportService reports) : IRequestHandler<ExportVisitsQuery, VisitJournal>
{
    public Task<VisitJournal> HandleAsync(ExportVisitsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return reports.GetVisitLinesAsync(null, request.From, request.To, cancellationToken);
    }
}

/// <summary>
/// <c>GET /admin/reports/visits.csv?from=YYYY-MM-DD&amp;to=YYYY-MM-DD</c> (ADR 0047): every visit that starts in the
/// period as a CSV file a spreadsheet opens, written like the money file (ADR 0042). Tenant only, behind the report's
/// own permissions (<see cref="Permissions.ReportsView"/> and <see cref="Permissions.ClientsView"/>: the rows name
/// clients). No phone numbers and no notes. Dates that cannot be read give this month, as on the report page.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.ReportsView)]
[RequirePermission(Permissions.ClientsView)]
[Route("admin/reports")]
public sealed class ExportVisitsController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    private static readonly string[] Columns = ["Date", "Time", "Client", "Service", "With", "Minutes", "Status", "BookedBy", "Price", "Currency"];

    [HttpGet("visits.csv")]
    public async Task<IActionResult> Visits(string? from, string? to, CancellationToken cancellationToken)
    {
        var journal = await dispatcher.SendAsync(new ExportVisitsQuery(ParseDate(from), ParseDate(to)), cancellationToken);
        var culture = CultureInfo.CurrentCulture;
        var headers = Columns.Select(column => localizer[$"Reports.Export.Column.{column}"]).ToArray();
        var rows = journal.Lines.Select(line => (IReadOnlyList<CsvValue>)
        [
            CsvValue.Text(LocalDatePattern.Iso.Format(line.Start.Date)),
            CsvValue.Text(line.Start.TimeOfDay.ToString("HH:mm", CultureInfo.InvariantCulture)),
            CsvValue.Text(line.ClientName),
            CsvValue.Text(line.ServiceName),
            CsvValue.Text(line.StaffName),
            CsvValue.Number(line.Minutes.ToString(CultureInfo.InvariantCulture)),
            CsvValue.Text(localizer[$"Appointments.Status.{line.Status}"]),
            CsvValue.Text(localizer[$"Reports.Export.BookedBy.{line.Source}"]),
            // No thousands separator: a number, not a label.
            CsvValue.Number(line.Price.Amount.ToString("0.00", culture.NumberFormat)),
            CsvValue.Text(line.Price.Currency),
        ]);

        var name = $"visits-{LocalDatePattern.Iso.Format(journal.Period.From)}-to-{LocalDatePattern.Iso.Format(journal.Period.To)}.csv";
        return CsvDownload(name, headers, rows);
    }

    private static LocalDate? ParseDate(string? date)
        => date is { Length: > 0 } && LocalDatePattern.Iso.Parse(date.Trim()) is { Success: true } parsed ? parsed.Value : null;
}