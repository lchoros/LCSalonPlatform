using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Appointments;

/// <summary>
/// The demo owner on a phone puts a client on the waitlist from their card, finds them on the list with what they
/// want and a way to book them, sees the count on the day list, and takes them off again (ADR 0041). 390 px, axe
/// clean, no horizontal scroll, 44 px buttons.
/// </summary>
public class WaitlistE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_puts_a_client_on_the_waitlist_and_takes_them_off_again_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        // A surname no earlier run can have left behind, so the row found below is this run's.
        var lastName = "Waits" + Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var (_, clientId, _) = await app.AddVisitAsync(tenantId, "Greta", lastName, -Duration.FromDays(30), deposit: 0m);
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

        // 1. From the client's card to the form: the client is given, the desk says what they are waiting for.
        await page.GetByTestId("add-to-waitlist").ClickAsync();
        await page.ReachAsync(url => new Uri(url).AbsolutePath == "/admin/appointments/waitlist/new");
        (await page.GetByTestId("waitlist-client").TextContentAsync()).ShouldBe($"Greta {lastName}");
        await page.GetByTestId("waitlist-service").SelectOptionAsync(new SelectOptionValue { Label = "Cut & blow-dry" });
        await page.GetByTestId("waitlist-note").FillAsync("mornings only");
        await CheckPageAsync(page, styled, "waitlist-submit", "waitlist-form-mobile-390.png");
        await page.GetByTestId("waitlist-submit").ClickAsync();

        // 2. The list has the client, what they want and how to reach and book them.
        await page.ReachAsync(url => new Uri(url).AbsolutePath == "/admin/appointments/waitlist");
        (await page.GetByTestId("flash").TextContentAsync()).ShouldBe($"Greta {lastName} is on the waitlist.");
        var row = page.GetByTestId("waiting").Filter(new LocatorFilterOptions { HasText = lastName });
        (await row.CountAsync()).ShouldBe(1);
        (await row.GetByTestId("waiting-service").TextContentAsync()).ShouldBe("Cut & blow-dry");
        (await row.GetByTestId("waiting-staff").TextContentAsync()).ShouldBe("Anyone available");
        (await row.GetByTestId("waiting-note").TextContentAsync()).ShouldBe("mornings only");
        (await row.GetByTestId("waiting-book").GetAttributeAsync("href")).ShouldNotBeNull().ShouldStartWith($"/admin/appointments/new?clientId={clientId}&serviceId=");
        await CheckPageAsync(page, styled, "waiting-book", "waitlist-mobile-390.png");

        // 3. The day list says somebody is waiting.
        await page.GetByTestId("back").ClickAsync();
        await page.ReachAsync(url => new Uri(url).AbsolutePath == "/admin/appointments");
        (await page.GetByTestId("waitlist-count").TextContentAsync()).ShouldNotBeNull().ShouldStartWith("1");
        await page.GetByTestId("waitlist-link").ClickAsync();
        await page.ReachAsync(url => new Uri(url).AbsolutePath == "/admin/appointments/waitlist");

        // 4. Taken off by hand: the list is empty again.
        await page.GetByTestId("waiting").Filter(new LocatorFilterOptions { HasText = lastName }).GetByTestId("waiting-remove").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("The client is off the waitlist.");
        (await page.GetByTestId("waiting").Filter(new LocatorFilterOptions { HasText = lastName }).CountAsync()).ShouldBe(0);
    }

    /// <summary>Touch target (when styled), no sideways scroll, a screenshot for the PR and an axe pass.</summary>
    private static async Task CheckPageAsync(IPage page, bool styled, string targetTestId, string screenshot)
    {
        if (styled)
        {
            (await page.GetByTestId(targetTestId).First.BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
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