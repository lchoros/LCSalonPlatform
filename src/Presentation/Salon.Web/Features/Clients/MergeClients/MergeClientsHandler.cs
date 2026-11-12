using Salon.Core.Cqrs;
using Salon.Services.Clients;

namespace Salon.Web.Features.Clients.MergeClients;

/// <summary>Looks both cards up through the tenant filter (another salon's id is "not found", never "merged"), then lets the service apply the rules.</summary>
public sealed class MergeClientsHandler(IClientService clients) : IRequestHandler<MergeClientsCommand, MergeClientsResult?>
{
    public async Task<MergeClientsResult?> HandleAsync(MergeClientsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var source = await clients.GetCardAsync(request.SourceId, cancellationToken);
        var target = await clients.GetCardAsync(request.TargetId, cancellationToken);
        if (source is null || target is null)
        {
            return null;
        }

        await clients.MergeAsync(request.TargetId, request.SourceId, request.ByUserId, cancellationToken);
        return new MergeClientsResult(source.FullName, target.FullName);
    }
}
