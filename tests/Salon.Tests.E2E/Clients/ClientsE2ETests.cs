using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Clients;

/// <summary>
/// An owner on a phone signs in, fills the client form (including encrypted health notes and the consent tick) and lands
/// on the new card. Both screens render at 390 px without horizontal scroll, pass axe and are captured for the PR.
/// </summary>
public class ClientsE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    // Own slug: test classes share one database and the fixture replaces the salon's rows.
    private const string Slug = "studio-20-clients";
    private const string Email = "owner@studio-20-clients.local";
    private const string Password = "Studio20!e2e";
    private const string HealthText = "Allergic to PPD; patch test negative";

    [Fact]
    public async Task The_owner_adds_a_client_on_a_phone_and_sees_the_card()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", []);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync(app.TenantUrl(Slug) + "/admin/clients/new");
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/clients/new");

        (await page.GetByTestId("create-client-form").IsVisibleAsync()).ShouldBeTrue();
        (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "clients-create-mobile-390.png"),
            FullPage = true,
        });
        var formAxe = await page.RunAxe();
        formAxe.Violations.ShouldBeEmpty(string.Join("; ", formAxe.Violations.Select(v => $"{v.Id}: {v.Help}")));

        // Unique contact details per run: phone and email are unique per salon and earlier runs leave their rows behind.
        var suffix = Random.Shared.Next(100_000, 999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await page.GetByLabel("First name", new PageGetByLabelOptions { Exact = true }).FillAsync("Olivia");
        await page.GetByLabel("Last name", new PageGetByLabelOptions { Exact = true }).FillAsync("Brown");
        await page.GetByLabel("Phone number", new PageGetByLabelOptions { Exact = true }).FillAsync("07700 " + suffix);
        await page.GetByLabel("Email", new PageGetByLabelOptions { Exact = true }).FillAsync($"olivia.{suffix}@example.com");
        await page.GetByLabel("Notes", new PageGetByLabelOptions { Exact = true }).FillAsync("Prefers afternoons.");
        await page.GetByLabel("Health notes (allergies, patch test)", new PageGetByLabelOptions { Exact = true }).FillAsync(HealthText);
        await page.GetByLabel("Last patch test", new PageGetByLabelOptions { Exact = true }).FillAsync("2026-10-01");
        await page.GetByTestId("marketing-consent").CheckAsync();
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save client" }).ClickAsync();
        await page.ReachAsync(url => url.Contains("/admin/clients/", StringComparison.Ordinal) && !url.EndsWith("/new", StringComparison.Ordinal));

        (await page.GetByTestId("client-card").IsVisibleAsync()).ShouldBeTrue();
        (await page.GetByTestId("flash").TextContentAsync()).ShouldNotBeNull().ShouldContain("Olivia Brown");
        (await page.GetByTestId("health-notes").TextContentAsync()).ShouldNotBeNull().ShouldContain(HealthText);
        (await page.GetByTestId("consent-status").TextContentAsync()).ShouldNotBeNull().ShouldContain("Granted on");
        (await page.GetByTestId("client-phone").TextContentAsync()).ShouldNotBeNull().ShouldContain("+447700" + suffix);
        (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "clients-card-mobile-390.png"),
            FullPage = true,
        });
        var cardAxe = await page.RunAxe();
        cardAxe.Violations.ShouldBeEmpty(string.Join("; ", cardAxe.Violations.Select(v => $"{v.Id}: {v.Help}")));
    }
}