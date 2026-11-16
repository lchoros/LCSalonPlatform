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

namespace Salon.Web.Features.Reports.ExportMoney;

/// <summary>
/// The money lines of a period (ADR 0042), one row per line, for the salon's own books or its accountant. A query: no
/// transaction, tenant required.
/// </summary>
public sealed record ExportMoneyQuery(LocalDate? From, LocalDate? To) : IQuery<MoneyJournal>;

public sealed class ExportMoneyHandler(IReportService reports) : IRequestHandler<ExportMoneyQuery, MoneyJournal>
{
    public Task<MoneyJournal> HandleAsync(ExportMoneyQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return reports.GetMoneyLinesAsync(null, request.From, request.To, cancellationToken);
    }
}

/// <summary>
/// <c>GET /admin/reports/money.csv?from=YYYY-MM-DD&amp;to=YYYY-MM-DD</c> (ADR 0042): every line of the period that
/// moved money as a CSV file a spreadsheet opens. Tenant only, behind the report's own permissions
/// (<see cref="Permissions.ReportsView"/> and <see cref="Permissions.ClientsView"/>: the rows name clients). The file
/// is written for the reader: column names, the kind of line and the payment method in their language, the list
/// separator and the decimal sign of their spreadsheet, UTF-8 with a byte order mark so Excel reads the letters.
/// Dates that cannot be read give this month, as on the report page.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.ReportsView)]
[RequirePermission(Permissions.ClientsView)]
[Route("admin/reports")]
public sealed class ExportMoneyController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    private static readonly string[] Columns = ["Date", "Time", "Client", "Kind", "Service", "Method", "Amount", "Currency", "RecordedBy", "Reference"];

    [HttpGet("money.csv")]
    public async Task<IActionResult> Money(string? from, string? to, CancellationToken cancellationToken)
    {
        var journal = await dispatcher.SendAsync(new ExportMoneyQuery(ParseDate(from), ParseDate(to)), cancellationToken);
        var culture = CultureInfo.CurrentCulture;
        var headers = Columns.Select(column => localizer[$"Reports.Export.Column.{column}"]).ToArray();
        var rows = journal.Lines.Select(line => (IReadOnlyList<CsvValue>)
        [
            CsvValue.Text(LocalDatePattern.Iso.Format(line.At.Date)),
            CsvValue.Text(line.At.TimeOfDay.ToString("HH:mm", CultureInfo.InvariantCulture)),
            CsvValue.Text(line.ClientName),
            CsvValue.Text(localizer[$"Payments.Kind.{line.Kind}"]),
            CsvValue.Text(line.ServiceName),
            CsvValue.Text(localizer[$"Payments.Method.{line.Method}"]),
            // A plain minus and no thousands separator: a number, not a label.
            CsvValue.Number(line.Amount.Amount.ToString("0.00", culture.NumberFormat)),
            CsvValue.Text(line.Amount.Currency),
            CsvValue.Text(line.RecordedBy),
            CsvValue.Text(line.Reference),
        ]);

        var name = $"money-{LocalDatePattern.Iso.Format(journal.Period.From)}-to-{LocalDatePattern.Iso.Format(journal.Period.To)}.csv";
        return CsvDownload(name, headers, rows);
    }

    private static LocalDate? ParseDate(string? date)
        => date is { Length: > 0 } && LocalDatePattern.Iso.Parse(date.Trim()) is { Success: true } parsed ? parsed.Value : null;
}