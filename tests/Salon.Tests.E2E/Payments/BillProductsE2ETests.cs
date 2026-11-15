using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Payments;

/// <summary>
/// The demo owner on a phone rings a bottle up on a client's bill, takes one payment for the visit and the bottle, and
/// finds the bottle gone from its shelf and "Products sold" in the till (ADR 0037). 390 px, axe clean, no horizontal
/// scroll, 44 px buttons.
/// </summary>
public class BillProductsE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_sells_a_bottle_on_a_visits_bill_and_takes_one_payment_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        // Names no earlier run can have left behind, so the rows found below are this run's.
        var suffix = Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var lastName = "Retail" + suffix;
        var productName = "Hair oil " + suffix + " ml";
        var productId = await app.AddProductAsync(tenantId, productName, 18m, onHand: 4);
        var (appointmentId, _, _) = await app.AddVisitAsync(tenantId, "Rosa", lastName, -Duration.FromMinutes(20), deposit: 0m);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();
        var checkout = $"/admin/payments/checkout/{appointmentId}";

        var response = await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/appointments/calendar/{appointmentId}");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync($"**/admin/appointments/calendar/{appointmentId}");
        var styled = await StylesheetLoadedAsync(page);

        // 1. The client is here; the checkout shows the visit and offers the products the salon sells.
        await page.GetByTestId("action-arrived").GetByRole(AriaRole.Button).ClickAsync();
        await page.ReachAsync("**/admin/appointments?date=*");
        await page.GetByTestId("checkout-link").ClickAsync();
        await page.ReachAsync("**" + checkout);
        (await page.GetByTestId("bill-outstanding").TextContentAsync()).ShouldBe("£45.00");

        // 2. Two bottles go on the bill: the total is the visit and the bottles together.
        await page.GetByTestId("bill-product-select").SelectOptionAsync(productId.ToString());
        await page.GetByTestId("bill-product-quantity").FillAsync("2");
        await page.GetByTestId("add-product").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync($"“{productName}” is on the bill.");
        await page.ReachAsync(url => new Uri(url).AbsolutePath == checkout);
        (await page.GetByTestId("bill-product-name").TextContentAsync()).ShouldBe($"{productName} × 2");
        (await page.GetByTestId("bill-product-total").TextContentAsync()).ShouldBe("£36.00");
        (await page.GetByTestId("bill-outstanding").TextContentAsync()).ShouldBe("£81.00");
        await CheckPageAsync(page, styled, "pay-bill", "checkout-products-mobile-390.png");

        // 3. One payment by card settles both; the line now says how it was paid and cannot be taken off.
        await page.GetByTestId("bill-method").SelectOptionAsync("CardAtSalonTerminal");
        await page.GetByTestId("pay-bill").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("The payment has been recorded.");
        await page.ReachAsync(url => new Uri(url).AbsolutePath == checkout);
        (await page.GetByTestId("bill-visit-status").TextContentAsync()).ShouldBe("Completed");
        (await page.GetByTestId("bill-outstanding").TextContentAsync()).ShouldBe("£0.00");
        (await page.GetByTestId("bill-product-paid").TextContentAsync()).ShouldBe("Paid: Card at the salon's terminal");
        (await page.GetByTestId("remove-product").CountAsync()).ShouldBe(0);
        await CheckPageAsync(page, styled, "add-product", "checkout-products-paid-mobile-390.png");

        // 4. The till of the day names the products apart from the visit.
        await page.GetByTestId("open-cash-up").ClickAsync();
        await page.ReachAsync("**/admin/payments/cash-up?date=*");
        (await page.GetByTestId("takings").TextContentAsync()).ShouldBe("£81.00");
        (await page.GetByTestId("total-CardAtSalonTerminal").TextContentAsync()).ShouldBe("£81.00");
        var movements = await page.GetByTestId("movement").AllTextContentsAsync();
        movements.Count.ShouldBe(2);
        movements.ShouldContain(m => m.Contains("Products sold", StringComparison.Ordinal) && m.Contains("£36.00", StringComparison.Ordinal));

        // 5. Both bottles have left the shelf, and the product's history says they were sold.
        await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/products/{productId}");
        await page.ReachAsync($"**/admin/products/{productId}");
        (await page.GetByTestId("on-hand").TextContentAsync()).ShouldBe("2");
        (await page.GetByTestId("stock-line").First.GetByTestId("line-kind").TextContentAsync()).ShouldBe("Sold");
        (await page.GetByTestId("stock-line").First.GetByTestId("line-quantity").TextContentAsync()).ShouldBe("−2");

        // 6. The client brings one bottle back unopened (ADR 0040): the return sits behind a disclosure on the paid line.
        await page.GotoAsync(app.TenantUrl(Slug) + checkout);
        await page.ReachAsync("**" + checkout);
        await page.GetByTestId("return-open").ClickAsync();
        (await page.GetByTestId("return-quantity").InputValueAsync()).ShouldBe("1");
        (await page.GetByTestId("return-back-on-shelf").IsCheckedAsync()).ShouldBeTrue();
        await CheckPageAsync(page, styled, "return-submit", "checkout-return-mobile-390.png");
        await page.GetByTestId("return-submit").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("The return has been recorded.");
        await page.ReachAsync(url => new Uri(url).AbsolutePath == checkout);
        (await page.GetByTestId("bill-product-returned").TextContentAsync()).ShouldBe("Returned: 1");

        // 7. The bottle is on the shelf again and the history says where it came from.
        await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/products/{productId}");
        await page.ReachAsync($"**/admin/products/{productId}");
        (await page.GetByTestId("on-hand").TextContentAsync()).ShouldBe("3");
        (await page.GetByTestId("stock-line").First.GetByTestId("line-kind").TextContentAsync()).ShouldBe("Returned by a client");
    }

    /// <summary>Touch target (when styled), no sideways scroll, a screenshot for the PR and an axe pass.</summary>
    private static async Task CheckPageAsync(IPage page, bool styled, string targetTestId, string screenshot)
    {
        if (styled)
        {
            (await page.GetByTestId(targetTestId).BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
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