using Salon.Core.Cqrs;
using Salon.Services.Clients;

namespace Salon.Web.Features.Clients.SearchClients;

/// <summary>Reads through the client service only, which never projects the health notes column (ADR 0011).</summary>
public sealed class SearchClientsHandler(IClientService clients) : IRequestHandler<SearchClientsQuery, IReadOnlyList<ClientSummary>>
{
    public Task<IReadOnlyList<ClientSummary>> HandleAsync(SearchClientsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return clients.SearchAsync(request.Query, cancellationToken: cancellationToken);
    }
}