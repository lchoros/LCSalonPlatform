using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Payments;

/// <summary>
/// The demo owner on a phone sells a £50 gift voucher, reads its code, and later uses it at the checkout of a £45
/// visit: the visit is covered and ended, and £5 is left on the voucher (ADR 0057). 390 px, axe clean, no sideways
/// scroll, buttons a full finger tall.
/// </summary>
public class GiftVoucherE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_owner_sells_a_voucher_and_uses_it_to_pay_for_a_visit_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        var suffix = Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var (appointmentId, _, _) = await app.AddVisitAsync(tenantId, "Gwen", "Gifted" + suffix, -Duration.FromMinutes(20), deposit: 0m);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/payments/vouchers");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/payments/vouchers");
        var styled = await StylesheetLoadedAsync(page);

        // 1. A voucher worth £50 is sold for cash.
        await page.GetByTestId("sell-voucher").ClickAsync();
        await page.ReachAsync("**/admin/payments/vouchers/new");
        await page.GetByTestId("voucher-amount").FillAsync("50");
        await page.GetByTestId("voucher-note-input").FillAsync("For Gwen " + suffix);
        await CheckPageAsync(page, styled, "sell-voucher-submit", "voucher-sell-mobile-390.png");
        await page.GetByTestId("sell-voucher-submit").ClickAsync();
        await page.ReachAsync(url => new Uri(url).AbsolutePath.StartsWith("/admin/payments/vouchers/", StringComparison.Ordinal) && !url.EndsWith("/new", StringComparison.Ordinal));

        // 2. Its page shows the code to hand over.
        var code = (await page.GetByTestId("voucher-code").TextContentAsync()).ShouldNotBeNull().Trim();
        code.Length.ShouldBe(11);
        (await page.GetByTestId("voucher-initial").TextContentAsync()).ShouldBe("£50.00");
        (await page.GetByTestId("voucher-left").TextContentAsync()).ShouldBe("£50.00");
        await CheckPageAsync(page, styled, null, "voucher-card-mobile-390.png");
        var voucherPage = page.Url;

        // 3. Gwen arrives; the code is typed at the checkout and covers her £45 visit.
        await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/appointments/calendar/{appointmentId}");
        await page.ReachAsync($"**/admin/appointments/calendar/{appointmentId}");
        await page.GetByTestId("action-arrived").GetByRole(AriaRole.Button).ClickAsync();
        await page.ReachAsync("**/admin/appointments?date=*");
        await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/payments/checkout/{appointmentId}");
        await page.ReachAsync($"**/admin/payments/checkout/{appointmentId}");
        await page.GetByTestId("voucher-open").ClickAsync();
        await page.GetByTestId("voucher-code-input").FillAsync(code);
        await CheckPageAsync(page, styled, "use-voucher", "voucher-checkout-mobile-390.png");
        await page.GetByTestId("use-voucher").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync($"£45.00 from voucher {code} went toward this visit. £5.00 is left on the voucher.");
        (await page.GetByTestId("bill-voucher").TextContentAsync()).ShouldBe("−£45.00");
        (await page.GetByTestId("settled-reason").TextContentAsync()).ShouldBe("A gift voucher covered this visit. Nothing is left to pay.");
        (await page.GetByTestId("bill").GetAttributeAsync("data-status")).ShouldBe("Completed");

        // 4. The voucher's page says where it was used and what is left.
        await page.GotoAsync(voucherPage);
        (await page.GetByTestId("voucher-left").TextContentAsync()).ShouldBe("£5.00");
        (await page.GetByTestId("voucher-use").TextContentAsync()).ShouldNotBeNull().ShouldContain("Gifted" + suffix);
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