using Salon.Core.Cqrs;
using Salon.Services.Clients;

using Card = Salon.Services.Clients.ClientCard;

namespace Salon.Web.Features.Clients.ClientCard;

/// <summary>
/// One client's full card, health notes decrypted, with the visit history (ADR 0017) and, for a card merged away, the
/// name of the card that absorbed it; null when the id is not a client of the current salon. A query: no transaction,
/// tenant required.
/// </summary>
public sealed record ClientCardQuery(Guid ClientId) : IQuery<ClientCardDetails?>;

/// <param name="Card">The decrypted card.</param>
/// <param name="History">Upcoming and past visits with counters; empty lists for a client who never booked.</param>
/// <param name="MergedIntoName">Full name of the client this card was merged into, when <see cref="Card.IsMerged"/>.</param>
public sealed record ClientCardDetails(Card Card, ClientHistory History, string? MergedIntoName);
