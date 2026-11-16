using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Reports;

/// <summary>
/// The demo owner on a phone downloads the stock list from the products page and the visits of the month from the
/// reports (ADR 0047). Both buttons are a full finger tall, the pages fit 390 px and are axe clean, and each file
/// opens with its headings and holds this run's row.
/// </summary>
public class ExportFilesE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_downloads_the_stock_list_and_the_visits_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        // Names no earlier run can have left behind, so the rows found below are this run's.
        var suffix = Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var product = "Shelf file serum " + suffix;
        await app.AddProductAsync(tenantId, product, 14.5m, 7);
        var lastName = "Sheet" + suffix;
        await app.AddVisitAsync(tenantId, "Wanda", lastName, -Duration.FromMinutes(20), deposit: 0m);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
            AcceptDownloads = true,
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/products");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/products");
        var styled = await StylesheetLoadedAsync(page);

        // 1. The products page offers the list with today's counts as a file.
        var stock = page.GetByTestId("export-stock");
        (await stock.TextContentAsync()).ShouldBe("Download the stock list");
        await CheckPageAsync(page, stock, styled, "products-export-mobile-390.png");
        var stockFile = await DownloadAsync(page, stock, "stock-");
        stockFile.ShouldStartWith("Name,Brand,Code,Price,Currency,In stock,Low stock at,In use\r\n");
        stockFile.ShouldContain($"{product},,,14.50,GBP,7,,Yes\r\n");

        // 2. The reports offer every visit of the period as a file.
        await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/reports?period=this-month");
        await page.ReachAsync(url => new Uri(url).AbsolutePath == "/admin/reports");
        var visits = page.GetByTestId("export-visits");
        (await visits.TextContentAsync()).ShouldBe("Download the visits");
        await visits.ScrollIntoViewIfNeededAsync();
        await CheckPageAsync(page, visits, styled, "reports-visits-export-mobile-390.png");
        var visitsFile = await DownloadAsync(page, visits, "visits-");
        visitsFile.ShouldStartWith("Date,Time,Client,Service,With,Minutes,Status,Booked,Price,Currency\r\n");
        visitsFile.ShouldContain($",Wanda {lastName},Cut & blow-dry,");
        visitsFile.ShouldContain(",45,Booked,At the salon,45.00,GBP\r\n");
    }

    /// <summary>Clicks the link and returns the text of the CSV it saves.</summary>
    private static async Task<string> DownloadAsync(IPage page, ILocator link, string namePrefix)
    {
        var download = await page.RunAndWaitForDownloadAsync(() => link.ClickAsync());
        download.SuggestedFilename.ShouldStartWith(namePrefix);
        download.SuggestedFilename.ShouldEndWith(".csv");
        var saved = Path.Combine(Path.GetTempPath(), "salon-e2e-" + Guid.NewGuid().ToString("N") + ".csv");
        await download.SaveAsAsync(saved);
        try
        {
            return await File.ReadAllTextAsync(saved);
        }
        finally
        {
            File.Delete(saved);
        }
    }

    /// <summary>A full-finger button and no sideways scroll (when styled), a screenshot for the PR and an axe pass.</summary>
    private static async Task CheckPageAsync(IPage page, ILocator button, bool styled, string screenshot)
    {
        if (styled)
        {
            (await button.BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
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