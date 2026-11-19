using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Appointments;

/// <summary>
/// The desk on a phone sees on the diary a time kept for a waiting client (ADR 0069). Somebody cancelled and the
/// first client waiting was told; the calendar of that day marks the time with the client's first name, a tap leads
/// to the day's list, which says for whom and until when, and one button there lets the time go: the same day comes
/// back without the mark. The day's list is captured for the PR, passes axe and never scrolls sideways at 390 px.
/// </summary>
public class KeptTimeOnDiaryE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_desk_sees_a_kept_time_on_the_diary_and_lets_it_go_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        var lastName = "Kept" + Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);

        try
        {
            var (_, start, serviceName) = await app.KeepFreedTimeAsync(tenantId, "Willa", lastName, "+447700900156");
            var local = start.InZone(DateTimeZoneProviders.Tzdb["Europe/London"]).LocalDateTime;
            var day = LocalDatePattern.Iso.Format(local.Date);

            // 1. The calendar of that day, on a phone, marks the time with the client's first name.
            await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 390, Height = 844 } });
            var page = await context.NewPageAsync();
            (await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/appointments/calendar?view=day&date={day}")).ShouldNotBeNull().Ok.ShouldBeTrue();
            await page.GetByLabel("Email").FillAsync(Email);
            await page.GetByLabel("Password").FillAsync(Password);
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
            await page.ReachAsync("**/admin/appointments/calendar?view=day&date=*");
            var styled = await StylesheetLoadedAsync(page);
            var mark = styled ? page.GetByTestId("calendar-list-kept") : page.GetByTestId("calendar-kept");
            await Assertions.Expect(mark.First).ToContainTextAsync("Kept for Willa");
            await Assertions.Expect(mark.First).ToContainTextAsync(local.TimeOfDay.ToString("HH:mm", CultureInfo.InvariantCulture));

            // 2. A tap leads to the day's list: for whom, which service, until when, and the button.
            await mark.First.ClickAsync();
            await page.ReachAsync($"**/admin/appointments?date={day}");
            var kept = page.GetByTestId("kept-time");
            await Assertions.Expect(kept).ToHaveCountAsync(1);
            await Assertions.Expect(kept.GetByTestId("kept-time-text")).ToContainTextAsync($"Kept for Willa {lastName} from the waitlist ({serviceName}) until ");
            await CheckPageAsync(page, styled, "release-kept", "kept-time-day-mobile-390.png");

            // 3. Let go: the same day again, without the mark.
            await kept.GetByTestId("release-kept").ClickAsync();
            await page.ReachAsync($"**/admin/appointments?date={day}");
            await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("The time is no longer kept. Anybody can book it now.");
            (await page.GetByTestId("kept-time").CountAsync()).ShouldBe(0);
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