using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Settings;

/// <summary>
/// The demo owner on a phone adds a payment method of the salon's own (ADR 0060): on the settings page "PayPal" is
/// added, a second one with the same name is refused in words; a client arrives and pays for her visit by PayPal at
/// the checkout; the till shows PayPal as its own line and nothing in the drawer. The settings page and the checkout
/// are captured for the PR, pass axe and never scroll sideways at 390 px.
/// </summary>
public class OwnPaymentMethodsE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_owner_adds_a_payment_method_and_takes_money_with_it_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        var suffix = Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var (appointmentId, _, _) = await app.AddVisitAsync(tenantId, "Petra", "Paypal" + suffix, -Duration.FromMinutes(20), deposit: 0m);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/settings/payment-methods");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/settings/payment-methods");
        var styled = await StylesheetLoadedAsync(page);
        (await page.GetByTestId("no-own-methods").IsVisibleAsync()).ShouldBeTrue();

        // 1. PayPal is added, and adding it again is refused in words.
        await page.GetByTestId("new-method-name").FillAsync("PayPal");
        await page.GetByTestId("add-method").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("PayPal is added. The desk can choose it now.");
        (await page.GetByTestId("own-method").CountAsync()).ShouldBe(1);
        (await page.GetByTestId("own-method-name").InputValueAsync()).ShouldBe("PayPal");
        await page.GetByTestId("new-method-name").FillAsync("paypal");
        await page.GetByTestId("add-method").ClickAsync();
        await Assertions.Expect(page.GetByTestId("problem")).ToHaveTextAsync("There is already a method with that name.");
        (await page.GetByTestId("own-method").CountAsync()).ShouldBe(1);
        await CheckPageAsync(page, styled, "add-method", "payment-methods-settings-mobile-390.png");

        // 2. Petra arrives and pays by PayPal.
        await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/appointments/calendar/{appointmentId}");
        await page.ReachAsync($"**/admin/appointments/calendar/{appointmentId}");
        await page.GetByTestId("action-arrived").GetByRole(AriaRole.Button).ClickAsync();
        await page.ReachAsync("**/admin/appointments?date=*");
        await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/payments/checkout/{appointmentId}");
        await page.ReachAsync($"**/admin/payments/checkout/{appointmentId}");
        var method = page.GetByTestId("bill-method");
        (await method.Locator("option").AllTextContentsAsync()).ShouldBe(["Cash", "Card at the salon's terminal", "Bank transfer", "Voucher", "PayPal"]);
        await method.SelectOptionAsync(new SelectOptionValue { Label = "PayPal" });
        await CheckPageAsync(page, styled, "pay-bill", "payment-methods-checkout-mobile-390.png");
        await page.GetByTestId("pay-bill").ClickAsync();
        await Assertions.Expect(page.GetByTestId("bill")).ToContainTextAsync("Paid: PayPal");
        (await page.GetByTestId("bill").GetAttributeAsync("data-status")).ShouldBe("Completed");

        // 3. The till has PayPal as its own line, and none of it in the drawer.
        await page.GetByTestId("open-cash-up").ClickAsync();
        await page.ReachAsync("**/admin/payments/cash-up*");
        var totals = page.GetByTestId("totals");
        (await totals.TextContentAsync()).ShouldNotBeNull().ShouldContain("PayPal");
        (await totals.TextContentAsync()).ShouldNotBeNull().ShouldContain("£45.00");
        (await page.GetByTestId("total-Cash").CountAsync()).ShouldBe(0, "no cash was taken");
        await CheckPageAsync(page, styled, null, "payment-methods-till-mobile-390.png");

        // 4. Renamed in the settings: the list has the new name, the till keeps the money.
        await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/settings/payment-methods");
        await page.GetByTestId("own-method-name").FillAsync("PayPal Here");
        await page.GetByTestId("rename-method").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("The method is now called PayPal Here.");
        await page.GetByTestId("retire-method").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("PayPal Here is no longer offered.");
        (await page.GetByTestId("retired-method").CountAsync()).ShouldBe(1, "money was recorded with it, so it is kept, out of use");
    }

    /// <summary>Touch target (when styled), no sideways scroll, a screenshot for the PR and an axe pass.</summary>
    private static async Task CheckPageAsync(IPage page, bool styled, string? buttonTestId, string screenshot)
    {
        if (styled)
        {
            if (buttonTestId is not null)
            {
                (await page.GetByTestId(buttonTestId).BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
            }

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