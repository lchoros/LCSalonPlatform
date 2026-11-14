using Salon.Core.Cqrs;
using Salon.Services.Clients;
using Card = Salon.Services.Clients.ClientCard;

namespace Salon.Web.Features.Clients.MergeClients;

/// <summary>
/// Both cards of a merge with their visit counters, for the confirmation page (ADR 0017). Null when either id is not a
/// client of the current salon. A query: no transaction, tenant required.
/// </summary>
public sealed record MergePreviewQuery(Guid SourceId, Guid TargetId) : IQuery<MergePreview?>;

/// <param name="Source">The card that will be merged away.</param>
/// <param name="Target">The card that is kept.</param>
public sealed record MergePreview(Card Source, ClientHistory SourceHistory, Card Target, ClientHistory TargetHistory);