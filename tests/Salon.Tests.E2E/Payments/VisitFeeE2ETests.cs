using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Payments;

/// <summary>
/// The demo owner on a phone sets a cancellation policy, marks a client as a no-show, is shown the fee the policy allows,
/// adds it, sees it on the client's card as owed, and records that the client paid in cash (ADR 0022). 390 px, axe clean,
/// no horizontal scroll, and the policy is put back afterwards so no other test meets it.
/// </summary>
public class VisitFeeE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_sets_a_policy_charges_a_no_show_fee_and_records_its_payment_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        // A surname no earlier run can have left behind, so the card found below is this run's.
        var lastName = "Noshow" + Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var (appointmentId, _, price) = await app.AddUnmarkedPastVisitAsync(tenantId, "Fiona", lastName);
        var fee = "£" + price.ToString("0.00", CultureInfo.InvariantCulture);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        try
        {
            var response = await page.GotoAsync(app.TenantUrl(Slug) + "/admin/appointments");
            response.ShouldNotBeNull().Ok.ShouldBeTrue();
            await page.GetByLabel("Email").FillAsync(Email);
            await page.GetByLabel("Password").FillAsync(Password);
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
            await page.ReachAsync("**/admin/appointments");

            // 1. The policy: free until a day before, half the price for a late cancellation, all of it for a no-show.
            await page.GetByTestId("nav-settings").ClickAsync();
            await page.ReachAsync("**/admin/settings/notifications");
            await page.GetByTestId("settings-nav-cancellation-policy").ClickAsync();
            await page.ReachAsync("**/admin/settings/cancellation-policy");
            var styled = await StylesheetLoadedAsync(page);
            await page.GetByTestId("FreeChangeHours").FillAsync("24");
            await page.GetByTestId("LateCancellationFeePercent").FillAsync("50");
            await page.GetByTestId("NoShowFeePercent").FillAsync("100");
            await page.GetByTestId("save-cancellation-policy").ClickAsync();
            (await page.GetByTestId("flash").TextContentAsync()).ShouldBe("The cancellation policy has been saved.");
            (await page.GetByTestId("NoShowFeePercent").InputValueAsync()).ShouldBe("100");
            await CheckPageAsync(page, styled, "save-cancellation-policy", "settings-cancellation-policy-mobile-390.png");

            // 2. Marking the visit as a no-show leads straight to the fee the policy allows.
            await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/appointments/calendar/{appointmentId}");
            await page.GetByTestId("action-no-show").GetByRole(AriaRole.Button).ClickAsync();
            await page.ReachAsync($"**/admin/payments/fees/{appointmentId}");
            (await page.GetByTestId("fee-client").TextContentAsync()).ShouldBe($"Fiona {lastName}");
            (await page.GetByTestId("fee-proposal-text").TextContentAsync())
                .ShouldBe($"The client did not turn up. Your policy sets a no-show fee of 100% of the price: {fee}.");
            (await page.GetByTestId("fee-amount").InputValueAsync()).ShouldBe(price.ToString("0.00", CultureInfo.InvariantCulture));
            await CheckPageAsync(page, styled, "charge-fee", "fee-preview-mobile-390.png");

            // 3. A person adds it; it is owed.
            await page.GetByTestId("charge-fee").ClickAsync();
            (await page.GetByTestId("flash").TextContentAsync()).ShouldBe("The fee has been added to the client's account.");
            (await page.GetByTestId("fee-status").TextContentAsync()).ShouldBe("Owed");
            (await page.GetByTestId("fee-amount-charged").TextContentAsync()).ShouldBe(fee);
            (await page.GetByTestId("fee-history-added").TextContentAsync()).ShouldNotBeNull().ShouldContain("Fee added by Studio 20 Owner");
            await CheckPageAsync(page, styled, "record-payment", "fee-owed-mobile-390.png");

            // 4. The client's card says what is owed and links back to the fee.
            await page.GetByTestId("fee-client").ClickAsync();
            await page.ReachAsync("**/admin/clients/*");
            (await page.GetByTestId("client-owes").TextContentAsync()).ShouldBe($"Owes {fee}");
            (await page.GetByTestId("fee-row-status").TextContentAsync()).ShouldBe("Owed");
            await CheckPageAsync(page, styled, null, "client-fees-mobile-390.png");
            await page.GetByTestId("fee-link").ClickAsync();
            await page.ReachAsync($"**/admin/payments/fees/{appointmentId}");

            // 5. The client pays in cash at the desk; the fee is settled, with who recorded it and the plugin's reference.
            await page.GetByTestId("pay-method").SelectOptionAsync("Cash");
            await page.GetByTestId("record-payment").ClickAsync();
            (await page.GetByTestId("flash").TextContentAsync()).ShouldBe("The payment has been recorded.");
            (await page.GetByTestId("fee-status").TextContentAsync()).ShouldBe("Paid");
            (await page.GetByTestId("fee-history-settled").TextContentAsync()).ShouldNotBeNull().ShouldContain("Paid: Cash. Recorded by Studio 20 Owner");
            (await page.GetByTestId("fee-reference").TextContentAsync()).ShouldNotBeNull().ShouldStartWith("Reference: manual:no-show-fee:");
            (await page.GetByTestId("pay-form").CountAsync()).ShouldBe(0, "a settled fee offers nothing more");
            await CheckPageAsync(page, styled, null, "fee-paid-mobile-390.png");
        }
        finally
        {
            // The demo salon is shared: without this, other tests would meet a policy they did not ask for.
            await app.UseDefaultSettingsAsync(tenantId);
        }
    }

    /// <summary>Touch target (when styled), no sideways scroll, a screenshot for the PR and an axe pass.</summary>
    private static async Task CheckPageAsync(IPage page, bool styled, string? buttonTestId, string screenshot)
    {
        if (styled)
        {
            if (buttonTestId is not null)
            {
                (await page.GetByTestId(buttonTestId).BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
            }

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