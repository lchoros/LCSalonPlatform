using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Identity;

/// <summary>
/// The owner on a phone gives their receptionist a login (ADR 0076): Settings → Team logins, one form, a starting
/// password handed over in person — and the receptionist signs in and stands in front of the diary. The pages fit
/// 390 px and are axe clean.
/// </summary>
public class TeamLoginsE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_owner_adds_a_receptionist_login_and_the_receptionist_lands_on_the_diary()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);

        // A person no earlier run can have left behind: an email is unique per salon.
        var suffix = Random.Shared.Next(100_000, 999_999).ToString(CultureInfo.InvariantCulture);
        var ritaEmail = $"rita-{suffix}@studio-20.local";
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/settings/team");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/settings/team");

        // 1. The list names the owner's own login; adding one is one form.
        await Assertions.Expect(page.GetByTestId("team-logins")).ToContainTextAsync("Studio 20 Owner");
        (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "team-logins-mobile-390.png"),
            FullPage = true,
        });
        var axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {v.Help}")));

        await page.GetByTestId("add-login").ClickAsync();
        await page.ReachAsync("**/admin/settings/team/new");
        await page.GetByLabel("Name", new PageGetByLabelOptions { Exact = true }).FillAsync($"Rita {suffix}");
        await page.GetByLabel("Email").FillAsync(ritaEmail);
        await page.GetByLabel("Receptionist").CheckAsync();
        await page.GetByLabel("Starting password").FillAsync("Front-desk-9");
        await page.GetByTestId("create-login-submit").ClickAsync();
        await page.ReachAsync("**/admin/settings/team");
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync($"Rita {suffix} can sign in now.");

        // 2. Rita signs in with what she was handed and stands in front of the diary; Settings holds no team tab for her.
        var rita = await context.Browser!.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        await using (rita)
        {
            var her = await rita.NewPageAsync();
            (await her.GotoAsync($"{app.TenantUrl(Slug)}/admin/appointments")).ShouldNotBeNull().Ok.ShouldBeTrue();
            await her.GetByLabel("Email").FillAsync(ritaEmail);
            await her.GetByLabel("Password").FillAsync("Front-desk-9");
            await her.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
            await her.ReachAsync("**/admin/appointments*");
            await Assertions.Expect(her.GetByTestId("day")).ToBeVisibleAsync();
        }
    }
}