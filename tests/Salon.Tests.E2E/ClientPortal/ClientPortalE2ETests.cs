using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.ClientPortal;

/// <summary>
/// "My bookings" on a 390 px phone (ADR 0083): a client of the salon follows the link in the header, types their
/// mobile number, reads the code from their "phone" (the recording sender), sees the visit ahead of them, follows its
/// cancel link to the page that already exists, and signs out. Every screen is captured, passes axe and does not
/// scroll sideways.
/// </summary>
public class ClientPortalE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const int Width = 390;

    [Fact]
    public async Task A_client_signs_in_with_a_code_sees_a_visit_follows_its_cancel_link_and_signs_out()
    {
        await app.ClearBookingDataAsync(Slug);
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.UseDefaultSettingsAsync(tenantId);
        // A number no earlier run can have left behind: a number gets three codes an hour.
        var digits = Random.Shared.Next(100_000, 999_999).ToString(CultureInfo.InvariantCulture);
        var phone = "07700 " + digits;
        var phoneE164 = "+447700" + digits;
        await app.AddVisitAsync(tenantId, "Wren", "Portal" + digits, Duration.FromDays(5), deposit: 0m, phoneE164: phoneE164);

        try
        {
            await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = Width, Height = 844 },
                IsMobile = true,
                HasTouch = true,
            });
            var page = await context.NewPageAsync();

            // 1. From the salon's booking page, the link in the header.
            (await page.GotoAsync(app.TenantUrl(Slug) + "/book")).ShouldNotBeNull().Ok.ShouldBeTrue();
            await page.GetByTestId("my-bookings-link").TapAsync();
            await page.ReachAsync("**/my-bookings/sign-in");

            // 2. The mobile number.
            await page.GetByLabel("Mobile number", new PageGetByLabelOptions { Exact = true }).FillAsync(phone);
            await CaptureAsync(page, "portal-sign-in-mobile-390.png");
            await page.GetByTestId("portal-send-code").TapAsync();
            await page.ReachAsync("**/my-bookings/code/*");
            page.Url.ShouldNotContain(digits, Case.Sensitive, "nothing personal in an address");

            // 3. The code, read from the "phone".
            (await page.GetByTestId("portal-code-sent").TextContentAsync()).ShouldNotBeNull().ShouldContain("+44•••••••" + digits[^3..]);
            var code = app.Sms.LastCodeFor(phoneE164).ShouldNotBeNull("the code went to the number the salon knows");
            await CaptureAsync(page, "portal-code-mobile-390.png");
            await page.GetByTestId("portal-code").FillAsync(code);
            await page.GetByTestId("portal-submit-code").TapAsync();
            await page.ReachAsync("**/my-bookings");

            // 4. The visit ahead, with its links.
            await Assertions.Expect(page.GetByTestId("portal-visit")).ToHaveCountAsync(1);
            (await page.GetByTestId("portal-visit-service").TextContentAsync()).ShouldBe("Cut & blow-dry");
            (await page.GetByTestId("portal-visit-change").GetAttributeAsync("href")).ShouldNotBeNull().ShouldStartWith("/book/change/");
            (await page.GetByTestId("portal-book-another").GetAttributeAsync("href")).ShouldBe("/book");
            (await page.ContentAsync()).ShouldNotContain("Wren", Case.Sensitive, "the page names nobody");
            await CaptureAsync(page, "portal-visits-mobile-390.png");

            // 5. The cancel link leads to the page that already exists (ADR 0014), which shows this visit and asks to
            //    confirm. That page is the booking flow's own and is walked by its own tests; here it only has to open.
            await page.GetByTestId("portal-visit-cancel").TapAsync();
            await page.ReachAsync("**/book/cancel/*");
            (await page.GetByTestId("booking-details").TextContentAsync()).ShouldNotBeNull().ShouldContain("Cut & blow-dry");
            (await page.GetByTestId("confirm-cancel").IsVisibleAsync()).ShouldBeTrue();

            // 6. Back on "My bookings" the client is still signed in; then they sign out.
            (await page.GotoAsync(app.TenantUrl(Slug) + "/my-bookings")).ShouldNotBeNull().Ok.ShouldBeTrue();
            await Assertions.Expect(page.GetByTestId("portal-visit")).ToHaveCountAsync(1);
            await page.GetByTestId("portal-sign-out").TapAsync();
            await page.ReachAsync("**/my-bookings/sign-in");
            await Assertions.Expect(page.GetByTestId("portal-flash")).ToHaveTextAsync("You have signed out.");
            await CaptureAsync(page, "portal-signed-out-mobile-390.png");

            // Signed out for good: the visits ask for the number again.
            await page.GotoAsync(app.TenantUrl(Slug) + "/my-bookings");
            await page.ReachAsync("**/my-bookings/sign-in");
            (await context.CookiesAsync()).ShouldNotContain(c => c.Name == "salon.client");
        }
        finally
        {
            // Other classes replace this salon's services and staff; the visit must not hold a foreign key on them.
            await app.ClearBookingDataAsync(Slug);
        }
    }

    /// <summary>Full-page screenshot, no sideways scroll (when the stylesheet is present), axe clean.</summary>
    private static async Task CaptureAsync(IPage page, string fileName)
    {
        if (await page.EvaluateAsync<bool>("() => Array.from(document.styleSheets).some(s => s.href && s.cssRules.length > 0)"))
        {
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(Width, fileName);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, fileName), FullPage = true });
        var axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(fileName + ": " + string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {v.Help}")));
    }
}