using Salon.Core.Domain;

namespace Salon.Core.Payments;

/// <summary>
/// Takes the deposit of an online booking on a payment gateway's own hosted page (ADR 0082). The app never sees a
/// card: it sends the client to the gateway with an amount and its own ids, and afterwards asks the gateway, server to
/// server, whether that session was paid. It stands next to <see cref="IPaymentProvider"/>, which stays the one way
/// money taken on the salon's floor is recorded.
/// The host always has one: <see cref="NoOnlineDepositCheckout"/> unless a gateway plugin replaces it.
/// </summary>
public interface IOnlineDepositCheckout
{
    /// <summary>
    /// True when a client can be sent to pay now: the feature is switched on and the gateway has what it needs. False
    /// means the booking flow behaves as if there were no gateway at all.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Opens a hosted payment page for exactly <see cref="OnlineCheckoutRequest.Amount"/>. Never throws for a gateway
    /// that is down or refuses: the result then has no address and the booking stands with its deposit still due.
    /// </summary>
    Task<OnlineCheckoutStart> StartAsync(OnlineCheckoutRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the gateway about one of its sessions. What a browser or a webhook says is never taken as payment; this
    /// answer is. Touches no salon data, so it runs without a tenant.
    /// </summary>
    Task<OnlineCheckoutReading> ReadAsync(string? sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks that a webhook really came from the gateway and says which session it is about. It only names the
    /// session; whether it was paid is still asked through <see cref="ReadAsync"/>.
    /// </summary>
    OnlineWebhookReading ReadWebhook(string? payload, string? signature);
}

public static class OnlineDepositCheckoutDefaults
{
    /// <summary>The query key under which the gateway hands its session id back on the success address.</summary>
    public const string SessionIdQueryKey = "session_id";
}

/// <param name="Amount">The deposit the booking asks for, to the penny, in the salon's currency.</param>
/// <param name="Description">What the client is paying for, as the payment page shows it. Never a person's name.</param>
/// <param name="SuccessUrl">Where the gateway sends the client after paying; it adds <see cref="OnlineDepositCheckoutDefaults.SessionIdQueryKey"/>.</param>
/// <param name="CancelUrl">Where the gateway sends a client who goes back without paying.</param>
public sealed record OnlineCheckoutRequest(Guid TenantId, Guid AppointmentId, Money Amount, string Description, string SuccessUrl, string CancelUrl);

/// <param name="RedirectUrl">The gateway's payment page; null when no session was opened.</param>
public sealed record OnlineCheckoutStart(string? RedirectUrl)
{
    public static OnlineCheckoutStart NotStarted { get; } = new((string?)null);
}

public enum OnlineCheckoutState
{
    /// <summary>The gateway holds the money of this session.</summary>
    Paid = 1,

    /// <summary>The session exists and nothing was paid (yet): the client went back, closed the page or is still there.</summary>
    NotPaid = 2,

    /// <summary>No such session, or one that does not say which salon and visit it is for.</summary>
    Unknown = 3,

    /// <summary>The gateway could not be asked: switched off, not set up, or not answering.</summary>
    Unavailable = 4,
}

/// <param name="Payment">What was paid; set exactly when <paramref name="State"/> is <see cref="OnlineCheckoutState.Paid"/>.</param>
public sealed record OnlineCheckoutReading(OnlineCheckoutState State, OnlineDepositPayment? Payment = null);

/// <summary>Money a gateway took for the deposit of one visit, as the ledger records it.</summary>
/// <param name="ProviderSystemName">The gateway plugin's system name, e.g. "Payments.Stripe".</param>
/// <param name="Reference">The gateway's id of the session. Recording is idempotent per reference. Never card data.</param>
public sealed record OnlineDepositPayment(Guid TenantId, Guid AppointmentId, Money Amount, string ProviderSystemName, string Reference);

public enum OnlineWebhookState
{
    /// <summary>The gateway's webhook secret is not set: nothing can be verified, so nothing is read.</summary>
    NotConfigured = 1,

    /// <summary>The signature is missing, wrong or too old.</summary>
    BadSignature = 2,

    /// <summary>Genuine, but about something this app does not act on.</summary>
    Ignored = 3,

    /// <summary>Genuine: the session named was completed.</summary>
    CheckoutCompleted = 4,
}

public sealed record OnlineWebhookReading(OnlineWebhookState State, string? SessionId = null);

/// <summary>The gateway of a host without one: never available, opens nothing, knows no session.</summary>
public sealed class NoOnlineDepositCheckout : IOnlineDepositCheckout
{
    public bool IsAvailable => false;

    public Task<OnlineCheckoutStart> StartAsync(OnlineCheckoutRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(OnlineCheckoutStart.NotStarted);

    public Task<OnlineCheckoutReading> ReadAsync(string? sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult(new OnlineCheckoutReading(OnlineCheckoutState.Unavailable));

    public OnlineWebhookReading ReadWebhook(string? payload, string? signature) => new(OnlineWebhookState.NotConfigured);
}