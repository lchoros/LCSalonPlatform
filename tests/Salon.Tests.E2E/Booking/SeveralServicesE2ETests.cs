using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Booking;

/// <summary>
/// Several services in one booking on a 390 px phone (ADR 0095): a client chooses a service and who with, taps "Add
/// another service", picks a second one from the same list and sees both in the summary with one total; the times are
/// those the whole visit fits; the details name both, and the confirmation is for the visit. The summary screen is
/// captured, passes axe and does not scroll sideways.
/// </summary>
public class SeveralServicesE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const int Width = 390;

    private static readonly (string Name, int Minutes, decimal Price)[] Services =
    [
        ("Cut & blow-dry", 45, 45m),
        ("Full head colour", 90, 85m),
        ("Beard trim", 20, 15m),
    ];

    [Fact]
    public async Task A_client_adds_a_second_service_and_books_the_visit_on_a_phone()
    {
        await app.ClearBookingDataAsync(Slug);
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", Services);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.UseDefaultSettingsAsync(tenantId);
        try
        {
            await BookAsync(Random.Shared.Next(100_000, 999_999).ToString(CultureInfo.InvariantCulture));
        }
        finally
        {
            // Other classes replace this salon's services and staff; the booking must not hold a foreign key on them.
            await app.ClearBookingDataAsync(Slug);
        }
    }

    private async Task BookAsync(string suffix)
    {
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = Width, Height = 844 },
            IsMobile = true,
            HasTouch = true,
        });
        var page = await context.NewPageAsync();

        // The first service and who with, as in any booking.
        (await page.GotoAsync(app.TenantUrl(Slug) + "/book")).ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByTestId("book-service").First.TapAsync();
        await page.ReachAsync(url => url.Contains("/book/", StringComparison.Ordinal) && !url.EndsWith("/book", StringComparison.Ordinal));
        await page.ChooseAnyoneAsync();
        (await page.GetByTestId("service-name").TextContentAsync()).ShouldBe("Cut & blow-dry");
        (await page.GetByTestId("remove-service").CountAsync()).ShouldBe(0, "one service is changed, not removed");

        // More services come after a time is chosen for what is here (the owner, 2026-11-25).
        (await page.GetByTestId("add-service").CountAsync()).ShouldBe(0);
        await Assertions.Expect(page.GetByTestId("add-after-time")).ToBeVisibleAsync();
        await page.GetByTestId("slot").First.TapAsync();
        await Assertions.Expect(page.GetByTestId("continue")).ToHaveCountAsync(1);
        var chosenTime = (await page.GetByTestId("summary-when").TextContentAsync()).ShouldNotBeNull();

        // "Add another service" opens the same list; what is already chosen is not offered again.
        await page.GetByTestId("add-service").TapAsync();
        await page.ReachAsync(url => url.Contains("add=", StringComparison.Ordinal));
        (await page.GetByTestId("adding-title").TextContentAsync()).ShouldBe("Add another service");
        var rows = page.GetByTestId("book-service");
        (await rows.CountAsync()).ShouldBe(Services.Length - 1);
        await rows.Filter(new LocatorFilterOptions { HasText = "Beard trim" }).TapAsync();
        await page.ReachAsync(url => url.Contains("also=", StringComparison.Ordinal));

        // Both in the summary, each with its price, one total; "who with" was not asked again.
        (await page.GetByTestId("visit-line").CountAsync()).ShouldBe(2);
        (await page.GetByTestId("service-name").TextContentAsync()).ShouldBe("Cut & blow-dry");
        (await page.GetByTestId("extra-service-name").TextContentAsync()).ShouldBe("Beard trim");
        (await page.GetByTestId("line-price").AllTextContentsAsync()).ShouldBe(["£45.00", "£15.00"]);
        (await page.GetByTestId("summary-total").TextContentAsync()).ShouldBe("£60.00");
        (await page.GetByTestId("summary-duration").TextContentAsync()).ShouldBe("65 min");
        // The time chosen before adding is kept: the first card still says it, the second follows on.
        (await page.GetByTestId("line-when").First.TextContentAsync()).ShouldBe(chosenTime);
        await Assertions.Expect(page.GetByTestId("continue")).ToHaveCountAsync(1);
        (await page.GetByTestId("choose-staff").CountAsync()).ShouldBe(0);
        (await page.GetByTestId("remove-service").CountAsync()).ShouldBe(2);
        var slots = page.GetByTestId("slot");
        (await slots.CountAsync()).ShouldBeGreaterThan(0, "the demo rota has room for a 65-minute visit within two weeks");
        await CaptureAsync(page, "book-several-services-mobile-390.png");

        await slots.First.TapAsync();
        await page.GetByTestId("continue").TapAsync();
        await page.ReachAsync("**/details?*");

        // The details name the visit; the code; the confirmation.
        (await page.GetByTestId("summary-service").TextContentAsync()).ShouldNotBeNull().ShouldContain("Cut & blow-dry + Beard trim");
        await page.GetByLabel("First name", new PageGetByLabelOptions { Exact = true }).FillAsync("Olivia");
        await page.GetByLabel("Last name", new PageGetByLabelOptions { Exact = true }).FillAsync("Brown");
        await page.GetByLabel("Mobile number", new PageGetByLabelOptions { Exact = true }).FillAsync("07700 " + suffix);
        await page.GetByTestId("submit-details").TapAsync();
        await page.ReachAsync("**/book/verify/*");
        await page.GetByTestId("code").FillAsync(app.Sms.LastCodeFor("+447700" + suffix).ShouldNotBeNull("the code SMS went to the typed number"));
        await page.GetByTestId("submit-code").TapAsync();
        await page.ReachAsync("**/book/done/*");
        (await page.GetByTestId("done-service").TextContentAsync()).ShouldBe("Cut & blow-dry + Beard trim");
        await CaptureAsync(page, "book-several-services-done-mobile-390.png");
    }

    /// <summary>No sideways scroll (when styled), a screenshot for the PR and an axe pass.</summary>
    private static async Task CaptureAsync(IPage page, string fileName)
    {
        await page.SettleBookingAsync();
        if (await page.EvaluateAsync<bool>("() => Array.from(document.styleSheets).some(s => s.href && s.cssRules.length > 0)"))
        {
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(Width, fileName);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, fileName), FullPage = true });
        var axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {v.Help}")));
    }
}