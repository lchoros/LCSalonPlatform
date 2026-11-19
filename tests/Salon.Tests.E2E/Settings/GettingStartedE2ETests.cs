using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Settings;

/// <summary>
/// The demo owner on a phone opens "Getting started" (ADR 0068): the demo salon has its team, hours, price list,
/// clients and coming visits, so those steps read as done; the privacy notice is still to do, with a link to where it
/// is done (saving it there ticks the step), and the page ends with the way to the booking page. 390 px, axe clean,
/// no sideways scroll.
/// </summary>
public class GettingStartedE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_sees_what_is_set_up_and_what_is_left_on_a_phone()
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

        await page.GotoAsync(app.TenantUrl(Slug) + "/admin/settings/getting-started");
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/settings/getting-started");

        foreach (var step in new[] { "Team", "Hours", "PriceList", "Clients", "Diary", "Reminders" })
        {
            (await page.Locator($"[data-step='{step}']").GetAttributeAsync("data-done")).ShouldBe("true", step);
        }

        (await page.Locator("[data-step='PrivacyNotice']").GetAttributeAsync("data-done")).ShouldBe("false");
        (await page.GetByTestId("setup-progress").TextContentAsync()).ShouldNotBeNull().ShouldContain("steps done.");
        (await page.GetByTestId("setup-booking-page").GetAttributeAsync("href")).ShouldBe("/book");

        (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "getting-started-mobile-390.png"),
            FullPage = true,
        });
        var axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {v.Help}")));

        // The privacy step's link leads to where it is done; saving there ticks it.
        await page.Locator("[data-step='PrivacyNotice']").GetByTestId("setup-step-link").ClickAsync();
        await page.ReachAsync("**/admin/settings/privacy");
        await page.GetByTestId("save-privacy").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToBeVisibleAsync();
        await page.GotoAsync(app.TenantUrl(Slug) + "/admin/settings/getting-started");
        await page.ReachAsync("**/admin/settings/getting-started");
        (await page.Locator("[data-step='PrivacyNotice']").GetAttributeAsync("data-done")).ShouldBe("true");
    }
}