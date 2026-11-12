using Salon.Core.Cqrs;
using Salon.Services.Clients;

namespace Salon.Web.Features.Clients.MergeClients;

/// <summary>Two cards and two histories through the client service, so the tenant filter decides what exists (ADR 0011).</summary>
public sealed class MergePreviewHandler(IClientService clients) : IRequestHandler<MergePreviewQuery, MergePreview?>
{
    public async Task<MergePreview?> HandleAsync(MergePreviewQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var source = await clients.GetCardAsync(request.SourceId, cancellationToken);
        var target = await clients.GetCardAsync(request.TargetId, cancellationToken);
        if (source is null || target is null)
        {
            return null;
        }

        var empty = new ClientHistory([], [], 0, 0, 0, null);
        var sourceHistory = await clients.GetHistoryAsync(request.SourceId, take: 1, cancellationToken) ?? empty;
        var targetHistory = await clients.GetHistoryAsync(request.TargetId, take: 1, cancellationToken) ?? empty;
        return new MergePreview(source, sourceHistory, target, targetHistory);
    }
}