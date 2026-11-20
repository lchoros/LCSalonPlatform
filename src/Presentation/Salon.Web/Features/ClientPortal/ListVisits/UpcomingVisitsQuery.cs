using NodaTime;
using Salon.Core.Cqrs;
using Salon.Services.ClientPortal;

namespace Salon.Web.Features.ClientPortal.ListVisits;

/// <summary>
/// The visits still ahead of the signed-in client (ADR 0083); null when their session no longer stands. A query: no
/// transaction, tenant required, and every row comes through the tenant filter.
/// </summary>
/// <param name="ClientId">The card the session hangs on, from the client's own cookie.</param>
/// <param name="SignedInAt">When the code was entered; a card changed since then ends the session.</param>
public sealed record UpcomingVisitsQuery(Guid ClientId, Instant SignedInAt) : IQuery<IReadOnlyList<ClientPortalVisit>?>;

public sealed class UpcomingVisitsHandler(IClientPortalService portal) : IRequestHandler<UpcomingVisitsQuery, IReadOnlyList<ClientPortalVisit>?>
{
    public Task<IReadOnlyList<ClientPortalVisit>?> HandleAsync(UpcomingVisitsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return portal.GetUpcomingVisitsAsync(request.ClientId, request.SignedInAt, cancellationToken);
    }
}