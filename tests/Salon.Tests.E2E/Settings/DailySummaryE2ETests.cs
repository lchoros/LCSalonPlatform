using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Settings;

/// <summary>
/// The demo owner on a phone opens Settings, reads what today's summary would say, and switches the daily email on at
/// half past seven (ADR 0050). A time that is not a time is refused next to its field. The page fits 390 px without a
/// sideways scroll and is axe clean.
/// </summary>
public class DailySummaryE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_owner_reads_todays_summary_and_switches_the_daily_email_on_from_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        try
        {
            var response = await page.GotoAsync(app.TenantUrl(Slug) + "/admin/appointments");
            response.ShouldNotBeNull().Ok.ShouldBeTrue();
            await page.GetByLabel("Email").FillAsync(Email);
            await page.GetByLabel("Password").FillAsync(Password);
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
            await page.ReachAsync("**/admin/appointments");

            // 1. Settings, then the "Daily summary" tab: off, at eight in the evening, with today's numbers on the page.
            await page.GetByTestId("nav-settings").ClickAsync();
            await page.ReachAsync("**/admin/settings/notifications");
            await page.GetByTestId("settings-nav-daily-summary").ClickAsync();
            await page.ReachAsync("**/admin/settings/daily-summary");
            var styled = await StylesheetLoadedAsync(page);
            (await page.GetByTestId("summary-enabled").IsCheckedAsync()).ShouldBeFalse();
            (await page.GetByTestId("summary-send-at").InputValueAsync()).ShouldBe("20:00");
            (await page.GetByTestId("summary-subject").TextContentAsync()).ShouldNotBeNull().ShouldStartWith("Studio 20: £");
            var text = (await page.GetByTestId("summary-text").TextContentAsync()).ShouldNotBeNull();
            text.ShouldContain("Completed visits: ");
            text.ShouldContain("/admin/reports?from=");
            (await page.GetByTestId("summary-recipients").TextContentAsync()).ShouldNotBeNull().ShouldContain("owner login");

            // 2. An empty time is refused next to its field.
            await page.GetByTestId("summary-enabled").CheckAsync();
            await page.GetByTestId("summary-send-at").FillAsync(string.Empty);
            await page.GetByTestId("save-daily-summary").ClickAsync();
            await Assertions.Expect(page.GetByTestId("send-at-error")).ToHaveTextAsync("Enter a time such as 20:00.");
            await CheckPageAsync(page, styled, "daily-summary-error-mobile-390.png");

            // 3. Half past seven is saved and the box stays ticked.
            await page.GetByTestId("summary-enabled").CheckAsync();
            await page.GetByTestId("summary-send-at").FillAsync("07:30");
            await page.GetByTestId("save-daily-summary").ClickAsync();
            await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("The daily summary settings have been saved.");
            (await page.GetByTestId("summary-enabled").IsCheckedAsync()).ShouldBeTrue();
            (await page.GetByTestId("summary-send-at").InputValueAsync()).ShouldBe("07:30");
            await CheckPageAsync(page, styled, "daily-summary-mobile-390.png");
        }
        finally
        {
            // The other browser tests share this salon; the summary goes back to off.
            await app.UseDefaultSettingsAsync(tenantId);
        }
    }

    /// <summary>Touch target (when styled), no sideways scroll, a screenshot for the PR and an axe pass.</summary>
    private static async Task CheckPageAsync(IPage page, bool styled, string screenshot)
    {
        if (styled)
        {
            (await page.GetByTestId("save-daily-summary").BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
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