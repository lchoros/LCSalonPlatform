using System.Globalization;
using System.Text;

using Deque.AxeCore.Playwright;

using Microsoft.Playwright;

using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Clients;

/// <summary>
/// An owner on a phone uploads a Booksy export, checks the preview and confirms (ADR 0019). All three screens render at
/// 390 px without horizontal scroll, pass axe and are captured for the PR.
/// </summary>
public class ImportClientsE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    // Own slug: test classes share one database and the fixture replaces the salon's rows.
    private const string Slug = "studio-20-import";
    private const string Email = "owner@studio-20-import.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_owner_imports_a_booksy_file_on_a_phone_through_upload_preview_and_result()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", []);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync(app.TenantUrl(Slug) + "/admin/clients/import");
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/clients/import");

        (await page.GetByTestId("import-upload-form").IsVisibleAsync()).ShouldBeTrue();
        await CheckAndShootAsync(page, "clients-import-upload-mobile-390.png");

        // Unique contact details per run: phone and email are unique per salon and earlier runs leave their rows behind.
        var suffix = Random.Shared.Next(100_000, 999_999).ToString(CultureInfo.InvariantCulture);
        var csv =
            "First Name,Last Name,Phone Number,Email,Customer notes,Accepts Marketing\n" +
            $"Olivia,Brown,07700 {suffix},olivia.{suffix}@example.com,Prefers afternoons,Yes\n" +
            $"Amelia,Taylor,+44 7700 {suffix[..3]}999,,Regular colour client,No\n" +
            $"Madonna,,07700 {suffix[..3]}111,,,\n";
        await page.GetByLabel("CSV file").SetInputFilesAsync(new FilePayload
        {
            Name = "booksy-customers.csv",
            MimeType = "text/csv",
            Buffer = Encoding.UTF8.GetBytes(csv),
        });
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Check the file" }).ClickAsync();
        await page.ReachAsync(url => url.Contains("/admin/clients/import/", StringComparison.Ordinal));

        (await page.GetByTestId("import-preview").IsVisibleAsync()).ShouldBeTrue();
        (await page.GetByTestId("count-new").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("2");
        (await page.GetByTestId("count-invalid").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("1");
        (await page.GetByText("Last name is missing.").CountAsync()).ShouldBe(1);
        await CheckAndShootAsync(page, "clients-import-preview-mobile-390.png");

        await page.GetByTestId("import-confirm").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='import-result']");

        (await page.GetByTestId("result-created").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("2");
        await CheckAndShootAsync(page, "clients-import-result-mobile-390.png");

        await page.GetByTestId("to-clients").ClickAsync();
        await page.ReachAsync("**/admin/clients");
        (await page.GetByTestId("client-list").TextContentAsync()).ShouldNotBeNull().ShouldContain("Olivia Brown");
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