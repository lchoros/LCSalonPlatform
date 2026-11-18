using FluentValidation;
using Salon.Core.Cqrs;
using Salon.Core.Import;
using Salon.Services.Catalog.Import;
using Salon.Services.Localization;

namespace Salon.Web.Features.Catalog.ImportServices;

/// <summary>
/// Step 1 to 2 of reading in a price list (ADR 0066): read the uploaded bytes and classify the rows against the
/// salon's services. Reads only; the controller keeps the preview server-side until somebody confirms it.
/// </summary>
/// <param name="FileName">Shown in the preview and on the result.</param>
public sealed record PreviewServiceImportQuery(ReadOnlyMemory<byte> Content, string FileName) : IQuery<PreviewServiceImportOutcome>
{
    public override string ToString() => $"{nameof(PreviewServiceImportQuery)} {{ Bytes = {Content.Length} }}";
}

/// <summary>Either a reason the whole file is refused (with the headings found, for the message) or the classified preview.</summary>
public sealed record PreviewServiceImportOutcome(ClientImportError? Error, IReadOnlyList<string> Header, ServiceImportPreview? Preview);

/// <summary>
/// Step 2 to 3: add the services the preview called new and update the ones it called changed. A command, so the
/// pipeline wraps all of it in one transaction. The preview comes from the server-side token, never from the browser.
/// </summary>
public sealed record ConfirmServiceImportCommand(ServiceImportPreview Preview, Guid? ByUserId) : ICommand<ServiceImportResult>
{
    public override string ToString() => $"{nameof(ConfirmServiceImportCommand)} {{ New = {Preview.NewCount}, Changed = {Preview.ChangedCount} }}";
}

/// <summary>A file must be there and within the byte cap before the reader sees it.</summary>
public sealed class PreviewServiceImportValidator : AbstractValidator<PreviewServiceImportQuery>
{
    public PreviewServiceImportValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        RuleFor(q => q.FileName)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage(_ => localizer["Catalog.Import.Validation.FileRequired"]);
        RuleFor(q => q.Content)
            .Must(content => content.Length > 0)
            .WithMessage(_ => localizer["Catalog.Import.Validation.FileRequired"])
            .Must(content => content.Length <= ClientImportLimits.MaxBytes)
            .WithMessage(_ => localizer.Format("Catalog.Import.Error.TooLarge", ImportServicesController.MaxMegabytes));
    }
}

/// <summary>The reader first (pure), then the service (reads the salon's services). A refused file never reaches the service.</summary>
public sealed class PreviewServiceImportHandler(IServiceImportProvider provider, IServiceImportService imports)
    : IRequestHandler<PreviewServiceImportQuery, PreviewServiceImportOutcome>
{
    public async Task<PreviewServiceImportOutcome> HandleAsync(PreviewServiceImportQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parsed = provider.Parse(request.Content);
        if (!parsed.IsSuccess)
        {
            return new PreviewServiceImportOutcome(parsed.Error, parsed.Header, null);
        }

        var preview = await imports.PreviewAsync(parsed, request.FileName, cancellationToken);
        return new PreviewServiceImportOutcome(null, parsed.Header, preview);
    }
}

/// <summary>Hands the preview to <see cref="IServiceImportService"/>, which decides again against the services as they are now.</summary>
public sealed class ConfirmServiceImportHandler(IServiceImportService imports) : IRequestHandler<ConfirmServiceImportCommand, ServiceImportResult>
{
    public Task<ServiceImportResult> HandleAsync(ConfirmServiceImportCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return imports.CommitAsync(request.Preview, request.ByUserId, cancellationToken);
    }
}