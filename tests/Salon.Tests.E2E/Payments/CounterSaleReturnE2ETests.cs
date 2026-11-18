using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Payments;

/// <summary>
/// The demo owner on a phone sells two bottles at the counter, and when one comes back finds the sale under today and
/// records the money given back (ADR 0054). 390 px, axe clean, no sideways scroll, buttons a full finger tall.
/// </summary>
public class CounterSaleReturnE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_owner_gives_the_money_back_for_one_bottle_of_a_counter_sale_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        // A name no earlier run can have left behind, so the sale found below is this run's.
        var productName = "Returned balm " + Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var productId = await app.AddProductAsync(tenantId, productName, 12.50m, onHand: 5, cost: 6m);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/payments/sale");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/payments/sale");
        var styled = await StylesheetLoadedAsync(page);

        // 1. Two bottles are sold at the counter.
        await page.GetByTestId("sale-product").First.SelectOptionAsync(productId.ToString());
        await page.GetByTestId("sale-quantity").First.FillAsync("2");
        await page.GetByTestId("show-total").ClickAsync();
        await page.ReachAsync(url => url.Contains("/admin/payments/sale?", StringComparison.Ordinal));
        await page.GetByTestId("record-sale").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("Sold for £25.00 (Cash).");

        // 2. One comes back: the sale is found under today.
        await page.GetByTestId("to-sales").ClickAsync();
        await page.ReachAsync(url => new Uri(url).AbsolutePath == "/admin/payments/sales");
        var sale = page.GetByTestId("sale").Filter(new LocatorFilterOptions { HasText = productName });
        (await sale.CountAsync()).ShouldBe(1);
        (await sale.GetByTestId("sale-total").TextContentAsync()).ShouldBe("£25.00");
        await sale.GetByTestId("return-open").ClickAsync();
        await CheckPageAsync(page, sale.GetByTestId("return-submit"), styled, "counter-sale-return-mobile-390.png");

        // 3. The money for one is given back and recorded.
        await sale.GetByTestId("return-quantity").FillAsync("1");
        await sale.GetByTestId("return-submit").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("The return has been recorded: £12.50 given back.");
        sale = page.GetByTestId("sale").Filter(new LocatorFilterOptions { HasText = productName });
        (await sale.GetByTestId("sale-returned").TextContentAsync()).ShouldBe("Given back: £12.50");
        (await sale.GetByTestId("line-returned").TextContentAsync()).ShouldBe("Returned: 1");
    }

    /// <summary>Touch target (when styled), no sideways scroll, a screenshot for the PR and an axe pass.</summary>
    private static async Task CheckPageAsync(IPage page, ILocator button, bool styled, string screenshot)
    {
        if (styled)
        {
            (await button.BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
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