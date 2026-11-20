using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Booking;

/// <summary>
/// Stripe in test mode for the deposit of an online booking (ADR 0082), on a 390 px phone against a pretend Stripe:
/// the client books a cut that asks for a deposit, types the code from their "phone" and is sent to pay exactly that
/// deposit. They go back without paying and find the booking standing with its deposit due and a way to pay it; they
/// pay, and the confirmation says the deposit was paid by card. Nothing reaches Stripe's network.
/// </summary>
public class StripeDepositE2ETests(StripeDepositsAppFixture app) : IClassFixture<StripeDepositsAppFixture>
{
    private const string Slug = "studio-20";
    private const int Width = 390;

    private static readonly (string Name, int Minutes, decimal Price)[] Services = [("Cut & blow-dry", 45, 45m)];

    [Fact]
    public async Task A_client_books_on_a_phone_pays_the_deposit_on_the_gateways_page_and_sees_it_paid()
    {
        await app.ClearBookingDataAsync(Slug);
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", Services);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.UseDefaultSettingsAsync(tenantId);
        await app.AskForDepositAsync(tenantId, 20m);
        try
        {
            await BookAndPayAsync(tenantId);
        }
        finally
        {
            // Other classes replace this salon's services and staff, and expect its settings as they come.
            await app.ClearBookingDataAsync(Slug);
            await app.UseDefaultSettingsAsync(tenantId);
        }
    }

    private async Task BookAndPayAsync(Guid tenantId)
    {
        // Unique per run: the per-phone code limit is three an hour and earlier runs may have used the number.
        var suffix = Random.Shared.Next(100_000, 999_999).ToString(CultureInfo.InvariantCulture);
        var opensBefore = app.Stripe.Requests.Count;
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = Width, Height = 844 },
            IsMobile = true,
            HasTouch = true,
        });
        var page = await context.NewPageAsync();

        // Service, time, details: the deposit and how it will be paid are said before the client commits.
        (await page.GotoAsync(app.TenantUrl(Slug) + "/book")).ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByTestId("book-service").First.TapAsync();
        await page.ReachAsync(url => url.Contains("/book/", StringComparison.Ordinal) && !url.EndsWith("/book", StringComparison.Ordinal));
        await page.GetByTestId("slot").First.TapAsync();
        await page.GetByTestId("continue").TapAsync();
        await page.ReachAsync("**/details?*");
        (await page.GetByTestId("deposit-terms").TextContentAsync()).ShouldNotBeNull()
            .ShouldContain("£9.00 secures this visit. Once you have booked, you pay it by card on Stripe's secure payment page.");
        await page.GetByLabel("First name", new PageGetByLabelOptions { Exact = true }).FillAsync("Olivia");
        await page.GetByLabel("Last name", new PageGetByLabelOptions { Exact = true }).FillAsync("Brown");
        await page.GetByLabel("Mobile number", new PageGetByLabelOptions { Exact = true }).FillAsync("07700 " + suffix);
        await CaptureAsync(page, "book-details-deposit-online-mobile-390.png");
        await page.GetByTestId("submit-details").TapAsync();
        await page.ReachAsync("**/book/verify/*");

        // The code from the "phone" leads to the gateway's page, for exactly the deposit.
        await page.GetByTestId("code").FillAsync(app.Sms.LastCodeFor("+447700" + suffix).ShouldNotBeNull("the code SMS went to the typed number"));
        await page.GetByTestId("submit-code").TapAsync();
        await page.ReachAsync($"**{PretendStripe.PagePrefix}/*");
        (await page.GetByTestId("pretend-amount").TextContentAsync()).ShouldBe("9.00 gbp");
        (await page.GetByTestId("pretend-line").TextContentAsync()).ShouldBe("Deposit for Cut & blow-dry at Studio 20");
        var sent = app.Stripe.Requests.Skip(opensBefore).ShouldHaveSingleItem();
        sent.AmountMinor.ShouldBe(900, "a fifth of the £45 cut, in pence");
        sent.TenantId.ShouldBe(tenantId);

        // Back without paying: the booking stands, the deposit is due, and it can be paid from here.
        await page.GetByTestId("pretend-back").TapAsync();
        await page.ReachAsync("**/book/done/*");
        (await page.GetByTestId("done-title").TextContentAsync()).ShouldBe("Your appointment is booked");
        (await page.GetByTestId("done-deposit-due").TextContentAsync()).ShouldBe("Not paid yet. You can pay it by card now, or the salon will tell you how to pay it.");
        (await page.GetByTestId("done-deposit-paid").CountAsync()).ShouldBe(0);
        await CaptureAsync(page, "book-done-deposit-due-mobile-390.png");

        // Paying: back on the confirmation, which now says the deposit was paid and how.
        await page.GetByTestId("pay-deposit").TapAsync();
        await page.ReachAsync($"**{PretendStripe.PagePrefix}/*");
        await page.GetByTestId("pretend-pay").TapAsync();
        await page.ReachAsync("**/book/done/*");
        (await page.GetByTestId("done-title").TextContentAsync()).ShouldBe("Your appointment is booked");
        (await page.GetByTestId("done-deposit").TextContentAsync()).ShouldNotBeNull().ShouldContain("£9.00");
        (await page.GetByTestId("done-deposit-paid").TextContentAsync()).ShouldBe("Paid. Card online (Stripe)");
        (await page.GetByTestId("pay-deposit").CountAsync()).ShouldBe(0, "a paid deposit is not offered again");
        (await page.GetByTestId("done-service").TextContentAsync()).ShouldBe("Cut & blow-dry");
        await CaptureAsync(page, "book-done-deposit-paid-mobile-390.png");

        // A reload of the confirmation, or of the address the gateway sent the client back to, changes nothing.
        await page.ReloadAsync();
        (await page.GetByTestId("done-deposit-paid").TextContentAsync()).ShouldBe("Paid. Card online (Stripe)");
        app.Stripe.Requests.Count.ShouldBe(opensBefore + 2, "one session for the first try, one for the second");
    }

    /// <summary>Full-page screenshot, no horizontal scroll (when the stylesheet is present), axe clean.</summary>
    private static async Task CaptureAsync(IPage page, string fileName)
    {
        if (await page.EvaluateAsync<bool>("() => Array.from(document.styleSheets).some(s => s.href && s.cssRules.length > 0)"))
        {
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(Width, fileName);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, fileName), FullPage = true });
        var axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(fileName + ": " + string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {v.Help}")));
    }
}