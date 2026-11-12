using Salon.Core.Cqrs;
using Salon.Services.Clients;

namespace Salon.Web.Features.Clients.SearchClients;

/// <summary>The current salon's clients matching a name prefix or phone digits; a blank query lists the first ones by name. A query: no transaction, tenant required.</summary>
public sealed record SearchClientsQuery(string? Query) : IQuery<IReadOnlyList<ClientSummary>>;