using System.Globalization;

using Deque.AxeCore.Playwright;

using Microsoft.Playwright;

using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Settings;

/// <summary>
/// The demo owner on a phone opens Settings, enters the salon's address and phone number and saves; a client who then
/// opens the booking page on their own phone sees where to find the salon, a maps link and a number to call
/// (ADR 0027). 390 px, axe clean, no horizontal scroll.
/// </summary>
public class SalonDetailsE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_owner_enters_the_address_and_a_client_sees_where_to_find_the_salon_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        // A house number a previous run cannot have left behind, so the assertions below prove this save.
        var street = Random.Shared.Next(100, 999).ToString(CultureInfo.InvariantCulture) + " Example Street";
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync(app.TenantUrl(Slug) + "/admin/appointments");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/appointments");

        // 1. Settings, then the "Salon details" tab.
        await page.GetByTestId("nav-settings").ClickAsync();
        await page.ReachAsync("**/admin/settings/notifications");
        await page.GetByTestId("settings-nav-salon-details").ClickAsync();
        await page.ReachAsync("**/admin/settings/salon-details");
        var styled = await StylesheetLoadedAsync(page);

        // 2. A phone number that is not one is refused next to its field, and what was typed is kept.
        await page.GetByTestId("AddressLine1").FillAsync(street);
        await page.GetByTestId("AddressLine2").FillAsync(string.Empty);
        await page.GetByTestId("City").FillAsync("London");
        await page.GetByTestId("Postcode").FillAsync("N1 9GU");
        await page.GetByTestId("Phone").FillAsync("ask at the desk");
        await page.GetByTestId("Email").FillAsync(string.Empty);
        await page.GetByTestId("Name").FillAsync(string.Empty);
        await page.GetByTestId("save-salon-details").ClickAsync();
        (await page.GetByTestId("Phone-error").TextContentAsync()).ShouldNotBeNull().ShouldStartWith("Enter a phone number");
        (await page.GetByTestId("AddressLine1").InputValueAsync()).ShouldBe(street);
        await CheckPageAsync(page, styled, "save-salon-details", "salon-details-error-mobile-390.png");

        // 3. With a real number it is saved and shown again.
        await page.GetByTestId("Phone").FillAsync("020 7946 0020");
        await page.GetByTestId("save-salon-details").ClickAsync();
        (await page.GetByTestId("flash").TextContentAsync()).ShouldBe("The salon details have been saved.");
        (await page.GetByTestId("AddressLine1").InputValueAsync()).ShouldBe(street);
        (await page.GetByTestId("Phone").InputValueAsync()).ShouldBe("020 7946 0020");
        await CheckPageAsync(page, styled, "save-salon-details", "salon-details-mobile-390.png");

        // 4. A client, not signed in, opens the booking page on their own phone.
        await using var clientContext = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var clientPage = await clientContext.NewPageAsync();
        (await clientPage.GotoAsync(app.TenantUrl(Slug) + "/book")).ShouldNotBeNull().Ok.ShouldBeTrue();
        (await clientPage.GetByTestId("salon-contact-address").TextContentAsync()).ShouldBe($"{street}, London N1 9GU");
        (await clientPage.GetByTestId("salon-contact-phone").GetAttributeAsync("href")).ShouldBe("tel:02079460020");
        (await clientPage.GetByTestId("salon-contact-phone").TextContentAsync()).ShouldBe("Call 020 7946 0020");
        (await clientPage.GetByTestId("salon-contact-map").GetAttributeAsync("href")).ShouldNotBeNull().ShouldStartWith("https://www.google.com/maps/search/?api=1&query=");
        (await clientPage.GetByTestId("salon-contact-email").CountAsync()).ShouldBe(0, "no email address was entered");
        await CheckPageAsync(clientPage, styled, "salon-contact-map", "booking-where-to-find-us-mobile-390.png");
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