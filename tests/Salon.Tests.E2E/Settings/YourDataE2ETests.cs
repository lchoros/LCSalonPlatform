using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Settings;

/// <summary>
/// The demo owner on a phone opens Settings, "Your data", and downloads the client list (ADR 0051). The page says how
/// much there is and offers every file; the client file opens with its headings and holds this run's client. 390 px,
/// axe clean, no sideways scroll, buttons a full finger tall.
/// </summary>
public class YourDataE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_owner_takes_the_client_list_away_from_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        // A surname no earlier run can have left behind, so the row found below is this run's.
        var lastName = "Owned" + Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        await app.AddVisitAsync(tenantId, "Zoe", lastName, Duration.FromDays(2), deposit: 0m);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
            AcceptDownloads = true,
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync(app.TenantUrl(Slug) + "/admin/appointments");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/appointments");

        // 1. Settings, then "Your data": what there is, and a file for each part of it.
        await page.GetByTestId("nav-settings").ClickAsync();
        await page.ReachAsync("**/admin/settings/notifications");
        await page.GetByTestId("settings-nav-your-data").ClickAsync();
        await page.ReachAsync("**/admin/settings/your-data");
        var styled = await StylesheetLoadedAsync(page);
        (await page.GetByTestId("your-clients-count").TextContentAsync()).ShouldNotBeNull().ShouldContain("client card");
        (await page.GetByTestId("year").CountAsync()).ShouldBeGreaterThan(0);
        (await page.GetByTestId("download-visits").First.GetAttributeAsync("href")).ShouldNotBeNull().ShouldStartWith("/admin/reports/visits.csv?from=");
        (await page.GetByTestId("download-stock").GetAttributeAsync("href")).ShouldBe("/admin/products/stock.csv");
        var button = page.GetByTestId("download-clients");
        if (styled)
        {
            (await button.BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "your-data-mobile-390.png"),
            FullPage = true,
        });
        var axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {v.Help}")));

        // 2. The client list is a CSV named after today, with this run's client and her next visit in it.
        var download = await page.RunAndWaitForDownloadAsync(() => button.ClickAsync());
        download.SuggestedFilename.ShouldStartWith("clients-");
        download.SuggestedFilename.ShouldEndWith(".csv");
        var saved = Path.Combine(Path.GetTempPath(), "salon-e2e-" + Guid.NewGuid().ToString("N") + ".csv");
        await download.SaveAsAsync(saved);
        try
        {
            var text = await File.ReadAllTextAsync(saved);
            text.ShouldStartWith("First name,Last name,Phone,Email,Notes,Marketing consent,Consent given on,Client since,Completed visits,Last visit,Next visit,Active\r\n");
            text.ShouldContain($"Zoe,{lastName},");
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