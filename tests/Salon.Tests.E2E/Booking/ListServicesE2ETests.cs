using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Booking;

/// <summary>Home → "View services" in one tap on a phone and on a desktop; the list renders, is axe-clean and does not scroll sideways.</summary>
public class ListServicesE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";

    private static readonly (string Name, int Minutes, decimal Price)[] Services =
    [
        ("Cut & blow-dry", 45, 45m),
        ("Full head colour", 90, 85m),
        ("Beard trim", 20, 15m),
    ];

    [Theory]
    [InlineData("mobile", 390, 844)]
    [InlineData("desktop", 1280, 800)]
    public async Task A_client_reaches_the_price_list_from_the_salon_home_page(string label, int width, int height)
    {
        await app.EnsureTenantAsync(Slug, "Studio 20", Services);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        var page = await context.NewPageAsync();

        var home = await page.GotoAsync(app.TenantUrl(Slug) + "/");
        home.ShouldNotBeNull().Ok.ShouldBeTrue();
        (await page.GetByTestId("tenant-name").TextContentAsync()).ShouldBe("Studio 20");

        // CI builds no Tailwind output yet, so layout (tap target, overflow) is only asserted when the stylesheet is present.
        var styled = await StylesheetLoadedAsync(page);
        var link = page.GetByTestId("view-services");
        if (styled)
        {
            (await link.BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
        }

        await link.ClickAsync();
        await page.ReachAsync("**/services");

        (await page.GetByTestId("salon-name").TextContentAsync()).ShouldBe("Studio 20");
        var cards = page.GetByTestId("service-card");
        (await cards.CountAsync()).ShouldBe(Services.Length);
        (await cards.Nth(0).GetByRole(AriaRole.Heading).TextContentAsync()).ShouldBe("Cut & blow-dry");
        (await page.GetByTestId("service-price").AllTextContentsAsync()).ShouldBe(["£45.00", "£85.00", "£15.00"]);
        (await page.GetByTestId("service-duration").First.TextContentAsync()).ShouldBe("45 min");

        if (styled)
        {
            // No horizontal scroll: the document is never wider than the viewport.
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(width);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, $"services-{label}-{width}.png"),
            FullPage = true,
        });

        var axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {v.Help}")));
    }

    /// <summary>True when a stylesheet resolved with rules in it (a 404 or an empty body parses to zero rules).</summary>
    private static Task<bool> StylesheetLoadedAsync(IPage page)
        => page.EvaluateAsync<bool>("() => Array.from(document.styleSheets).some(s => s.href && s.cssRules.length > 0)");
}