using FluentValidation;
using Salon.Core.Cqrs;
using Salon.Core.Import;
using Salon.Services.Localization;
using Salon.Services.Products.Import;

namespace Salon.Web.Features.Products.ImportProducts;

/// <summary>
/// Step 1 to 2 of the import (ADR 0045): read the uploaded bytes and classify the rows against the salon's products.
/// Reads only; the controller keeps the preview server-side until somebody confirms it.
/// </summary>
/// <param name="FileName">Shown in the preview and written on every opening count.</param>
public sealed record PreviewProductImportQuery(ReadOnlyMemory<byte> Content, string FileName) : IQuery<PreviewProductImportOutcome>
{
    public override string ToString() => $"{nameof(PreviewProductImportQuery)} {{ FileName = {FileName}, Bytes = {Content.Length} }}";
}

/// <summary>Either a reason the whole file is refused (with the headings found, for the message) or the classified preview.</summary>
public sealed record PreviewProductImportOutcome(ClientImportError? Error, IReadOnlyList<string> Header, ProductImportPreview? Preview);

/// <summary>
/// Step 2 to 3: add the rows the preview called new. A command, so the pipeline wraps the products and their opening
/// counts in one transaction. The preview comes from the server-side token, never from the browser.
/// </summary>
public sealed record ConfirmProductImportCommand(ProductImportPreview Preview, Guid? ByUserId) : ICommand<ProductImportResult>
{
    public override string ToString() => $"{nameof(ConfirmProductImportCommand)} {{ FileName = {Preview.FileName}, New = {Preview.NewCount} }}";
}

/// <summary>A file must be there and within the byte cap before the reader sees it.</summary>
public sealed class PreviewProductImportValidator : AbstractValidator<PreviewProductImportQuery>
{
    public PreviewProductImportValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        RuleFor(q => q.FileName)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage(_ => localizer["Products.Import.Validation.FileRequired"]);
        RuleFor(q => q.Content)
            .Must(content => content.Length > 0)
            .WithMessage(_ => localizer["Products.Import.Validation.FileRequired"])
            .Must(content => content.Length <= ClientImportLimits.MaxBytes)
            .WithMessage(_ => localizer.Format("Products.Import.Error.TooLarge", ImportProductsController.MaxMegabytes));
    }
}

/// <summary>The reader first (pure), then the service (reads the salon's products). A refused file never reaches the service.</summary>
public sealed class PreviewProductImportHandler(IProductImportProvider provider, IProductImportService imports)
    : IRequestHandler<PreviewProductImportQuery, PreviewProductImportOutcome>
{
    public async Task<PreviewProductImportOutcome> HandleAsync(PreviewProductImportQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parsed = provider.Parse(request.Content);
        if (!parsed.IsSuccess)
        {
            return new PreviewProductImportOutcome(parsed.Error, parsed.Header, null);
        }

        var preview = await imports.PreviewAsync(parsed, request.FileName, cancellationToken);
        return new PreviewProductImportOutcome(null, parsed.Header, preview);
    }
}

/// <summary>The note on each opening count names the file in the salon's language, so the stock history says where the number came from.</summary>
public sealed class ConfirmProductImportHandler(IProductImportService imports, ILocalizationService localizer)
    : IRequestHandler<ConfirmProductImportCommand, ProductImportResult>
{
    public Task<ProductImportResult> HandleAsync(ConfirmProductImportCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return imports.CommitAsync(request.Preview, localizer.Format("Products.Import.StockNote", request.Preview.FileName), request.ByUserId, cancellationToken);
    }
}