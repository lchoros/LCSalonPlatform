using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Staff;

/// <summary>
/// The demo owner on a phone signs in, opens Anna Kowalska's time-off page from the staff list, adds next Monday as a day
/// off and sees it listed as a whole day (ADR 0015). The page is captured for the PR, passes axe and never scrolls sideways.
/// </summary>
public class StaffTimeOffE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];

    [Fact]
    public async Task The_demo_owner_gives_a_stylist_next_Monday_off_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId); // replaces the staff, so earlier time-off rows go with them (cascade)
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        var nextMonday = SystemClock.Instance.GetCurrentInstant().InZone(London).Date.Next(IsoDayOfWeek.Monday);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync(app.TenantUrl(Slug) + "/admin/staff");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/staff");

        await page.GetByTestId("staff-card").Nth(0).GetByTestId("time-off").ClickAsync();
        await page.ReachAsync("**/admin/staff/*/time-off");
        var styled = await StylesheetLoadedAsync(page);

        (await page.GetByTestId("staff-name").TextContentAsync()).ShouldBe("Anna Kowalska");
        (await page.GetByTestId("empty-state").IsVisibleAsync()).ShouldBeTrue();
        var iso = LocalDatePattern.Iso.Format(nextMonday);
        await page.Locator("#FromDate").FillAsync(iso);
        await page.Locator("#ToDate").FillAsync(iso);
        await page.Locator("#Reason").FillAsync("Day off");
        await page.GetByTestId("add-time-off").ClickAsync();
        await page.ReachAsync("**/admin/staff/*/time-off");

        (await page.GetByTestId("flash").TextContentAsync()).ShouldBe("Time off has been added.");
        var entries = page.GetByTestId("time-off-entry");
        (await entries.CountAsync()).ShouldBe(1);
        (await entries.First.GetAttributeAsync("data-whole-day")).ShouldBe("true");
        (await entries.First.GetByTestId("entry-dates").TextContentAsync()).ShouldBe(LocalDatePattern.Create("d", CultureInfo.GetCultureInfo("en-GB")).Format(nextMonday));
        (await entries.First.GetByTestId("entry-time").TextContentAsync()).ShouldBe("Whole day");
        (await entries.First.GetByTestId("entry-reason").TextContentAsync()).ShouldBe("Day off");
        if (styled)
        {
            (await page.GetByTestId("add-time-off").BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "staff-time-off-mobile-390.png"),
            FullPage = true,
        });
        var axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {v.Help}")));
    }

    /// <summary>True when a stylesheet resolved with rules in it (a 404 or an empty body parses to zero rules).</summary>
    private static Task<bool> StylesheetLoadedAsync(IPage page)
        => page.EvaluateAsync<bool>("() => Array.from(document.styleSheets).some(s => s.href && s.cssRules.length > 0)");
}