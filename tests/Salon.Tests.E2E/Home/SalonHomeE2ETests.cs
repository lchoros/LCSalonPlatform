using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Home;

/// <summary>
/// The salon's own front page on a phone (the English pass, ADR 0035): it invites a booking in plain words, the first
/// button leads into the booking flow in one tap, and that page is the price list too (ADR 0094).
/// </summary>
public class SalonHomeE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const int Width = 390;

    private static readonly (string Name, int Minutes, decimal Price)[] Services =
    [
        ("Cut & blow-dry", 45, 45m),
        ("Full head colour", 90, 85m),
    ];

    [Fact]
    public async Task A_client_starts_a_booking_from_the_salon_home_page_in_one_tap()
    {
        await app.EnsureTenantAsync(Slug, "Studio 20", Services);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = Width, Height = 844 },
        });
        var page = await context.NewPageAsync();

        var home = await page.GotoAsync(app.TenantUrl(Slug) + "/");
        home.ShouldNotBeNull().Ok.ShouldBeTrue();
        (await page.GetByTestId("tenant-name").TextContentAsync()).ShouldBe("Studio 20");
        (await page.GetByTestId("book-lead").TextContentAsync()).ShouldBe("Book your appointment online at any time. Choose a service, pick a time and you're done.");
        var book = page.GetByTestId("book-now");
        (await book.TextContentAsync()).ShouldBe("Book an appointment");
        (await page.GetByTestId("view-services").CountAsync()).ShouldBe(0, "the booking page is the price list too (ADR 0094): one way in");
        (await page.ContentAsync()).ShouldNotContain("opens here");

        // CI builds no Tailwind output, so layout (tap target, overflow) is only asserted when the stylesheet is present.
        var styled = await StylesheetLoadedAsync(page);
        if (styled)
        {
            (await book.BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(Width);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, $"salon-home-mobile-{Width}.png"),
            FullPage = true,
        });
        var axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {v.Help}")));

        await book.ClickAsync();
        await page.ReachAsync(url => new Uri(url).AbsolutePath == "/book");

        (await page.GetByRole(AriaRole.Heading, new() { Level = 1 }).TextContentAsync()).ShouldBe("Choose a service");
    }

    /// <summary>True when a stylesheet resolved with rules in it (a 404 or an empty body parses to zero rules).</summary>
    private static Task<bool> StylesheetLoadedAsync(IPage page)
        => page.EvaluateAsync<bool>("() => Array.from(document.styleSheets).some(s => s.href && s.cssRules.length > 0)");
}