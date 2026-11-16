using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Import;
using Salon.Core.Tenancy;
using Salon.Services.Localization;
using Salon.Services.Products.Import;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Infrastructure;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Products.ImportProducts;

/// <summary>
/// Product list import at <c>/admin/products/import</c> (ADR 0045), in the shape of the client import (ADR 0019).
/// Tenant only, for signed-in users with <see cref="Permissions.CatalogManage"/>, as the product form is. Three steps,
/// three plain forms: upload the file, check the preview addressed by a server-side token, confirm. The token's value
/// carries the tenant; a token of another salon is a 404, an unknown or expired one sends the person back to step 1.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.CatalogManage)]
[Route("admin/products/import")]
public sealed class ImportProductsController(
    IDispatcher dispatcher,
    ITenantContext tenantContext,
    IMemoryCache cache,
    ILocalizationService localizer) : SalonControllerBase
{
    public const string UploadPath = "/admin/products/import";

    /// <summary>The list; the ListProducts slice owns the route.</summary>
    public const string ListPath = "/admin/products";

    /// <summary>Rows listed on the preview; the rest is a count.</summary>
    public const int PreviewRowLimit = 50;

    /// <summary>The byte cap as shown in messages.</summary>
    public const int MaxMegabytes = (int)(ClientImportLimits.MaxBytes / (1024 * 1024));

    /// <summary>Above the cap but still accepted by the request pipeline, so an oversized file gets the localized message instead of a bare 413.</summary>
    private const long RequestLimitBytes = 8 * 1024 * 1024;

    private TokenStore<ProductImportSession> Store => new(cache, "product-import:");

    /// <summary>The confirm form of a preview.</summary>
    public static string ConfirmPath(string token) => $"{UploadPath}/{token}/confirm";

    [HttpGet("")]
    public IActionResult Upload() => View(new UploadProductsViewModel(TakeFlash(), null));

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(RequestLimitBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = RequestLimitBytes)]
    public async Task<IActionResult> Upload(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return View(new UploadProductsViewModel(null, localizer["Products.Import.Validation.FileRequired"]));
        }

        if (file.Length > ClientImportLimits.MaxBytes)
        {
            return View(new UploadProductsViewModel(null, ErrorMessage(ClientImportError.TooLarge, [])));
        }

        byte[] content;
        using (var buffer = new MemoryStream((int)file.Length))
        {
            await file.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        PreviewProductImportOutcome outcome;
        try
        {
            outcome = await dispatcher.SendAsync(new PreviewProductImportQuery(content, Path.GetFileName(file.FileName)), cancellationToken);
        }
        catch (ValidationException ex)
        {
            return View(new UploadProductsViewModel(null, string.Join(" ", ex.Errors.Select(e => e.ErrorMessage).Distinct(StringComparer.Ordinal))));
        }

        if (outcome.Error is { } error)
        {
            return View(new UploadProductsViewModel(null, ErrorMessage(error, outcome.Header)));
        }

        // [RequireTenant] has already turned a tenant-less request into a 404.
        var token = Store.Save(new ProductImportSession(tenantContext.TenantId!.Value, outcome.Preview, null));
        return RedirectToAction(nameof(Show), new { token });
    }

    [HttpGet("{token:length(64)}")]
    public IActionResult Show(string token)
    {
        var session = Store.Find(token);
        if (session is null)
        {
            SetFlash(localizer["Products.Import.Expired"]);
            return Redirect(UploadPath);
        }

        if (session.TenantId != tenantContext.TenantId)
        {
            return NotFound();
        }

        return session.Result is { } result
            ? View("Result", result)
            : View("Preview", PreviewProductsViewModel.From(token, session.Preview!, PreviewRowLimit));
    }

    [HttpPost("{token:length(64)}/confirm")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(string token, CancellationToken cancellationToken)
    {
        var session = Store.Find(token);
        if (session is null)
        {
            SetFlash(localizer["Products.Import.Expired"]);
            return Redirect(UploadPath);
        }

        if (session.TenantId != tenantContext.TenantId)
        {
            return NotFound();
        }

        if (session.Preview is not { } preview || session.Result is not null)
        {
            // Already imported (a double submit lands here): show the result again, run nothing.
            return RedirectToAction(nameof(Show), new { token });
        }

        if (preview.NewCount == 0)
        {
            Store.Replace(token, session with { Preview = null, Result = new ProductImportResult(preview.FileName, 0, preview.DuplicateCount, preview.InvalidCount, 0) });
            return RedirectToAction(nameof(Show), new { token });
        }

        // Spend the token before the import runs so a second submit meanwhile finds nothing to confirm; a failure puts it back.
        Store.Replace(token, session with { Preview = null });
        ProductImportResult result;
        try
        {
            result = await dispatcher.SendAsync(new ConfirmProductImportCommand(preview, CurrentUserId()), cancellationToken);
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
        ClientImportError.Empty => localizer["Products.Import.Error.Empty"],
        ClientImportError.TooLarge => localizer.Format("Products.Import.Error.TooLarge", MaxMegabytes),
        ClientImportError.TooManyRows => localizer.Format("Products.Import.Error.TooManyRows", ProductImportLimits.MaxRows),
        ClientImportError.NotText => localizer["Products.Import.Error.NotText"],
        _ => localizer.Format("Products.Import.Error.UnknownFormat", string.Join(", ", header)),
    };

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}

/// <summary>What a token points at: the classified preview before the confirm, the result after it. The tenant is checked on every use.</summary>
public sealed record ProductImportSession(Guid TenantId, ProductImportPreview? Preview, ProductImportResult? Result);

/// <summary>Step 1. <see cref="Flash"/> is the message left by an expired token; <see cref="Error"/> why the last file was refused.</summary>
public sealed record UploadProductsViewModel(string? Flash, string? Error);

/// <summary>Step 2: the counts, the first rows and how many are not listed.</summary>
public sealed record PreviewProductsViewModel(
    string Token,
    string FileName,
    string Currency,
    int Total,
    int NewCount,
    int DuplicateCount,
    int InvalidCount,
    IReadOnlyList<ClassifiedProductRow> Rows,
    int HiddenCount)
{
    public static PreviewProductsViewModel From(string token, ProductImportPreview preview, int rowLimit)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var shown = preview.Rows.Take(rowLimit).ToList();
        return new PreviewProductsViewModel(
            token,
            preview.FileName,
            preview.Currency,
            preview.Rows.Count,
            preview.NewCount,
            preview.DuplicateCount,
            preview.InvalidCount,
            shown,
            preview.Rows.Count - shown.Count);
    }
}