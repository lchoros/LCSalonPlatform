using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Reports;

/// <summary>
/// The demo owner on a phone sets commission (ADR 0059): from the staff list to the rates page, a wrong percent is
/// refused next to its box, the right ones are saved, and the report shows the commission and what is left for the
/// person who did a visit today. Both pages are captured for the PR, pass axe and never scroll sideways at 390 px.
/// </summary>
public class StaffCommissionE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_owner_sets_commission_on_a_phone_and_reads_it_in_the_report()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m), ("Colour (roots)", 90, 65m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        var (visitId, _, _) = await app.AddVisitAsync(tenantId, "Clara", "Commission", Duration.FromMinutes(-50), 0m);
        await app.SetVisitStatusAsync(tenantId, visitId, AppointmentStatus.Completed);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync(app.TenantUrl(Slug) + "/admin/staff");
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/staff");

        await page.GetByTestId("commission-link").ClickAsync();
        await page.ReachAsync("**/admin/staff/commission");
        var rows = page.GetByTestId("commission-row");
        (await rows.CountAsync()).ShouldBeGreaterThanOrEqualTo(1);
        var first = rows.First;

        await first.GetByTestId("commission-services").FillAsync("140");
        await first.GetByTestId("commission-products").FillAsync("10");
        await page.GetByTestId("commission-submit").ClickAsync();
        await Assertions.Expect(first.GetByTestId("commission-error").First).ToHaveTextAsync("Enter a percent from 0 to 100, with at most two decimals, or leave it empty.");
        (await first.GetByTestId("commission-products").InputValueAsync()).ShouldBe("10");
        var styled = await StylesheetLoadedAsync(page);
        if (styled)
        {
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "staff-commission-mobile-390.png"),
            FullPage = true,
        });
        var ratesAxe = await page.RunAxe();
        ratesAxe.Violations.ShouldBeEmpty(string.Join("; ", ratesAxe.Violations.Select(v => $"{v.Id}: {v.Help}")));

        await first.GetByTestId("commission-services").FillAsync("40");
        await page.GetByTestId("commission-submit").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("The rates are saved.");
        (await first.GetByTestId("commission-services").InputValueAsync()).ShouldBe("40");

        await page.GetByTestId("commission-report").ClickAsync();
        await page.ReachAsync("**/admin/reports");
        var paid = page.GetByTestId("staff-pay").Filter(new LocatorFilterOptions { HasText = "Commission (40% services, 10% products)" });
        (await paid.CountAsync()).ShouldBe(1);
        (await paid.GetByTestId("staff-commission").TextContentAsync()).ShouldNotBeNull().ShouldContain("£");
        (await paid.GetByTestId("staff-after-commission").TextContentAsync()).ShouldNotBeNull().ShouldContain("£");
        await paid.ScrollIntoViewIfNeededAsync();
        if (styled)
        {
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        }

        await page.GetByTestId("staff").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "report-staff-commission-mobile-390.png"),
        });
        var reportAxe = await page.RunAxe();
        reportAxe.Violations.ShouldBeEmpty(string.Join("; ", reportAxe.Violations.Select(v => $"{v.Id}: {v.Help}")));

        // Leave the demo salon as it was: nobody on commission.
        await page.GotoAsync(app.TenantUrl(Slug) + "/admin/staff/commission");
        await first.GetByTestId("commission-services").FillAsync(string.Empty);
        await first.GetByTestId("commission-products").FillAsync(string.Empty);
        await page.GetByTestId("commission-submit").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("The rates are saved.");
    }

    private static Task<bool> StylesheetLoadedAsync(IPage page)
        => page.EvaluateAsync<bool>("() => Array.from(document.styleSheets).some(s => s.href && s.cssRules.length > 0)");
}