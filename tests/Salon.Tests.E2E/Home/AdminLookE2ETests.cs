using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Home;

/// <summary>
/// The demo owner on a phone walks every area of the admin pages in the shared look (ADR 0033): the menu marks the
/// area she is in and no other, every page passes axe and does not scroll sideways, and the reports open with the
/// four headline numbers. On a desk the same reports lay their sections out in two columns.
/// </summary>
public class AdminLookE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    private static readonly (string Path, string Link, string Screenshot)[] Areas =
    [
        ("/admin/services", "nav-services", "admin-services-mobile-390.png"),
        ("/admin/clients", "nav-clients", "admin-clients-mobile-390.png"),
        ("/admin/appointments", "nav-appointments", "admin-day-mobile-390.png"),
        ("/admin/appointments/calendar", "nav-calendar", "admin-calendar-mobile-390.png"),
        ("/admin/payments/cash-up", "nav-cash-up", "admin-cash-up-mobile-390.png"),
        ("/admin/reports", "nav-reports", "admin-reports-mobile-390.png"),
        ("/admin/staff", "nav-staff", "admin-staff-mobile-390.png"),
        ("/admin/settings/notifications", "nav-settings", "admin-settings-mobile-390.png"),
    ];

    [Fact]
    public async Task The_demo_owner_walks_every_admin_area_in_the_shared_look_on_a_phone_and_a_desk()
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
        var root = app.TenantUrl(Slug);

        var response = await page.GotoAsync(root + Areas[0].Path);
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**" + Areas[0].Path);
        var styled = await StylesheetLoadedAsync(page);

        // 1. Every area: one marked link, the right one, reached by tapping the menu.
        foreach (var (path, link, screenshot) in Areas)
        {
            await page.GetByTestId(link).ClickAsync();
            await page.ReachAsync(url => new Uri(url).AbsolutePath == path);
            var current = page.GetByTestId("admin-nav").Locator("[aria-current=true]");
            (await current.CountAsync()).ShouldBe(1, path);
            (await current.GetAttributeAsync("data-testid")).ShouldBe(link, path);
            (await page.Locator("h1.page-title").CountAsync()).ShouldBe(1, path);
            await CheckPageAsync(page, styled, 390, screenshot);
        }

        // 2. The reports open with the four numbers an owner looks for first.
        (await page.GotoAsync(root + "/admin/reports")).ShouldNotBeNull().Ok.ShouldBeTrue();
        foreach (var headline in new[] { "headline-takings", "headline-visits", "headline-no-show-rate", "headline-unpaid" })
        {
            (await page.GetByTestId(headline).IsVisibleAsync()).ShouldBeTrue(headline);
        }

        (await page.GetByTestId("headline-takings").TextContentAsync()).ShouldBe(await TakingsAsync(page));

        // 3. On a desk the headlines sit in one row and the sections in two columns.
        await page.SetViewportSizeAsync(1280, 900);
        (await page.GotoAsync(root + "/admin/reports")).ShouldNotBeNull().Ok.ShouldBeTrue();
        if (styled)
        {
            var first = (await page.GetByTestId("headline-takings").BoundingBoxAsync()).ShouldNotBeNull();
            var last = (await page.GetByTestId("headline-unpaid").BoundingBoxAsync()).ShouldNotBeNull();
            last.Y.ShouldBe(first.Y, 1, "four headlines in one row");
            var takings = (await page.GetByTestId("takings").BoundingBoxAsync()).ShouldNotBeNull();
            var visits = (await page.GetByTestId("visits").BoundingBoxAsync()).ShouldNotBeNull();
            visits.X.ShouldBeGreaterThan(takings.X + takings.Width - 1, "the visits sit beside the takings");
        }

        await CheckPageAsync(page, styled, 1280, "admin-reports-desk-1280.png");
    }

    /// <summary>The takings as the detail section states them; a period without money shows no total there.</summary>
    private static async Task<string?> TakingsAsync(IPage page)
        => await page.GetByTestId("takings-total").CountAsync() == 0
            ? await page.GetByTestId("headline-takings").TextContentAsync()
            : await page.GetByTestId("takings-total").TextContentAsync();

    /// <summary>No sideways scroll (when styled), a screenshot for the PR and an axe pass.</summary>
    private static async Task CheckPageAsync(IPage page, bool styled, int width, string screenshot)
    {
        if (styled)
        {
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(width);
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