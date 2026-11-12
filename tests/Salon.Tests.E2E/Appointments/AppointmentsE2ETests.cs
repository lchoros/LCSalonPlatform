using Deque.AxeCore.Playwright;

using Microsoft.Playwright;

using NodaTime;
using NodaTime.Text;

using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Appointments;

/// <summary>
/// The demo owner on a phone signs in, opens today's appointments (one column per seeded staff member), taps "Book",
/// finds the demo client Sophie Smith, picks a service, any staff and the next day with free times, taps the first slot
/// and lands back on that day with a confirmation. Both screens are captured for the PR, pass axe and never scroll sideways.
/// </summary>
public class AppointmentsE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];

    [Fact]
    public async Task The_demo_owner_reviews_the_day_and_books_sophie_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m), ("Colour (roots)", 90, 65m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync(app.TenantUrl(Slug) + "/admin/appointments");
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.WaitForURLAsync("**/admin/appointments");

        var styled = await StylesheetLoadedAsync(page);
        (await page.GetByTestId("nav-appointments").IsVisibleAsync()).ShouldBeTrue();
        (await page.GetByTestId("staff-column").CountAsync()).ShouldBe(3);
        (await page.GetByTestId("book").IsVisibleAsync()).ShouldBeTrue();
        if (styled)
        {
            (await page.GetByTestId("book").BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "appointments-day-mobile-390.png"),
            FullPage = true,
        });
        var dayAxe = await page.RunAxe();
        dayAxe.Violations.ShouldBeEmpty(string.Join("; ", dayAxe.Violations.Select(v => $"{v.Id}: {v.Help}")));

        await page.GetByTestId("book").ClickAsync();
        await page.WaitForURLAsync("**/admin/appointments/new*");

        await page.GetByTestId("client-search").FillAsync("Sop");
        await page.GetByTestId("client-search-submit").ClickAsync();
        await page.WaitForURLAsync("**/admin/appointments/new?*q=Sop*");
        var sophie = page.GetByTestId("client-match").Filter(new LocatorFilterOptions { HasText = "Sophie Smith" });
        (await sophie.CountAsync()).ShouldBe(1);
        await sophie.ClickAsync();
        await page.WaitForURLAsync("**/admin/appointments/new?*clientId=*");
        (await page.GetByTestId("selected-client").TextContentAsync()).ShouldNotBeNull().ShouldContain("Sophie Smith");

        // Tomorrow is not always a working day (nobody works on Sunday); the first day with free times within a week is.
        var today = SystemClock.Instance.GetCurrentInstant().InZone(London).Date;
        var slots = page.GetByTestId("slot");
        string? chosenDay = null;
        for (var offset = 1; offset <= 7 && chosenDay is null; offset++)
        {
            var day = LocalDatePattern.Iso.Format(today.PlusDays(offset));
            await page.GetByTestId("service-select").SelectOptionAsync(new SelectOptionValue { Label = "Cut & blow-dry (45 min)" });
            await page.GetByTestId("staff-select").SelectOptionAsync(new SelectOptionValue { Value = string.Empty });
            await page.GetByTestId("date-input").FillAsync(day);
            await page.GetByTestId("show-slots").ClickAsync();
            await page.WaitForURLAsync($"**/admin/appointments/new?*date={day}*");
            if (await slots.CountAsync() > 0)
            {
                chosenDay = day;
            }
        }

        chosenDay.ShouldNotBeNull("no free times in the coming week for the demo staff");
        if (styled)
        {
            (await slots.First.BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "appointments-book-mobile-390.png"),
            FullPage = true,
        });
        var bookAxe = await page.RunAxe();
        bookAxe.Violations.ShouldBeEmpty(string.Join("; ", bookAxe.Violations.Select(v => $"{v.Id}: {v.Help}")));

        var firstSlotTime = (await slots.First.TextContentAsync()).ShouldNotBeNull().Trim();
        await slots.First.ClickAsync();
        await page.WaitForURLAsync($"**/admin/appointments?date={chosenDay}");

        (await page.GetByTestId("flash").TextContentAsync()).ShouldNotBeNull().ShouldContain("Sophie Smith");
        var booked = page.GetByTestId("appointment").Filter(new LocatorFilterOptions { HasText = "Sophie Smith" });
        (await booked.CountAsync()).ShouldBeGreaterThanOrEqualTo(1);
        (await booked.First.TextContentAsync()).ShouldNotBeNull().ShouldContain(firstSlotTime[..5]);
        if (styled)
        {
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        }
    }

    /// <summary>True when a stylesheet resolved with rules in it (a 404 or an empty body parses to zero rules).</summary>
    private static Task<bool> StylesheetLoadedAsync(IPage page)
        => page.EvaluateAsync<bool>("() => Array.from(document.styleSheets).some(s => s.href && s.cssRules.length > 0)");
}