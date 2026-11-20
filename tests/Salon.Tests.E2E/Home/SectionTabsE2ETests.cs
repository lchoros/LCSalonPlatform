using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Home;

/// <summary>
/// The owner on a phone moves between the pages of a module by its row of tabs (ADR 0089): the page she is on is the
/// one tab marked, a tap lands on the page the tab names, and the row scrolls inside its own box — the page itself
/// never slides sideways.
/// </summary>
public class SectionTabsE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    private static readonly (string Start, string Row, (string Tab, string Path)[] Steps)[] Walks =
    [
        ("/admin/appointments", "section-tabs-appointments",
        [
            ("section-tab-calendar", "/admin/appointments/calendar"),
            ("section-tab-waitlist", "/admin/appointments/waitlist"),
            ("section-tab-import", "/admin/appointments/import"),
            ("section-tab-day", "/admin/appointments"),
        ]),
        ("/admin/payments/cash-up", "section-tabs-till",
        [
            ("section-tab-sale", "/admin/payments/sale"),
            ("section-tab-vouchers", "/admin/payments/vouchers"),
            ("section-tab-cash-up", "/admin/payments/cash-up"),
        ]),
        ("/admin/staff", "section-tabs-staff",
        [
            ("section-tab-commission", "/admin/staff/commission"),
            ("section-tab-list", "/admin/staff"),
        ]),
    ];

    [Fact]
    public async Task On_a_phone_the_owner_moves_between_the_pages_of_a_module_by_its_tabs()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();
        var root = app.TenantUrl(Slug);

        (await page.GotoAsync(root + Walks[0].Start)).ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**" + Walks[0].Start);
        var styled = await page.EvaluateAsync<bool>("() => Array.from(document.styleSheets).some(s => s.href && s.cssRules.length > 0)");

        foreach (var (start, row, steps) in Walks)
        {
            (await page.GotoAsync(root + start)).ShouldNotBeNull().Ok.ShouldBeTrue();

            foreach (var (tab, path) in steps)
            {
                await page.GetByTestId(row).GetByTestId(tab).ClickAsync();
                await page.ReachAsync(url => new Uri(url).AbsolutePath == path);

                var marked = page.GetByTestId(row).Locator("[aria-current=page]");
                (await marked.CountAsync()).ShouldBe(1, path);
                (await marked.GetAttributeAsync("data-testid")).ShouldBe(tab, path);
                if (styled)
                {
                    (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390, path);
                }
            }
        }

        // One page of the walk as the owner sees it: a screenshot for the PR and an axe pass with the row on it.
        (await page.GotoAsync(root + "/admin/appointments/waitlist")).ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "admin-section-tabs-mobile-390.png"),
            FullPage = true,
        });
        var axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {v.Help}")));
    }
}