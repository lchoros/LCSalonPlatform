using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Products;

/// <summary>
/// The demo owner on a phone puts a product on the shelf, takes one off as used and sees it turn low, on its page and
/// in the list (ADR 0036). 390 px, axe clean, no horizontal scroll, 44 px buttons.
/// </summary>
public class ProductsE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_adds_a_product_uses_one_and_sees_it_run_low_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        // A name no earlier run can have left behind, so the product found below is this run's.
        var name = "Shampoo " + Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture) + " ml";
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/products");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync(url => new Uri(url).AbsolutePath == "/admin/products");
        var styled = await StylesheetLoadedAsync(page);
        (await page.GetByTestId("admin-nav").Locator("[aria-current=true]").GetAttributeAsync("data-testid")).ShouldBe("nav-products");

        // 1. The form: a name, a price, a warning number and what is on the shelf today.
        await page.GetByTestId("add-product").ClickAsync();
        await page.ReachAsync(url => new Uri(url).AbsolutePath == "/admin/products/new");
        await page.GetByTestId("product-name-input").FillAsync(name);
        await page.GetByLabel("Brand (optional)").FillAsync("Salon Care");
        await page.GetByTestId("product-price-input").FillAsync("12.50");
        await page.GetByTestId("product-low-input").FillAsync("3");
        await page.GetByTestId("product-opening-input").FillAsync("4");
        await CheckPageAsync(page, styled, "save-product", "product-form-mobile-390.png");
        await page.GetByTestId("save-product").ClickAsync();

        // 2. The product's page: four on the shelf, one line in the history.
        await page.ReachAsync(url => new Uri(url).AbsolutePath.StartsWith("/admin/products/", StringComparison.Ordinal) && !url.EndsWith("/new", StringComparison.Ordinal));
        var card = new Uri(page.Url).AbsolutePath;
        (await page.GetByTestId("flash").TextContentAsync()).ShouldBe($"“{name}” has been added.");
        (await page.GetByTestId("on-hand").TextContentAsync()).ShouldBe("4");
        (await page.GetByTestId("on-hand-panel").GetAttributeAsync("data-low")).ShouldBe("false");
        (await page.GetByTestId("stock-line").CountAsync()).ShouldBe(1);

        // 3. One is used at the basin: three left, which is the warning number.
        // The form posts and comes back to the same address, so the new text is what tells the page has changed.
        await page.GetByTestId("submit-used").ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("1 unit taken off as used. The shelf now holds 3.");
        await page.ReachAsync(url => new Uri(url).AbsolutePath == card);
        (await page.GetByTestId("on-hand").TextContentAsync()).ShouldBe("3");
        (await page.GetByTestId("on-hand-panel").GetAttributeAsync("data-low")).ShouldBe("true");
        (await page.GetByTestId("low-note").TextContentAsync()).ShouldBe("Running low: the warning is set at 3. Time to reorder.");
        (await page.GetByTestId("stock-line").First.GetByTestId("line-kind").TextContentAsync()).ShouldBe("Used in the salon");
        await CheckPageAsync(page, styled, "submit-received", "product-card-mobile-390.png");

        // 4. Taking more than the record holds is refused and says what to do.
        await page.GetByTestId("used-number").FillAsync("9");
        await page.GetByTestId("submit-used").ClickAsync();
        await Assertions.Expect(page.GetByTestId("stock-problem")).ToHaveTextAsync("The record shows only 3 on the shelf. If there are more, count the shelf first.");
        await page.ReachAsync(url => new Uri(url).AbsolutePath == card);
        (await page.GetByTestId("on-hand").TextContentAsync()).ShouldBe("3");

        // 5. The list marks it as low, and the low filter finds it.
        await page.GetByTestId("back").ClickAsync();
        await page.ReachAsync(url => new Uri(url).AbsolutePath == "/admin/products");
        await page.GetByTestId("filter-tile").Filter(new LocatorFilterOptions { HasText = "Low stock" }).ClickAsync();
        await page.ReachAsync(url => url.Contains("show=low", StringComparison.Ordinal));
        var row = page.GetByTestId("product-card").Filter(new LocatorFilterOptions { HasText = name });
        (await row.CountAsync()).ShouldBe(1);
        (await row.GetAttributeAsync("data-low")).ShouldBe("true");
        (await row.GetByTestId("product-on-hand").TextContentAsync()).ShouldBe("3");
        (await row.GetByTestId("badge-low").IsVisibleAsync()).ShouldBeTrue();
        await CheckPageAsync(page, styled, "add-product", "products-list-mobile-390.png");
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