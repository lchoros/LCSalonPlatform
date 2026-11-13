using Salon.Core.Cqrs;
using Salon.Core.Settings;
using Salon.Services.Clients;
using Salon.Services.Payments;
using Salon.Services.Settings;

namespace Salon.Web.Features.Clients.ClientCard;

/// <summary>
/// The one read path that decrypts health notes (ADR 0011); the client service does it through the tenant filter. The
/// history comes from the same service, names resolved through the filter too (ADR 0017).
/// </summary>
public sealed class ClientCardHandler(IClientService clients, IClientAccountService accounts, ISettingService settings) : IRequestHandler<ClientCardQuery, ClientCardDetails?>
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

        var account = await accounts.GetAccountAsync(request.ClientId, cancellationToken);
        var policy = await settings.LoadAsync<CancellationPolicySettings>(cancellationToken);
        var feesInUse = account.Fees.Count > 0 || policy.NoShowFeePercent > 0m || policy.LateCancellationFeePercent > 0m;
        return new ClientCardDetails(card, history, mergedIntoName, account, feesInUse);
    }
}