using Deque.AxeCore.Playwright;

using Microsoft.Playwright;

using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Settings;

/// <summary>
/// The demo owner on a phone opens Settings from the admin nav, changes both reminder lead times and the quiet hours,
/// saves, and sees the confirmation with the new values (ADR 0020). 390 px, axe clean, no horizontal scroll.
/// </summary>
public class NotificationSettingsE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_changes_the_reminders_and_quiet_hours_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync(app.TenantUrl(Slug) + "/admin/appointments");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/appointments");

        await page.GetByTestId("nav-settings").ClickAsync();
        await page.ReachAsync("**/admin/settings/notifications");
        var styled = await StylesheetLoadedAsync(page);

        // Values a previous run cannot have left behind, so the assertions below prove this save.
        var first = Random.Shared.Next(30, 60);
        var second = Random.Shared.Next(3, 9);
        await page.GetByTestId("reminders-enabled").CheckAsync();
        await page.GetByTestId("FirstReminderHours").FillAsync(first.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await page.GetByTestId("SecondReminderHours").FillAsync(second.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await page.GetByTestId("QuietFrom").FillAsync("22:00");
        await page.GetByTestId("QuietTo").FillAsync("07:30");
        await page.GetByTestId("save-notification-settings").ClickAsync();

        // The flash exists only on the page after the save; waiting for it is waiting for the redirect.
        (await page.GetByTestId("flash").TextContentAsync()).ShouldBe("Reminder settings have been saved.");
        (await page.GetByTestId("FirstReminderHours").InputValueAsync()).ShouldBe(first.ToString(System.Globalization.CultureInfo.InvariantCulture));
        (await page.GetByTestId("SecondReminderHours").InputValueAsync()).ShouldBe(second.ToString(System.Globalization.CultureInfo.InvariantCulture));
        (await page.GetByTestId("QuietFrom").InputValueAsync()).ShouldBe("22:00");
        (await page.GetByTestId("QuietTo").InputValueAsync()).ShouldBe("07:30");
        (await page.GetByTestId("reminders-enabled").IsCheckedAsync()).ShouldBeTrue();
        if (styled)
        {
            (await page.GetByTestId("save-notification-settings").BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "settings-notifications-mobile-390.png"),
            FullPage = true,
        });
        var axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {v.Help}")));

        // A rule broken on purpose: the message appears next to the field and nothing is saved.
        await page.GetByTestId("SecondReminderHours").FillAsync((first + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        await page.GetByTestId("save-notification-settings").ClickAsync();
        (await page.GetByTestId("SecondReminderHours-error").Filter(new LocatorFilterOptions { HasText = "closer to the visit" }).TextContentAsync())
            .ShouldBe("The second reminder must be closer to the visit than the first.");
    }

    /// <summary>True when a stylesheet resolved with rules in it (a 404 or an empty body parses to zero rules).</summary>
    private static Task<bool> StylesheetLoadedAsync(IPage page)
        => page.EvaluateAsync<bool>("() => Array.from(document.styleSheets).some(s => s.href && s.cssRules.length > 0)");
}