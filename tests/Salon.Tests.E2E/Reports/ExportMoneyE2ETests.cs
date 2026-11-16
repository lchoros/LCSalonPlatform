using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Reports;

/// <summary>
/// The demo owner on a phone takes a payment, opens the reports and downloads the period's money lines as a file a
/// spreadsheet opens (ADR 0042). 390 px, axe clean, no horizontal scroll, a 44 px button.
/// </summary>
public class ExportMoneyE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_downloads_the_money_lines_of_the_period_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        // A surname no earlier run can have left behind, so the row found below is this run's.
        var lastName = "Ledger" + Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var (appointmentId, _, _) = await app.AddVisitAsync(tenantId, "Petra", lastName, -Duration.FromMinutes(20), deposit: 0m);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
            AcceptDownloads = true,
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

        // 1. A payment, so the period has money in it.
        await page.GetByTestId("action-arrived").GetByRole(AriaRole.Button).ClickAsync();
        await page.ReachAsync("**/admin/appointments?date=*");
        await page.GetByTestId("checkout-link").ClickAsync();
        await page.ReachAsync("**" + checkout);
        await page.GetByTestId("pay-bill").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("The payment has been recorded.");

        // 2. The reports page offers the same lines as a file.
        await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/reports?period=this-month");
        await page.ReachAsync(url => new Uri(url).AbsolutePath == "/admin/reports");
        var button = page.GetByTestId("export-money");
        (await button.TextContentAsync()).ShouldBe("Download for a spreadsheet");
        if (styled)
        {
            (await button.BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "reports-export-mobile-390.png"),
            FullPage = true,
        });
        var axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {v.Help}")));

        // 3. The download is a CSV named after the period, with the payment in it.
        var download = await page.RunAndWaitForDownloadAsync(() => button.ClickAsync());
        download.SuggestedFilename.ShouldStartWith("money-");
        download.SuggestedFilename.ShouldEndWith(".csv");
        var saved = Path.Combine(Path.GetTempPath(), "salon-e2e-" + Guid.NewGuid().ToString("N") + ".csv");
        await download.SaveAsAsync(saved);
        try
        {
            var text = await File.ReadAllTextAsync(saved);
            text.ShouldStartWith("Date,Time,Client,What it was for,Service,How it was paid,Amount,Currency,Recorded by,Reference\r\n");
            text.ShouldContain($",Petra {lastName},Visit paid,Cut & blow-dry,Cash,45.00,GBP,Studio 20 Owner,manual:payment:");
        }
        finally
        {
            File.Delete(saved);
        }
    }

    /// <summary>True when a stylesheet resolved with rules in it (a 404 or an empty body parses to zero rules).</summary>
    private static Task<bool> StylesheetLoadedAsync(IPage page)
        => page.EvaluateAsync<bool>("() => Array.from(document.styleSheets).some(s => s.href && s.cssRules.length > 0)");
}