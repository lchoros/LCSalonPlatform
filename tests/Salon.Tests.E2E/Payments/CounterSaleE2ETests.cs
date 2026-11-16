using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Payments;

/// <summary>
/// The demo owner on a phone sells two bottles to somebody who walked in, without a visit (ADR 0053): from Products
/// to "Sell at the counter", choose, see the total, record, and find the money on today's till as a counter sale.
/// 390 px, axe clean, no sideways scroll, buttons a full finger tall.
/// </summary>
public class CounterSaleE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_owner_sells_two_bottles_to_a_walk_in_and_finds_the_money_on_the_till_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        // A name no earlier run can have left behind, so the product chosen below is this run's.
        var productName = "Counter balm " + Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var productId = await app.AddProductAsync(tenantId, productName, 12.50m, onHand: 5, cost: 6m);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/products");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/products");
        var styled = await StylesheetLoadedAsync(page);

        // 1. From the shelves to the counter.
        await page.GetByTestId("sell-at-counter").ClickAsync();
        await page.ReachAsync("**/admin/payments/sale");
        await CheckPageAsync(page, styled, "show-total", "counter-sale-form-mobile-390.png");

        // 2. Two of this run's product: the total is shown before anything is recorded.
        await page.GetByTestId("sale-product").First.SelectOptionAsync(productId.ToString());
        await page.GetByTestId("sale-quantity").First.FillAsync("2");
        await page.GetByTestId("show-total").ClickAsync();
        await page.ReachAsync(url => url.Contains("/admin/payments/sale?", StringComparison.Ordinal));
        (await page.GetByTestId("sale-total").TextContentAsync()).ShouldBe("£25.00");
        (await page.GetByTestId("quote-line").TextContentAsync()).ShouldNotBeNull().ShouldContain(productName);
        await CheckPageAsync(page, styled, "record-sale", "counter-sale-total-mobile-390.png");

        // 3. Paid in cash and recorded.
        await page.GetByTestId("sale-method").SelectOptionAsync("Cash");
        await page.GetByTestId("record-sale").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("Sold for £25.00 (Cash).");
        (await page.GetByTestId("sale-quote").CountAsync()).ShouldBe(0, "the form is empty again");

        // 4. Today's till has it as a counter sale.
        await page.GetByTestId("to-till").ClickAsync();
        await page.ReachAsync(url => new Uri(url).AbsolutePath == "/admin/payments/cash-up");
        var movement = page.GetByTestId("movement").Filter(new LocatorFilterOptions { HasText = "Counter sale" });
        (await movement.CountAsync()).ShouldBeGreaterThan(0);
        (await movement.Last.GetByTestId("movement-amount").TextContentAsync()).ShouldBe("£25.00");
    }

    /// <summary>Touch target (when styled), no sideways scroll, a screenshot for the PR and an axe pass.</summary>
    private static async Task CheckPageAsync(IPage page, bool styled, string buttonTestId, string screenshot)
    {
        if (styled)
        {
            (await page.GetByTestId(buttonTestId).BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
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