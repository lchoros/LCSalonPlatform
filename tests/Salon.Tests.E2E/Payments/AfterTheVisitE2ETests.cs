using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Payments;

/// <summary>
/// The demo owner on a phone takes a payment and books the client's next visit from the same page: one tap opens the
/// booking form with the client, the service, the person and the day six weeks on (ADR 0039). 390 px, axe clean, no
/// horizontal scroll, 44 px buttons.
/// </summary>
public class AfterTheVisitE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_takes_a_payment_and_opens_the_next_visit_in_one_tap_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        // A surname no earlier run can have left behind, so the rows found below are this run's.
        var lastName = "Again" + Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var (appointmentId, clientId, _) = await app.AddVisitAsync(tenantId, "Nina", lastName, -Duration.FromMinutes(20), deposit: 0m);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();
        var checkout = $"/admin/payments/checkout/{appointmentId}";

        var response = await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/appointments/calendar/{appointmentId}");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync($"**/admin/appointments/calendar/{appointmentId}");
        var styled = await StylesheetLoadedAsync(page);

        // 1. The client arrives and pays: before that the page does not talk about a next visit.
        await page.GetByTestId("action-arrived").GetByRole(AriaRole.Button).ClickAsync();
        await page.ReachAsync("**/admin/appointments?date=*");
        await page.GetByTestId("checkout-link").ClickAsync();
        await page.ReachAsync("**" + checkout);
        (await page.GetByTestId("rebook-step").CountAsync()).ShouldBe(0);
        await page.GetByTestId("pay-bill").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("The payment has been recorded.");
        await page.ReachAsync(url => new Uri(url).AbsolutePath == checkout);

        // 2. The paid bill offers the next visit in four, six and eight weeks.
        var choices = page.GetByTestId("rebook-choice");
        (await choices.CountAsync()).ShouldBe(3);
        (await choices.Nth(1).GetAttributeAsync("data-weeks")).ShouldBe("6");
        (await choices.Nth(1).TextContentAsync()).ShouldNotBeNull().ShouldContain("In 6 weeks");
        await CheckPageAsync(page, styled, "rebook-choice", "checkout-rebook-mobile-390.png");

        // 3. One tap: the booking form knows who, what, with whom and when.
        await choices.Nth(1).ClickAsync();
        await page.ReachAsync(url => new Uri(url).AbsolutePath == "/admin/appointments/new");
        (await page.GetByTestId("selected-client").TextContentAsync()).ShouldNotBeNull().ShouldContain($"Nina {lastName}");
        var expectedDay = SystemClock.Instance.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb["Europe/London"]).Date.PlusWeeks(6);
        var shownDay = await page.GetByTestId("date-input").InputValueAsync();
        // The visit began twenty minutes ago; around midnight in London that is yesterday, so either day is right.
        new[] { expectedDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), expectedDay.PlusDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }.ShouldContain(shownDay);
        (await page.GetByTestId("service-select").Locator("option:checked").TextContentAsync()).ShouldNotBeNull().ShouldStartWith("Cut & blow-dry");
        await CheckPageAsync(page, styled, "date-input", "rebook-form-mobile-390.png");

        // 4. The client's card has its own "Book a visit".
        await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/clients/{clientId}");
        await page.ReachAsync($"**/admin/clients/{clientId}");
        (await page.GetByTestId("book-visit").GetAttributeAsync("href")).ShouldBe($"/admin/appointments/new?clientId={clientId}");
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