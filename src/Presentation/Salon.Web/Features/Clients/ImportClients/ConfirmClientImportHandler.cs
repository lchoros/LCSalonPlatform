using Salon.Core.Cqrs;
using Salon.Services.Clients.Import;
using Salon.Services.Localization;

namespace Salon.Web.Features.Clients.ImportClients;

/// <summary>
/// The consent wording recorded for imported rows names the file and the source product in the salon's language, so a
/// card later shows where the marketing claim came from (ADR 0011 keeps the wording with the consent).
/// </summary>
public sealed class ConfirmClientImportHandler(IClientImportService imports, ILocalizationService localizer)
    : IRequestHandler<ConfirmClientImportCommand, ClientImportResult>
{
    public Task<ClientImportResult> HandleAsync(ConfirmClientImportCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var wording = ConsentWording(localizer, request.Preview.FileName, request.Preview.Format);
        return imports.CommitAsync(request.Preview, wording, cancellationToken);
    }

    public static string ConsentWording(ILocalizationService localizer, string fileName, string format)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return localizer.Format("Clients.Import.ConsentSource", fileName, format);
    }
}