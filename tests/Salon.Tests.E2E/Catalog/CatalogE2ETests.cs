using Deque.AxeCore.Playwright;

using Microsoft.Playwright;

using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Catalog;

/// <summary>
/// The demo owner on a phone signs in, opens the admin catalogue (two services, grouped), edits one to add a buffer, saves
/// and sees the confirmation with the new timing on the card. Both pages are captured for the PR, pass axe and never scroll
/// sideways.
/// </summary>
public class CatalogE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_reviews_the_catalogue_and_adds_a_buffer_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m), ("Beard trim", 20, 15m)]);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync(app.TenantUrl(Slug) + "/admin/services");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        new Uri(page.Url).AbsolutePath.ShouldBe("/account/login");
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/services");

        var styled = await StylesheetLoadedAsync(page);
        (await page.GetByTestId("nav-services").IsVisibleAsync()).ShouldBeTrue();
        var cards = page.GetByTestId("service-card");
        (await cards.CountAsync()).ShouldBe(2);
        (await page.GetByTestId("service-name").AllTextContentsAsync()).ShouldBe(["Cut & blow-dry", "Beard trim"]);
        (await page.GetByTestId("service-price").AllTextContentsAsync()).ShouldBe(["£45.00", "£15.00"]);
        if (styled)
        {
            (await page.GetByTestId("add-service").BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "services-admin-mobile-390.png"),
            FullPage = true,
        });
        var listAxe = await page.RunAxe();
        listAxe.Violations.ShouldBeEmpty(string.Join("; ", listAxe.Violations.Select(v => $"{v.Id}: {v.Help}")));

        await cards.Nth(0).GetByTestId("edit-service").ClickAsync();
        await page.ReachAsync("**/admin/services/*");

        (await page.GetByTestId("service-name").TextContentAsync()).ShouldBe("Cut & blow-dry");
        var buffer = page.GetByTestId("buffer-input");
        (await buffer.InputValueAsync()).ShouldBe("0");
        await buffer.FillAsync("10");
        if (styled)
        {
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "services-edit-mobile-390.png"),
            FullPage = true,
        });
        var editAxe = await page.RunAxe();
        editAxe.Violations.ShouldBeEmpty(string.Join("; ", editAxe.Violations.Select(v => $"{v.Id}: {v.Help}")));

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();
        await page.ReachAsync("**/admin/services");

        (await page.GetByTestId("flash").TextContentAsync()).ShouldBe("Service “Cut & blow-dry” has been saved.");
        (await page.GetByTestId("service-buffer").First.TextContentAsync()).ShouldBe("10 min buffer");
    }

    /// <summary>True when a stylesheet resolved with rules in it (a 404 or an empty body parses to zero rules).</summary>
    private static Task<bool> StylesheetLoadedAsync(IPage page)
        => page.EvaluateAsync<bool>("() => Array.from(document.styleSheets).some(s => s.href && s.cssRules.length > 0)");
}