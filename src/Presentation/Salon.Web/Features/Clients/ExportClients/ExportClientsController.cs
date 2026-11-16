using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Appointments;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Reports;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Localization;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Clients.ExportClients;

/// <summary>
/// Every client of the salon with how to reach them and how often they came (ADR 0051), by name. Cards merged into
/// another are left out: they are pointers, not clients. A query: no transaction, tenant required.
/// </summary>
public sealed record ExportClientsQuery : IQuery<ClientFile>;

/// <param name="Day">Today in the salon's zone: the day the file is of.</param>
public sealed record ClientFile(LocalDate Day, IReadOnlyList<ClientFileLine> Lines);

/// <param name="ConsentOn">The day marketing consent was given; null without consent.</param>
/// <param name="LastVisit">The day of the latest completed visit.</param>
/// <param name="NextVisit">The day of the next visit that still stands.</param>
public sealed record ClientFileLine(
    string FirstName,
    string LastName,
    string? Phone,
    string? Email,
    string? Notes,
    bool MarketingConsent,
    LocalDate? ConsentOn,
    LocalDate Since,
    int CompletedVisits,
    LocalDate? LastVisit,
    LocalDate? NextVisit,
    bool IsActive);

/// <summary>
/// Reads through the tenant filter only. Health notes are never read here: they are special-category data, stay
/// encrypted on the client's card and are in no list, search or file (ADR 0011).
/// </summary>
public sealed class ExportClientsHandler(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IClock clock) : IRequestHandler<ExportClientsQuery, ClientFile>
{
    public async Task<ClientFile> HandleAsync(ExportClientsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The tenant guard has already refused tenant-less requests; this only narrows the type.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(ExportClientsQuery)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc;
        var now = clock.GetCurrentInstant();

        var clients = await db.Clients.AsNoTracking()
            .Where(c => c.MergedIntoClientId == null)
            .Select(c => new { c.Id, c.FirstName, c.LastName, c.Phone, c.Email, c.Notes, c.MarketingConsent, c.MarketingConsentAt, c.CreatedAt, c.IsActive })
            .ToListAsync(cancellationToken);
        var active = AppointmentStateMachine.Active.ToArray();
        var visits = await db.Appointments.AsNoTracking()
            .Where(a => a.Status == AppointmentStatus.Completed || (active.Contains(a.Status) && a.Start >= now))
            .Select(a => new { a.ClientId, a.Status, a.Start })
            .ToListAsync(cancellationToken);
        var byClient = visits.ToLookup(v => v.ClientId);

        var lines = clients
            .OrderBy(c => c.LastName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(c => c.FirstName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(c => c.Id)
            .Select(c =>
            {
                var completed = byClient[c.Id].Where(v => v.Status == AppointmentStatus.Completed).Select(v => v.Start).ToArray();
                var ahead = byClient[c.Id].Where(v => v.Status != AppointmentStatus.Completed).Select(v => v.Start).ToArray();
                return new ClientFileLine(
                    c.FirstName,
                    c.LastName,
                    c.Phone,
                    c.Email,
                    c.Notes,
                    c.MarketingConsent,
                    c.MarketingConsent && c.MarketingConsentAt is { } consentAt ? consentAt.InZone(zone).Date : null,
                    c.CreatedAt.InZone(zone).Date,
                    completed.Length,
                    completed.Length > 0 ? completed.Max().InZone(zone).Date : null,
                    ahead.Length > 0 ? ahead.Min().InZone(zone).Date : null,
                    c.IsActive);
            })
            .ToArray();
        return new ClientFile(now.InZone(zone).Date, lines);
    }
}

/// <summary>
/// <c>GET /admin/clients/export.csv</c> (ADR 0051): the salon's client list as a CSV file, written like the money
/// file (ADR 0042). The salon owns its data, so the owner can take it away whenever they like. Tenant only, and for
/// the owner only (<see cref="Permissions.SettingsManage"/> with <see cref="Permissions.ClientsManage"/>): the list
/// of clients with their numbers is what a salon is worth. Each download is written to the log with who took it and
/// how many rows, never with what is in them. The headings are ones the client import reads (ADR 0019).
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.SettingsManage)]
[RequirePermission(Permissions.ClientsManage)]
[Route("admin/clients")]
public sealed class ExportClientsController(IDispatcher dispatcher, ILocalizationService localizer, ILogger<ExportClientsController> logger) : SalonControllerBase
{
    private static readonly string[] Columns =
        ["FirstName", "LastName", "Phone", "Email", "Notes", "MarketingConsent", "ConsentOn", "Since", "CompletedVisits", "LastVisit", "NextVisit", "Active"];

    [HttpGet("export.csv")]
    public async Task<IActionResult> Clients(CancellationToken cancellationToken)
    {
        var file = await dispatcher.SendAsync(new ExportClientsQuery(), cancellationToken);
        var headers = Columns.Select(column => localizer[$"Clients.Export.Column.{column}"]).ToArray();
        var rows = file.Lines.Select(line => (IReadOnlyList<CsvValue>)
        [
            CsvValue.Text(line.FirstName),
            CsvValue.Text(line.LastName),
            // A number that starts with "+" is text to a person and a formula to a spreadsheet: Text makes it plain.
            CsvValue.Text(line.Phone),
            CsvValue.Text(line.Email),
            CsvValue.Text(line.Notes),
            CsvValue.Text(YesNo(line.MarketingConsent)),
            CsvValue.Text(Day(line.ConsentOn)),
            CsvValue.Text(Day(line.Since)),
            CsvValue.Number(line.CompletedVisits.ToString(CultureInfo.InvariantCulture)),
            CsvValue.Text(Day(line.LastVisit)),
            CsvValue.Text(Day(line.NextVisit)),
            CsvValue.Text(YesNo(line.IsActive)),
        ]);

        logger.LogInformation("Client file downloaded by user {UserId}: {Count} clients", User.FindFirstValue(UserClaimTypes.Subject), file.Lines.Count);
        return CsvDownload($"clients-{LocalDatePattern.Iso.Format(file.Day)}.csv", headers, rows);
    }

    private string YesNo(bool value) => localizer[value ? "Clients.Export.Yes" : "Clients.Export.No"];

    private static string Day(LocalDate? day) => day is { } value ? LocalDatePattern.Iso.Format(value) : string.Empty;
}