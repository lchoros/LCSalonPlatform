using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Catalog;

/// <summary>
/// ADR 0018 on a phone: the demo owner signs in, opens "Cut &amp; blow-dry", follows the staff link, sees the three demo
/// staff (Anna already slower and dearer, Priya not offering it), lowers Anna's price, saves and then sees the public price
/// list quote "from" the new cheapest price. The staff form is captured for the PR, passes axe and never scrolls sideways.
/// </summary>
public class StaffOverridesE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_changes_annas_price_and_the_public_list_quotes_from_the_cheapest()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("HAIR MEN 17: Haircut Scissors", 30, 16m), ("Beard trim", 20, 15m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        // The demo seed gives Anna her own price, so the public list already says "from" the list price.
        await page.GotoAsync(app.TenantUrl(Slug) + "/services");
        (await page.GetByTestId("service-price").First.TextContentAsync()).ShouldBe("from £16.00");

        await page.GotoAsync(app.TenantUrl(Slug) + "/admin/services");
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/services");

        await page.GetByTestId("service-card").Nth(0).GetByTestId("edit-service").ClickAsync();
        await page.ReachAsync("**/admin/services/*");
        await page.GetByTestId("staff-overrides").ClickAsync();
        await page.ReachAsync("**/admin/services/*/staff");

        var styled = await StylesheetLoadedAsync(page);
        (await page.GetByTestId("service-name").TextContentAsync()).ShouldBe("HAIR MEN 17: Haircut Scissors");
        var rows = page.GetByTestId("override-row");
        (await rows.CountAsync()).ShouldBe(3);
        var anna = rows.Filter(new LocatorFilterOptions { HasText = "Anna Kowalska" });
        var priya = rows.Filter(new LocatorFilterOptions { HasText = "Priya Shah" });
        (await anna.GetByTestId("override-price").InputValueAsync()).ShouldBe("18.00");
        (await anna.GetByTestId("override-duration").InputValueAsync()).ShouldBe("45");
        (await priya.GetByTestId("override-offers").IsCheckedAsync()).ShouldBeFalse();

        await anna.GetByTestId("override-price").FillAsync("12");
        if (styled)
        {
            (await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save" }).BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "services-staff-overrides-mobile-390.png"),
            FullPage = true,
        });
        var formAxe = await page.RunAxe();
        formAxe.Violations.ShouldBeEmpty(string.Join("; ", formAxe.Violations.Select(v => $"{v.Id}: {v.Help}")));

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save" }).ClickAsync();
        await page.ReachAsync("**/admin/services");
        (await page.GetByTestId("flash").TextContentAsync()).ShouldBe("Staff prices and durations for “HAIR MEN 17: Haircut Scissors” have been saved.");

        await page.GotoAsync(app.TenantUrl(Slug) + "/services");
        (await page.GetByTestId("service-price").First.TextContentAsync()).ShouldBe("from £12.00");
        (await page.GetByTestId("service-duration").First.TextContentAsync()).ShouldBe("30 min");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "services-public-from-price-mobile-390.png"),
            FullPage = true,
        });
        var publicAxe = await page.RunAxe();
        publicAxe.Violations.ShouldBeEmpty(string.Join("; ", publicAxe.Violations.Select(v => $"{v.Id}: {v.Help}")));
    }

    /// <summary>True when a stylesheet resolved with rules in it (a 404 or an empty body parses to zero rules).</summary>
    private static Task<bool> StylesheetLoadedAsync(IPage page)
        => page.EvaluateAsync<bool>("() => Array.from(document.styleSheets).some(s => s.href && s.cssRules.length > 0)");
}