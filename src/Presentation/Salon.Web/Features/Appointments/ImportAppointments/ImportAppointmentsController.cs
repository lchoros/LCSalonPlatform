using System.Globalization;
using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using NodaTime;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Import;
using Salon.Core.Reports;
using Salon.Core.Tenancy;
using Salon.Services.Appointments.Import;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Infrastructure;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Appointments.ImportAppointments;

/// <summary>
/// The coming weeks of the old system's diary from a file, at <c>/admin/appointments/import</c> (ADR 0067), in the
/// shape of the other imports: upload, a preview addressed by a server-side token, confirm. The preview says for every
/// row whether it adds a visit, is in the diary already, was cancelled in the old system, or cannot be read in and
/// why, and until when the old system must keep sending its reminders. The visits that will not be brought over can
/// be downloaded as a file. Tenant only, for owners and managers (<see cref="Permissions.BookingsManage"/>,
/// <see cref="Permissions.ClientsView"/> and <see cref="Permissions.ClientsManage"/>), and behind the same switch as
/// the client import it needs first. The token's value carries the tenant; another salon's is a 404.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.BookingsManage)]
[RequirePermission(Permissions.ClientsView)]
[RequirePermission(Permissions.ClientsManage)]
[FeatureGate(nameof(FeaturesOptions.ClientImport))]
[Route("admin/appointments/import")]
public sealed class ImportAppointmentsController(
    IDispatcher dispatcher,
    ITenantContext tenantContext,
    IMemoryCache cache,
    ILocalizationService localizer) : SalonControllerBase
{
    public const string UploadPath = "/admin/appointments/import";

    /// <summary>The day list; the DayList slice owns the route.</summary>
    public const string DiaryPath = "/admin/appointments";

    /// <summary>The client import, the step before this one; the ImportClients slice owns the route.</summary>
    public const string ClientImportPath = "/admin/clients/import";

    /// <summary>The price list from a file; the ImportServices slice owns the route.</summary>
    public const string ServiceImportPath = "/admin/services/import";

    /// <summary>Where a read-in file is taken out again (ADR 0074); the UndoImport slice owns the route.</summary>
    public const string UndoPath = "/admin/appointments/import/undo";

    /// <summary>Rows listed on the preview; the rest is a count, and the file of rows not brought over has them all.</summary>
    public const int PreviewRowLimit = 300;

    /// <summary>The byte cap as shown in messages.</summary>
    public const int MaxMegabytes = (int)(AppointmentImportLimits.MaxBytes / (1024 * 1024));

    /// <summary>Above the cap but still accepted by the request pipeline, so an oversized file gets the localized message instead of a bare 413.</summary>
    private const long RequestLimitBytes = AppointmentImportLimits.MaxBytes + (4 * 1024 * 1024);

    private static readonly string[] Columns = ["Row", "Date", "Time", "Client", "Phone", "Email", "Service", "Staff", "Reason"];

    private TokenStore<AppointmentImportSession> Store => new(cache, "appointment-import:");

    public static string ConfirmPath(string token) => $"{UploadPath}/{token}/confirm";

    /// <summary>The file of the future visits that are not brought over, with the reason for each.</summary>
    public static string NotBroughtPath(string token) => $"{UploadPath}/{token}/not-brought.csv";

    /// <summary>A visit's card; the Calendar slice owns the route.</summary>
    public static string VisitPath(Guid appointmentId) => "/admin/appointments/calendar/" + appointmentId;

    [HttpGet("")]
    public IActionResult Upload() => View(new UploadAppointmentsViewModel(TakeFlash(), null));

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(RequestLimitBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = RequestLimitBytes)]
    public async Task<IActionResult> Upload(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return View(new UploadAppointmentsViewModel(null, localizer["Appointments.Import.Validation.FileRequired"]));
        }

        if (file.Length > AppointmentImportLimits.MaxBytes)
        {
            return View(new UploadAppointmentsViewModel(null, localizer.Format("Appointments.Import.Error.TooLarge", MaxMegabytes)));
        }

        byte[] content;
        using (var buffer = new MemoryStream((int)file.Length))
        {
            await file.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        PreviewAppointmentImportOutcome outcome;
        try
        {
            outcome = await dispatcher.SendAsync(new PreviewAppointmentImportQuery(content, Path.GetFileName(file.FileName)), cancellationToken);
        }
        catch (ValidationException ex)
        {
            return View(new UploadAppointmentsViewModel(null, string.Join(" ", ex.Errors.Select(e => e.ErrorMessage).Distinct(StringComparer.Ordinal))));
        }

        if (outcome.Preview is not { } preview)
        {
            return View(new UploadAppointmentsViewModel(null, ErrorMessage(outcome.Parsed)));
        }

        // [RequireTenant] has already turned a tenant-less request into a 404.
        var token = Store.Save(new AppointmentImportSession(tenantContext.TenantId!.Value, preview, null));
        return RedirectToAction(nameof(Show), new { token });
    }

    [HttpGet("{token:length(64)}")]
    public IActionResult Show(string token)
    {
        if (Session(token) is not { } session)
        {
            return SessionMissing(token);
        }

        return session.Result is { } result
            ? View("Result", new AppointmentImportResultViewModel(token, result))
            : View("Preview", PreviewAppointmentsViewModel.From(token, session.Preview!, PreviewRowLimit));
    }

    [HttpPost("{token:length(64)}/confirm")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(string token, CancellationToken cancellationToken)
    {
        if (Session(token) is not { } session)
        {
            return SessionMissing(token);
        }

        if (session.Preview is not { } preview || session.Result is not null)
        {
            // Already read in (a double submit lands here): show the result again, run nothing.
            return RedirectToAction(nameof(Show), new { token });
        }

        // Spend the token before the file is read in so a second submit meanwhile finds nothing to confirm; a failure puts it back.
        Store.Replace(token, session with { Preview = null });
        AppointmentImportResult result;
        try
        {
            result = await dispatcher.SendAsync(new ConfirmAppointmentImportCommand(preview, CurrentUserId()), cancellationToken);
        }
        catch (AppointmentImportClashException)
        {
            // The command has rolled back: nothing of the file is in the diary. The preview is stale; check again.
            Store.Replace(token, session);
            SetFlash(localizer["Appointments.Import.Clash"]);
            return Redirect(UploadPath);
        }
        catch
        {
            Store.Replace(token, session);
            throw;
        }

        Store.Replace(token, session with { Preview = null, Result = result });
        return RedirectToAction(nameof(Show), new { token });
    }

    /// <summary>The future visits not brought over, as a file the owner can work through or hand on.</summary>
    [HttpGet("{token:length(64)}/not-brought.csv")]
    public IActionResult NotBrought(string token)
    {
        if (Session(token) is not { } session)
        {
            return SessionMissing(token);
        }

        var rows = session.Result?.NotBrought
            ?? [.. session.Preview!.Rows.Where(r => r.Status is AppointmentImportStatus.Invalid or AppointmentImportStatus.CancelledSince)];
        var culture = CultureInfo.CurrentCulture;
        var headers = Columns.Select(column => localizer[$"Appointments.Import.File.Column.{column}"]).ToArray();
        var lines = rows.OrderBy(r => r.Row.SourceRow).Select(r => (IReadOnlyList<CsvValue>)
        [
            CsvValue.Number(r.Row.SourceRow.ToString(CultureInfo.InvariantCulture)),
            CsvValue.Text(r.Row.Start?.Date.ToString("d", culture)),
            CsvValue.Text(r.Row.Start?.TimeOfDay.ToString("HH:mm", CultureInfo.InvariantCulture)),
            CsvValue.Text(r.Row.ClientName),
            CsvValue.Text(r.Row.ClientPhone),
            CsvValue.Text(r.Row.ClientEmail),
            CsvValue.Text(r.Row.Service),
            CsvValue.Text(r.Row.Staff),
            CsvValue.Text(AppointmentImportText.Reasons(localizer, r)),
        ]);

        return CsvDownload("visits-not-brought-over.csv", headers, lines);
    }

    /// <summary>The session of this salon behind a token; null when it expired or is another salon's (told apart by the caller).</summary>
    private AppointmentImportSession? Session(string token) => Store.Find(token) is { } session && session.TenantId == tenantContext.TenantId ? session : null;

    /// <summary>Another salon's token is a 404; an unknown or expired one goes back to the start with the reason.</summary>
    private IActionResult SessionMissing(string token)
    {
        if (Store.Find(token) is not null)
        {
            return NotFound();
        }

        SetFlash(localizer["Appointments.Import.Expired"]);
        return Redirect(UploadPath);
    }

    private string ErrorMessage(AppointmentImportParseResult parsed) => (parsed.Error, parsed.FileError) switch
    {
        (ClientImportError.Empty, _) => localizer["Appointments.Import.Error.Empty"],
        (ClientImportError.TooLarge, _) => localizer.Format("Appointments.Import.Error.TooLarge", MaxMegabytes),
        (ClientImportError.TooManyRows, _) => localizer.Format("Appointments.Import.Error.TooManyRowsRead", AppointmentImportLimits.MaxRowsRead),
        (ClientImportError.NotText, _) => localizer["Appointments.Import.Error.NotText"],
        (_, AppointmentImportFileError.MonthFirstDates) => localizer["Appointments.Import.Error.MonthFirst"],
        (_, AppointmentImportFileError.TooManyFutureRows) => localizer.Format("Appointments.Import.Error.TooManyFutureRows", parsed.FutureCount, AppointmentImportLimits.MaxRows),
        (_, AppointmentImportFileError.MissingColumns) => localizer.Format(
            "Appointments.Import.Error.MissingColumns",
            string.Join(", ", (parsed.Missing ?? []).Select(m => localizer[$"Appointments.Import.Column.{m}"])),
            string.Join(", ", parsed.Header)),
        _ => localizer.Format("Appointments.Import.Error.MissingColumns", string.Empty, string.Join(", ", parsed.Header)),
    };

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}

