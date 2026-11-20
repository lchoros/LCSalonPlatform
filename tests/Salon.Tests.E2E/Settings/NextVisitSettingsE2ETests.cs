using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Settings;

/// <summary>
/// The demo owner on a phone opens Settings, changes the weeks offered for the next visit to five and ten, and the
/// very next payment offers exactly those two (ADR 0039). 390 px, axe clean, no horizontal scroll, 44 px buttons.
/// </summary>
public class NextVisitSettingsE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_owner_sets_five_and_ten_weeks_and_the_next_payment_offers_exactly_those_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        // A surname no earlier run can have left behind, so the visit found below is this run's.
        var lastName = "Weeks" + Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var (appointmentId, _, _) = await app.AddVisitAsync(tenantId, "Olga", lastName, -Duration.FromMinutes(20), deposit: 0m);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();
        var checkout = $"/admin/payments/checkout/{appointmentId}";

        try
        {
            var response = await page.GotoAsync(app.TenantUrl(Slug) + "/admin/appointments");
            response.ShouldNotBeNull().Ok.ShouldBeTrue();
            await page.GetByLabel("Email").FillAsync(Email);
            await page.GetByLabel("Password").FillAsync(Password);
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
            await page.ReachAsync("**/admin/appointments");

            // 1. Settings, then the "Next visit" tab: four, six and eight weeks to begin with.
            await page.OpenNavAsync("nav-settings");
            await page.ReachAsync("**/admin/settings/notifications");
            await page.GetByTestId("settings-nav-next-visit").ClickAsync();
            await page.ReachAsync("**/admin/settings/next-visit");
            var styled = await StylesheetLoadedAsync(page);
            (await page.GetByTestId("FirstWeeks").InputValueAsync()).ShouldBe("4");
            (await page.GetByTestId("offered-choice").AllTextContentsAsync()).Select(text => text.Trim()).ShouldBe(["In 4 weeks", "In 6 weeks", "In 8 weeks"]);

            // 2. More than a year ahead is refused next to its field, and what was typed is kept.
            await page.GetByTestId("FirstWeeks").FillAsync("5");
            await page.GetByTestId("SecondWeeks").FillAsync("0");
            await page.GetByTestId("ThirdWeeks").FillAsync("60");
            await page.GetByTestId("save-next-visit").ClickAsync();
            await Assertions.Expect(page.GetByTestId("ThirdWeeks-error")).ToHaveTextAsync("Weeks must be between 0 and 52.");
            (await page.GetByTestId("FirstWeeks").InputValueAsync()).ShouldBe("5");
            await CheckPageAsync(page, styled, "save-next-visit", "next-visit-settings-error-mobile-390.png");

            // 3. Five, none and ten are saved; the page shows what the desk will see.
            await page.GetByTestId("ThirdWeeks").FillAsync("10");
            await page.GetByTestId("save-next-visit").ClickAsync();
            await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("The choices for the next visit have been saved.");
            (await page.GetByTestId("offered-choice").AllTextContentsAsync()).Select(text => text.Trim()).ShouldBe(["In 5 weeks", "In 10 weeks"]);
            await CheckPageAsync(page, styled, "save-next-visit", "next-visit-settings-mobile-390.png");

            // 4. The next payment offers exactly those two.
            await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/appointments/calendar/{appointmentId}");
            await page.ReachAsync($"**/admin/appointments/calendar/{appointmentId}");
            await page.GetByTestId("action-arrived").GetByRole(AriaRole.Button).ClickAsync();
            await page.ReachAsync("**/admin/appointments?date=*");
            await page.GotoAsync(app.TenantUrl(Slug) + checkout);
            await page.ReachAsync("**" + checkout);
            await page.GetByTestId("pay-bill").ClickAsync();
            await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("The payment has been recorded.");
            var choices = page.GetByTestId("rebook-choice");
            (await choices.CountAsync()).ShouldBe(2);
            (await choices.Nth(0).GetAttributeAsync("data-weeks")).ShouldBe("5");
            (await choices.Nth(1).GetAttributeAsync("data-weeks")).ShouldBe("10");
        }
        finally
        {
            // The other browser tests share this salon and expect four, six and eight weeks.
            await app.UseDefaultSettingsAsync(tenantId);
        }
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