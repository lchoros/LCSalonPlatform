using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Booking;

/// <summary>
/// The owner switches the public waitlist on, and a visitor on a phone who finds no time that suits leaves a name and
/// a number, enters the code from the text message and is told they are on the list (ADR 0055). The owner then finds
/// them on the desk's waitlist, marked as asked online. 390 px, axe clean, no sideways scroll.
/// </summary>
public class PublicWaitlistE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task A_visitor_asks_to_be_told_when_a_time_comes_up_and_the_owner_finds_them_on_the_list()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        // A surname and a number no earlier run can have left behind.
        var digits = Random.Shared.Next(100000, 999999).ToString(CultureInfo.InvariantCulture);
        var lastName = "Waiting" + digits;
        var phone = "07700 " + digits;
        var phoneE164 = "+447700" + digits;
        var mobile = new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 390, Height = 844 } };

        try
        {
            // 1. The owner switches the offer on.
            await using (var ownerContext = await app.Browser.NewContextAsync(mobile))
            {
                var admin = await ownerContext.NewPageAsync();
                (await admin.GotoAsync($"{app.TenantUrl(Slug)}/admin/settings/waitlist")).ShouldNotBeNull().Ok.ShouldBeTrue();
                await admin.GetByLabel("Email").FillAsync(Email);
                await admin.GetByLabel("Password").FillAsync(Password);
                await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
                await admin.ReachAsync("**/admin/settings/waitlist");
                (await admin.GetByTestId("public-waitlist-enabled").IsCheckedAsync()).ShouldBeFalse();
                await admin.GetByTestId("public-waitlist-enabled").CheckAsync();
                await admin.GetByTestId("save-public-waitlist").ClickAsync();
                await Assertions.Expect(admin.GetByTestId("flash")).ToHaveTextAsync("The waitlist settings have been saved.");
                await CheckPageAsync(admin, await StylesheetLoadedAsync(admin), "settings-waitlist-mobile-390.png");
            }

            // 2. A visitor picks a service and, under the times, asks to be told.
            await using var context = await app.Browser.NewContextAsync(mobile);
            var page = await context.NewPageAsync();
            (await page.GotoAsync($"{app.TenantUrl(Slug)}/book")).ShouldNotBeNull().Ok.ShouldBeTrue();
            var styled = await StylesheetLoadedAsync(page);
            await page.GetByTestId("book-service").First.ClickAsync();
            await page.ReachAsync(url => url.Contains("/book/", StringComparison.Ordinal) && !url.EndsWith("/book", StringComparison.Ordinal));
            await page.GetByTestId("join-waitlist").ClickAsync();
            await page.ReachAsync(url => url.Contains("/waitlist", StringComparison.Ordinal));

            // 3. Name, number, a remark.
            await page.GetByLabel("First name", new PageGetByLabelOptions { Exact = true }).FillAsync("Wren");
            await page.GetByLabel("Last name", new PageGetByLabelOptions { Exact = true }).FillAsync(lastName);
            await page.GetByLabel("Mobile number", new PageGetByLabelOptions { Exact = true }).FillAsync(phone);
            await page.GetByTestId("waitlist-note").FillAsync("Mornings only");
            await CheckPageAsync(page, styled, "public-waitlist-form-mobile-390.png");
            await page.GetByTestId("submit-waitlist").ClickAsync();
            await page.ReachAsync(url => url.Contains("/book/waitlist/verify/", StringComparison.Ordinal));

            // 4. The code from the text message.
            var code = app.Sms.LastCodeFor(phoneE164).ShouldNotBeNull();
            await page.GetByTestId("code").FillAsync(code);
            await page.GetByTestId("submit-code").ClickAsync();
            await page.ReachAsync(url => url.Contains("/book/waitlist/done", StringComparison.Ordinal));
            (await page.GetByTestId("waitlist-done").TextContentAsync()).ShouldBe("You are on the list");
            await CheckPageAsync(page, styled, "public-waitlist-done-mobile-390.png");

            // 5. The owner finds them on the desk's list.
            await using var listContext = await app.Browser.NewContextAsync(mobile);
            var list = await listContext.NewPageAsync();
            await list.GotoAsync($"{app.TenantUrl(Slug)}/admin/appointments/waitlist");
            await list.GetByLabel("Email").FillAsync(Email);
            await list.GetByLabel("Password").FillAsync(Password);
            await list.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
            await list.ReachAsync("**/admin/appointments/waitlist");
            (await list.GetByText("Wren " + lastName).CountAsync()).ShouldBeGreaterThan(0);
            (await list.GetByTestId("asked-online").CountAsync()).ShouldBeGreaterThan(0);
        }
        finally
        {
            // The other browser tests share this salon and expect the offer to be off.
            await app.UseDefaultSettingsAsync(tenantId);
        }
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