using Salon.Core.Cqrs;
using Salon.Services.Clients;
using Card = Salon.Services.Clients.ClientCard;

namespace Salon.Web.Features.Clients.MergeClients;

/// <summary>
/// The card about to be merged away and the salon's other clients matching <paramref name="Query"/> (name prefix or phone
/// digits, as the client search); a blank query lists nobody. Null when <paramref name="SourceId"/> is not a client of the
/// current salon. A query: no transaction, tenant required.
/// </summary>
public sealed record MergeCandidatesQuery(Guid SourceId, string? Query) : IQuery<MergeCandidates?>;

public sealed record MergeCandidates(Card Source, IReadOnlyList<ClientSummary> Matches);