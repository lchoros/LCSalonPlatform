using System.Globalization;
using System.Text;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Products;

/// <summary>
/// The demo owner on a phone brings a product list over from a spreadsheet file: upload, a preview that says what is
/// new and what is wrong, a confirm, and the products are on the list with their units and a line in their history
/// that says where the number came from (ADR 0045). 390 px, axe clean, no horizontal scroll.
/// </summary>
public class ImportProductsE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_imports_a_product_list_on_a_phone_through_upload_preview_and_result()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", []);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        // Names no earlier run can have left behind: a product name is unique per salon and products are kept.
        var suffix = Random.Shared.Next(100_000, 999_999).ToString(CultureInfo.InvariantCulture);
        var mask = $"Repair mask {suffix}";
        var csv =
            "Name,Brand,Price,Stock,Low stock at\n" +
            $"{mask},Salon Range,£14.50,6,2\n" +
            $"Colour tube {suffix},,,10,\n" +
            $"{mask},,9.00,1,\n" +
            $"Styling wax {suffix},,twelve,2,\n";
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync(app.TenantUrl(Slug) + "/admin/products");
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/products");

        // 1. From the products page to the import; the page says what a file looks like.
        await page.GetByTestId("import-products").ClickAsync();
        await page.ReachAsync("**/admin/products/import");
        (await page.GetByTestId("import-columns").TextContentAsync()).ShouldNotBeNull().ShouldContain("Name,Brand,Code,Price,Stock,Low stock at");
        await CheckAndShootAsync(page, "products-import-upload-mobile-390.png");

        // 2. The file is checked: two new, one repeated, one with a price that is not a price. Nothing is added yet.
        await page.GetByLabel("CSV file").SetInputFilesAsync(new FilePayload
        {
            Name = "products.csv",
            MimeType = "text/csv",
            Buffer = Encoding.UTF8.GetBytes(csv),
        });
        await page.GetByTestId("import-upload").ClickAsync();
        await page.ReachAsync(url => url.Contains("/admin/products/import/", StringComparison.Ordinal));
        (await page.GetByTestId("count-new").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("2");
        (await page.GetByTestId("count-duplicate").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("1");
        (await page.GetByTestId("count-invalid").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("1");
        (await page.GetByTestId("import-row-details").First.TextContentAsync()).ShouldBe("Salon Range · £14.50 · on the shelf: 6");
        (await page.GetByText("Repeats an earlier row of this file.").CountAsync()).ShouldBe(1);
        (await page.GetByText("The price is not an amount of money such as 12.50.").CountAsync()).ShouldBe(1);
        (await page.GetByTestId("import-confirm").TextContentAsync()).ShouldBe("Import 2 new products");
        await CheckAndShootAsync(page, "products-import-preview-mobile-390.png");

        // 3. Confirmed: two products, sixteen units counted onto the shelves.
        await page.GetByTestId("import-confirm").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='import-result']");
        (await page.GetByTestId("result-created").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("2");
        (await page.GetByTestId("result-units").TextContentAsync()).ShouldBe("16 units were counted onto the shelves.");
        (await page.GetByTestId("result-skipped").TextContentAsync()).ShouldBe("1 already existed, 1 could not be imported.");
        await CheckAndShootAsync(page, "products-import-result-mobile-390.png");

        // 4. The mask is on the list with its price and units, and its history says where the six came from.
        await page.GetByTestId("to-products").ClickAsync();
        await page.ReachAsync("**/admin/products");
        await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/products?q={suffix}");
        var card = page.GetByTestId("product-card").Filter(new LocatorFilterOptions { HasText = mask });
        (await card.CountAsync()).ShouldBe(1);
        (await card.GetByTestId("product-price").TextContentAsync()).ShouldBe("£14.50");
        (await card.GetByTestId("product-on-hand").TextContentAsync()).ShouldBe("6");
        await card.GetByTestId("open-product").ClickAsync();
        await page.ReachAsync(url => new Uri(url).AbsolutePath.StartsWith("/admin/products/", StringComparison.Ordinal) && !url.Contains("?q=", StringComparison.Ordinal));
        (await page.GetByTestId("stock-history").TextContentAsync()).ShouldNotBeNull().ShouldContain("Imported from products.csv");
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