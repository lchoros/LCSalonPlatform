using System.Globalization;
using System.Text;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Catalog;

/// <summary>
/// The demo owner on a phone reads a price list in from a spreadsheet file (ADR 0066): upload, a preview that says
/// which service is added, which one changes (the old price and the new) and which row is left out and why, a
/// confirm, and the services page has the new service under its new heading and the cut at its new price. 390 px,
/// axe clean, no horizontal scroll.
/// </summary>
public class ImportServicesE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_reads_a_price_list_in_on_a_phone_through_upload_preview_and_result()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.RemoveCategoriesAsync(tenantId);

        // A name no earlier run can have left behind: a service name is unique per salon.
        var suffix = Random.Shared.Next(100_000, 999_999).ToString(CultureInfo.InvariantCulture);
        var gel = $"Gel nails {suffix}";
        var heading = $"Nails {suffix}";
        var csv =
            "Category,Service,Variant,Minutes,Price,Deposit %\n" +
            ",Cut & blow-dry,,45,47.50,\n" +
            $"{heading},{gel},,60,£30.00,10\n" +
            $"{heading},Keratin {suffix},Long hair,120,150,\n" +
            $"{heading},Manicure {suffix},,47,20,\n";
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync(app.TenantUrl(Slug) + "/admin/services");
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/services");

        // 1. From the services page to the file; the page says what a file looks like and what will and will not change.
        await page.GetByTestId("import-services").ClickAsync();
        await page.ReachAsync("**/admin/services/import");
        (await page.GetByTestId("import-columns").TextContentAsync()).ShouldNotBeNull().ShouldContain("Category,Service,Variant,Minutes,Price,Deposit %");
        (await page.GetByTestId("import-rules").TextContentAsync()).ShouldNotBeNull().ShouldContain("Nothing is removed.");
        await CheckAndShootAsync(page, "services-import-upload-mobile-390.png");

        // 2. The file is checked: one to add, one to change, one that cannot be read. Nothing has changed yet.
        await page.GetByLabel("CSV file").SetInputFilesAsync(new FilePayload
        {
            Name = "prices.csv",
            MimeType = "text/csv",
            Buffer = Encoding.UTF8.GetBytes(csv),
        });
        await page.GetByTestId("import-upload").ClickAsync();
        await page.ReachAsync(url => url.Contains("/admin/services/import/", StringComparison.Ordinal));
        (await page.GetByTestId("count-new").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("2");
        (await page.GetByTestId("count-changed").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("1");
        (await page.GetByTestId("count-unchanged").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("0");
        (await page.GetByTestId("count-left-out").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("1");
        var changed = page.Locator("[data-testid='import-row'][data-status='Changed']");
        (await changed.GetByTestId("import-row-details").TextContentAsync()).ShouldBe("£45.00 → £47.50");
        var added = page.Locator("[data-testid='import-row'][data-status='New']").Filter(new LocatorFilterOptions { HasText = gel });
        (await added.GetByTestId("import-row-details").TextContentAsync()).ShouldBe($"{heading} · 60 min · £30.00 · deposit 10%");

        // The variant row is the service "Name · Variant" (ADR 0073).
        var variant = page.Locator("[data-testid='import-row'][data-status='New']").Filter(new LocatorFilterOptions { HasText = "Long hair" });
        (await variant.GetByTestId("import-row-name").TextContentAsync()).ShouldBe($"Keratin {suffix} · Long hair");
        (await page.GetByText("The time must be whole minutes from 5 to 600, in steps of 5, such as 45.").CountAsync()).ShouldBe(1);
        (await page.GetByTestId("import-confirm").TextContentAsync()).ShouldBe("Add 2 and change 1");
        await CheckAndShootAsync(page, "services-import-preview-mobile-390.png");

        // 3. Confirmed: one service added with its heading, one changed.
        await page.GetByTestId("import-confirm").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='import-result']");
        (await page.GetByTestId("result-added").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("2");
        (await page.GetByTestId("result-updated").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("1");
        (await page.GetByTestId("result-categories").TextContentAsync()).ShouldBe("1 category was added with them.");
        (await page.GetByTestId("result-rest").TextContentAsync()).ShouldBe("Stayed as they were: 0. Rows left out: 1.");
        await CheckAndShootAsync(page, "services-import-result-mobile-390.png");

        // 4. The services page has the cut at its new price and the gel nails under the new heading.
        await page.GetByTestId("to-services").ClickAsync();
        await page.ReachAsync("**/admin/services");
        var cut = page.GetByTestId("service-card").Filter(new LocatorFilterOptions { HasText = "Cut & blow-dry" });
        (await cut.GetByTestId("service-price").TextContentAsync()).ShouldNotBeNull().ShouldContain("£47.50");
        var nails = page.GetByTestId("service-card").Filter(new LocatorFilterOptions { HasText = gel });
        (await nails.CountAsync()).ShouldBe(1);
        (await nails.GetByTestId("service-price").TextContentAsync()).ShouldNotBeNull().ShouldContain("£30.00");
        (await page.GetByTestId("group-heading").AllTextContentsAsync()).ShouldContain(h => h.Contains(heading, StringComparison.Ordinal));
        var keratin = page.GetByTestId("service-card").Filter(new LocatorFilterOptions { HasText = "Long hair" });
        (await keratin.GetByTestId("service-variant-of").TextContentAsync()).ShouldBe($"Variant of Keratin {suffix}");
    }

    private static async Task CheckAndShootAsync(IPage page, string fileName)
    {
        (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, fileName),
            FullPage = true,
        });
        var axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {v.Help}")));
    }
}