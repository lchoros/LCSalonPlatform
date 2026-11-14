using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Clients;

/// <summary>
/// The demo owner on a phone opens James Brown's card (the demo client with a completed visit yesterday), sees the
/// history, adds a duplicate "James Browne", merges it into James Brown through the search and confirmation pages and
/// lands on the kept card with the flash (ADR 0017). The confirmation and the kept card are captured for the PR, pass axe
/// and never scroll sideways at 390 px.
/// </summary>
public class MergeClientsE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_reviews_a_history_and_merges_a_duplicate_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m), ("Colour (roots)", 90, 65m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync(app.TenantUrl(Slug) + "/admin/clients?q=Brown");
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/clients?q=Brown");

        // The demo client with history: one visit completed yesterday.
        await page.GetByTestId("client-row").Filter(new LocatorFilterOptions { HasText = "James Brown" }).First.GetByRole(AriaRole.Link).First.ClickAsync(); // the link, not the row: unstyled pages (CI has no CSS) put the row centre outside it
        await page.ReachAsync(url => url.Contains("/admin/clients/", StringComparison.Ordinal) && !url.Contains('?', StringComparison.Ordinal));
        var targetUrl = page.Url;
        (await page.GetByTestId("history").IsVisibleAsync()).ShouldBeTrue();
        (await page.GetByTestId("visits-completed").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("1");
        (await page.GetByTestId("past-list").GetByTestId("visit").CountAsync()).ShouldBeGreaterThanOrEqualTo(1);
        (await page.GetByTestId("last-visit").TextContentAsync()).ShouldNotBeNull().ShouldNotBe("—");
        (await page.GetByTestId("merge-link").IsVisibleAsync()).ShouldBeTrue();
        var styled = await StylesheetLoadedAsync(page);
        if (styled)
        {
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "clients-history-mobile-390.png"),
            FullPage = true,
        });
        var historyAxe = await page.RunAxe();
        historyAxe.Violations.ShouldBeEmpty(string.Join("; ", historyAxe.Violations.Select(v => $"{v.Id}: {v.Help}")));

        // A fresh duplicate, unique per run (phone and email are unique per salon).
        var suffix = Random.Shared.Next(100_000, 999_999).ToString(CultureInfo.InvariantCulture);
        await page.GotoAsync(app.TenantUrl(Slug) + "/admin/clients/new");
        await page.GetByLabel("First name", new PageGetByLabelOptions { Exact = true }).FillAsync("James");
        await page.GetByLabel("Last name", new PageGetByLabelOptions { Exact = true }).FillAsync("Browne");
        await page.GetByLabel("Phone number", new PageGetByLabelOptions { Exact = true }).FillAsync("07700 " + suffix);
        await page.GetByLabel("Notes", new PageGetByLabelOptions { Exact = true }).FillAsync("Typed in twice at the desk.");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save client" }).ClickAsync();
        await page.ReachAsync(url => url.Contains("/admin/clients/", StringComparison.Ordinal) && !url.EndsWith("/new", StringComparison.Ordinal));

        await page.GetByTestId("merge-link").ClickAsync();
        await page.ReachAsync("**/admin/clients/*/merge");
        await page.GetByTestId("merge-search").FillAsync("Brown");
        await page.GetByTestId("merge-search-submit").ClickAsync();
        await page.ReachAsync("**/admin/clients/*/merge?q=Brown");
        var jamesBrown = page.GetByTestId("merge-candidate").Filter(new LocatorFilterOptions { HasText = "James Brown" }).Filter(new LocatorFilterOptions { HasNotText = "Browne" });
        (await jamesBrown.CountAsync()).ShouldBe(1);
        await jamesBrown.GetByTestId("choose-target").ClickAsync();
        await page.ReachAsync("**/admin/clients/*/merge/*");

        (await page.GetByTestId("merge-confirm").IsVisibleAsync()).ShouldBeTrue();
        (await page.GetByTestId("source-name").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("James Browne");
        (await page.GetByTestId("target-name").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("James Brown");
        (await page.GetByTestId("what-happens").TextContentAsync()).ShouldNotBeNull().ShouldContain("This cannot be undone.");
        if (styled)
        {
            (await page.GetByTestId("merge-submit").BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "clients-merge-mobile-390.png"),
            FullPage = true,
        });
        var confirmAxe = await page.RunAxe();
        confirmAxe.Violations.ShouldBeEmpty(string.Join("; ", confirmAxe.Violations.Select(v => $"{v.Id}: {v.Help}")));

        await page.GetByTestId("merge-submit").ClickAsync();
        await page.ReachAsync(targetUrl);

        (await page.GetByTestId("flash").TextContentAsync()).ShouldNotBeNull().ShouldContain("James Browne has been merged into James Brown.");
        (await page.GetByTestId("client-name").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("James Brown");
        (await page.GetByTestId("client-notes").TextContentAsync()).ShouldNotBeNull().ShouldContain("Typed in twice at the desk.");
        (await page.GetByTestId("visits-completed").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("1");
        if (styled)
        {
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        }

        var cardAxe = await page.RunAxe();
        cardAxe.Violations.ShouldBeEmpty(string.Join("; ", cardAxe.Violations.Select(v => $"{v.Id}: {v.Help}")));
    }

    /// <summary>True when a stylesheet resolved with rules in it (a 404 or an empty body parses to zero rules).</summary>
    private static Task<bool> StylesheetLoadedAsync(IPage page)
        => page.EvaluateAsync<bool>("() => Array.from(document.styleSheets).some(s => s.href && s.cssRules.length > 0)");
}