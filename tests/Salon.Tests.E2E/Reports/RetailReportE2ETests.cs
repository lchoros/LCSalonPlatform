using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Reports;

/// <summary>
/// The demo owner on a phone sells two bottles on a visit's bill and finds them in the reports: what the products
/// brought in, how many left the shelf and which product it was (ADR 0044). 390 px, axe clean, no horizontal scroll.
/// </summary>
public class RetailReportE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_sells_two_bottles_and_finds_them_in_the_reports_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        // Names no earlier run can have left behind, so the rows found below are this run's.
        var suffix = Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var productName = "Hair mask " + suffix + " ml";
        var productId = await app.AddProductAsync(tenantId, productName, 18m, onHand: 4);
        var (appointmentId, _, _) = await app.AddVisitAsync(tenantId, "Tess", "Sold" + suffix, -Duration.FromMinutes(20), deposit: 0m);
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

        // 1. Before anything is sold the reports do not talk about products.
        await page.OpenNavAsync("nav-reports");
        await page.ReachAsync("**/admin/reports");
        (await page.GetByTestId("retail").CountAsync()).ShouldBe(0);

        // 2. The client is here; two bottles go on the bill and one payment by card settles the visit and the bottles.
        await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/appointments/calendar/{appointmentId}");
        await page.ReachAsync($"**/admin/appointments/calendar/{appointmentId}");
        await page.GetByTestId("action-arrived").GetByRole(AriaRole.Button).ClickAsync();
        await page.ReachAsync("**/admin/appointments?date=*");
        await page.GotoAsync(app.TenantUrl(Slug) + checkout);
        await page.ReachAsync("**" + checkout);
        await page.GetByTestId("bill-product-select").SelectOptionAsync(productId.ToString());
        await page.GetByTestId("bill-product-quantity").FillAsync("2");
        await page.GetByTestId("add-product").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync($"“{productName}” is on the bill.");
        await page.GetByTestId("bill-method").SelectOptionAsync("CardAtSalonTerminal");
        await page.GetByTestId("pay-bill").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("The payment has been recorded.");

        // 3. The reports of this month: £36 of products beside the £45 visit, two units on one bill, and the product by name.
        await page.OpenNavAsync("nav-reports");
        await page.ReachAsync("**/admin/reports");
        (await page.GetByTestId("retail-sold").TextContentAsync()).ShouldBe("£36.00");
        (await page.GetByTestId("retail-net").TextContentAsync()).ShouldBe("£36.00");
        (await page.GetByTestId("retail-returned").CountAsync()).ShouldBe(0, "nothing came back");
        (await page.GetByTestId("retail-units").TextContentAsync()).ShouldBe("2");
        (await page.GetByTestId("retail-bills").TextContentAsync()).ShouldBe("1");
        (await page.GetByTestId("retail-share").TextContentAsync()).ShouldBe("44.4%");
        var line = page.GetByTestId("top-product").Filter(new LocatorFilterOptions { HasText = productName });
        (await line.CountAsync()).ShouldBe(1);
        var text = (await line.TextContentAsync()).ShouldNotBeNull();
        text.ShouldContain("2 sold");
        text.ShouldContain("£36.00");
        (await page.GetByTestId("kind-ProductSale").TextContentAsync()).ShouldBe("£36.00");
        await page.GetByTestId("retail").ScrollIntoViewIfNeededAsync();
        await CheckPageAsync(page, styled, "reports-retail-mobile-390.png");
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