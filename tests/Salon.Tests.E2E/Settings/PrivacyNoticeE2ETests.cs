using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Settings;

/// <summary>
/// The privacy notice on a phone (ADR 0063): a visitor on the salon's price list taps "How we use your details" at
/// the bottom and reads the standard notice, which names the salon and what she can ask. The owner then puts the
/// salon's own text in its place on the settings page, and the visitor's page shows that text. The notice and the
/// settings page are captured for the PR, pass axe and never scroll sideways at 390 px.
/// </summary>
public class PrivacyNoticeE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task A_visitor_reads_the_notice_and_the_owner_replaces_it_with_the_salons_own_text_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        await using var visitorContext = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var visitor = await visitorContext.NewPageAsync();

        // 1. From the price list to the notice.
        var response = await visitor.GotoAsync($"{app.TenantUrl(Slug)}/book");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await visitor.GetByTestId("footer-privacy").ClickAsync();
        await visitor.ReachAsync("**/privacy");
        var styled = await StylesheetLoadedAsync(visitor);
        (await visitor.GetByTestId("privacy-notice").GetAttributeAsync("data-own")).ShouldBe("false");
        (await visitor.GetByTestId("privacy-who").TextContentAsync()).ShouldNotBeNull().ShouldStartWith("Studio 20");
        (await visitor.GetByTestId("privacy-rights").TextContentAsync()).ShouldNotBeNull().ShouldContain("For a copy of what we hold about you.");
        (await visitor.GetByTestId("privacy-rights").TextContentAsync()).ShouldNotBeNull().ShouldContain("To delete your details.");
        await CheckPageAsync(visitor, styled, "privacy-notice-mobile-390.png");

        // 2. The owner puts the salon's own text in its place.
        await using var ownerContext = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var owner = await ownerContext.NewPageAsync();
        await owner.GotoAsync($"{app.TenantUrl(Slug)}/admin/settings/privacy");
        await owner.GetByLabel("Email").FillAsync(Email);
        await owner.GetByLabel("Password").FillAsync(Password);
        await owner.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await owner.ReachAsync("**/admin/settings/privacy");
        (await owner.GetByTestId("privacy-not-advice").TextContentAsync()).ShouldNotBeNull().ShouldContain("not legal advice");

        // Ticked without a text: told so, nothing saved.
        await owner.GetByTestId("use-own-text").CheckAsync();
        await owner.GetByTestId("save-privacy").ClickAsync();
        await Assertions.Expect(owner.GetByTestId("own-text-error")).ToHaveTextAsync("Write your own text, or untick the box to show the standard notice.");
        await owner.GetByTestId("own-text").FillAsync("We are a small salon.\n\nAsk at the desk for anything about your details.");
        await CheckPageAsync(owner, styled, "privacy-settings-mobile-390.png");
        await owner.GetByTestId("save-privacy").ClickAsync();
        await Assertions.Expect(owner.GetByTestId("flash")).ToHaveTextAsync("The privacy notice is saved.");

        // 3. The visitor's page now shows the salon's own text.
        await visitor.ReloadAsync();
        (await visitor.GetByTestId("privacy-notice").GetAttributeAsync("data-own")).ShouldBe("true");
        (await visitor.GetByTestId("privacy-own").TextContentAsync()).ShouldNotBeNull().ShouldContain("Ask at the desk for anything about your details.");
        (await visitor.GetByTestId("privacy-rights").CountAsync()).ShouldBe(0);

        // 4. Back to the standard notice, so the demo salon is as it was.
        await owner.GetByTestId("use-own-text").UncheckAsync();
        await owner.GetByTestId("save-privacy").ClickAsync();
        await Assertions.Expect(owner.GetByTestId("flash")).ToHaveTextAsync("The privacy notice is saved.");
        await visitor.ReloadAsync();
        (await visitor.GetByTestId("privacy-notice").GetAttributeAsync("data-own")).ShouldBe("false");
    }

    /// <summary>No sideways scroll (when styled), a screenshot for the PR and an axe pass.</summary>
    private static async Task CheckPageAsync(IPage page, bool styled, string screenshot)
    {
        if (styled)
        {
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