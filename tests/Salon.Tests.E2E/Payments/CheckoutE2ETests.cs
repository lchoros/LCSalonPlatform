using System.Globalization;

using Deque.AxeCore.Playwright;

using Microsoft.Playwright;

using NodaTime;

using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Payments;

/// <summary>
/// The demo owner on a phone marks a client as arrived, takes the payment at the checkout in cash (which completes the
/// visit), opens the till for the day, sees the cash it expects and closes the day with the amount counted (ADR 0024).
/// 390 px, axe clean, no horizontal scroll.
/// </summary>
public class CheckoutE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_takes_a_payment_at_the_checkout_and_closes_the_day_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        // A surname no earlier run can have left behind, so the rows found below are this run's.
        var lastName = "Checkout" + Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var (appointmentId, _, _) = await app.AddVisitAsync(tenantId, "Paula", lastName, -Duration.FromMinutes(20), deposit: 0m);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/appointments/calendar/{appointmentId}");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync($"**/admin/appointments/calendar/{appointmentId}");
        var styled = await StylesheetLoadedAsync(page);
        (await page.GetByTestId("nav-cash-up").IsVisibleAsync()).ShouldBeTrue();

        // 1. The client is here: the day list then offers the checkout for that visit.
        await page.GetByTestId("action-arrived").GetByRole(AriaRole.Button).ClickAsync();
        await page.ReachAsync("**/admin/appointments?date=*");
        await page.GetByTestId("checkout-link").ClickAsync();
        await page.ReachAsync($"**/admin/payments/checkout/{appointmentId}");
        (await page.GetByTestId("bill-client").TextContentAsync()).ShouldBe($"Paula {lastName}");
        (await page.GetByTestId("bill-price").TextContentAsync()).ShouldBe("£45.00");
        (await page.GetByTestId("bill-outstanding").TextContentAsync()).ShouldBe("£45.00");
        await CheckPageAsync(page, styled, "pay-bill", "checkout-mobile-390.png");

        // 2. Cash at the desk: the bill is paid and the visit is completed without another tap.
        await page.GetByTestId("bill-method").SelectOptionAsync("Cash");
        await page.GetByTestId("pay-bill").ClickAsync();
        (await page.GetByTestId("flash").TextContentAsync()).ShouldBe("The payment has been recorded.");
        (await page.GetByTestId("bill-visit-status").TextContentAsync()).ShouldBe("Completed");
        (await page.GetByTestId("bill-outstanding").TextContentAsync()).ShouldBe("£0.00");
        (await page.GetByTestId("bill-reference").TextContentAsync()).ShouldNotBeNull().ShouldStartWith("Reference: manual:payment:");
        (await page.GetByTestId("pay-bill-form").CountAsync()).ShouldBe(0, "a paid bill offers nothing more");
        await CheckPageAsync(page, styled, null, "checkout-paid-mobile-390.png");

        // 3. The till of that day has the cash and says what the drawer should hold.
        await page.GetByTestId("open-cash-up").ClickAsync();
        await page.ReachAsync("**/admin/payments/cash-up?date=*");
        (await page.GetByTestId("total-Cash").TextContentAsync()).ShouldBe("£45.00");
        (await page.GetByTestId("takings").TextContentAsync()).ShouldBe("£45.00");
        (await page.GetByTestId("expected-cash").TextContentAsync()).ShouldBe("£45.00");
        (await page.GetByTestId("movement").TextContentAsync()).ShouldNotBeNull().ShouldContain($"Paula {lastName}");
        await CheckPageAsync(page, styled, "close-day", "cash-up-mobile-390.png");

        // 4. The drawer is counted and the day is closed, once.
        await page.GetByTestId("counted-cash").FillAsync("45.00");
        await page.GetByTestId("close-day").ClickAsync();
        (await page.GetByTestId("flash").TextContentAsync()).ShouldBe("The day has been closed.");
        (await page.GetByTestId("difference-badge").TextContentAsync()).ShouldBe("Balanced");
        (await page.GetByTestId("closed-counted").TextContentAsync()).ShouldBe("£45.00");
        (await page.GetByTestId("closed-by").TextContentAsync()).ShouldNotBeNull().ShouldStartWith("Closed by Studio 20 Owner");
        (await page.GetByTestId("close-form").CountAsync()).ShouldBe(0, "a closed day cannot be closed again");
        await CheckPageAsync(page, styled, null, "cash-up-closed-mobile-390.png");
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