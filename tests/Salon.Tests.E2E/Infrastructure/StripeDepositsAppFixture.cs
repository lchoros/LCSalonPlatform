using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Plugin.Payments.Stripe;
using Salon.Services.Settings;

namespace Salon.Tests.E2E.Infrastructure;

/// <summary>
/// The E2E host with <c>Features:StripeDeposits</c> on and a pretend Stripe (ADR 0082). The flag is set for this
/// host only, so every other browser test runs with it off, as a real host does. The pretend Stripe takes the place of
/// the one class that talks to Stripe and serves its "hosted payment page" from a test-only address of this same
/// server: nothing reaches Stripe's network and no key is needed.
/// </summary>
public sealed class StripeDepositsAppFixture : KestrelAppFixture
{
    public PretendStripe Stripe { get; } = new();

    /// <summary>Sets the share of the price the salon asks for as a deposit on every booking (ADR 0023).</summary>
    public async Task AskForDepositAsync(Guid tenantId, decimal percent)
    {
        using var scope = HostServices.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMutableTenantContext>().SetTenant(tenantId);
        var settings = scope.ServiceProvider.GetRequiredService<ISettingService>();
        var booking = await settings.LoadAsync<BookingSettings>();
        booking.DefaultDepositPercent = percent;
        await settings.SaveAsync(booking);
    }

    protected override void ConfigureHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseSetting("Features:StripeDeposits", "true");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IStripeGateway>();
            services.AddSingleton<IStripeGateway>(Stripe);
            services.AddSingleton<IStartupFilter>(Stripe);
        });
    }
}

/// <summary>
/// Stripe as the browser test needs it: sessions in memory, and a plain page under <see cref="PagePrefix"/> with one
/// button that pays and one that goes back, each leading where the app told "Stripe" to send the client. The
/// addresses are followed by their path only, so the browser stays on the test server whatever port it got.
/// </summary>
public sealed class PretendStripe : IStripeGateway, IStartupFilter
{
    public const string PagePrefix = "/test-only/pretend-stripe-checkout";

    private readonly ConcurrentDictionary<string, (StripeCheckoutRequest Request, StripeCheckoutSession Session)> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<StripeCheckoutRequest> _requests = new();

    public bool IsConfigured => true;

    /// <summary>Everything the app asked "Stripe" to open, oldest first.</summary>
    public IReadOnlyList<StripeCheckoutRequest> Requests => [.. _requests];

    public Task<StripeCheckoutSession> CreateCheckoutSessionAsync(StripeCheckoutRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _requests.Enqueue(request);
        var id = $"cs_test_e2e{Guid.CreateVersion7():N}";
        var session = new StripeCheckoutSession(id, $"{PagePrefix}/{id}", IsPaid: false, request.AmountMinor, request.Currency, request.TenantId, request.AppointmentId, LiveMode: false);
        _sessions[id] = (request, session);
        return Task.FromResult(session);
    }

    public Task<StripeCheckoutSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult(_sessions.TryGetValue(sessionId, out var known) ? known.Session : null);

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            app.Map(PagePrefix, page => page.Run(ServeAsync));
            next(app);
        };

    private async Task ServeAsync(HttpContext context)
    {
        // "/{session}" shows the page; "/{session}/pay" is its Pay button.
        var parts = (context.Request.Path.Value ?? string.Empty).Trim('/').Split('/');
        if (parts.Length is 0 or > 2 || !_sessions.TryGetValue(parts[0], out var known))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var (request, session) = known;
        if (parts.Length == 2)
        {
            _sessions[session.Id] = (request, session with { IsPaid = true });
            context.Response.Redirect(PathOf(request.SuccessUrl.Replace("{CHECKOUT_SESSION_ID}", session.Id, StringComparison.Ordinal)));
            return;
        }

        var amount = (request.AmountMinor / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(
            "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Pretend Stripe Checkout</title></head>"
            + "<body><main><h1>Pretend Stripe Checkout (test only)</h1>"
            + $"<p data-testid=\"pretend-line\">{WebUtility.HtmlEncode(request.ProductName)}</p>"
            + $"<p data-testid=\"pretend-amount\">{WebUtility.HtmlEncode(amount)} {WebUtility.HtmlEncode(request.Currency)}</p>"
            + $"<p><a data-testid=\"pretend-pay\" href=\"{PagePrefix}/{session.Id}/pay\">Pay</a></p>"
            + $"<p><a data-testid=\"pretend-back\" href=\"{WebUtility.HtmlEncode(PathOf(request.CancelUrl))}\">Back</a></p>"
            + "</main></body></html>",
            context.RequestAborted);
    }

    private static string PathOf(string url) => new Uri(url).PathAndQuery;
}