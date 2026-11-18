using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Booking;

/// <summary>
/// A waiting client on a phone takes the time that is kept for them (ADR 0065). Somebody cancelled; the client got a
/// text with a short link. The desk's waitlist says whose the time is. The client opens the link without signing in,
/// sees which service, when, with whom, the price and until when it is kept, taps one button and has the visit; the
/// link from there leads to the page where the visit can be changed or cancelled. The link again shows the visit, and
/// the desk's list no longer has the client. Both pages are captured for the PR, pass axe and never scroll sideways
/// at 390 px.
/// </summary>
public class TakeHeldTimeE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task A_waiting_client_books_the_time_kept_for_them_with_one_tap_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        var suffix = Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var lastName = "Waiting" + suffix;

        try
        {
            var (linkPath, start, serviceName) = await app.KeepFreedTimeAsync(tenantId, "Wanda", lastName, "+447700900155");
            var when = start.InZone(DateTimeZoneProviders.Tzdb["Europe/London"]).LocalDateTime;
            app.Sms.Messages[^1].Body.ShouldEndWith(linkPath);

            // 1. The desk sees that the time is kept and for whom.
            await using var deskContext = await app.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 390, Height = 844 } });
            var desk = await deskContext.NewPageAsync();
            (await desk.GotoAsync($"{app.TenantUrl(Slug)}/admin/appointments/waitlist")).ShouldNotBeNull().Ok.ShouldBeTrue();
            await desk.GetByLabel("Email").FillAsync(Email);
            await desk.GetByLabel("Password").FillAsync(Password);
            await desk.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
            await desk.ReachAsync("**/admin/appointments/waitlist");
            var styled = await StylesheetLoadedAsync(desk);
            await Assertions.Expect(desk.GetByTestId("waiting-held")).ToContainTextAsync("Kept for this client: ");
            await Assertions.Expect(desk.GetByTestId("waiting-held")).ToContainTextAsync(when.ToString("HH:mm", CultureInfo.InvariantCulture));
            await CheckPageAsync(desk, styled, "release-held", "waitlist-held-desk-mobile-390.png");

            // 2. The client, signed in nowhere, opens the link from the text.
            await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 390, Height = 844 } });
            var page = await context.NewPageAsync();
            (await page.GotoAsync(app.TenantUrl(Slug) + linkPath)).ShouldNotBeNull().Ok.ShouldBeTrue();
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Level = 1 })).ToHaveTextAsync("A time is kept for you");
            await Assertions.Expect(page.GetByTestId("held-details")).ToContainTextAsync(serviceName);
            await Assertions.Expect(page.GetByTestId("held-when")).ToContainTextAsync(when.ToString("HH:mm", CultureInfo.InvariantCulture));
            (await page.GetByTestId("held-price").TextContentAsync()).ShouldBe("£45.00");
            await Assertions.Expect(page.GetByTestId("held-until")).ToContainTextAsync("Wanda, this time is kept for you until ");
            await CheckPageAsync(page, styled, "take-time", "held-time-mobile-390.png");

            // 3. One tap.
            await page.GetByTestId("take-time").ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Level = 1 })).ToHaveTextAsync("Your visit is booked");
            await Assertions.Expect(page.GetByTestId("held-result")).ToHaveTextAsync("It is booked. We look forward to seeing you.");
            (await page.GetByTestId("take-time").CountAsync()).ShouldBe(0);
            await CheckPageAsync(page, styled, "manage-visit", "held-time-booked-mobile-390.png");

            // 4. From there to the visit's own page, where it can be changed or cancelled.
            await page.GetByTestId("manage-visit").ClickAsync();
            await page.ReachAsync("**/book/cancel/*");
            await Assertions.Expect(page.GetByTestId("booking-details")).ToContainTextAsync(serviceName);

            // 5. The link again is the visit, not a second booking; the desk's list no longer keeps anything.
            (await page.GotoAsync(app.TenantUrl(Slug) + linkPath)).ShouldNotBeNull().Ok.ShouldBeTrue();
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Level = 1 })).ToHaveTextAsync("Your visit is booked");
            await desk.ReloadAsync();
            (await desk.GetByTestId("waiting-held").CountAsync()).ShouldBe(0);
        }
        finally
        {
            // The other browser tests share this salon and expect the text to waiting clients to be off.
            await app.UseDefaultSettingsAsync(tenantId);
        }
    }

    /// <summary>Touch target (when styled), no sideways scroll, a screenshot for the PR and an axe pass.</summary>
    private static async Task CheckPageAsync(IPage page, bool styled, string buttonTestId, string screenshot)
    {
        if (styled)
        {
            (await page.GetByTestId(buttonTestId).First.BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
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