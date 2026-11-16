using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Reports;

/// <summary>
/// The demo owner on a phone takes a payment and sees the days of the period as bars (ADR 0048): one bar per day,
/// today's above the line, the best day named in words. The picture fits 390 px without a sideways scroll and the
/// page is axe clean.
/// </summary>
public class ReportChartE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_sees_the_takings_day_by_day_as_bars_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        var suffix = Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var (visit, _, _) = await app.AddVisitAsync(tenantId, "Yara", "Bars" + suffix, -Duration.FromMinutes(20), deposit: 0m);
        // Yesterday to tomorrow holds the visit of twenty minutes ago whatever the hour.
        var day = SystemClock.Instance.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb["Europe/London"]).Date;
        var iso = LocalDatePattern.Iso;
        var report = $"/admin/reports?from={iso.Format(day.PlusDays(-1))}&to={iso.Format(day.PlusDays(1))}";
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();
        var checkout = $"/admin/payments/checkout/{visit}";

        var response = await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/appointments/calendar/{visit}");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync($"**/admin/appointments/calendar/{visit}");
        var styled = await StylesheetLoadedAsync(page);

        // 1. Today's client arrives and pays £45 in cash.
        await page.GetByTestId("action-arrived").GetByRole(AriaRole.Button).ClickAsync();
        await page.ReachAsync("**/admin/appointments?date=*");
        await page.GotoAsync(app.TenantUrl(Slug) + checkout);
        await page.ReachAsync("**" + checkout);
        await page.GetByTestId("bill-method").SelectOptionAsync("Cash");
        await page.GetByTestId("pay-bill").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("The payment has been recorded.");

        // 2. The report draws the three days; today has a bar and tomorrow has none.
        (await page.GotoAsync(app.TenantUrl(Slug) + report)).ShouldNotBeNull().Ok.ShouldBeTrue();
        var chart = page.GetByTestId("takings-chart");
        var bars = chart.GetByTestId("chart-bar");
        (await bars.CountAsync()).ShouldBe(3);
        var today = chart.Locator($"[data-day='{iso.Format(day)}']");
        decimal.Parse((await today.GetAttributeAsync("height")).ShouldNotBeNull(), CultureInfo.InvariantCulture).ShouldBeGreaterThan(0m);
        (await today.Locator("title").TextContentAsync()).ShouldNotBeNull().ShouldContain("£");
        (await chart.Locator($"[data-day='{iso.Format(day.PlusDays(1))}']").GetAttributeAsync("height")).ShouldBe("0");
        (await page.GetByTestId("chart-best").TextContentAsync()).ShouldNotBeNull().ShouldStartWith("Best day: ");

        // 3. The picture is the width of the card and tall enough to read.
        await chart.ScrollIntoViewIfNeededAsync();
        if (styled)
        {
            var box = (await chart.Locator("svg").BoundingBoxAsync()).ShouldNotBeNull();
            box.Height.ShouldBeGreaterThanOrEqualTo(120);
            box.Width.ShouldBeGreaterThan(250);
            box.Width.ShouldBeLessThanOrEqualTo(390);
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "reports-chart-mobile-390.png"),
            FullPage = true,
        });
        var axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {v.Help}")));
    }

    /// <summary>True when a stylesheet resolved with rules in it (a 404 or an empty body parses to zero rules).</summary>
    private static Task<bool> StylesheetLoadedAsync(IPage page)
        => page.EvaluateAsync<bool>("() => Array.from(document.styleSheets).some(s => s.href && s.cssRules.length > 0)");
}