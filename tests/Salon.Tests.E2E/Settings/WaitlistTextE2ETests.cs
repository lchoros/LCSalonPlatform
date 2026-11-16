using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Settings;

/// <summary>
/// The owner on a phone switches on the text to the first person waiting when a visit is cancelled (ADR 0056), and
/// finds the wording of that text among the messages. 390 px, axe clean, no sideways scroll.
/// </summary>
public class WaitlistTextE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_owner_switches_on_the_text_for_a_freed_time_and_reads_its_wording_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        try
        {
            (await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/settings/waitlist")).ShouldNotBeNull().Ok.ShouldBeTrue();
            await page.GetByLabel("Email").FillAsync(Email);
            await page.GetByLabel("Password").FillAsync(Password);
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
            await page.ReachAsync("**/admin/settings/waitlist");
            var styled = await StylesheetLoadedAsync(page);

            // 1. Off to begin with; ticked and saved, it stays ticked and the other switch stays off.
            (await page.GetByTestId("tell-when-freed").IsCheckedAsync()).ShouldBeFalse();
            await page.GetByTestId("tell-when-freed").CheckAsync();
            await page.GetByTestId("save-public-waitlist").ClickAsync();
            await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("The waitlist settings have been saved.");
            (await page.GetByTestId("tell-when-freed").IsCheckedAsync()).ShouldBeTrue();
            (await page.GetByTestId("public-waitlist-enabled").IsCheckedAsync()).ShouldBeFalse();
            if (styled)
            {
                (await page.GetByTestId("save-public-waitlist").BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
                (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
            }

            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "settings-waitlist-text-mobile-390.png"),
                FullPage = true,
            });
            var axe = await page.RunAxe();
            axe.Violations.ShouldBeEmpty(string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {v.Help}")));

            // 2. The wording of the text is with the other messages, and the owner can change it there.
            await page.GetByTestId("settings-nav-messages").ClickAsync();
            await page.ReachAsync("**/admin/settings/messages");
            await page.GetByTestId("edit-Waitlist.TimeFreed-Sms").ClickAsync();
            await page.ReachAsync(url => url.Contains("/admin/settings/messages/Waitlist.TimeFreed/", StringComparison.OrdinalIgnoreCase));
            (await page.ContentAsync()).ShouldContain("has come up on {{AppointmentStart}}");
        }
        finally
        {
            // The other browser tests share this salon and expect the switch to be off.
            await app.UseDefaultSettingsAsync(tenantId);
        }
    }

    /// <summary>True when a stylesheet resolved with rules in it (a 404 or an empty body parses to zero rules).</summary>
    private static Task<bool> StylesheetLoadedAsync(IPage page)
        => page.EvaluateAsync<bool>("() => Array.from(document.styleSheets).some(s => s.href && s.cssRules.length > 0)");
}