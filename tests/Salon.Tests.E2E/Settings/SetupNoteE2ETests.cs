using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Settings;

/// <summary>
/// The owner of a salon that is not set up yet opens the diary on a phone and is told how far the set-up has come
/// (ADR 0072); the note leads to the getting started page. The salon here never saves a privacy notice, so its
/// set-up is never finished and the note is always due.
/// </summary>
public class SetupNoteE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-setup";
    private const string Email = "owner@studio-setup.local";
    private const string Password = "StudioSetup!e2e";

    [Fact]
    public async Task The_owner_is_told_on_the_diary_that_the_salon_is_not_set_up_and_led_to_the_steps()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio Setup", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio Setup Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/appointments");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/appointments*");

        // 1. The day list carries the note; the salon has no privacy notice and no clients, so set-up is unfinished.
        var note = page.GetByTestId("setup-note");
        await Assertions.Expect(note).ToBeVisibleAsync();
        (await note.TextContentAsync()).ShouldNotBeNull().ShouldContain("Your salon is not set up yet:");
        (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "diary-setup-note-mobile-390.png"),
            FullPage = true,
        });
        var axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {v.Help}")));

        // 2. The calendar carries the same note.
        (await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/appointments/calendar")).ShouldNotBeNull().Ok.ShouldBeTrue();
        await Assertions.Expect(page.GetByTestId("setup-note")).ToBeVisibleAsync();

        // 3. The note leads to the getting started page, where the same count stands.
        await page.GetByTestId("setup-note-link").ClickAsync();
        await page.ReachAsync("**/admin/settings/getting-started");
        await Assertions.Expect(page.GetByTestId("setup-progress")).ToBeVisibleAsync();
    }
}