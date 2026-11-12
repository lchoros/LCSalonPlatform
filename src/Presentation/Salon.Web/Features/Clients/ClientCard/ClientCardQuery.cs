using Salon.Core.Cqrs;
using Salon.Services.Clients;

using Card = Salon.Services.Clients.ClientCard;

namespace Salon.Web.Features.Clients.ClientCard;

/// <summary>One client's full card, health notes decrypted; null when the id is not a client of the current salon. A query: no transaction, tenant required.</summary>
public sealed record ClientCardQuery(Guid ClientId) : IQuery<Card?>;