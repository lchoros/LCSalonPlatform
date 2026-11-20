namespace Salon.Plugin.Payments.Stripe;

/// <summary>
/// The two calls this plugin makes to Stripe (ADR 0082), and nothing else of Stripe's API. The real one wraps
/// Stripe.net; tests put a fake here, so no automated test ever reaches Stripe's network.
/// </summary>
public interface IStripeGateway
{
    /// <summary>True when a test-mode secret key is configured. False means no call can be made.</summary>
    bool IsConfigured { get; }

    /// <summary>Opens a Checkout Session in payment mode for one line. Throws <see cref="StripeGatewayException"/> when Stripe refuses or cannot be reached.</summary>
    Task<StripeCheckoutSession> CreateCheckoutSessionAsync(StripeCheckoutRequest request, CancellationToken cancellationToken = default);

    /// <summary>The session as Stripe has it now; null when Stripe knows no such session. Throws <see cref="StripeGatewayException"/> when Stripe cannot be reached.</summary>
    Task<StripeCheckoutSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}

/// <summary>What is sent to Stripe for one deposit: an amount, a line of text, two addresses and our own two ids.</summary>
/// <param name="AmountMinor">In the currency's smallest unit (pence, grosze).</param>
/// <param name="Currency">Lower case, as Stripe writes it.</param>
/// <param name="ProductName">The one line on the payment page. Never a person's name.</param>
/// <param name="SuccessUrl">Carries Stripe's <c>{CHECKOUT_SESSION_ID}</c> placeholder.</param>
/// <param name="ExpiresAtUnixSeconds">When an unpaid session closes by itself.</param>
public sealed record StripeCheckoutRequest(
    long AmountMinor,
    string Currency,
    string ProductName,
    string SuccessUrl,
    string CancelUrl,
    Guid TenantId,
    Guid AppointmentId,
    long ExpiresAtUnixSeconds);

/// <summary>What is read back from Stripe about a session: whether it was paid, how much, and our own two ids.</summary>
/// <param name="Url">The hosted payment page; set on a session that was just opened.</param>
/// <param name="TenantId">From the session's metadata; null when it carries none of ours.</param>
/// <param name="AppointmentId">From the session's metadata; null when it carries none of ours.</param>
public sealed record StripeCheckoutSession(
    string Id,
    string? Url,
    bool IsPaid,
    long? AmountTotalMinor,
    string? Currency,
    Guid? TenantId,
    Guid? AppointmentId,
    bool LiveMode);

/// <summary>Stripe refused a call or could not be reached. Carries Stripe's error code at most, never a message with data in it.</summary>
public sealed class StripeGatewayException : Exception
{
    public StripeGatewayException()
        : this("Stripe could not be asked.")
    {
    }

    public StripeGatewayException(string message)
        : base(message)
    {
    }

    public StripeGatewayException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}