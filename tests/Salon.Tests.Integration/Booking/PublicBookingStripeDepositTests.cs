using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Data;
using Salon.Plugin.Payments.Stripe;
using Salon.Services.Payments;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Booking;

/// <summary>
/// Stripe in test mode for the deposit of an online booking (ADR 0082), over HTTP against a pretend Stripe
/// (<see cref="FakeStripeGateway"/>): no test reaches Stripe's network. With <c>Features:StripeDeposits</c> off, which
/// is how the shared test host and every real host start, the flow is what it always was. With it on, a verified
/// booking that asks for a deposit is sent to pay exactly that; coming back paid writes one held deposit on the
/// client's account, by "Card online (Stripe)", however often the address or the webhook is hit; coming back unpaid
/// leaves the booking standing with its deposit due. The cut costs £20 and the salon asks for a quarter.
/// </summary>
public partial class PublicBookingTests
{
    private const string WebhookPath = "/webhooks/stripe";
    private const string TestWebhookSecret = "integration-test-webhook-secret";

    [Fact]
    public async Task With_the_Stripe_flag_off_a_booking_that_asks_for_a_deposit_is_exactly_what_it_was()
    {
        await SaveSettingsAsync(_studio.Id, s => s.DefaultDepositPercent = 25m);
        using var client = factory.ClientFor(HostA);

        var details = await GetAsync(client, DetailsPath(At(10, 0), _annaId));
        var (appointment, afterCode) = await BookThroughTheFlowAsync(client, At(10, 0), _annaId);
        var done = await GetAsync(client, afterCode);

        details.ShouldContain("A deposit of £5.00 secures this visit. The salon will tell you how to pay it.");
        afterCode.ShouldBe($"/book/done/{appointment.Id}", "the code leads straight to the confirmation");
        done.ShouldContain("data-testid=\"done-deposit\"");
        done.ShouldContain("The salon will tell you how to pay it.");
        done.ShouldNotContain("data-testid=\"pay-deposit\"");
        details.ShouldNotContain("Stripe");
        done.ShouldNotContain("Stripe");

        // The pay step, its return address and the webhook do not exist.
        (await client.GetAsync(new Uri($"/book/pay/{appointment.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync(new Uri($"/book/pay/{appointment.Id}/return?session_id=cs_test_fake", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var platform = factory.ClientFor("localhost");
        (await platform.PostAsync(new Uri(WebhookPath, UriKind.Relative), new StringContent("{}"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        factory.Services.GetRequiredService<IOnlineDepositCheckout>().IsAvailable.ShouldBeFalse();
        (await DepositLinesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task With_the_flag_on_but_no_key_the_flow_is_also_what_it_was()
    {
        await SaveSettingsAsync(_studio.Id, s => s.DefaultDepositPercent = 25m);

        // The real gateway class, with no key: it cannot make a call, and says so before anyone is sent anywhere.
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Features:StripeDeposits", "true").UseSetting("Stripe:SecretKey", string.Empty));
        using var client = BrowserFor(host, HostA);

        var (appointment, afterCode) = await BookOnAsync(host, client, At(10, 0));

        host.Services.GetRequiredService<IOnlineDepositCheckout>().IsAvailable.ShouldBeFalse();
        afterCode.ShouldBe($"/book/done/{appointment.Id}");
        (await GetAsync(client, afterCode)).ShouldContain("The salon will tell you how to pay it.");
        var pay = await client.GetAsync(new Uri($"/book/pay/{appointment.Id}", UriKind.Relative));
        pay.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        pay.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"/book/done/{appointment.Id}");
    }

    [Fact]
    public async Task With_Stripe_on_a_verified_booking_is_sent_to_pay_exactly_the_deposit_and_nothing_personal_goes_along()
    {
        await SaveSettingsAsync(_studio.Id, s => s.DefaultDepositPercent = 25m);
        var (host, stripe) = StripeHost();
        using var disposeHost = host;
        using var client = BrowserFor(host, HostA);

        var details = await GetAsync(client, DetailsPath(At(10, 0), _annaId));
        var (appointment, afterCode) = await BookOnAsync(host, client, At(10, 0));
        var pay = await client.GetAsync(new Uri(afterCode, UriKind.Relative));

        details.ShouldContain("A deposit of £5.00 secures this visit. Once you have booked, you pay it by card on Stripe's secure payment page.");
        afterCode.ShouldBe($"/book/pay/{appointment.Id}");
        pay.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var session = stripe.Opened.ShouldHaveSingleItem();
        pay.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe(session.Url);

        var sent = stripe.Requests.ShouldHaveSingleItem();
        sent.AmountMinor.ShouldBe(500, "a quarter of the £20 cut, in pence");
        sent.Currency.ShouldBe("gbp");
        sent.TenantId.ShouldBe(_studio.Id);
        sent.AppointmentId.ShouldBe(appointment.Id);
        sent.ProductName.ShouldBe("Deposit for Cut at Studio 20");
        sent.SuccessUrl.ShouldBe($"https://studio-20.localhost/book/pay/{appointment.Id}/return?session_id={{CHECKOUT_SESSION_ID}}");
        sent.CancelUrl.ShouldBe($"https://studio-20.localhost/book/done/{appointment.Id}");
        foreach (var personal in new[] { "Olivia", "Brown", "7700", "900123", "example.com" })
        {
            sent.ToString().ShouldNotContain(personal, Case.Insensitive);
        }

        // The booking stands whatever happens next; nothing is on the account until Stripe says it was paid.
        appointment.Status.ShouldBe(AppointmentStatus.Booked);
        appointment.DepositAmount.ShouldBe(5m);
        (await DepositLinesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Coming_back_paid_records_one_held_deposit_by_Stripe_however_often_the_address_is_opened()
    {
        await SaveSettingsAsync(_studio.Id, s => s.DefaultDepositPercent = 25m);
        var (host, stripe) = StripeHost();
        using var disposeHost = host;
        using var client = BrowserFor(host, HostA);
        var (appointment, session) = await BookAndOpenCheckoutAsync(host, stripe, client, At(10, 0));
        var returnPath = $"/book/pay/{appointment.Id}/return?session_id={session.Id}";

        stripe.Pay(session.Id);
        var first = await client.GetAsync(new Uri(returnPath, UriKind.Relative));
        var second = await client.GetAsync(new Uri(returnPath, UriKind.Relative));

        foreach (var back in new[] { first, second })
        {
            back.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            back.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"/book/done/{appointment.Id}");
        }

        var line = (await DepositLinesAsync()).ShouldHaveSingleItem();
        line.Kind.ShouldBe(ClientAccountEntryKind.DepositPaid);
        line.AppointmentId.ShouldBe(appointment.Id);
        line.ClientId.ShouldBe(appointment.ClientId);
        (line.Amount, line.Currency).ShouldBe((5m, "GBP"));
        line.Method.ShouldBe(PaymentMethod.OnlineCard);
        line.CustomMethodId.ShouldBeNull();
        line.ProviderSystemName.ShouldBe("Payments.Stripe");
        line.ProviderReference.ShouldBe(session.Id);
        line.ByUserId.ShouldBeNull("nobody at the desk recorded it");

        // Held, for the fee and the checkout to count (ADR 0023, ADR 0024).
        var deposit = await DepositAsync(appointment.Id);
        deposit.Deposit.ShouldNotBeNull().IsHeld.ShouldBeTrue();
        deposit.Deposit.Amount.Amount.ShouldBe(5m);

        // The confirmation says so, and there is nothing left to pay online.
        var done = await GetAsync(client, $"/book/done/{appointment.Id}");
        done.ShouldContain("data-testid=\"done-deposit-paid\"");
        done.ShouldContain("Paid. Card online (Stripe)");
        done.ShouldContain("£5.00");
        done.ShouldNotContain("data-testid=\"pay-deposit\"");
        var again = await client.GetAsync(new Uri($"/book/pay/{appointment.Id}", UriKind.Relative));
        again.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"/book/done/{appointment.Id}");
        stripe.Requests.Count.ShouldBe(1, "a paid deposit opens no second session");

        // The desk reads the method by its name on the visit's deposit page.
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var page = await GetAsync(desk, $"/admin/payments/deposits/{appointment.Id}");
        page.ShouldContain("Card online (Stripe)");
        page.ShouldNotContain("data-testid=\"record-deposit-form\"", customMessage: "the visit has its deposit");

        // What the gateway wrote stands as written: the desk cannot move it to another method (ADR 0064).
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var corrections = scope.ServiceProvider.GetRequiredService<IMethodCorrectionService>();
        (await corrections.GetAsync(line.Id)).ShouldBeNull();
        (await corrections.CorrectAsync(line.Id, PaymentMethod.Cash, byUserId: null)).Outcome.ShouldBe(MethodCorrectionOutcome.NotCorrectable);
        (await DepositLinesAsync()).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Coming_back_without_paying_leaves_the_booking_standing_with_its_deposit_due()
    {
        await SaveSettingsAsync(_studio.Id, s => s.DefaultDepositPercent = 25m);
        var (host, stripe) = StripeHost();
        using var disposeHost = host;
        using var client = BrowserFor(host, HostA);
        var (appointment, session) = await BookAndOpenCheckoutAsync(host, stripe, client, At(10, 0));

        // "Back" on Stripe's page leads to the confirmation: booked, deposit not paid, and a way to pay it.
        var done = await GetAsync(client, new Uri(stripe.Requests.Single().CancelUrl).PathAndQuery);
        done.ShouldContain("Your appointment is booked");
        done.ShouldContain("data-testid=\"done-deposit-due\"");
        done.ShouldContain("Not paid yet. You can pay it by card now, or the salon will tell you how to pay it.");
        done.ShouldContain($"href=\"/book/pay/{appointment.Id}\" data-testid=\"pay-deposit\"");

        // The return address proves nothing by itself: an unpaid session, a made-up one, none at all, something else.
        string[] attempts =
        [
            $"/book/pay/{appointment.Id}/return?session_id={session.Id}",
            $"/book/pay/{appointment.Id}/return?session_id=cs_test_fakenobodyopenedthis",
            $"/book/pay/{appointment.Id}/return",
            $"/book/pay/{appointment.Id}/return?session_id=paid",
        ];
        foreach (var attempt in attempts)
        {
            var back = await client.GetAsync(new Uri(attempt, UriKind.Relative));
            back.StatusCode.ShouldBe(HttpStatusCode.Redirect, attempt);
            back.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"/book/done/{appointment.Id}");
        }

        (await DepositLinesAsync()).ShouldBeEmpty();
        var stands = (await AppointmentsAsync(_studio.Id)).ShouldHaveSingleItem();
        stands.Status.ShouldBe(AppointmentStatus.Booked, "an unpaid deposit cancels nothing (ADR 0023)");
        stands.DepositAmount.ShouldBe(5m);
        (await DepositAsync(appointment.Id)).Deposit.ShouldBeNull();

        // Paying later is the same step again, with a session of its own.
        var retry = await client.GetAsync(new Uri($"/book/pay/{appointment.Id}", UriKind.Relative));
        retry.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe(stripe.Opened[1].Url);
        stripe.Requests[1].AmountMinor.ShouldBe(500);
    }

    [Fact]
    public async Task Money_that_is_not_what_the_booking_asked_for_or_belongs_elsewhere_is_never_recorded()
    {
        await SaveSettingsAsync(_studio.Id, s => s.DefaultDepositPercent = 25m);
        var (host, stripe) = StripeHost();
        using var disposeHost = host;
        using var client = BrowserFor(host, HostA);
        var (appointment, session) = await BookAndOpenCheckoutAsync(host, stripe, client, At(10, 0));
        var returnPath = $"/book/pay/{appointment.Id}/return?session_id={session.Id}";

        // Another amount, then another currency: left for a person, not written.
        stripe.Pay(session.Id, amountMinor: 50);
        await client.GetAsync(new Uri(returnPath, UriKind.Relative));
        stripe.Pay(session.Id, amountMinor: 500, currency: "pln");
        await client.GetAsync(new Uri(returnPath, UriKind.Relative));
        (await DepositLinesAsync()).ShouldBeEmpty();

        // Paid in full, but carried to another salon's address, or to another visit's: not theirs to record.
        stripe.Pay(session.Id, amountMinor: 500, currency: "gbp");
        using var polish = BrowserFor(host, HostB);
        await polish.GetAsync(new Uri(returnPath, UriKind.Relative));
        await client.GetAsync(new Uri($"/book/pay/{Guid.CreateVersion7()}/return?session_id={session.Id}", UriKind.Relative));
        (await DepositLinesAsync()).ShouldBeEmpty();

        // And at its own address it is.
        await client.GetAsync(new Uri(returnPath, UriKind.Relative));
        (await DepositLinesAsync()).ShouldHaveSingleItem().ProviderReference.ShouldBe(session.Id);
    }

    [Fact]
    public async Task The_webhook_refuses_a_bad_signature_and_a_genuine_one_shares_the_one_line_with_the_return()
    {
        await SaveSettingsAsync(_studio.Id, s => s.DefaultDepositPercent = 25m);
        var (host, stripe) = StripeHost();
        using var disposeHost = host;
        using var client = BrowserFor(host, HostA);
        var (appointment, session) = await BookAndOpenCheckoutAsync(host, stripe, client, At(10, 0));
        stripe.Pay(session.Id);
        var payload = CompletedEvent(session.Id);

        // Stripe posts to the platform's own address: no salon in the request.
        using var platform = BrowserFor(host, "localhost");
        var readsBefore = stripe.Reads;
        (await PostWebhookAsync(platform, payload, StripeSignature(payload, "somebody-elses-secret"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostWebhookAsync(platform, payload, signature: null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostWebhookAsync(platform, payload, StripeSignature(payload, TestWebhookSecret, DateTimeOffset.UtcNow.AddMinutes(-10)))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostWebhookAsync(platform, CompletedEvent("cs_test_fakeother"), StripeSignature(payload, TestWebhookSecret))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await DepositLinesAsync()).ShouldBeEmpty();
        stripe.Reads.ShouldBe(readsBefore, "nothing of a refused body is acted on");

        // Genuine: recorded. Stripe delivers again, and the client's browser comes back as well: still one line.
        (await PostWebhookAsync(platform, payload, StripeSignature(payload, TestWebhookSecret))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostWebhookAsync(platform, payload, StripeSignature(payload, TestWebhookSecret))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await client.GetAsync(new Uri($"/book/pay/{appointment.Id}/return?session_id={session.Id}", UriKind.Relative));

        var line = (await DepositLinesAsync()).ShouldHaveSingleItem();
        line.Method.ShouldBe(PaymentMethod.OnlineCard);
        line.ProviderReference.ShouldBe(session.Id);
        (line.Amount, line.Currency).ShouldBe((5m, "GBP"));

        // Genuine events this app does not act on are acknowledged and change nothing.
        var other = "{\"type\":\"payment_intent.succeeded\",\"data\":{\"object\":{\"id\":\"pi_test_fake\"}}}";
        (await PostWebhookAsync(platform, other, StripeSignature(other, TestWebhookSecret))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await DepositLinesAsync()).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_webhook_alone_records_the_deposit_of_a_client_who_never_came_back_and_without_a_secret_it_does_not_exist()
    {
        await SaveSettingsAsync(_studio.Id, s => s.DefaultDepositPercent = 25m);
        var (host, stripe) = StripeHost();
        using var disposeHost = host;
        using var client = BrowserFor(host, HostA);
        var (appointment, session) = await BookAndOpenCheckoutAsync(host, stripe, client, At(10, 0));
        var payload = CompletedEvent(session.Id);
        using var platform = BrowserFor(host, "localhost");

        // Completed but not paid (it cannot be for a card, but the answer is Stripe's, not the event's): nothing.
        (await PostWebhookAsync(platform, payload, StripeSignature(payload, TestWebhookSecret))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await DepositLinesAsync()).ShouldBeEmpty();

        stripe.Pay(session.Id);
        (await PostWebhookAsync(platform, payload, StripeSignature(payload, TestWebhookSecret))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await DepositLinesAsync()).ShouldHaveSingleItem().AppointmentId.ShouldBe(appointment.Id);
        (await GetAsync(client, $"/book/done/{appointment.Id}")).ShouldContain("Paid. Card online (Stripe)");

        // A host that was given no webhook secret cannot verify anything, so it reads nothing.
        var (noSecret, _) = StripeHost(webhookSecret: null);
        using var disposeNoSecret = noSecret;
        using var unverified = BrowserFor(noSecret, "localhost");
        (await PostWebhookAsync(unverified, payload, StripeSignature(payload, TestWebhookSecret))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task With_Stripe_on_a_booking_without_a_deposit_goes_straight_to_its_confirmation()
    {
        var (host, stripe) = StripeHost();
        using var disposeHost = host;
        using var client = BrowserFor(host, HostA);

        var (appointment, afterCode) = await BookOnAsync(host, client, At(10, 0));
        var pay = await client.GetAsync(new Uri(afterCode, UriKind.Relative));

        afterCode.ShouldBe($"/book/pay/{appointment.Id}");
        pay.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        pay.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"/book/done/{appointment.Id}");
        stripe.Requests.ShouldBeEmpty("no deposit, no session");
        var done = await GetAsync(client, $"/book/done/{appointment.Id}");
        done.ShouldNotContain("data-testid=\"done-deposit\"");
        done.ShouldNotContain("data-testid=\"pay-deposit\"");

        // Not an online booking of this salon: no pay step at all.
        (await client.GetAsync(new Uri($"/book/pay/{Guid.CreateVersion7()}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var polish = BrowserFor(host, HostB);
        (await polish.GetAsync(new Uri($"/book/pay/{appointment.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_deposit_paid_after_the_visit_was_cancelled_is_still_on_the_account_so_it_can_be_given_back()
    {
        await SaveSettingsAsync(_studio.Id, s => s.DefaultDepositPercent = 25m);
        var (host, stripe) = StripeHost();
        using var disposeHost = host;
        using var client = BrowserFor(host, HostA);
        var (appointment, session) = await BookAndOpenCheckoutAsync(host, stripe, client, At(10, 0));

        // The client cancels in another tab and then pays on the page that was still open.
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Appointments
                .Where(a => a.Id == appointment.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AppointmentStatus.CancelledByClient));
        }

        stripe.Pay(session.Id);
        await client.GetAsync(new Uri($"/book/pay/{appointment.Id}/return?session_id={session.Id}", UriKind.Relative));

        (await DepositAsync(appointment.Id)).Deposit.ShouldNotBeNull().IsHeld.ShouldBeTrue("the salon holds the money, so the desk must see it to return it");

        // A cancelled visit is not offered the pay step again.
        var pay = await client.GetAsync(new Uri($"/book/pay/{appointment.Id}", UriKind.Relative));
        pay.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"/book/done/{appointment.Id}");
        stripe.Requests.Count.ShouldBe(1);
    }

    /// <summary>A host like the shared one with the flag on, a webhook secret and the pretend Stripe in place of the real gateway.</summary>
    private (WebApplicationFactory<Program> Host, FakeStripeGateway Stripe) StripeHost(string? webhookSecret = TestWebhookSecret)
    {
        var stripe = new FakeStripeGateway();
        var host = factory.WithWebHostBuilder(b => b
            .UseSetting("Features:StripeDeposits", "true")
            .UseSetting("Stripe:WebhookSecret", webhookSecret ?? string.Empty)
            .ConfigureServices(services =>
            {
                services.RemoveAll<IStripeGateway>();
                services.AddSingleton<IStripeGateway>(stripe);
            }));
        return (host, stripe);
    }

    private static HttpClient BrowserFor(WebApplicationFactory<Program> host, string hostName)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri($"https://{hostName}") });
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
        return client;
    }

    /// <summary>Details, then the code this host's "phone" received; returns the booking and where the code led.</summary>
    private async Task<(Appointment Appointment, string AfterCode)> BookOnAsync(WebApplicationFactory<Program> host, HttpClient client, Instant start)
    {
        var verifyPath = await StartAsync(client, Fields(start, _annaId));
        var code = host.Services.GetRequiredService<RecordingSmsSender>().LastCodeFor(PhoneE164).ShouldNotBeNull();
        var response = await PostCodeAsync(client, verifyPath, code);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
        var appointment = (await AppointmentsAsync(_studio.Id)).Single(a => a.Start == start);
        return (appointment, response.Headers.Location.ShouldNotBeNull().OriginalString);
    }

    /// <summary>Books and follows the pay step to the pretend Stripe; returns the booking and the session opened for it.</summary>
    private async Task<(Appointment Appointment, StripeCheckoutSession Session)> BookAndOpenCheckoutAsync(WebApplicationFactory<Program> host, FakeStripeGateway stripe, HttpClient client, Instant start)
    {
        var (appointment, afterCode) = await BookOnAsync(host, client, start);
        var pay = await client.GetAsync(new Uri(afterCode, UriKind.Relative));
        pay.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var session = stripe.Opened.ShouldHaveSingleItem();
        pay.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe(session.Url);
        return (appointment, session);
    }

    private async Task<List<ClientAccountEntry>> DepositLinesAsync()
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().ClientAccountEntries.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
    }

    private async Task<VisitDeposit> DepositAsync(Guid appointmentId)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return (await scope.ServiceProvider.GetRequiredService<IClientAccountService>().GetDepositAsync(appointmentId)).ShouldNotBeNull();
    }

    private static string CompletedEvent(string sessionId)
        => "{\"id\":\"evt_test_fake\",\"object\":\"event\",\"type\":\"checkout.session.completed\",\"data\":{\"object\":{\"id\":\"" + sessionId + "\",\"object\":\"checkout.session\"}}}";

    /// <summary>Stripe's scheme, worked out by hand: HMAC-SHA256 of "timestamp.payload" with the endpoint's secret.</summary>
    private static string StripeSignature(string payload, string secret, DateTimeOffset? at = null)
    {
        var timestamp = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return $"t={timestamp},v1={Convert.ToHexStringLower(mac)}";
    }

    private static async Task<HttpResponseMessage> PostWebhookAsync(HttpClient client, string payload, string? signature)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(WebhookPath, UriKind.Relative))
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        if (signature is not null)
        {
            request.Headers.TryAddWithoutValidation("Stripe-Signature", signature);
        }

        return await client.SendAsync(request);
    }
}