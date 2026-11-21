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

            // Any date straight from the browser's own calendar (ADR 0092): pick a day next week and come back. The bar
            // has no "Day list" or "Waitlist" button any more — those are the tabs above it.
            var elsewhere = DateOnly.ParseExact(day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).AddDays(7).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            await page.GetByTestId("jump-date").FillAsync(elsewhere);
            await page.ReachAsync(url => url.Contains($"date={elsewhere}", StringComparison.Ordinal));
            (await page.GetByTestId("jump-date").InputValueAsync()).ShouldBe(elsewhere);
            await page.GetByTestId("jump-date").FillAsync(day);
            await page.ReachAsync(url => url.Contains($"date={day}", StringComparison.Ordinal));
            (await page.GetByTestId("calendar-bar").GetByTestId("day-list").CountAsync()).ShouldBe(0);
            (await page.GetByTestId("section-tab-day").CountAsync()).ShouldBe(1);

            // One person's day is a grid on every screen size.
            await page.GetByTestId("staff-select").SelectOptionAsync(staffId.ToString()); // auto-submit.js sends the form on change (ADR 0087)
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
                await page.GetByTestId("move-date").FillAsync(LocalDatePattern.Iso.Format(today.PlusDays(offset))); // the change submits the form (auto-submit.js)
                await page.ReachAsync($"**/admin/appointments/calendar/{appointmentId}?*date=*");
            }

            (await slots.CountAsync()).ShouldBeGreaterThanOrEqualTo(1, "no free time to move the visit to within a week");
            if (styled)
            {
                (await slots.First.BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
            }

            // The card tells the story of the visit so far (ADR 0091): at least the booking.
            (await page.GetByTestId("card-history").GetByTestId("history-line").CountAsync()).ShouldBeGreaterThanOrEqualTo(1);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, $"calendar-card-history-{label}-{width}.png"), FullPage = true });

            await AssertNoInlineScriptAsync(page);
            var cardAxe = await page.RunAxe();
            cardAxe.Violations.ShouldBeEmpty(string.Join("; ", cardAxe.Violations.Select(v => $"{v.Id}: {v.Help}")));

            var chosenTime = (await slots.First.TextContentAsync()).ShouldNotBeNull().Trim();
            await slots.First.ClickAsync();

            // The tap only chooses; the button does the move (ADR 0086).
            await page.GetByTestId("move-confirm").ClickAsync();
            await page.ReachAsync("**/admin/appointments/calendar?view=day&date=*");

            (await page.GetByTestId("flash").TextContentAsync()).ShouldNotBeNull().ShouldContain(chosenTime[..5]);

            // The answer floats above the page instead of pushing the grid down under the pointer (ADR 0091).
            (await page.GetByTestId("toasts").GetByTestId("flash").CountAsync()).ShouldBe(1);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, $"calendar-moved-toast-{label}-{width}.png") });
            if (styled)
            {
                (await page.GetByTestId("toasts").EvaluateAsync<string>("e => getComputedStyle(e).position")).ShouldBe("fixed");
            }

            var moved = page.Locator($"[data-testid='calendar-block'][data-appointment-id='{appointmentId}']").First;
            (await moved.CountAsync()).ShouldBe(1);
            (await moved.TextContentAsync()).ShouldNotBeNull().ShouldContain(chosenTime[..5]);

            if (width >= 1024)
            {
                // Dragging the block an hour down with the mouse: the script posts the drop and the SERVER decides
                // (ADR 0086). Either answer proves the wiring end to end — a confirmation after the reload, or the
                // refusal in the server's own words — and the visit is on the grid exactly once either way.
                await page.GotoAsync(page.Url);
                await page.ReachAsync("**/admin/appointments/calendar?view=day&date=*");
                (await page.GetByTestId("flash").CountAsync()).ShouldBe(0, "the earlier confirmation was shown once");
                var dragged = page.Locator($"[data-testid='calendar-block'][data-appointment-id='{appointmentId}']").First;
                await Assertions.Expect(dragged).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-draggable"));
                var box = (await dragged.BoundingBoxAsync()).ShouldNotBeNull();
                var x = box.X + (box.Width / 2);
                var y = box.Y + Math.Min(box.Height / 2, 6);
                await page.Mouse.MoveAsync(x, y);
                await page.Mouse.DownAsync();
                await page.Mouse.MoveAsync(x, y + 32, new MouseMoveOptions { Steps = 8 });
                await page.Mouse.MoveAsync(x, y + 64, new MouseMoveOptions { Steps = 8 });
                await page.Mouse.UpAsync();

                var outcome = page.Locator("[data-testid='flash'], [data-testid='drag-message']").First;
                await Assertions.Expect(outcome).ToBeVisibleAsync();
                (await outcome.TextContentAsync()).ShouldNotBeNullOrWhiteSpace();
                (await page.GetByTestId("toasts").Locator("[data-testid='flash'], [data-testid='drag-message']").CountAsync())
                    .ShouldBe(1, "one answer at a time, in the floating stack: never \"moved\" and \"not free\" together");
                (await page.Locator($"[data-testid='calendar-block'][data-appointment-id='{appointmentId}']").CountAsync()).ShouldBe(1);
            }
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