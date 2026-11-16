using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Products;

/// <summary>
/// The demo owner on a phone adds a product with what it costs, reads on its page what the salon keeps of each one
/// sold, and sees on the list what the shelves are worth (ADR 0049). The pages fit 390 px without a sideways scroll
/// and are axe clean.
/// </summary>
public class ProductCostE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_records_a_cost_and_sees_the_margin_and_the_worth_of_the_shelves_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        // A name no earlier run can have left behind, so the product found below is this run's.
        var name = "Costed serum " + Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/products/new");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync(url => new Uri(url).AbsolutePath == "/admin/products/new");
        var styled = await StylesheetLoadedAsync(page);

        // 1. The form has a place for what the product costs the salon.
        await page.GetByTestId("product-name-input").FillAsync(name);
        await page.GetByTestId("product-price-input").FillAsync("12.50");
        await page.GetByLabel("What it costs you").FillAsync("6.00");
        await page.GetByTestId("product-opening-input").FillAsync("4");
        await CheckPageAsync(page, styled, "product-cost-form-mobile-390.png");
        await page.GetByTestId("save-product").ClickAsync();

        // 2. The product's page says what the salon keeps of each one sold.
        await page.ReachAsync(url => new Uri(url).AbsolutePath.StartsWith("/admin/products/", StringComparison.Ordinal) && !url.EndsWith("/new", StringComparison.Ordinal));
        (await page.GetByTestId("product-cost").TextContentAsync()).ShouldBe("Costs you £6.00. You keep £6.50 of each one sold (52%).");
        await CheckPageAsync(page, styled, "product-cost-card-mobile-390.png");

        // 3. The list says what the shelves are worth, at cost and at the prices to clients.
        await page.GetByTestId("back").ClickAsync();
        await page.ReachAsync(url => new Uri(url).AbsolutePath == "/admin/products");
        var worth = page.GetByTestId("shelf-worth");
        (await worth.IsVisibleAsync()).ShouldBeTrue();
        (await page.GetByTestId("worth-cost").TextContentAsync()).ShouldNotBeNull().ShouldStartWith("£");
        (await page.GetByTestId("worth-retail").TextContentAsync()).ShouldNotBeNull().ShouldStartWith("£");
        await CheckPageAsync(page, styled, "products-worth-mobile-390.png");
    }

    /// <summary>No sideways scroll (when styled), a screenshot for the PR and an axe pass.</summary>
    private static async Task CheckPageAsync(IPage page, bool styled, string screenshot)
    {
        if (styled)
        {
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