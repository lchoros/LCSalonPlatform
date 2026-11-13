using Salon.Core.Cqrs;
using Salon.Services.Clients;
using Card = Salon.Services.Clients.ClientCard;

namespace Salon.Web.Features.Clients.EditClient;

/// <summary>The current values for the edit form, health notes decrypted; null when the id is not a client of the current salon. A query: no transaction, tenant required.</summary>
public sealed record EditClientQuery(Guid ClientId) : IQuery<Card?>;

/// <summary>Reads the card through the client service (the one path that decrypts, ADR 0011) and the tenant filter: another salon's id is simply not found.</summary>
public sealed class EditClientQueryHandler(IClientService clients) : IRequestHandler<EditClientQuery, Card?>
{
    public Task<Card?> HandleAsync(EditClientQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return clients.GetCardAsync(request.ClientId, cancellationToken);
    }
}