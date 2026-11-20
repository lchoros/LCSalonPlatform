using System.Collections.Concurrent;
using Salon.Plugin.Payments.Stripe;

namespace Salon.Tests.Integration.Infrastructure;

/// <summary>
/// A pretend Stripe for the tests that switch <c>Features:StripeDeposits</c> on (ADR 0082): sessions live in memory
/// and are paid when the test says so. It takes the place of the one class that talks to Stripe, so nothing here, or
/// anywhere in the suite, reaches Stripe's network, and no key is needed.
/// </summary>
public sealed class FakeStripeGateway : IStripeGateway
{
    public const string CheckoutBaseUrl = "https://checkout.stripe.test/c/pay/";

    private readonly ConcurrentDictionary<string, StripeCheckoutSession> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<StripeCheckoutRequest> _requests = new();
    private readonly ConcurrentQueue<StripeCheckoutSession> _opened = new();
    private int _reads;

    public bool IsConfigured => true;

    /// <summary>Everything the app asked Stripe to open, oldest first.</summary>
    public IReadOnlyList<StripeCheckoutRequest> Requests => [.. _requests];

    /// <summary>The sessions opened for those requests, oldest first.</summary>
    public IReadOnlyList<StripeCheckoutSession> Opened => [.. _opened];

    /// <summary>How often the app asked about a session.</summary>
    public int Reads => _reads;

    public Task<StripeCheckoutSession> CreateCheckoutSessionAsync(StripeCheckoutRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _requests.Enqueue(request);
        var id = $"cs_test_fake{Guid.CreateVersion7():N}";
        var session = new StripeCheckoutSession(id, CheckoutBaseUrl + id, IsPaid: false, request.AmountMinor, request.Currency, request.TenantId, request.AppointmentId, LiveMode: false);
        _sessions[id] = session;
        _opened.Enqueue(session);
        return Task.FromResult(session);
    }

    public Task<StripeCheckoutSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _reads);
        return Task.FromResult(_sessions.GetValueOrDefault(sessionId));
    }

    /// <summary>The client paid on Stripe's page: exactly what was asked, unless the test says another amount or currency.</summary>
    public void Pay(string sessionId, long? amountMinor = null, string? currency = null)
    {
        var session = _sessions[sessionId];
        _sessions[sessionId] = session with { IsPaid = true, AmountTotalMinor = amountMinor ?? session.AmountTotalMinor, Currency = currency ?? session.Currency };
    }
}