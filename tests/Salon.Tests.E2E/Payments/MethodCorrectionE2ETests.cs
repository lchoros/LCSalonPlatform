using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Payments;

/// <summary>
/// The demo owner on a phone puts right a payment recorded under the wrong method (ADR 0064): a £45 cut is paid by
/// card but "cash" is pressed. The till says £45 cash and £45 in the drawer. From the line on the till the owner opens
/// the correction, is told in words that cash is what it already says, chooses card, and is back on the till: £0 cash,
/// £45 card, nothing expected in the drawer, the same £45 taken, and the line says what it was corrected to. The bill
/// says card. The correction page and the till are captured for the PR, pass axe and never scroll sideways at 390 px.
/// </summary>
public class MethodCorrectionE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";
    private const string CardName = "Card at the salon's terminal";

    [Fact]
    public async Task The_owner_corrects_cash_to_card_from_the_till_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        var suffix = Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var (appointmentId, _, _) = await app.AddVisitAsync(tenantId, "Wendy", "Wrong" + suffix, -Duration.FromMinutes(20), deposit: 0m);
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
        await page.GetByTestId("action-arrived").GetByRole(AriaRole.Button).ClickAsync();
        await page.ReachAsync("**/admin/appointments?date=*");

        // 1. The card payment is recorded as cash by mistake.
        await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/payments/checkout/{appointmentId}");
        await page.ReachAsync($"**/admin/payments/checkout/{appointmentId}");
        var styled = await StylesheetLoadedAsync(page);
        await page.GetByTestId("bill-method").SelectOptionAsync(new SelectOptionValue { Label = "Cash" });
        await page.GetByTestId("pay-bill").ClickAsync();
        await Assertions.Expect(page.GetByTestId("bill-paid")).ToHaveTextAsync("−£45.00");

        // 2. The till believes it: £45 in the drawer.
        await page.GetByTestId("open-cash-up").ClickAsync();
        await page.ReachAsync("**/admin/payments/cash-up*");
        (await page.GetByTestId("total-Cash").TextContentAsync()).ShouldBe("£45.00");
        (await page.GetByTestId("expected-cash").TextContentAsync()).ShouldBe("£45.00");
        (await page.GetByTestId("corrected-to").CountAsync()).ShouldBe(0);
        if (styled)
        {
            (await page.GetByTestId("correct-link").BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
        }

        // 3. From the line to its correction: the page says what was recorded and offers the salon's methods.
        await page.GetByTestId("correct-link").ClickAsync();
        await page.ReachAsync("**/admin/payments/corrections/*");
        (await page.GetByTestId("correct-client").TextContentAsync()).ShouldBe("Wendy Wrong" + suffix);
        (await page.GetByTestId("correct-amount").TextContentAsync()).ShouldBe("Visit paid · £45.00");
        (await page.GetByTestId("correct-recorded").TextContentAsync()).ShouldBe("Cash");
        (await page.GetByTestId("correct-stands-at").CountAsync()).ShouldBe(0);

        // 4. Cash again is said to be what it is already; nothing changes.
        await page.GetByTestId("correct-submit").ClickAsync();
        await Assertions.Expect(page.GetByTestId("error")).ToHaveTextAsync("It is already recorded that way. Nothing was changed.");

        // 5. It was card.
        await page.GetByTestId("correct-method").SelectOptionAsync(new SelectOptionValue { Label = CardName });
        await CheckPageAsync(page, styled, "correct-submit", "method-correction-mobile-390.png");
        await page.GetByTestId("correct-submit").ClickAsync();
        await page.ReachAsync("**/admin/payments/cash-up?date=*");
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync($"Corrected. It was paid by: {CardName}.");

        // 6. The till adds up: the same £45 taken, none of it in the drawer.
        (await page.GetByTestId("total-Cash").TextContentAsync()).ShouldBe("£0.00");
        (await page.GetByTestId("total-CardAtSalonTerminal").TextContentAsync()).ShouldBe("£45.00");
        (await page.GetByTestId("takings").TextContentAsync()).ShouldBe("£45.00");
        (await page.GetByTestId("expected-cash").TextContentAsync()).ShouldBe("£0.00");
        (await page.GetByTestId("corrected-to").TextContentAsync()).ShouldBe($"corrected to {CardName}");
        (await page.GetByTestId("movement").CountAsync()).ShouldBe(3, "the payment and the two lines that move it");
        (await page.GetByTestId("correct-link").CountAsync()).ShouldBe(1, "the payment can be corrected again; a correction cannot");
        await CheckPageAsync(page, styled, null, "method-correction-till-mobile-390.png");

        // 7. The bill reads as it was paid.
        await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/payments/checkout/{appointmentId}");
        await page.ReachAsync($"**/admin/payments/checkout/{appointmentId}");
        await Assertions.Expect(page.GetByTestId("bill")).ToContainTextAsync($"Paid: {CardName}");
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