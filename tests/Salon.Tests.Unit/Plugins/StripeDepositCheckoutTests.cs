using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NodaTime;
using Salon.Core.Domain;
using Salon.Core.Payments;
using Salon.Plugin.Payments.Stripe;

namespace Salon.Tests.Unit.Plugins;

/// <summary>
/// The Stripe plugin against a pretend Stripe (ADR 0082): what is sent for a deposit (pence, a lower-case code, our two
/// ids and nothing about a person), what a session read back is taken to mean, and which webhooks are believed. No
/// test here, or anywhere, reaches Stripe's network.
/// </summary>
public class StripeDepositCheckoutTests
{
    private const string WebhookSecret = "unit-test-webhook-secret";
    private static readonly Guid Tenant = Guid.CreateVersion7();
    private static readonly Guid Appointment = Guid.CreateVersion7();
    private static readonly Instant Now = Instant.FromUtc(2026, 11, 20, 10, 0);

    private readonly FakeGateway _gateway = new();

    private StripeDepositCheckout Checkout(bool featureOn = true, string? webhookSecret = WebhookSecret)
        => new(
            _gateway,
            new Fixed<StripeFeatureOptions>(new StripeFeatureOptions { StripeDeposits = featureOn }),
            new Fixed<StripeOptions>(new StripeOptions { WebhookSecret = webhookSecret }),
            new FixedClock(Now),
            NullLogger<StripeDepositCheckout>.Instance);

    private static OnlineCheckoutRequest Request(decimal amount = 11.25m, string currency = "GBP")
        => new(Tenant, Appointment, new Money(amount, currency), "Deposit for Cut & blow-dry at Studio 20", "https://studio-20.salon.example/book/pay/1/return", "https://studio-20.salon.example/book/done/1");

    [Fact]
    public async Task A_deposit_is_sent_as_pence_with_our_two_ids_and_nothing_about_a_person()
    {
        var start = await Checkout().StartAsync(Request());

        start.RedirectUrl.ShouldBe(_gateway.Opened.ShouldHaveSingleItem().Url);
        var sent = _gateway.Requests.ShouldHaveSingleItem();
        sent.AmountMinor.ShouldBe(1125);
        sent.Currency.ShouldBe("gbp");
        sent.ProductName.ShouldBe("Deposit for Cut & blow-dry at Studio 20");
        sent.TenantId.ShouldBe(Tenant);
        sent.AppointmentId.ShouldBe(Appointment);
        sent.SuccessUrl.ShouldBe("https://studio-20.salon.example/book/pay/1/return?session_id={CHECKOUT_SESSION_ID}");
        sent.CancelUrl.ShouldBe("https://studio-20.salon.example/book/done/1");
        sent.ExpiresAtUnixSeconds.ShouldBe((Now + Duration.FromMinutes(35)).ToUnixTimeSeconds(), "an unpaid session closes by itself");
    }

