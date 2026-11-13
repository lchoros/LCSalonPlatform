using Salon.Core.Cqrs;
using Salon.Core.Import;
using Salon.Services.Clients.Import;

namespace Salon.Web.Features.Clients.ImportClients;

/// <summary>Plugin first (pure), then the service (reads the salon's clients). A refused file never reaches the service.</summary>
public sealed class PreviewClientImportHandler(IClientImportProvider provider, IClientImportService imports)
    : IRequestHandler<PreviewClientImportQuery, PreviewClientImportOutcome>
{
    public async Task<PreviewClientImportOutcome> HandleAsync(PreviewClientImportQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parsed = provider.Parse(request.Content);
        if (!parsed.IsSuccess)
        {
            return new PreviewClientImportOutcome(parsed.Error, parsed.Header, null);
        }

        var preview = await imports.PreviewAsync(parsed, request.FileName, cancellationToken);
        return new PreviewClientImportOutcome(null, parsed.Header, preview);
    }
}