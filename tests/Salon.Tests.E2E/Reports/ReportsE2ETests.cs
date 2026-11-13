using System.Globalization;

using Deque.AxeCore.Playwright;

using Microsoft.Playwright;

using NodaTime;
using NodaTime.Text;

using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Reports;

/// <summary>
/// The demo owner on a phone completes a visit without taking payment, opens Reports from the menu, picks her own
/// dates, finds the visit under "Completed visits not paid", takes the payment from there and sees it gone from the
/// list and in the takings (ADR 0026). 390 px, axe clean, no horizontal scroll.
/// </summary>
public class ReportsE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_finds_an_unpaid_visit_in_the_reports_and_takes_the_payment_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        // A surname no earlier run can have left behind, so the rows found below are this run's.
        var lastName = "Report" + Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var (appointmentId, _, _) = await app.AddVisitAsync(tenantId, "Rita", lastName, -Duration.FromMinutes(20), deposit: 0m);
        // Yesterday to tomorrow in the salon's zone holds the visit whatever the hour, also on the first of a month.
        var today = SystemClock.Instance.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb["Europe/London"]).Date;
        var from = LocalDatePattern.Iso.Format(today.PlusDays(-1));
        var to = LocalDatePattern.Iso.Format(today.PlusDays(1));
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();
        var cardUrl = $"{app.TenantUrl(Slug)}/admin/appointments/calendar/{appointmentId}";

        var response = await page.GotoAsync(cardUrl);
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync($"**/admin/appointments/calendar/{appointmentId}");
        var styled = await StylesheetLoadedAsync(page);

        // 1. The client came and left; the visit is completed but nobody opened the checkout.
        await page.GetByTestId("action-arrived").GetByRole(AriaRole.Button).ClickAsync();
        await page.ReachAsync("**/admin/appointments?date=*");
        (await page.GotoAsync(cardUrl)).ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByTestId("action-complete").GetByRole(AriaRole.Button).ClickAsync();
        await page.ReachAsync("**/admin/appointments?date=*");

        // 2. Reports from the menu open on this month.
        await page.GetByTestId("nav-reports").ClickAsync();
        await page.ReachAsync("**/admin/reports");
        (await page.GetByTestId("preset-ThisMonth").GetAttributeAsync("aria-current")).ShouldBe("page");

        // 3. Her own dates: the visit is named as not paid, with what is left.
        await page.GetByTestId("custom-period").Locator("summary").ClickAsync();
        await page.GetByTestId("period-from").FillAsync(from);
        await page.GetByTestId("period-to").FillAsync(to);
        await page.GetByTestId("period-show").ClickAsync();
        await page.ReachAsync("**/admin/reports?from=*");
        (await page.GetByTestId("period").GetAttributeAsync("data-from")).ShouldBe(from);
        (await page.GetByTestId("period").GetAttributeAsync("data-to")).ShouldBe(to);
        var unpaid = page.GetByTestId("unpaid-visit").Filter(new LocatorFilterOptions { HasText = lastName });
        (await unpaid.CountAsync()).ShouldBe(1);
        (await unpaid.GetByTestId("unpaid-amount").TextContentAsync()).ShouldBe("£45.00");
        (await page.GetByTestId("top-service").First.TextContentAsync()).ShouldNotBeNull().ShouldContain("Cut & blow-dry");
        (await page.GetByTestId("staff-line").CountAsync()).ShouldBeGreaterThan(0);
        var reportUrl = page.Url;
        await CheckPageAsync(page, styled, "period-show", "reports-mobile-390.png");

        // 4. The payment is taken from the list.
        await unpaid.GetByTestId("unpaid-checkout").ClickAsync();
        await page.ReachAsync($"**/admin/payments/checkout/{appointmentId}");
        await page.GetByTestId("bill-method").SelectOptionAsync("Cash");
        await page.GetByTestId("pay-bill").ClickAsync();
        (await page.GetByTestId("flash").TextContentAsync()).ShouldBe("The payment has been recorded.");

        // 5. The same report: the visit is off the list and the cash is in the takings.
        (await page.GotoAsync(reportUrl)).ShouldNotBeNull().Ok.ShouldBeTrue();
        (await page.GetByTestId("unpaid-visit").Filter(new LocatorFilterOptions { HasText = lastName }).CountAsync()).ShouldBe(0);
        (await page.GetByTestId("takings-total").IsVisibleAsync()).ShouldBeTrue();
        (await page.GetByTestId("method-Cash").IsVisibleAsync()).ShouldBeTrue();
        (await page.GetByTestId("kind-VisitPayment").IsVisibleAsync()).ShouldBeTrue();
        await page.GetByTestId("takings-by-day").Locator("summary").ClickAsync();
        (await page.GetByTestId("takings-day").CountAsync()).ShouldBeGreaterThan(0);
        await CheckPageAsync(page, styled, null, "reports-paid-mobile-390.png");

        // 6. One tap to another period.
        await page.GetByTestId("preset-LastMonth").ClickAsync();
        await page.ReachAsync("**/admin/reports?period=last-month");
        (await page.GetByTestId("preset-LastMonth").GetAttributeAsync("aria-current")).ShouldBe("page");
        (await page.GetByTestId("preset-ThisMonth").GetAttributeAsync("aria-current")).ShouldBeNull();
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