    [Fact]
    public void What_Stripe_is_told_is_one_line_in_payment_mode_and_ids_only_in_the_metadata()
    {
        var request = new StripeCheckoutRequest(1125, "gbp", "Deposit for Cut", "https://s.example/return?session_id={CHECKOUT_SESSION_ID}", "https://s.example/done", Tenant, Appointment, Now.ToUnixTimeSeconds());

        var options = StripeNetGateway.BuildOptions(request);

        options.Mode.ShouldBe("payment");
        options.SuccessUrl.ShouldBe(request.SuccessUrl);
        options.CancelUrl.ShouldBe(request.CancelUrl);
        var line = options.LineItems.ShouldHaveSingleItem();
        line.Quantity.ShouldBe(1);
        line.PriceData.UnitAmount.ShouldBe(1125);
        line.PriceData.Currency.ShouldBe("gbp");
        line.PriceData.ProductData.Name.ShouldBe("Deposit for Cut");
        var ids = new Dictionary<string, string> { ["tenant_id"] = Tenant.ToString(), ["appointment_id"] = Appointment.ToString() };
        options.Metadata.ShouldBe(ids, ignoreOrder: true);
        options.PaymentIntentData.Metadata.ShouldBe(ids, ignoreOrder: true);
        options.ClientReferenceId.ShouldBe(Appointment.ToString());
        options.ExpiresAt.ShouldBe(Now.ToDateTimeUtc());

        // Nothing that could carry the client: Stripe asks for what it needs on its own page.
        options.CustomerEmail.ShouldBeNull();
        options.Customer.ShouldBeNull();
        options.CustomerCreation.ShouldBeNull();
        options.PhoneNumberCollection.ShouldBeNull();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task With_the_flag_off_or_no_test_key_nothing_is_opened_and_nothing_is_asked(bool featureOn, bool configured)
    {
        _gateway.IsConfigured = configured;
        var checkout = Checkout(featureOn);

        checkout.IsAvailable.ShouldBeFalse();
        (await checkout.StartAsync(Request())).RedirectUrl.ShouldBeNull();
        (await checkout.ReadAsync("cs_test_unit_one")).State.ShouldBe(OnlineCheckoutState.Unavailable);
        _gateway.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData(9, "JPY")]
    [InlineData(9.005, "GBP")]
    [InlineData(0, "GBP")]
    public async Task An_amount_Stripe_cannot_be_given_to_the_penny_opens_no_session(decimal amount, string currency)
    {
        (await Checkout().StartAsync(Request(amount, currency))).RedirectUrl.ShouldBeNull();

        _gateway.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task When_Stripe_refuses_or_is_away_the_booking_simply_goes_on_without_it()
    {
        _gateway.Fails = true;

        (await Checkout().StartAsync(Request())).RedirectUrl.ShouldBeNull();
        (await Checkout().ReadAsync("cs_test_unit_one")).State.ShouldBe(OnlineCheckoutState.Unavailable);
    }

    [Fact]
    public async Task A_paid_session_reads_as_the_money_the_ledger_records()
    {
        var checkout = Checkout();
        await checkout.StartAsync(Request());
        var session = _gateway.Opened.Single();
        _gateway.Pay(session.Id);

        var reading = await checkout.ReadAsync(session.Id);

        reading.State.ShouldBe(OnlineCheckoutState.Paid);
        reading.Payment.ShouldBe(new OnlineDepositPayment(Tenant, Appointment, new Money(11.25m, "GBP"), "Payments.Stripe", session.Id));
    }

    [Fact]
    public async Task A_session_nobody_paid_is_not_a_payment()
    {
        var checkout = Checkout();
        await checkout.StartAsync(Request());

        var reading = await checkout.ReadAsync(_gateway.Opened.Single().Id);

        reading.State.ShouldBe(OnlineCheckoutState.NotPaid);
        reading.Payment.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pi_test_unit_one")]
    [InlineData("cs_test_unit one")]
    [InlineData("cs_test_unit/../other")]
    [InlineData("cs_test_unit\r\nforged log line")]
    public async Task Something_that_is_not_a_session_id_is_never_sent_to_Stripe(string? sessionId)
    {
        (await Checkout().ReadAsync(sessionId)).State.ShouldBe(OnlineCheckoutState.Unknown);

        _gateway.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_session_Stripe_does_not_know_or_one_without_our_ids_is_unknown()
    {
        var checkout = Checkout();
        _gateway.Add(new StripeCheckoutSession("cs_test_unit_bare", null, IsPaid: true, 1125, "gbp", TenantId: null, AppointmentId: null, LiveMode: false));
        _gateway.Add(new StripeCheckoutSession("cs_test_unit_half", null, IsPaid: true, 1125, "gbp", Tenant, AppointmentId: null, LiveMode: false));

        (await checkout.ReadAsync("cs_test_unit_missing")).State.ShouldBe(OnlineCheckoutState.Unknown);
        (await checkout.ReadAsync("cs_test_unit_bare")).State.ShouldBe(OnlineCheckoutState.Unknown);
        (await checkout.ReadAsync("cs_test_unit_half")).State.ShouldBe(OnlineCheckoutState.Unknown);
    }

    [Fact]
    public async Task A_live_session_or_money_in_an_unmapped_currency_is_never_read_as_paid()
    {
        var checkout = Checkout();
        _gateway.Add(new StripeCheckoutSession("cs_live_unit_one", null, IsPaid: true, 1125, "gbp", Tenant, Appointment, LiveMode: true));
        _gateway.Add(new StripeCheckoutSession("cs_test_unit_yen", null, IsPaid: true, 1125, "jpy", Tenant, Appointment, LiveMode: false));
        _gateway.Add(new StripeCheckoutSession("cs_test_unit_zero", null, IsPaid: true, 0, "gbp", Tenant, Appointment, LiveMode: false));

        (await checkout.ReadAsync("cs_live_unit_one")).State.ShouldBe(OnlineCheckoutState.Unknown);
        (await checkout.ReadAsync("cs_test_unit_yen")).State.ShouldBe(OnlineCheckoutState.Unknown);
        (await checkout.ReadAsync("cs_test_unit_zero")).State.ShouldBe(OnlineCheckoutState.Unknown);
    }

    [Fact]
    public void A_webhook_signed_with_our_secret_names_the_completed_session()
    {
        var payload = Event("checkout.session.completed", "cs_test_unit_one");

        var reading = Checkout().ReadWebhook(payload, Sign(payload, WebhookSecret));

        reading.ShouldBe(new OnlineWebhookReading(OnlineWebhookState.CheckoutCompleted, "cs_test_unit_one"));
    }

    [Fact]
    public void A_webhook_with_a_wrong_missing_or_old_signature_is_refused()
    {
        var payload = Event("checkout.session.completed", "cs_test_unit_one");
        var checkout = Checkout();
        var old = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();

        checkout.ReadWebhook(payload, Sign(payload, "somebody-elses-secret")).State.ShouldBe(OnlineWebhookState.BadSignature);
        checkout.ReadWebhook(payload, null).State.ShouldBe(OnlineWebhookState.BadSignature);
        checkout.ReadWebhook(payload, "not a signature").State.ShouldBe(OnlineWebhookState.BadSignature);
        checkout.ReadWebhook(payload, Sign(payload, WebhookSecret, old)).State.ShouldBe(OnlineWebhookState.BadSignature, "a captured webhook cannot be played again later");

        // Signed for one body, sent with another: the session named is not believed.
        var tampered = Event("checkout.session.completed", "cs_test_unit_two");
        checkout.ReadWebhook(tampered, Sign(payload, WebhookSecret)).State.ShouldBe(OnlineWebhookState.BadSignature);
        checkout.ReadWebhook(string.Empty, Sign(string.Empty, WebhookSecret)).State.ShouldBe(OnlineWebhookState.BadSignature);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(true, " ")]
    [InlineData(false, WebhookSecret)]
    public void Without_a_webhook_secret_or_with_the_flag_off_no_webhook_is_read(bool featureOn, string? secret)
    {
        var payload = Event("checkout.session.completed", "cs_test_unit_one");

        Checkout(featureOn, secret).ReadWebhook(payload, Sign(payload, WebhookSecret)).State.ShouldBe(OnlineWebhookState.NotConfigured);
    }

    [Fact]
    public void A_genuine_webhook_about_anything_else_is_ignored()
    {
        var checkout = Checkout();
        string[] others =
        [
            Event("payment_intent.succeeded", "pi_test_unit_one"),
            Event("checkout.session.expired", "cs_test_unit_one"),
            Event("checkout.session.completed", "not a session id"),
            "{\"type\":\"checkout.session.completed\"}",
            "[]",
            "not json",
        ];

        foreach (var payload in others)
        {
            checkout.ReadWebhook(payload, Sign(payload, WebhookSecret)).ShouldBe(new OnlineWebhookReading(OnlineWebhookState.Ignored), payload);
        }

        // A payment that settles later says so with its own event; it names the session the same way.
        var later = Event("checkout.session.async_payment_succeeded", "cs_test_unit_one");
        checkout.ReadWebhook(later, Sign(later, WebhookSecret)).State.ShouldBe(OnlineWebhookState.CheckoutCompleted);
    }

    [Fact]
    public void Only_a_test_mode_key_counts_and_the_options_never_print_one()
    {
        // Built from pieces so that nothing shaped like a key stands in the source.
        var test = new StripeOptions { SecretKey = "sk_" + "test_" + "unit", WebhookSecret = WebhookSecret };
        var restricted = new StripeOptions { SecretKey = "rk_" + "test_" + "unit" };
        var live = new StripeOptions { SecretKey = "sk_" + "live_" + "unit" };

        test.HasTestModeKey.ShouldBeTrue();
        restricted.HasTestModeKey.ShouldBeTrue();
        live.HasSecretKey.ShouldBeTrue();
        live.HasTestModeKey.ShouldBeFalse("a live key would charge real cards on the platform's own account");
        new StripeOptions().HasTestModeKey.ShouldBeFalse();
        new StripeOptions { SecretKey = " " }.HasSecretKey.ShouldBeFalse();

        test.ToString().ShouldNotContain("unit");
        test.ToString().ShouldNotContain(WebhookSecret);
        test.ToString().ShouldContain("HasTestModeKey = True");
    }

    private static string Event(string type, string objectId)
        => System.Text.Json.JsonSerializer.Serialize(new { id = "evt_test_unit", @object = "event", type, data = new { @object = new { id = objectId, @object = "checkout.session" } } });

    /// <summary>Stripe's scheme, worked out by hand: HMAC-SHA256 of "timestamp.payload" with the endpoint's secret.</summary>
    private static string Sign(string payload, string secret, long? timestamp = null)
    {
        var at = (timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString(CultureInfo.InvariantCulture);
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{at}.{payload}"));
        return $"t={at},v1={Convert.ToHexStringLower(mac)}";
    }

    private sealed class Fixed<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class FixedClock(Instant now) : IClock
    {
        public Instant GetCurrentInstant() => now;
    }

    /// <summary>A pretend Stripe: sessions in memory, paid when the test says so.</summary>
    private sealed class FakeGateway : IStripeGateway
    {
        private readonly Dictionary<string, StripeCheckoutSession> _sessions = new(StringComparer.Ordinal);

        public bool IsConfigured { get; set; } = true;

        public bool Fails { get; set; }

        public int Calls { get; private set; }

        public List<StripeCheckoutRequest> Requests { get; } = [];

        public List<StripeCheckoutSession> Opened { get; } = [];

        public void Add(StripeCheckoutSession session) => _sessions[session.Id] = session;

        public void Pay(string sessionId) => _sessions[sessionId] = _sessions[sessionId] with { IsPaid = true };

        public Task<StripeCheckoutSession> CreateCheckoutSessionAsync(StripeCheckoutRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Fails)
            {
                throw new StripeGatewayException();
            }

            Requests.Add(request);
            var id = $"cs_test_unit_{Requests.Count}";
            var session = new StripeCheckoutSession(id, $"https://checkout.stripe.example/pay/{id}", IsPaid: false, request.AmountMinor, request.Currency, request.TenantId, request.AppointmentId, LiveMode: false);
            Add(session);
            Opened.Add(session);
            return Task.FromResult(session);
        }

        public Task<StripeCheckoutSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Fails ? throw new StripeGatewayException() : Task.FromResult(_sessions.GetValueOrDefault(sessionId));
        }
    }
}