using Salon.Core.Cqrs;
using Salon.Services.Clients;

namespace Salon.Web.Features.Clients.ClientCard;

/// <summary>
/// The one read path that decrypts health notes (ADR 0011); the client service does it through the tenant filter. The
/// history comes from the same service, names resolved through the filter too (ADR 0017).
/// </summary>
public sealed class ClientCardHandler(IClientService clients) : IRequestHandler<ClientCardQuery, ClientCardDetails?>
{
    public async Task<ClientCardDetails?> HandleAsync(ClientCardQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var card = await clients.GetCardAsync(request.ClientId, cancellationToken);
        if (card is null)
        {
            return null;
        }

        var history = await clients.GetHistoryAsync(request.ClientId, cancellationToken: cancellationToken)
            ?? new ClientHistory([], [], 0, 0, 0, null);
        string? mergedIntoName = null;
        if (card.MergedIntoClientId is { } targetId)
        {
            mergedIntoName = (await clients.GetCardAsync(targetId, cancellationToken))?.FullName;
        }

        return new ClientCardDetails(card, history, mergedIntoName);
    }
}