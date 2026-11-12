using Salon.Core.Cqrs;
using Salon.Services.Clients;

using Card = Salon.Services.Clients.ClientCard;

namespace Salon.Web.Features.Clients.ClientCard;

/// <summary>The one read path that decrypts health notes (ADR 0011); the client service does it through the tenant filter.</summary>
public sealed class ClientCardHandler(IClientService clients) : IRequestHandler<ClientCardQuery, Card?>
{
    public Task<Card?> HandleAsync(ClientCardQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return clients.GetCardAsync(request.ClientId, cancellationToken);
    }
}