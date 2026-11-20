using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Salon.Plugin.Payments.Stripe;

/// <summary>
/// <see cref="IStripeGateway"/> over Stripe.net. The only class that talks to Stripe. The key is read from
/// configuration for each call and handed to the SDK; it is never logged, and neither is anything Stripe says in an
/// error, because Stripe's messages can quote part of a key. Calls give up after a short time: a client who is
/// waiting to pay is better sent on with the deposit still due than kept on a white page.
/// </summary>
internal sealed class StripeNetGateway(IOptionsMonitor<StripeOptions> options, ILogger<StripeNetGateway> logger) : IStripeGateway, IDisposable
{
    internal const string TenantIdKey = "tenant_id";
    internal const string AppointmentIdKey = "appointment_id";
    internal const string PaymentMode = "payment";
    internal const string PaidStatus = "paid";

    private const int MaxNetworkRetries = 1;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = Timeout };

    public bool IsConfigured => options.CurrentValue.HasTestModeKey;

    public async Task<StripeCheckoutSession> CreateCheckoutSessionAsync(StripeCheckoutRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var service = new SessionService(Client());
        try
        {
            return Map(await service.CreateAsync(BuildOptions(request), cancellationToken: cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex) when (IsStripeFailure(ex, cancellationToken))
        {
            throw Failure(ex);
        }
    }

    public async Task<StripeCheckoutSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var service = new SessionService(Client());
        try
        {
            return Map(await service.GetAsync(sessionId, cancellationToken: cancellationToken).ConfigureAwait(false));
        }
        catch (StripeException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (IsStripeFailure(ex, cancellationToken))
        {
            throw Failure(ex);
        }
    }

    public void Dispose() => _http.Dispose();

    /// <summary>
    /// One line, in payment mode, with our two ids as the only metadata, on the session and on the payment it makes.
    /// Nothing about the client is sent: no name, phone or email. Stripe asks for an email on its own page.
    /// </summary>
    internal static SessionCreateOptions BuildOptions(StripeCheckoutRequest request)
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TenantIdKey] = request.TenantId.ToString("D"),
            [AppointmentIdKey] = request.AppointmentId.ToString("D"),
        };
        return new SessionCreateOptions
        {
            Mode = PaymentMode,
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            ClientReferenceId = request.AppointmentId.ToString("D"),
            ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(request.ExpiresAtUnixSeconds).UtcDateTime,
            Metadata = ids,
            PaymentIntentData = new SessionPaymentIntentDataOptions { Metadata = new Dictionary<string, string>(ids, StringComparer.Ordinal) },
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = request.Currency,
                        UnitAmount = request.AmountMinor,
                        ProductData = new SessionLineItemPriceDataProductDataOptions { Name = request.ProductName },
                    },
                },
            ],
        };
    }

    internal static StripeCheckoutSession Map(Session session)
        => new(
            session.Id,
            session.Url,
            string.Equals(session.PaymentStatus, PaidStatus, StringComparison.Ordinal),
            session.AmountTotal,
            session.Currency,
            IdFrom(session.Metadata, TenantIdKey),
            IdFrom(session.Metadata, AppointmentIdKey),
            session.Livemode);

    private static Guid? IdFrom(Dictionary<string, string>? metadata, string key)
        => metadata is not null && metadata.TryGetValue(key, out var text) && Guid.TryParseExact(text, "D", out var id) ? id : null;

    private static bool IsStripeFailure(Exception ex, CancellationToken cancellationToken)
        => ex is StripeException or HttpRequestException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private StripeClient Client()
    {
        var current = options.CurrentValue;
        if (!current.HasTestModeKey)
        {
            throw new StripeGatewayException("No test-mode Stripe key is configured.");
        }

        return new StripeClient(apiKey: current.SecretKey, httpClient: new SystemNetHttpClient(_http, MaxNetworkRetries, appInfo: null, enableTelemetry: false));
    }

    /// <summary>Stripe's own words are dropped on purpose: its messages can quote the end of a key.</summary>
    private StripeGatewayException Failure(Exception ex)
    {
        var status = ex is StripeException stripe ? (int)stripe.HttpStatusCode : 0;
        logger.LogWarning("A call to Stripe failed: HTTP status {Status}, a timeout or no connection when 0", status);
        return new StripeGatewayException($"A call to Stripe failed (HTTP status {status}).");
    }
}