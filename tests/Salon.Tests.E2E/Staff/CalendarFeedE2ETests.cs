using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Staff;

/// <summary>
/// The demo owner on a phone switches a staff member's calendar address on, a calendar app reads it without signing
/// in and finds the visit, and switching it off stops the address (ADR 0038). 390 px, axe clean, no horizontal scroll,
/// 44 px buttons.
/// </summary>
public class CalendarFeedE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_switches_a_calendar_address_on_and_a_calendar_app_reads_the_visits()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        // A surname no earlier run can have left behind; the feed shows only its initial.
        var lastName = "Zed" + Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        await app.AddVisitAsync(tenantId, "Wanda", lastName, Duration.FromHours(26), deposit: 0m);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/staff");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/staff");
        var styled = await StylesheetLoadedAsync(page);

        // 1. The first person on the list (the one visits are booked with) has a "Calendar" link; the address is off.
        await page.GetByTestId("staff-card").First.GetByTestId("calendar-feed").ClickAsync();
        await page.ReachAsync("**/admin/staff/*/calendar-feed");
        var feedPage = new Uri(page.Url).AbsolutePath;
        (await page.GetByTestId("feed-off-state").IsVisibleAsync()).ShouldBeTrue();
        await CheckPageAsync(page, styled, "feed-on-button", "calendar-feed-off-mobile-390.png");

        // 2. Switched on: the page shows a private address on the salon's own host.
        await page.GetByTestId("feed-on-button").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("The address is on. Paste it into the calendar.");
        await page.ReachAsync(url => new Uri(url).AbsolutePath == feedPage);
        var address = await page.GetByTestId("feed-address").InputValueAsync();
        address.ShouldStartWith(app.TenantUrl(Slug) + "/calendar/");
        address.ShouldEndWith(".ics");
        await CheckPageAsync(page, styled, "feed-off", "calendar-feed-on-mobile-390.png");

        // 3. A calendar app has no sign-in: a request without cookies gets the visit, with the surname cut to its initial.
        // Outside Chromium "studio-20.localhost" does not resolve, so the request goes to the loopback address with the salon's host.
        var calendarApp = await app.Browser.NewContextAsync();
        var direct = app.BaseUrl + new Uri(address).PathAndQuery;
        var asSalon = new APIRequestContextOptions { Headers = new Dictionary<string, string> { ["Host"] = new Uri(address).Authority } };
        var file = await calendarApp.APIRequest.GetAsync(direct, asSalon);
        file.Status.ShouldBe(200);
        file.Headers["content-type"].ShouldBe("text/calendar; charset=utf-8");
        var text = await file.TextAsync();
        text.ShouldStartWith("BEGIN:VCALENDAR");
        text.ShouldContain("SUMMARY:Cut & blow-dry · Wanda Z.");
        text.ShouldNotContain(lastName);

        // 4. Switched off: the same address is gone at once.
        await page.GetByTestId("feed-off").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("The address is switched off. It no longer works.");
        (await calendarApp.APIRequest.GetAsync(direct, asSalon)).Status.ShouldBe(404);
        await calendarApp.DisposeAsync();
    }

    /// <summary>Touch target (when styled), no sideways scroll, a screenshot for the PR and an axe pass.</summary>
    private static async Task CheckPageAsync(IPage page, bool styled, string targetTestId, string screenshot)
    {
        if (styled)
        {
            (await page.GetByTestId(targetTestId).BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
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