/// <summary>What a token points at: the classified preview before the confirm, the result after it. The tenant is checked on every use.</summary>
public sealed record AppointmentImportSession(Guid TenantId, AppointmentImportPreview? Preview, AppointmentImportResult? Result)
{
    public override string ToString() => $"{nameof(AppointmentImportSession)} {{ TenantId = {TenantId} }}";
}

/// <summary>Step 1. <see cref="Flash"/> is the message left by an expired token or a clash; <see cref="Error"/> why the last file was refused.</summary>
public sealed record UploadAppointmentsViewModel(string? Flash, string? Error);

/// <summary>Step 2: the counts, the summary of what the salon does not have, and the rows, those that need the owner first.</summary>
public sealed record PreviewAppointmentsViewModel(string Token, AppointmentImportPreview Preview, IReadOnlyList<ClassifiedAppointmentRow> Rows, int HiddenCount, LocalDate? FirstDay, LocalDate? LastDay)
{
    public static PreviewAppointmentsViewModel From(string token, AppointmentImportPreview preview, int rowLimit)
    {
        ArgumentNullException.ThrowIfNull(preview);

        // What cannot be read first (the owner fixes the file), then what was cancelled since, what is added, and the rest.
        var ordered = preview.Rows
            .OrderBy(r => r.Status switch
            {
                AppointmentImportStatus.Invalid => 0,
                AppointmentImportStatus.CancelledSince => 1,
                AppointmentImportStatus.New => 2,
                AppointmentImportStatus.AlreadyThere => 3,
                _ => 4,
            })
            .ThenBy(r => r.Row.SourceRow)
            .ToList();
        var shown = ordered.Take(rowLimit).ToList();
        var span = preview.Span(preview.ReadAt.Zone);
        return new PreviewAppointmentsViewModel(token, preview, shown, ordered.Count - shown.Count, span?.First, span?.Last);
    }
}

/// <summary>Step 3.</summary>
public sealed record AppointmentImportResultViewModel(string Token, AppointmentImportResult Result)
{
    /// <summary>The visits the old system cancelled since an earlier file, still booked here: the owner opens each one.</summary>
    public IReadOnlyList<ClassifiedAppointmentRow> CancelledSince => [.. Result.NotBrought.Where(r => r.Status == AppointmentImportStatus.CancelledSince)];

    public int Invalid => Result.NotBrought.Count(r => r.Status == AppointmentImportStatus.Invalid);
}