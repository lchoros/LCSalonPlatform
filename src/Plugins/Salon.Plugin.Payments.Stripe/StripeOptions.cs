namespace Salon.Plugin.Payments.Stripe;

/// <summary>
/// The platform's own Stripe account (section "Stripe", ADR 0082). The values live in user-secrets locally and in the
/// server's own configuration; never in a file of this repository, a log line or a page. The publishable key the
/// owner also keeps there is not read: the hosted Checkout page needs none on our side.
/// </summary>
public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    private const string TestSecretKeyPrefix = "sk_test_";
    private const string TestRestrictedKeyPrefix = "rk_test_";

    /// <summary>The secret (or restricted) key. Only a test-mode key is ever used.</summary>
    public string? SecretKey { get; set; }

    /// <summary>The signing secret of the webhook endpoint; without it the webhook is not read at all.</summary>
    public string? WebhookSecret { get; set; }

    public bool HasSecretKey => !string.IsNullOrWhiteSpace(SecretKey);

    /// <summary>
    /// True for a key of Stripe's test mode. This plugin refuses to run with a live key: it charges on the platform's
    /// account, which is right for a test and wrong for a salon's real money (ADR 0082).
    /// </summary>
    public bool HasTestModeKey
        => SecretKey is { } key
            && (key.StartsWith(TestSecretKeyPrefix, StringComparison.Ordinal) || key.StartsWith(TestRestrictedKeyPrefix, StringComparison.Ordinal));

    public bool HasWebhookSecret => !string.IsNullOrWhiteSpace(WebhookSecret);

    /// <summary>Says what is set, never the values, so the options can never print a key.</summary>
    public override string ToString()
        => $"{nameof(StripeOptions)} {{ HasSecretKey = {HasSecretKey}, HasTestModeKey = {HasTestModeKey}, HasWebhookSecret = {HasWebhookSecret} }}";
}

/// <summary>
/// The host's switch for this plugin, read from the same place as every other feature flag (section "Features", key
/// <c>StripeDeposits</c>). Off unless configuration says otherwise.
/// </summary>
public sealed class StripeFeatureOptions
{
    public const string SectionName = "Features";

    public bool StripeDeposits { get; set; }
}