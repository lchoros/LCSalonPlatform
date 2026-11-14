using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Booking;

/// <summary>
/// A client on a phone finds a service in a real-sized catalogue (ADR 0032): category tiles, a search that opens the
/// variants it found, a variant picked from under its title, square day tiles on the next step, and a second page of
/// services. 390 px, axe clean, no horizontal scroll.
/// </summary>
public class ServiceCatalogueE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";

    [Fact]
    public async Task A_client_filters_searches_and_picks_a_variant_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.UseDefaultSettingsAsync(tenantId);
        var services = new List<(string Category, string Name, string? VariantGroup, string? VariantLabel, int Minutes, decimal Price)>();
        for (var i = 1; i <= 9; i++)
        {
            services.Add(("Hair", $"Hair treatment {i}", null, null, 30, 10m + i));
        }

        services.Add(("Hair", "Keratin · Medium hair", "Keratin", "Medium hair", 60, 70m));
        services.Add(("Hair", "Keratin · Long hair", "Keratin", "Long hair", 60, 85m));
        for (var i = 1; i <= 4; i++)
        {
            services.Add(("Brows", $"Brows treatment {i}", null, null, 15, 10m + i));
        }

        await app.SetCatalogueAsync(tenantId, services);
        try
        {
            await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 390, Height = 844 },
            });
            var page = await context.NewPageAsync();

            // 1. Fourteen entries: search, tiles and a pager appear; the variants are closed under their title.
            var response = await page.GotoAsync($"{app.TenantUrl(Slug)}/book");
            response.ShouldNotBeNull().Ok.ShouldBeTrue();
            var styled = await StylesheetLoadedAsync(page);
            (await page.GetByTestId("filter-tile").CountAsync()).ShouldBe(3);
            (await page.GetByTestId("variant-prices").TextContentAsync()).ShouldBe("£70.00 – £85.00");
            (await page.GetByTestId("book-variant").First.IsVisibleAsync()).ShouldBeFalse("variants stay folded until asked for");
            (await page.GetByTestId("pager-status").TextContentAsync()).ShouldBe("Page 1 of 2");
            await CheckPageAsync(page, styled, "filter-tile", "book-catalogue-mobile-390.png");

            // 2. The second page holds the rest.
            await page.GetByTestId("pager-next").ClickAsync();
            await page.ReachAsync("**/book?page=2");
            (await page.GetByTestId("book-service").CountAsync()).ShouldBe(2);
            (await page.GetByTestId("pager-status").TextContentAsync()).ShouldBe("Page 2 of 2");

            // 3. A category tile narrows the list to that category.
            await page.GetByTestId("filter-tile").Filter(new LocatorFilterOptions { HasText = "Brows" }).ClickAsync();
            await page.ReachAsync("**/book?category=*");
            (await page.GetByTestId("book-service").CountAsync()).ShouldBe(4);
            (await page.GetByTestId("pager").CountAsync()).ShouldBe(0);

            // 4. A search over everything opens the variant it found.
            await page.GetByTestId("filter-tile").Filter(new LocatorFilterOptions { HasText = "All" }).ClickAsync();
            await page.ReachAsync("**/book");
            await page.GetByTestId("search-input").FillAsync("keratin long");
            await page.GetByTestId("search-submit").ClickAsync();
            await page.ReachAsync("**/book?q=*");
            (await page.GetByTestId("book-variant").CountAsync()).ShouldBe(1);
            (await page.GetByTestId("book-variant").IsVisibleAsync()).ShouldBeTrue();
            (await page.GetByTestId("book-service").CountAsync()).ShouldBe(0);
            await CheckPageAsync(page, styled, "book-variant", "book-search-variant-mobile-390.png");

            // 5. The variant leads to the times, named in full, with square day tiles.
            await page.GetByTestId("book-variant").ClickAsync();
            await page.ReachAsync("**/book/*");
            (await page.GetByTestId("service-name").TextContentAsync()).ShouldBe("Keratin · Long hair");
            if (styled)
            {
                var day = (await page.GetByTestId("day").First.BoundingBoxAsync()).ShouldNotBeNull();
                Math.Abs(day.Width - day.Height).ShouldBeLessThanOrEqualTo(1, "a day is a square");
                day.Height.ShouldBeInRange(44, 60);
            }

            await CheckPageAsync(page, styled, "day", "book-square-days-mobile-390.png");
        }
        finally
        {
            await app.RemoveCategoriesAsync(tenantId);
        }
    }

    /// <summary>Touch target (when styled), no sideways scroll, a screenshot for the PR and an axe pass.</summary>
    private static async Task CheckPageAsync(IPage page, bool styled, string targetTestId, string screenshot)
    {
        if (styled)
        {
            (await page.GetByTestId(targetTestId).First.BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, screenshot),
            FullPage = true,
        });
        var axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {v.Help}")));
    }

    /// <summary>True when a stylesheet resolved with rules in it (a 404 or an empty body parses to zero rules).</summary>
    private static Task<bool> StylesheetLoadedAsync(IPage page)
        => page.EvaluateAsync<bool>("() => Array.from(document.styleSheets).some(s => s.href && s.cssRules.length > 0)");
}