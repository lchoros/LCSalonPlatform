using Salon.Core.Cqrs;
using Salon.Services.Clients;

namespace Salon.Web.Features.Clients.MergeClients;

/// <summary>Reads through the client service only: the search never projects health notes and already hides merged cards (ADR 0011, ADR 0017).</summary>
public sealed class MergeCandidatesHandler(IClientService clients) : IRequestHandler<MergeCandidatesQuery, MergeCandidates?>
{
    public async Task<MergeCandidates?> HandleAsync(MergeCandidatesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var source = await clients.GetCardAsync(request.SourceId, cancellationToken);
        if (source is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return new MergeCandidates(source, []);
        }

        var matches = await clients.SearchAsync(request.Query, cancellationToken: cancellationToken);
        return new MergeCandidates(source, matches.Where(m => m.Id != source.Id).ToArray());
    }
}