using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Identity;

/// <summary>
/// An owner on a phone opens the admin form, is sent to the login page, signs in and lands back on the form with the
/// sign-out button in the header. Both pages render, pass axe and are captured for the PR.
/// </summary>
public class LoginE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    // Own slug: the fixture replaces the salon's catalogue and test classes run in parallel against one database.
    private const string Slug = "studio-20-admin";
    private const string Email = "owner@studio-20-admin.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_owner_signs_in_on_a_phone_and_reaches_the_admin_form()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync(app.TenantUrl(Slug) + "/admin/services/new");

        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        new Uri(page.Url).AbsolutePath.ShouldBe("/account/login");
        new Uri(page.Url).Query.ShouldContain("returnUrl=");
        (await page.GetByTestId("login-form").IsVisibleAsync()).ShouldBeTrue();
        (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "login-mobile-390.png"),
            FullPage = true,
        });
        var loginAxe = await page.RunAxe();
        loginAxe.Violations.ShouldBeEmpty(string.Join("; ", loginAxe.Violations.Select(v => $"{v.Id}: {v.Help}")));

        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/services/new");

        (await page.GetByTestId("create-service-form").IsVisibleAsync()).ShouldBeTrue();
        (await page.GetByTestId("user-name").TextContentAsync()).ShouldBe("Studio 20 Owner");
        (await page.GetByTestId("sign-out").IsVisibleAsync()).ShouldBeTrue();
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "admin-services-new-mobile-390.png"),
            FullPage = true,
        });
        var adminAxe = await page.RunAxe();
        adminAxe.Violations.ShouldBeEmpty(string.Join("; ", adminAxe.Violations.Select(v => $"{v.Id}: {v.Help}")));
    }

    [Fact]
    public async Task A_wrong_password_shows_one_generic_message()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await using var context = await app.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(app.TenantUrl(Slug) + "/account/login");

        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync("not-it");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();

        await page.GetByTestId("login-error").WaitForAsync();
        (await page.GetByTestId("login-error").TextContentAsync()).ShouldNotBeNull().ShouldContain("The email or password is incorrect.");
        new Uri(page.Url).AbsolutePath.ShouldBe("/account/login");
    }
}