using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;
using Salon.Core.Payments;

namespace Salon.Plugin.Payments.Stripe;

/// <summary>
/// <see cref="IOnlineDepositCheckout"/> on Stripe's hosted Checkout page, in test mode, on the platform's own Stripe
/// account (ADR 0082). It turns the app's request into the few things Stripe is told (an amount in pence, one line of
/// text, two addresses, our two ids) and Stripe's answer back into money the ledger can record. It writes nothing to
/// the database and knows nothing about a client. Log lines carry ids, numbers and enums only.
/// </summary>
internal sealed class StripeDepositCheckout(
    IStripeGateway gateway,
    IOptionsMonitor<StripeFeatureOptions> feature,
    IOptionsMonitor<StripeOptions> options,
    IClock clock,
    ILogger<StripeDepositCheckout> logger) : IOnlineDepositCheckout
{
    /// <summary>Stripe's placeholder: it puts the session's id there when it sends the client back.</summary>
    internal const string SessionIdPlaceholder = "{CHECKOUT_SESSION_ID}";

    /// <summary>Every Checkout Session id starts so; anything else in the address is not asked about at all.</summary>
    internal const string SessionIdPrefix = "cs_";

    /// <summary>
    /// An unpaid session closes by itself after this long. Stripe allows no less than half an hour; the few minutes
    /// on top keep a clock that runs a little behind from being refused.
    /// </summary>
    internal static readonly Duration SessionLifetime = Duration.FromMinutes(35);

    private int _saidWebhookIsNotSetUp;

    public bool IsAvailable => feature.CurrentValue.StripeDeposits && gateway.IsConfigured;

    public async Task<OnlineCheckoutStart> StartAsync(OnlineCheckoutRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsAvailable)
        {
            return OnlineCheckoutStart.NotStarted;
        }

        if (!StripeAmounts.TryToStripe(request.Amount, out var amountMinor, out var currency))
        {
            logger.LogWarning("No Stripe Checkout for appointment {AppointmentId}: {Amount} is not an amount in a currency this plugin charges", request.AppointmentId, request.Amount.Amount);
            return OnlineCheckoutStart.NotStarted;
        }

        var session = new StripeCheckoutRequest(
            amountMinor,
            currency,
            request.Description,
            WithSessionId(request.SuccessUrl),
            request.CancelUrl,
            request.TenantId,
            request.AppointmentId,
            (clock.GetCurrentInstant() + SessionLifetime).ToUnixTimeSeconds());
        try
        {
            var opened = await gateway.CreateCheckoutSessionAsync(session, cancellationToken).ConfigureAwait(false);
            if (opened.LiveMode || string.IsNullOrWhiteSpace(opened.Url))
            {
                logger.LogError("Stripe opened a session for appointment {AppointmentId} that cannot be used: live mode {LiveMode}", request.AppointmentId, opened.LiveMode);
                return OnlineCheckoutStart.NotStarted;
            }

            logger.LogInformation("Stripe Checkout opened for appointment {AppointmentId} of tenant {TenantId}: {AmountMinor} in the smallest unit", request.AppointmentId, request.TenantId, amountMinor);
            return new OnlineCheckoutStart(opened.Url);
        }
        catch (StripeGatewayException)
        {
            // Already logged where it failed. The booking stands and the deposit stays due.
            return OnlineCheckoutStart.NotStarted;
        }
    }

    public async Task<OnlineCheckoutReading> ReadAsync(string? sessionId, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return new OnlineCheckoutReading(OnlineCheckoutState.Unavailable);
        }

        if (!IsSessionId(sessionId))
        {
            return new OnlineCheckoutReading(OnlineCheckoutState.Unknown);
        }

        StripeCheckoutSession? session;
        try
        {
            session = await gateway.GetSessionAsync(sessionId!, cancellationToken).ConfigureAwait(false);
        }
        catch (StripeGatewayException)
        {
            return new OnlineCheckoutReading(OnlineCheckoutState.Unavailable);
        }

        if (session is null
            || !string.Equals(session.Id, sessionId, StringComparison.Ordinal)
            || session.TenantId is not { } tenantId
            || session.AppointmentId is not { } appointmentId)
        {
            return new OnlineCheckoutReading(OnlineCheckoutState.Unknown);
        }

        if (session.LiveMode)
        {
            // A test-mode key cannot read a live session; if it ever does, nothing is recorded from it.
            logger.LogError("A live-mode Stripe session was read for appointment {AppointmentId}; this plugin only works in test mode", appointmentId);
            return new OnlineCheckoutReading(OnlineCheckoutState.Unknown);
        }

        if (!session.IsPaid)
        {
            return new OnlineCheckoutReading(OnlineCheckoutState.NotPaid);
        }

        if (!StripeAmounts.TryFromStripe(session.AmountTotalMinor, session.Currency, out var paid))
        {
            logger.LogError("Stripe says appointment {AppointmentId} was paid {AmountMinor} in a currency this plugin does not read; nothing recorded", appointmentId, session.AmountTotalMinor ?? 0);
            return new OnlineCheckoutReading(OnlineCheckoutState.Unknown);
        }

        return new OnlineCheckoutReading(
            OnlineCheckoutState.Paid,
            new OnlineDepositPayment(tenantId, appointmentId, paid, StripePaymentsPlugin.PluginSystemName, session.Id));
    }

    public OnlineWebhookReading ReadWebhook(string? payload, string? signature)
    {
        var secret = options.CurrentValue.WebhookSecret;
        if (!feature.CurrentValue.StripeDeposits || string.IsNullOrWhiteSpace(secret))
        {
            if (Interlocked.Exchange(ref _saidWebhookIsNotSetUp, 1) == 0)
            {
                logger.LogWarning("A Stripe webhook arrived but no webhook secret is configured (or the feature is off); webhooks are not read. A payment is still confirmed when the client comes back from Stripe");
            }

            return new OnlineWebhookReading(OnlineWebhookState.NotConfigured);
        }

        if (!StripeWebhook.IsGenuine(payload, signature, secret))
        {
            return new OnlineWebhookReading(OnlineWebhookState.BadSignature);
        }

        return StripeWebhook.CompletedSessionId(payload) is { } sessionId && IsSessionId(sessionId)
            ? new OnlineWebhookReading(OnlineWebhookState.CheckoutCompleted, sessionId)
            : new OnlineWebhookReading(OnlineWebhookState.Ignored);
    }

    /// <summary>A Checkout Session id as Stripe writes them: the prefix, then letters, digits and underscores, short enough to keep whole.</summary>
    internal static bool IsSessionId(string? sessionId)
        => sessionId is not null
            && sessionId.StartsWith(SessionIdPrefix, StringComparison.Ordinal)
            && OnlineDepositRules.IsReference(sessionId)
            && sessionId.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>The success address with Stripe's placeholder under the agreed key. Stripe needs the braces as they are.</summary>
    internal static string WithSessionId(string successUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(successUrl);
        var separator = successUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{successUrl}{separator}{OnlineDepositCheckoutDefaults.SessionIdQueryKey}={SessionIdPlaceholder}";
    }
}