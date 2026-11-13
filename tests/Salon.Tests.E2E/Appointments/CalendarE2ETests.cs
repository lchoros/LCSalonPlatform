using System.Text.RegularExpressions;

using Deque.AxeCore.Playwright;

using Microsoft.Playwright;

using NodaTime;
using NodaTime.Text;

using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Appointments;

/// <summary>
/// The demo owner opens the calendar on the day of the first seeded visit (ADR 0016): the day grid (or, on a phone with
/// "All", the stacked list) shows a block, the week of that person shows it too, the block opens its card and the first
/// free time moves it, landing back on the calendar with a confirmation. Both sizes are captured, pass axe, never scroll
/// sideways and ship no inline script.
/// </summary>
public partial class CalendarE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];

    [Theory]
    [InlineData("mobile", 390, 844)]
    [InlineData("desktop", 1280, 800)]
    public async Task The_demo_owner_reads_the_day_and_the_week_and_moves_a_visit(string label, int width, int height)
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m), ("Colour (roots)", 90, 65m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        var (appointmentId, start, staffId) = await app.FirstUpcomingAppointmentAsync(tenantId);
        var day = LocalDatePattern.Iso.Format(start.InZone(London).Date);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        var page = await context.NewPageAsync();
        // Trace saved next to the screenshots (CI uploads that folder) so a hang on the runner can be read offline.
        await context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true });
        try
        {

            await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/appointments/calendar?view=day&date={day}");
            await page.GetByLabel("Email").FillAsync(Email);
            await page.GetByLabel("Password").FillAsync(Password);
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
            await page.ReachAsync("**/admin/appointments/calendar?*");

            var styled = await StylesheetLoadedAsync(page);
            (await page.GetByTestId("nav-calendar").IsVisibleAsync()).ShouldBeTrue();
            (await page.GetByTestId("calendar-block").CountAsync()).ShouldBeGreaterThanOrEqualTo(1, "the seeded visit is drawn");
            (await page.GetByTestId("calendar-list-item").CountAsync()).ShouldBeGreaterThanOrEqualTo(1, "the phone list mirrors the grid");
            if (styled)
            {
                var phone = width < 640;
                (await page.GetByTestId("calendar-grid").IsVisibleAsync()).ShouldBe(!phone, "a phone shows the list for everyone, a desktop the grid");
                (await page.GetByTestId("calendar-list").IsVisibleAsync()).ShouldBe(phone);
                (await page.GetByTestId("book").BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
                (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(width);
            }

            await AssertNoInlineScriptAsync(page);
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, $"calendar-day-{label}-{width}.png"),
                FullPage = true,
            });
            var dayAxe = await page.RunAxe();
            dayAxe.Violations.ShouldBeEmpty(string.Join("; ", dayAxe.Violations.Select(v => $"{v.Id}: {v.Help}")));

            // One person's day is a grid on every screen size.
            await page.Locator($"[data-testid='staff-chip'][data-staff-id='{staffId}']").ClickAsync();
            await page.ReachAsync($"**/admin/appointments/calendar?view=day&date={day}&staff={staffId}");
            var block = page.Locator($"[data-testid='calendar-block'][data-appointment-id='{appointmentId}']").First;
            (await block.IsVisibleAsync()).ShouldBeTrue();
            if (styled)
            {
                (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(width);
            }

            await page.GetByTestId("view-week").ClickAsync();
            await page.ReachAsync("**/admin/appointments/calendar?view=week*");
            (await page.GetByTestId("calendar-column").CountAsync()).ShouldBe(7);
            (await page.GetByTestId("calendar-block").CountAsync()).ShouldBeGreaterThanOrEqualTo(1);
            if (styled)
            {
                // The week is wider than a phone; it scrolls inside its own box, never the page.
                (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(width);
            }

            await AssertNoInlineScriptAsync(page);
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, $"calendar-week-{label}-{width}.png"),
                FullPage = true,
            });
            var weekAxe = await page.RunAxe();
            weekAxe.Violations.ShouldBeEmpty(string.Join("; ", weekAxe.Violations.Select(v => $"{v.Id}: {v.Help}")));

            await page.Locator($"[data-testid='calendar-block'][data-appointment-id='{appointmentId}']").First.ClickAsync();
            await page.ReachAsync($"**/admin/appointments/calendar/{appointmentId}*");
            (await page.GetByTestId("appointment-card").IsVisibleAsync()).ShouldBeTrue();
            (await page.GetByTestId("move-step").IsVisibleAsync()).ShouldBeTrue();

            // The visit's own day usually has free times; if not (a full day, the notice rule), the next days within a week do.
            var slots = page.GetByTestId("move-slot");
            var today = SystemClock.Instance.GetCurrentInstant().InZone(London).Date;
            for (var offset = 1; offset <= 7 && await slots.CountAsync() == 0; offset++)
            {
                await page.GetByTestId("move-date").FillAsync(LocalDatePattern.Iso.Format(today.PlusDays(offset)));
                await page.GetByTestId("move-show-slots").ClickAsync();
                await page.ReachAsync($"**/admin/appointments/calendar/{appointmentId}?*date=*");
            }

            (await slots.CountAsync()).ShouldBeGreaterThanOrEqualTo(1, "no free time to move the visit to within a week");
            if (styled)
            {
                (await slots.First.BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
            }

            await AssertNoInlineScriptAsync(page);
            var cardAxe = await page.RunAxe();
            cardAxe.Violations.ShouldBeEmpty(string.Join("; ", cardAxe.Violations.Select(v => $"{v.Id}: {v.Help}")));

            var chosenTime = (await slots.First.TextContentAsync()).ShouldNotBeNull().Trim();
            await slots.First.ClickAsync();
            await page.ReachAsync("**/admin/appointments/calendar?view=day&date=*");

            (await page.GetByTestId("flash").TextContentAsync()).ShouldNotBeNull().ShouldContain(chosenTime[..5]);
            var moved = page.Locator($"[data-testid='calendar-block'][data-appointment-id='{appointmentId}']").First;
            (await moved.CountAsync()).ShouldBe(1);
            (await moved.TextContentAsync()).ShouldNotBeNull().ShouldContain(chosenTime[..5]);
        }
        finally
        {
            await context.Tracing.StopAsync(new TracingStopOptions { Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, $"calendar-{label}-trace.zip") });
        }
    }

    /// <summary>CLAUDE.md rule 8: every script on the page comes from a file; none is inline.</summary>
    private static async Task AssertNoInlineScriptAsync(IPage page)
    {
        var html = await page.ContentAsync();
        InlineScriptPattern().IsMatch(html).ShouldBeFalse("the page has a <script> without src");
    }

    /// <summary>True when a stylesheet resolved with rules in it (a 404 or an empty body parses to zero rules).</summary>
    private static Task<bool> StylesheetLoadedAsync(IPage page)
        => page.EvaluateAsync<bool>("() => Array.from(document.styleSheets).some(s => s.href && s.cssRules.length > 0)");

    [GeneratedRegex("<script(?![^>]*\\ssrc=)", RegexOptions.IgnoreCase)]
    private static partial Regex InlineScriptPattern();
}