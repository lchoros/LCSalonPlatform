using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Identity;

/// <summary>
/// The receptionist on a phone changes the starting password the owner handed over (ADR 0077): their own name in
/// the header opens the page, the change keeps this phone signed in, and the new password signs them back in after
/// a sign-out. The page fits 390 px and is axe clean.
/// </summary>
public class ChangePasswordE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";

    [Fact]
    public async Task A_person_changes_their_own_password_from_their_name_in_the_header()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);

        // A person no earlier run can have left behind: an email is unique per salon, and the password changes here.
        var suffix = Random.Shared.Next(100_000, 999_999).ToString(CultureInfo.InvariantCulture);
        var email = $"pat-{suffix}@studio-20.local";
        await app.EnsureUserAsync(tenantId, email, $"Pat {suffix}", Role.Receptionist, "Starting-pass-9");

        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/appointments");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Password").FillAsync("Starting-pass-9");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/appointments*");

        // 1. Their own name in the menu is the way in (on a phone the menu lives in the drawer, ADR 0080).
        await page.OpenNavAsync("user-name");
        await page.ReachAsync("**/account/password");
        (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "change-password-mobile-390.png"),
            FullPage = true,
        });
        var axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {v.Help}")));

        // 2. The change confirms on the same page — this phone is still signed in.
        await page.GetByLabel("Current password").FillAsync("Starting-pass-9");
        await page.GetByLabel("New password", new PageGetByLabelOptions { Exact = true }).FillAsync("My-own-pass-9");
        await page.GetByLabel("New password again").FillAsync("My-own-pass-9");
        await page.GetByTestId("change-password-submit").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToContainTextAsync("Your password has been changed.");

        // 3. Back at the desk they sign out from the menu; the new password is the one that works.
        await page.GetByTestId("back-to-admin").ClickAsync();
        await page.ReachAsync("**/admin/appointments*");
        await page.OpenNavAsync("sign-out");
        await page.ReachAsync(url => new Uri(url).AbsolutePath == "/");
        await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/appointments");
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Password").FillAsync("My-own-pass-9");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/appointments*");
        await Assertions.Expect(page.GetByTestId("day")).ToBeVisibleAsync();
    }
}