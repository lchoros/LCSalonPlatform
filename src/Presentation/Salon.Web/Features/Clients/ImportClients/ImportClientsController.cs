using FluentValidation;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Import;
using Salon.Core.Tenancy;
using Salon.Services.Clients.Import;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Infrastructure;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Clients.ImportClients;

/// <summary>
/// Client CSV import at <c>/admin/clients/import</c> (ADR 0019), behind the ClientImport flag. Tenant only, for signed-in
/// users with <see cref="Permissions.ClientsManage"/> (Owner, Manager). Three steps, three plain forms: upload the file,
/// check the preview addressed by a server-side token, confirm. The token's session carries the tenant; a token of another
/// salon is a 404, an unknown or expired one sends the owner back to step 1 with a message.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.ClientsManage)]
[FeatureGate(nameof(FeaturesOptions.ClientImport))]
[Route("admin/clients/import")]
public sealed class ImportClientsController(
    IDispatcher dispatcher,
    ITenantContext tenantContext,
    IMemoryCache cache,
    ILocalizationService localizer) : SalonControllerBase
{
    /// <summary>Rows listed on the preview; the rest is a count.</summary>
    public const int PreviewRowLimit = 50;

    /// <summary>The byte cap as shown in messages.</summary>
    public const int MaxMegabytes = (int)(ClientImportLimits.MaxBytes / (1024 * 1024));

    /// <summary>Above the cap but still accepted by the request pipeline, so an oversized file gets the localized message instead of a bare 413.</summary>
    private const long RequestLimitBytes = 8 * 1024 * 1024;

    private const string UploadPath = "/admin/clients/import";

    private ImportSessionStore Store => new(cache);

    [HttpGet("")]
    public IActionResult Upload() => View(new UploadViewModel(TakeFlash(), null));

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(RequestLimitBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = RequestLimitBytes)]
    public async Task<IActionResult> Upload(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return View(new UploadViewModel(null, localizer["Clients.Import.Validation.FileRequired"]));
        }

        if (file.Length > ClientImportLimits.MaxBytes)
        {
            return View(new UploadViewModel(null, ErrorMessage(ClientImportError.TooLarge, [])));
        }

        byte[] content;
        using (var buffer = new MemoryStream((int)file.Length))
        {
            await file.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        PreviewClientImportOutcome outcome;
        try
        {
            outcome = await dispatcher.SendAsync(new PreviewClientImportQuery(content, Path.GetFileName(file.FileName)), cancellationToken);
        }
        catch (ValidationException ex)
        {
            return View(new UploadViewModel(null, string.Join(" ", ex.Errors.Select(e => e.ErrorMessage).Distinct(StringComparer.Ordinal))));
        }

        if (outcome.Error is { } error)
        {
            return View(new UploadViewModel(null, ErrorMessage(error, outcome.Header)));
        }

        // [RequireTenant] has already turned a tenant-less request into a 404.
        var token = Store.Save(new ImportSession(tenantContext.TenantId!.Value, outcome.Preview, null));
        return Redirect($"{UploadPath}/{token}");
    }

    [HttpGet("{token:length(64)}")]
    public IActionResult Show(string token)
    {
        var session = Store.Find(token);
        if (session is null)
        {
            SetFlash(localizer["Clients.Import.Expired"]);
            return Redirect(UploadPath);
        }

        if (session.TenantId != tenantContext.TenantId)
        {
            return NotFound();
        }

        if (session.Result is { } result)
        {
            return View("Result", new ResultViewModel(result));
        }

        return View("Preview", PreviewViewModel.From(token, session.Preview!, PreviewRowLimit));
    }

    [HttpPost("{token:length(64)}/confirm")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(string token, CancellationToken cancellationToken)
    {
        var session = Store.Find(token);
        if (session is null)
        {
            SetFlash(localizer["Clients.Import.Expired"]);
            return Redirect(UploadPath);
        }

        if (session.TenantId != tenantContext.TenantId)
        {
            return NotFound();
        }

        if (session.Preview is null || session.Result is not null)
        {
            // Already imported (a double submit lands here): show the result again, run nothing.
            return Redirect($"{UploadPath}/{token}");
        }

        if (session.Preview.NewCount == 0)
        {
            Store.Replace(token, session with { Preview = null, Result = new ClientImportResult(session.Preview.FileName, session.Preview.Format, 0, session.Preview.DuplicateCount, session.Preview.InvalidCount) });
            return Redirect($"{UploadPath}/{token}");
        }

        // Spend the token before the import runs so a second submit meanwhile finds nothing to confirm; a failure puts it back.
        Store.Replace(token, session with { Preview = null });
        ClientImportResult result;
        try
        {
            result = await dispatcher.SendAsync(new ConfirmClientImportCommand(session.Preview), cancellationToken);
        }
        catch
        {
            Store.Replace(token, session);
            throw;
        }

        Store.Replace(token, session with { Preview = null, Result = result });
        return Redirect($"{UploadPath}/{token}");
    }

    private string ErrorMessage(ClientImportError error, IReadOnlyList<string> header) => error switch
    {
        ClientImportError.Empty => localizer["Clients.Import.Error.Empty"],
        ClientImportError.TooLarge => localizer.Format("Clients.Import.Error.TooLarge", MaxMegabytes),
        ClientImportError.TooManyRows => localizer.Format("Clients.Import.Error.TooManyRows", ClientImportLimits.MaxRows),
        ClientImportError.UnknownFormat => localizer.Format("Clients.Import.Error.UnknownFormat", string.Join(", ", header)),
        ClientImportError.NotText => localizer["Clients.Import.Error.NotText"],
        _ => localizer["Clients.Import.Error.UnknownFormat"],
    };
}

/// <summary>Step 1. <see cref="Flash"/> is the message left by an expired token; <see cref="Error"/> why the last file was refused.</summary>
public sealed record UploadViewModel(string? Flash, string? Error);

/// <summary>Step 2: the counts, the first rows and what is left out.</summary>
public sealed record PreviewViewModel(
    string Token,
    string FileName,
    string Format,
    int Total,
    int NewCount,
    int DuplicateCount,
    int InvalidCount,
    IReadOnlyList<ClassifiedImportRow> Rows,
    int HiddenCount)
{
    public static PreviewViewModel From(string token, ClientImportPreview preview, int rowLimit)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var shown = preview.Rows.Take(rowLimit).ToList();
        return new PreviewViewModel(
            token,
            preview.FileName,
            preview.Format,
            preview.Rows.Count,
            preview.NewCount,
            preview.DuplicateCount,
            preview.InvalidCount,
            shown,
            preview.Rows.Count - shown.Count);
    }
}

/// <summary>Step 3.</summary>
public sealed record ResultViewModel(ClientImportResult Result);