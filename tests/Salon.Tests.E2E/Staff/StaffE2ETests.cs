using Deque.AxeCore.Playwright;

using Microsoft.Playwright;

using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Staff;

/// <summary>
/// The demo owner on a phone signs in, opens the staff list (three seeded people with their rota lines), then Tom Reed's
/// hours page (seven days, his split shift pre-filled, weekend ticked off). Both pages are captured for the PR, pass axe and
/// never scroll sideways.
/// </summary>
public class StaffE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_reviews_the_team_and_opens_a_rota_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync(app.TenantUrl(Slug) + "/admin/staff");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        new Uri(page.Url).AbsolutePath.ShouldBe("/account/login");
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/staff");

        var styled = await StylesheetLoadedAsync(page);
        (await page.GetByTestId("nav-staff").IsVisibleAsync()).ShouldBeTrue();
        var cards = page.GetByTestId("staff-card");
        (await cards.CountAsync()).ShouldBe(3);
        (await page.GetByTestId("staff-name").AllTextContentsAsync()).ShouldBe(["Anna Kowalska", "Tom Reed", "Priya Shah"]);
        var hours = await page.GetByTestId("staff-hours").AllInnerTextsAsync();
        Normalise(hours[0]).ShouldContain("Tue–Sat 10:00–18:00");
        Normalise(hours[1]).ShouldContain("Mon–Fri 09:00–13:00, 14:00–17:00");
        Normalise(hours[2]).ShouldContain("Mon 09:00–15:00");
        Normalise(hours[2]).ShouldContain("Wed 09:00–15:00");
        Normalise(hours[2]).ShouldContain("Fri 09:00–15:00");
        if (styled)
        {
            (await page.GetByTestId("add-staff").BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "staff-list-mobile-390.png"),
            FullPage = true,
        });
        var listAxe = await page.RunAxe();
        listAxe.Violations.ShouldBeEmpty(string.Join("; ", listAxe.Violations.Select(v => $"{v.Id}: {v.Help}")));

        await cards.Nth(1).GetByTestId("edit-hours").ClickAsync();
        await page.ReachAsync("**/admin/staff/*/hours");

        (await page.GetByTestId("staff-name").TextContentAsync()).ShouldBe("Tom Reed");
        var days = page.GetByTestId("day-row");
        (await days.CountAsync()).ShouldBe(7);
        (await days.Nth(0).Locator("legend").TextContentAsync()).ShouldBe("Monday");
        (await days.Nth(0).GetByTestId("day-off").IsCheckedAsync()).ShouldBeFalse();
        (await days.Nth(0).Locator("input[type=time]").Nth(0).InputValueAsync()).ShouldBe("09:00");
        (await days.Nth(0).Locator("input[type=time]").Nth(1).InputValueAsync()).ShouldBe("13:00");
        (await days.Nth(0).Locator("input[type=time]").Nth(2).InputValueAsync()).ShouldBe("14:00");
        (await days.Nth(0).Locator("input[type=time]").Nth(3).InputValueAsync()).ShouldBe("17:00");
        (await days.Nth(5).GetByTestId("day-off").IsCheckedAsync()).ShouldBeTrue();
        (await days.Nth(6).GetByTestId("day-off").IsCheckedAsync()).ShouldBeTrue();
        if (styled)
        {
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "staff-hours-mobile-390.png"),
            FullPage = true,
        });
        var hoursAxe = await page.RunAxe();
        hoursAxe.Violations.ShouldBeEmpty(string.Join("; ", hoursAxe.Violations.Select(v => $"{v.Id}: {v.Help}")));
    }

    /// <summary>Inner text keeps the line structure of the card; collapse it so the assertion reads like the screen.</summary>
    private static string Normalise(string text) => string.Join(' ', text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));

    /// <summary>True when a stylesheet resolved with rules in it (a 404 or an empty body parses to zero rules).</summary>
    private static Task<bool> StylesheetLoadedAsync(IPage page)
        => page.EvaluateAsync<bool>("() => Array.from(document.styleSheets).some(s => s.href && s.cssRules.length > 0)");
}