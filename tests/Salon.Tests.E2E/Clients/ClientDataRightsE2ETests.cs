using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Clients;

/// <summary>
/// The demo owner on a phone answers a client who asks what the salon holds about her and then asks to be forgotten
/// (ADR 0062): the copy page shows her details and her visit; the erase page first says a visit still stands in the
/// way; once the visit is over it lists what goes and what stays, refuses without the tick, and erases with it. Her
/// card then says she was erased and carries no name. The pages are captured for the PR, pass axe and never scroll
/// sideways at 390 px.
/// </summary>
public class ClientDataRightsE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_owner_gives_a_client_a_copy_of_her_data_and_then_erases_her_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        var surname = "Forget" + Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var (appointmentId, clientId, _) = await app.AddVisitAsync(tenantId, "Fiona", surname, -Duration.FromMinutes(20), deposit: 0m);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/clients/{clientId}");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync($"**/admin/clients/{clientId}");
        var styled = await StylesheetLoadedAsync(page);

        // 1. A copy of what the salon holds.
        await page.GetByTestId("data-copy-link").ClickAsync();
        await page.ReachAsync($"**/admin/clients/{clientId}/data");
        (await page.GetByTestId("copy-name").TextContentAsync()).ShouldBe("Fiona " + surname);
        (await page.GetByTestId("copy-visit").CountAsync()).ShouldBe(1);
        await CheckPageAsync(page, styled, "client-data-copy-mobile-390.png");

        // 2. Her visit is still on: erasing waits.
        await page.GetByTestId("back").ClickAsync();
        await page.ReachAsync($"**/admin/clients/{clientId}");
        await page.GetByTestId("erase-link").ClickAsync();
        await page.ReachAsync($"**/admin/clients/{clientId}/erase");
        (await page.GetByTestId("erase-blocker").TextContentAsync()).ShouldBe("The client has a visit still to come. Cancel it first.");
        (await page.GetByTestId("erase-form").CountAsync()).ShouldBe(0);

        // 3. The visit is over: the page says what goes and what stays; without the tick nothing happens.
        await app.SetVisitStatusAsync(tenantId, appointmentId, AppointmentStatus.Completed);
        await page.ReloadAsync();
        (await page.GetByTestId("erase-blocked").CountAsync()).ShouldBe(0);
        (await page.GetByTestId("erase-visits").TextContentAsync()).ShouldNotBeNull().ShouldStartWith("Visits: 1.");
        await page.GetByTestId("erase-submit").ClickAsync();
        await Assertions.Expect(page.GetByTestId("erase-problem")).ToHaveTextAsync("Tick the box to confirm that this cannot be undone.");
        await CheckPageAsync(page, styled, "client-erase-mobile-390.png");

        // 4. With the tick she is erased; the card says so and has no name.
        await page.GetByTestId("erase-understood").CheckAsync();
        await page.GetByTestId("erase-submit").ClickAsync();
        await page.ReachAsync($"**/admin/clients/{clientId}");
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("The client was erased.");
        (await page.GetByTestId("erased-banner").IsVisibleAsync()).ShouldBeTrue();
        (await page.GetByTestId("client-name").TextContentAsync()).ShouldBe("Erased client");
        (await page.GetByTestId("client-card").TextContentAsync()).ShouldNotBeNull().ShouldNotContain(surname);
        (await page.GetByTestId("visits-completed").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("1", "the visit stays as a record");
        (await page.GetByTestId("data-rights").CountAsync()).ShouldBe(0);
        await CheckPageAsync(page, styled, "client-erased-card-mobile-390.png");

        // 5. The list no longer finds her.
        await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/clients?q={surname}");
        (await page.GetByTestId("client-row").CountAsync()).ShouldBe(0);
    }

    /// <summary>No sideways scroll (when styled), a screenshot for the PR and an axe pass.</summary>
    private static async Task CheckPageAsync(IPage page, bool styled, string screenshot)
    {
        if (styled)
        {
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