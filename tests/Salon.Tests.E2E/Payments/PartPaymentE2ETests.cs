using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Payments;

/// <summary>
/// The demo owner on a phone takes one bill two ways (ADR 0061): a client arrives for a £45 cut, gives £20 in cash
/// and pays the rest by card. The whole of what is left is refused as a part, in words; the £20 is recorded, the bill
/// says £25 is left, the card pays it and the visit is completed. The till has £20 cash, £25 card and £20 expected in
/// the drawer. The checkout with the part form is captured for the PR, passes axe and never scrolls sideways at 390 px.
/// </summary>
public class PartPaymentE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_owner_takes_cash_for_a_part_and_the_rest_by_card_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        var suffix = Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var (appointmentId, _, _) = await app.AddVisitAsync(tenantId, "Sally", "Split" + suffix, -Duration.FromMinutes(20), deposit: 0m);
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
        await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/payments/checkout/{appointmentId}");
        await page.ReachAsync($"**/admin/payments/checkout/{appointmentId}");
        var styled = await StylesheetLoadedAsync(page);
        (await page.GetByTestId("bill-outstanding").TextContentAsync()).ShouldBe("£45.00");

        // 1. All of what is left is not a part.
        await page.GetByTestId("part-open").ClickAsync();
        await page.GetByTestId("part-amount").FillAsync("45");
        await page.GetByTestId("pay-part").ClickAsync();
        await Assertions.Expect(page.GetByTestId("error")).ToContainTextAsync("A part is more than nothing and less than what is left to pay for the visit.");
        (await page.GetByTestId("bill-part").CountAsync()).ShouldBe(0);

        // 2. £20 in cash.
        await page.GetByTestId("part-open").ClickAsync();
        await page.GetByTestId("part-amount").FillAsync("20");
        await page.GetByTestId("part-method").SelectOptionAsync(new SelectOptionValue { Label = "Cash" });
        await CheckPageAsync(page, styled, "pay-part", "part-payment-checkout-mobile-390.png");
        await page.GetByTestId("pay-part").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("£20.00 is recorded. £25.00 is left to pay for the visit.");
        (await page.GetByTestId("bill-part").TextContentAsync()).ShouldBe("−£20.00");
        (await page.GetByTestId("bill-part-method").TextContentAsync()).ShouldBe("Part paid: Cash");
        (await page.GetByTestId("bill-outstanding").TextContentAsync()).ShouldBe("£25.00");
        (await page.GetByTestId("bill").GetAttributeAsync("data-status")).ShouldBe("Arrived", "a part never ends the visit");

        // 3. The rest by card.
        await page.GetByTestId("bill-method").SelectOptionAsync(new SelectOptionValue { Label = "Card at the salon's terminal" });
        await page.GetByTestId("pay-bill").ClickAsync();
        await Assertions.Expect(page.GetByTestId("bill-paid")).ToHaveTextAsync("−£25.00");
        (await page.GetByTestId("bill").GetAttributeAsync("data-status")).ShouldBe("Completed");
        (await page.GetByTestId("part-step").CountAsync()).ShouldBe(0);
        await CheckPageAsync(page, styled, null, "part-payment-paid-mobile-390.png");

        // 4. The till: £20 cash, £25 card, £20 in the drawer.
        await page.GetByTestId("open-cash-up").ClickAsync();
        await page.ReachAsync("**/admin/payments/cash-up*");
        (await page.GetByTestId("total-Cash").TextContentAsync()).ShouldBe("£20.00");
        (await page.GetByTestId("total-CardAtSalonTerminal").TextContentAsync()).ShouldBe("£25.00");
        (await page.GetByTestId("totals").TextContentAsync()).ShouldNotBeNull().ShouldContain("£45.00");
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