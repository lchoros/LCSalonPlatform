using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Reports;

/// <summary>
/// The demo owner on a phone sells two bottles that cost £6 each for £12.50 each and reads in the reports what the
/// salon kept: £13.00 on that product (ADR 0052). The section fits 390 px without a sideways scroll and the page is
/// axe clean.
/// </summary>
public class ProductProfitE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_sells_two_bottles_and_reads_what_the_salon_kept_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        // Names no earlier run can have left behind, so the rows found below are this run's.
        var suffix = Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var productName = "Profit serum " + suffix;
        var productId = await app.AddProductAsync(tenantId, productName, 12.50m, onHand: 4, cost: 6m);
        var (appointmentId, _, _) = await app.AddVisitAsync(tenantId, "Una", "Kept" + suffix, -Duration.FromMinutes(20), deposit: 0m);
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

        // 1. The client arrives, two bottles go on the bill and the bill is paid.
        await page.GetByTestId("action-arrived").GetByRole(AriaRole.Button).ClickAsync();
        await page.ReachAsync("**/admin/appointments?date=*");
        await page.GotoAsync(app.TenantUrl(Slug) + checkout);
        await page.ReachAsync("**" + checkout);
        await page.GetByTestId("bill-product-select").SelectOptionAsync(productId.ToString());
        await page.GetByTestId("bill-product-quantity").FillAsync("2");
        await page.GetByTestId("add-product").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync($"“{productName}” is on the bill.");
        await page.GetByTestId("bill-method").SelectOptionAsync("Cash");
        await page.GetByTestId("pay-bill").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("The payment has been recorded.");

        // 2. The reports say what the salon kept: on this product, twice £6.50.
        await page.OpenNavAsync("nav-reports");
        await page.ReachAsync("**/admin/reports");
        var line = page.GetByTestId("top-product").Filter(new LocatorFilterOptions { HasText = productName });
        (await line.GetByTestId("top-product-profit").TextContentAsync()).ShouldBe("Profit: £13.00");
        (await page.GetByTestId("retail-profit").TextContentAsync()).ShouldNotBeNull().ShouldContain("£");
        await page.GetByTestId("retail").ScrollIntoViewIfNeededAsync();
        if (styled)
        {
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "reports-profit-mobile-390.png"),
            FullPage = true,
        });
        var axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {v.Help}")));
    }

    /// <summary>True when a stylesheet resolved with rules in it (a 404 or an empty body parses to zero rules).</summary>
    private static Task<bool> StylesheetLoadedAsync(IPage page)
        => page.EvaluateAsync<bool>("() => Array.from(document.styleSheets).some(s => s.href && s.cssRules.length > 0)");
}