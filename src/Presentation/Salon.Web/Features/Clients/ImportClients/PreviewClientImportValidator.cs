using FluentValidation;
using Salon.Core.Import;
using Salon.Services.Localization;

namespace Salon.Web.Features.Clients.ImportClients;

/// <summary>A file must be there and within the byte cap before the plugin sees it (ADR 0019).</summary>
public sealed class PreviewClientImportValidator : AbstractValidator<PreviewClientImportQuery>
{
    public PreviewClientImportValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        RuleFor(q => q.FileName)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage(_ => localizer["Clients.Import.Validation.FileRequired"]);
        RuleFor(q => q.Content)
            .Must(content => content.Length > 0)
            .WithMessage(_ => localizer["Clients.Import.Validation.FileRequired"])
            .Must(content => content.Length <= ClientImportLimits.MaxBytes)
            .WithMessage(_ => localizer.Format("Clients.Import.Error.TooLarge", ImportClientsController.MaxMegabytes));
    }
}