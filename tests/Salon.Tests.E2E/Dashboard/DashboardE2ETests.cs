using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Dashboard;

/// <summary>
/// The demo owner signs in and lands on today at a glance (ADR 0084), on a phone and at a desk: the visit that is due
/// now is the first of the next visits, by first name only; tapping it opens its card. She marks the client as
/// arrived, takes the payment, and back on the dashboard the money of the day says so. The owner of a salon on its
/// first day is told there is nothing to show yet and is led to getting started. Axe clean, no sideways scroll,
/// captured for the PR.
/// </summary>
public class DashboardE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";
    private const string NewSlug = "studio-new";
    private const string NewEmail = "owner@studio-new.local";

    [Theory]
    [InlineData("mobile", 390, 844)]
    [InlineData("desk", 1280, 900)]
    public async Task The_demo_owner_lands_on_today_opens_the_next_visit_and_sees_the_payment_she_took(string label, int width, int height)
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        // A surname no earlier run can have left behind; the dashboard must never show it.
        var lastName = "Glance" + Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var (appointmentId, staffName, serviceName, _) = await app.AddVisitDueNowAsync(tenantId, "Daisy", lastName);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        var page = await context.NewPageAsync();
        var root = app.TenantUrl(Slug);

        // 1. Signing in with nowhere else to go lands on the dashboard.
        (await page.GotoAsync(root + "/account/login")).ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync(url => new Uri(url).AbsolutePath == "/admin");
        var styled = await StylesheetLoadedAsync(page);
        (await page.Locator("h1.page-title").TextContentAsync()).ShouldBe("Today");
        var current = page.GetByTestId("admin-nav").Locator("[aria-current=true]");
        (await current.CountAsync()).ShouldBe(1);
        (await current.GetAttributeAsync("data-testid")).ShouldBe("nav-dashboard");

        // 2. The visit that is due now leads the next visits: time, first name, service, who with.
        var next = page.Locator($"[data-testid='next-visit'][data-visit-id='{appointmentId}']");
        (await next.CountAsync()).ShouldBe(1);
        (await page.GetByTestId("next-visit").First.GetAttributeAsync("data-visit-id")).ShouldBe(appointmentId.ToString());
        (await next.GetByTestId("next-visit-client").TextContentAsync()).ShouldBe("Daisy");
        (await next.GetByTestId("next-visit-service").TextContentAsync()).ShouldBe(serviceName);
        (await next.GetByTestId("next-visit-staff").TextContentAsync()).ShouldBe(staffName);
        (await page.ContentAsync()).ShouldNotContain(lastName);
        int.Parse((await page.GetByTestId("headline-visits").TextContentAsync()).ShouldNotBeNull(), CultureInfo.InvariantCulture).ShouldBeGreaterThanOrEqualTo(1);
        (await page.GetByTestId("takings-total").TextContentAsync()).ShouldBe("£0.00");
        (await page.GetByTestId("occupancy-row").CountAsync()).ShouldBe(3, "the three demo people take bookings");
        if (styled)
        {
            (await page.GetByTestId("action-book").BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
            (await next.GetByTestId("next-visit-link").BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
        }

        await CheckPageAsync(page, styled, width, $"dashboard-next-{label}-{width}.png");

        // 3. Tapping it opens its card.
        await next.GetByTestId("next-visit-link").ClickAsync();
        await page.ReachAsync($"**/admin/appointments/calendar/{appointmentId}");
        (await page.GetByTestId("action-arrived").CountAsync()).ShouldBe(1);

        // 4. The client is here and pays in cash.
        await page.GetByTestId("action-arrived").GetByRole(AriaRole.Button).ClickAsync();
        await page.ReachAsync("**/admin/appointments?date=*");
        (await page.GotoAsync($"{root}/admin/payments/checkout/{appointmentId}")).ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByTestId("bill-method").SelectOptionAsync("Cash");
        await page.GetByTestId("pay-bill").ClickAsync();
        (await page.GetByTestId("flash").TextContentAsync()).ShouldBe("The payment has been recorded.");

        // 5. Back on the dashboard, from the menu: the money of the day has it and the visit is done.
        await page.OpenNavAsync("nav-dashboard");
        await page.ReachAsync(url => new Uri(url).AbsolutePath == "/admin");
        (await page.GetByTestId("takings-total").TextContentAsync()).ShouldBe("£45.00");
        (await page.GetByTestId("headline-takings").TextContentAsync()).ShouldBe("£45.00");
        (await page.GetByTestId("method-Cash").TextContentAsync()).ShouldBe("£45.00");
        (await page.GetByTestId("no-unpaid").CountAsync()).ShouldBe(1);
        int.Parse((await page.GetByTestId("status-Completed").TextContentAsync()).ShouldNotBeNull(), CultureInfo.InvariantCulture).ShouldBeGreaterThanOrEqualTo(1);
        (await page.Locator($"[data-testid='next-visit'][data-visit-id='{appointmentId}']").CountAsync()).ShouldBe(0, "a completed visit is not next");
        if (styled && width >= 1024)
        {
            // At a desk the money sits beside the visits instead of under them.
            var visits = (await page.GetByTestId("next").BoundingBoxAsync()).ShouldNotBeNull();
            var money = (await page.GetByTestId("money").BoundingBoxAsync()).ShouldNotBeNull();
            money.X.ShouldBeGreaterThan(visits.X + visits.Width - 1, "two columns at a desk");
        }

        await CheckPageAsync(page, styled, width, $"dashboard-{label}-{width}.png");
    }

    [Fact]
    public async Task The_owner_of_a_new_salon_is_told_there_is_nothing_yet_and_is_led_to_getting_started_on_a_phone()
    {
        // Own slug with no services and nobody on the team: a salon on its first day, whatever ran before.
        var tenantId = await app.EnsureTenantAsync(NewSlug, "Studio New", []);
        await app.EnsureUserAsync(tenantId, NewEmail, "Studio New Owner", Role.Owner, Password);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        (await page.GotoAsync(app.TenantUrl(NewSlug) + "/account/login")).ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(NewEmail);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync(url => new Uri(url).AbsolutePath == "/admin");
        var styled = await StylesheetLoadedAsync(page);

        // No row of zeros: the page says what is missing and where to start.
        (await page.GetByTestId("new-salon").IsVisibleAsync()).ShouldBeTrue();
        (await page.GetByTestId("headline-visits").CountAsync()).ShouldBe(0);
        (await page.GetByTestId("quick-actions").CountAsync()).ShouldBe(0);
        (await page.GetByTestId("dashboard-setup").IsVisibleAsync()).ShouldBeTrue();
        if (styled)
        {
            (await page.GetByTestId("dashboard-setup-link").BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
        }

        await CheckPageAsync(page, styled, 390, "dashboard-new-salon-mobile-390.png");

        await page.GetByTestId("dashboard-setup-link").ClickAsync();
        await page.ReachAsync("**/admin/settings/getting-started");
        (await page.GetByTestId("setup-steps").IsVisibleAsync()).ShouldBeTrue();
    }

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