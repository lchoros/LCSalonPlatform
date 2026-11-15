using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Appointments;

/// <summary>
/// The demo owner on a phone finds two visits of earlier days that nobody marked: the day list says so, the list names
/// them, one tap each records that one client came and the other did not, and the notice is gone (ADR 0034). 390 px,
/// axe clean, no horizontal scroll.
/// </summary>
public class VisitsWithoutOutcomeE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_marks_two_forgotten_visits_from_the_list_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        // Surnames no earlier run can have left behind, so the rows found below are this run's.
        var suffix = Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var came = "Came" + suffix;
        var missed = "Missed" + suffix;
        await app.AddVisitAsync(tenantId, "Cora", came, -Duration.FromDays(2), deposit: 0m);
        await app.AddVisitAsync(tenantId, "Mick", missed, -Duration.FromDays(3), deposit: 0m);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/appointments");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/appointments");
        var styled = await StylesheetLoadedAsync(page);

        // 1. The day list says that earlier visits were never marked.
        var notice = page.GetByTestId("without-outcome-notice");
        (await notice.GetAttributeAsync("data-count")).ShouldBe("2");
        (await notice.TextContentAsync()).ShouldNotBeNull().ShouldContain("2 earlier visits have no outcome yet.");
        await CheckPageAsync(page, styled, "without-outcome-link", "without-outcome-notice-mobile-390.png");

        // 2. The list names both, the later one first, each with its buttons.
        await page.GetByTestId("without-outcome-link").ClickAsync();
        await page.ReachAsync("**/admin/appointments/without-outcome");
        (await page.GetByTestId("admin-nav").Locator("[aria-current=true]").GetAttributeAsync("data-testid")).ShouldBe("nav-appointments");
        var rows = page.GetByTestId("visit");
        (await rows.CountAsync()).ShouldBe(2);
        (await rows.First.GetByTestId("visit-client").TextContentAsync()).ShouldBe("Cora " + came);
        (await rows.Last.GetByTestId("visit-client").TextContentAsync()).ShouldBe("Mick " + missed);
        if (styled)
        {
            (await rows.First.GetByTestId("outcome-came").GetByRole(AriaRole.Button).BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
        }

        await CheckPageAsync(page, styled, null, "without-outcome-list-mobile-390.png");

        // 3. Cora came: one tap, and her visit leaves the list.
        await page.GetByTestId("visit").Filter(new LocatorFilterOptions { HasText = came }).GetByTestId("outcome-came").GetByRole(AriaRole.Button).ClickAsync();
        await page.ReachAsync("**/admin/appointments/without-outcome");
        (await page.GetByTestId("flash").TextContentAsync()).ShouldNotBeNull().ShouldStartWith("The visit is marked as completed.");
        (await page.GetByTestId("visit").CountAsync()).ShouldBe(1);

        // 4. Mick did not: the list is empty.
        await page.GetByTestId("visit").Filter(new LocatorFilterOptions { HasText = missed }).GetByTestId("outcome-did-not-come").GetByRole(AriaRole.Button).ClickAsync();
        await page.ReachAsync("**/admin/appointments/without-outcome");
        (await page.GetByTestId("flash").TextContentAsync()).ShouldBe("The visit is marked as a no-show.");
        (await page.GetByTestId("empty-state").IsVisibleAsync()).ShouldBeTrue();
        await CheckPageAsync(page, styled, null, "without-outcome-empty-mobile-390.png");

        // 5. Back on the day list the notice is gone.
        await page.GetByTestId("back").ClickAsync();
        await page.ReachAsync("**/admin/appointments");
        (await page.GetByTestId("without-outcome-notice").CountAsync()).ShouldBe(0);
    }

    /// <summary>Touch target (when styled), no sideways scroll, a screenshot for the PR and an axe pass.</summary>
    private static async Task CheckPageAsync(IPage page, bool styled, string? targetTestId, string screenshot)
    {
        if (styled)
        {
            if (targetTestId is not null)
            {
                (await page.GetByTestId(targetTestId).BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
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