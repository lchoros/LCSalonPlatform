using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Appointments;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Reports;

/// <summary>
/// The demo owner on a phone takes a payment and sees the period set against the same days one period earlier: twice
/// the visits, and takings with nothing to be set against (ADR 0046). The table fits 390 px without a sideways scroll
/// and is axe clean.
/// </summary>
public class ReportComparisonE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_sees_this_period_against_the_one_before_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        // Surnames no earlier run can have left behind.
        var suffix = Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var (today, _, _) = await app.AddVisitAsync(tenantId, "Uma", "Now" + suffix, -Duration.FromMinutes(20), deposit: 0m);
        var (earlier, _, _) = await app.AddVisitAsync(tenantId, "Vera", "Before" + suffix, -Duration.FromDays(3), deposit: 0m);
        await app.SetVisitStatusAsync(tenantId, earlier, AppointmentStatus.Completed);
        // Yesterday to tomorrow holds the visit of twenty minutes ago whatever the hour; two of its three days have
        // happened, so it is set against the two days three and four days ago, where the earlier visit is.
        var day = SystemClock.Instance.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb["Europe/London"]).Date;
        var iso = LocalDatePattern.Iso;
        var report = $"/admin/reports?from={iso.Format(day.PlusDays(-1))}&to={iso.Format(day.PlusDays(1))}";
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();
        var checkout = $"/admin/payments/checkout/{today}";

        var response = await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/appointments/calendar/{today}");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync($"**/admin/appointments/calendar/{today}");
        var styled = await StylesheetLoadedAsync(page);

        // 1. Today's client arrives and pays £45 in cash; the visit is completed by the payment.
        await page.GetByTestId("action-arrived").GetByRole(AriaRole.Button).ClickAsync();
        await page.ReachAsync("**/admin/appointments?date=*");
        await page.GotoAsync(app.TenantUrl(Slug) + checkout);
        await page.ReachAsync("**" + checkout);
        await page.GetByTestId("bill-method").SelectOptionAsync("Cash");
        await page.GetByTestId("pay-bill").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("The payment has been recorded.");

        // 2. The report says which days are set against which.
        (await page.GotoAsync(app.TenantUrl(Slug) + report)).ShouldNotBeNull().Ok.ShouldBeTrue();
        var days = page.GetByTestId("comparison-days");
        (await days.GetAttributeAsync("data-before-from")).ShouldBe(iso.Format(day.PlusDays(-4)));
        (await days.GetAttributeAsync("data-before-to")).ShouldBe(iso.Format(day.PlusDays(-3)));

        // 3. Two completed visits now (the demo salon's one of yesterday and today's) and one before: up 100%.
        //    £45 now and nothing before: no percentage is made up.
        (await page.GetByTestId("compare-completed-now").TextContentAsync()).ShouldBe("2");
        (await page.GetByTestId("compare-completed-before").TextContentAsync()).ShouldBe("1");
        (await page.GetByTestId("compare-completed-change").TextContentAsync()).ShouldBe("Up 100%");
        (await page.GetByTestId("compare-takings-now").TextContentAsync()).ShouldBe("£45.00");
        (await page.GetByTestId("compare-takings-before").TextContentAsync()).ShouldBe("£0.00");
        (await page.GetByTestId("compare-takings-change").TextContentAsync()).ShouldBe("Nothing before");
        (await page.GetByTestId("compare-no-shows-change").TextContentAsync()).ShouldBe("Nothing before");
        await page.GetByTestId("comparison").ScrollIntoViewIfNeededAsync();
        await CheckPageAsync(page, styled, "reports-comparison-mobile-390.png");
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