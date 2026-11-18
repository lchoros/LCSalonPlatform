using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Import;
using Salon.Core.Tenancy;
using Salon.Services.Catalog.Import;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Infrastructure;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Catalog.ImportServices;

/// <summary>
/// The price list from a file at <c>/admin/services/import</c> (ADR 0066), in the shape of the product import
/// (ADR 0045). Tenant only, for signed-in users with <see cref="Permissions.CatalogManage"/>, as the service form is.
/// Three steps, three plain forms: upload the file, check the preview addressed by a server-side token, confirm.
/// The preview says for every row whether it adds a service, changes one (with the old and the new time and price),
/// changes nothing, or is left out and why. The token's value carries the tenant; a token of another salon is a 404,
/// an unknown or expired one sends the person back to step 1.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.CatalogManage)]
[Route("admin/services/import")]
public sealed class ImportServicesController(
    IDispatcher dispatcher,
    ITenantContext tenantContext,
    IMemoryCache cache,
    ILocalizationService localizer) : SalonControllerBase
{
    public const string UploadPath = "/admin/services/import";

    /// <summary>The list; the ListServices slice owns the route.</summary>
    public const string ListPath = "/admin/services";

    /// <summary>The salon's price list as a file, to edit and read back; the ExportServices slice owns the route.</summary>
    public const string FilePath = "/admin/services/price-list.csv";

    /// <summary>Rows listed on the preview; the rest is a count. A price list is read row by row before it is confirmed.</summary>
    public const int PreviewRowLimit = 200;

    /// <summary>The byte cap as shown in messages.</summary>
    public const int MaxMegabytes = (int)(ClientImportLimits.MaxBytes / (1024 * 1024));

    /// <summary>Above the cap but still accepted by the request pipeline, so an oversized file gets the localized message instead of a bare 413.</summary>
    private const long RequestLimitBytes = 8 * 1024 * 1024;

    private TokenStore<ServiceImportSession> Store => new(cache, "service-import:");

    /// <summary>The confirm form of a preview.</summary>
    public static string ConfirmPath(string token) => $"{UploadPath}/{token}/confirm";

    [HttpGet("")]
    public IActionResult Upload() => View(new UploadServicesViewModel(TakeFlash(), null));

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(RequestLimitBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = RequestLimitBytes)]
    public async Task<IActionResult> Upload(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return View(new UploadServicesViewModel(null, localizer["Catalog.Import.Validation.FileRequired"]));
        }

        if (file.Length > ClientImportLimits.MaxBytes)
        {
            return View(new UploadServicesViewModel(null, ErrorMessage(ClientImportError.TooLarge, [])));
        }

        byte[] content;
        using (var buffer = new MemoryStream((int)file.Length))
        {
            await file.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        PreviewServiceImportOutcome outcome;
        try
        {
            outcome = await dispatcher.SendAsync(new PreviewServiceImportQuery(content, Path.GetFileName(file.FileName)), cancellationToken);
        }
        catch (ValidationException ex)
        {
            return View(new UploadServicesViewModel(null, string.Join(" ", ex.Errors.Select(e => e.ErrorMessage).Distinct(StringComparer.Ordinal))));
        }

        if (outcome.Error is { } error)
        {
            return View(new UploadServicesViewModel(null, ErrorMessage(error, outcome.Header)));
        }

        // [RequireTenant] has already turned a tenant-less request into a 404.
        var token = Store.Save(new ServiceImportSession(tenantContext.TenantId!.Value, outcome.Preview, null));
        return RedirectToAction(nameof(Show), new { token });
    }

    [HttpGet("{token:length(64)}")]
    public IActionResult Show(string token)
    {
        var session = Store.Find(token);
        if (session is null)
        {
            SetFlash(localizer["Catalog.Import.Expired"]);
            return Redirect(UploadPath);
        }

        if (session.TenantId != tenantContext.TenantId)
        {
            return NotFound();
        }

        return session.Result is { } result
            ? View("Result", result)
            : View("Preview", PreviewServicesViewModel.From(token, session.Preview!, PreviewRowLimit));
    }

    [HttpPost("{token:length(64)}/confirm")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(string token, CancellationToken cancellationToken)
    {
        var session = Store.Find(token);
        if (session is null)
        {
            SetFlash(localizer["Catalog.Import.Expired"]);
            return Redirect(UploadPath);
        }

        if (session.TenantId != tenantContext.TenantId)
        {
            return NotFound();
        }

        if (session.Preview is not { } preview || session.Result is not null)
        {
            // Already read in (a double submit lands here): show the result again, run nothing.
            return RedirectToAction(nameof(Show), new { token });
        }

        if (!preview.HasWork)
        {
            Store.Replace(token, session with { Preview = null, Result = new ServiceImportResult(preview.FileName, 0, 0, preview.UnchangedCount, preview.InvalidCount, preview.RepeatedCount, 0) });
            return RedirectToAction(nameof(Show), new { token });
        }

        // Spend the token before the file is read in so a second submit meanwhile finds nothing to confirm; a failure puts it back.
        Store.Replace(token, session with { Preview = null });
        ServiceImportResult result;
        try
        {
            result = await dispatcher.SendAsync(new ConfirmServiceImportCommand(preview, CurrentUserId()), cancellationToken);
        }
        catch
        {
            Store.Replace(token, session);
            throw;
        }

        Store.Replace(token, session with { Preview = null, Result = result });
        return RedirectToAction(nameof(Show), new { token });
    }

    private string ErrorMessage(ClientImportError error, IReadOnlyList<string> header) => error switch
    {
        ClientImportError.Empty => localizer["Catalog.Import.Error.Empty"],
        ClientImportError.TooLarge => localizer.Format("Catalog.Import.Error.TooLarge", MaxMegabytes),
        ClientImportError.TooManyRows => localizer.Format("Catalog.Import.Error.TooManyRows", ServiceImportLimits.MaxRows),
        ClientImportError.NotText => localizer["Catalog.Import.Error.NotText"],
        _ => localizer.Format("Catalog.Import.Error.UnknownFormat", string.Join(", ", header)),
    };

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}

