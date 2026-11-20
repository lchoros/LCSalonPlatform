using System.Text.Json;
using Stripe;

namespace Salon.Plugin.Payments.Stripe;

/// <summary>
/// Reading a webhook from Stripe (ADR 0082). The signature is checked with Stripe's own verifier, which also refuses
/// one older than five minutes. From a genuine event only two things are taken: its type and the id of the session
/// it is about. Whether that session was paid is then asked from Stripe, exactly as on the client's return.
/// </summary>
public static class StripeWebhook
{
    internal const string CheckoutCompletedEvent = "checkout.session.completed";

    /// <summary>A payment method that settles later (not a card) reports its money with this event instead.</summary>
    internal const string AsyncPaymentSucceededEvent = "checkout.session.async_payment_succeeded";

    private const long ToleranceSeconds = 300;

    /// <summary>True when <paramref name="payload"/> was signed with <paramref name="secret"/> within the last five minutes.</summary>
    public static bool IsGenuine(string? payload, string? signatureHeader, string? secret)
    {
        if (string.IsNullOrEmpty(payload) || string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        try
        {
            EventUtility.ValidateSignature(payload, signatureHeader, secret, ToleranceSeconds);
            return true;
        }
        catch (StripeException)
        {
            return false;
        }
    }

    /// <summary>The id of the Checkout Session a completed-checkout event is about; null for any other event or a body that is not one.</summary>
    public static string? CompletedSessionId(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || type.GetString() is not (CheckoutCompletedEvent or AsyncPaymentSucceededEvent))
            {
                return null;
            }

            return root.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("object", out var session)
                && session.ValueKind == JsonValueKind.Object
                && session.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                    ? id.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}