/// <summary>What a token points at: the classified preview before the confirm, the result after it. The tenant is checked on every use.</summary>
public sealed record ServiceImportSession(Guid TenantId, ServiceImportPreview? Preview, ServiceImportResult? Result);

/// <summary>Step 1. <see cref="Flash"/> is the message left by an expired token; <see cref="Error"/> why the last file was refused.</summary>
public sealed record UploadServicesViewModel(string? Flash, string? Error);

/// <summary>Step 2: the counts, the rows and how many are not listed. Rows that change something come first.</summary>
public sealed record PreviewServicesViewModel(
    string Token,
    string FileName,
    string Currency,
    int Total,
    int NewCount,
    int ChangedCount,
    int UnchangedCount,
    int LeftOutCount,
    bool HasWork,
    IReadOnlyList<ClassifiedServiceRow> Rows,
    int HiddenCount)
{
    public static PreviewServicesViewModel From(string token, ServiceImportPreview preview, int rowLimit)
    {
        ArgumentNullException.ThrowIfNull(preview);

        // What cannot be read first (the owner fixes the file), then what changes, what is added, and what stays.
        var ordered = preview.Rows
            .OrderBy(r => r.Status switch
            {
                ServiceImportStatus.Invalid => 0,
                ServiceImportStatus.Repeated => 1,
                ServiceImportStatus.Changed => 2,
                ServiceImportStatus.New => 3,
                _ => 4,
            })
            .ThenBy(r => r.Row.SourceRow)
            .ToList();
        var shown = ordered.Take(rowLimit).ToList();
        return new PreviewServicesViewModel(
            token,
            preview.FileName,
            preview.Currency,
            preview.Rows.Count,
            preview.NewCount,
            preview.ChangedCount,
            preview.UnchangedCount,
            preview.InvalidCount + preview.RepeatedCount,
            preview.HasWork,
            shown,
            ordered.Count - shown.Count);
    }
}