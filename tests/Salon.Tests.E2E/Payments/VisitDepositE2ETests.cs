using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Payments;

/// <summary>
/// The demo owner on a phone opens a visit that asks for a deposit, records that it arrived by bank transfer, sees it on
/// the visit and on the client's card, and records that it went back in cash (ADR 0023). 390 px, axe clean, no
/// horizontal scroll.
/// </summary>
public class VisitDepositE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_demo_owner_records_a_deposit_and_its_return_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        // A surname no earlier run can have left behind, so the card found below is this run's.
        var lastName = "Deposit" + Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var (appointmentId, _, _) = await app.AddVisitAsync(tenantId, "Dora", lastName, Duration.FromDays(20), deposit: 9m);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/appointments/calendar/{appointmentId}");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync($"**/admin/appointments/calendar/{appointmentId}");
        var styled = await StylesheetLoadedAsync(page);

        // 1. The visit says what is due and leads to the deposit.
        (await page.GetByTestId("card-deposit-due").TextContentAsync()).ShouldBe("£9.00 due, not received");
        await CheckPageAsync(page, styled, null, "card-deposit-due-mobile-390.png");
        await page.GetByTestId("card-deposit-link").ClickAsync();
        await page.ReachAsync($"**/admin/payments/deposits/{appointmentId}");
        (await page.GetByTestId("deposit-client").TextContentAsync()).ShouldBe($"Dora {lastName}");
        (await page.GetByTestId("deposit-due").TextContentAsync()).ShouldBe("£9.00");
        (await page.GetByTestId("deposit-amount").InputValueAsync()).ShouldBe("9.00");

        // 2. The transfer arrived: record it.
        await page.GetByTestId("deposit-method").SelectOptionAsync("BankTransfer");
        await CheckPageAsync(page, styled, "record-deposit", "deposit-record-mobile-390.png");
        await page.GetByTestId("record-deposit").ClickAsync();
        (await page.GetByTestId("flash").TextContentAsync()).ShouldBe("The deposit has been recorded.");
        (await page.GetByTestId("deposit-status").TextContentAsync()).ShouldBe("Held");
        (await page.GetByTestId("deposit-amount-received").TextContentAsync()).ShouldBe("£9.00");
        (await page.GetByTestId("deposit-history-received").TextContentAsync()).ShouldNotBeNull().ShouldContain("Received: Bank transfer. Recorded by Studio 20 Owner");
        (await page.GetByTestId("deposit-reference").TextContentAsync()).ShouldNotBeNull().ShouldStartWith("Reference: manual:deposit:");
        await CheckPageAsync(page, styled, "return-deposit", "deposit-held-mobile-390.png");

        // 3. The visit and the client's card both show it.
        await page.GetByTestId("back").ClickAsync();
        await page.ReachAsync($"**/admin/appointments/calendar/{appointmentId}");
        (await page.GetByTestId("card-deposit-received").TextContentAsync()).ShouldBe("£9.00 received");
        await page.GetByTestId("card-deposit-link").ClickAsync();
        await page.ReachAsync($"**/admin/payments/deposits/{appointmentId}");
        await page.GetByTestId("deposit-client").ClickAsync();
        await page.ReachAsync("**/admin/clients/*");
        (await page.GetByTestId("deposit-row-status").TextContentAsync()).ShouldBe("Held");
        (await page.GetByTestId("deposit-row-amount").TextContentAsync()).ShouldBe("£9.00");
        await CheckPageAsync(page, styled, null, "client-deposits-mobile-390.png");

        // 4. The client cancels in time: the deposit goes back in cash.
        await page.GetByTestId("deposit-link").ClickAsync();
        await page.ReachAsync($"**/admin/payments/deposits/{appointmentId}");
        await page.GetByTestId("return-method").SelectOptionAsync("Cash");
        await page.GetByTestId("return-deposit").ClickAsync();
        (await page.GetByTestId("flash").TextContentAsync()).ShouldBe("The return of the deposit has been recorded.");
        (await page.GetByTestId("deposit-status").TextContentAsync()).ShouldBe("Returned");
        (await page.GetByTestId("deposit-history-returned").TextContentAsync()).ShouldNotBeNull().ShouldContain("Returned: Cash. Recorded by Studio 20 Owner");
        (await page.GetByTestId("deposit-return-reference").TextContentAsync()).ShouldNotBeNull().ShouldStartWith("Reference: manual:deposit-return:");
        (await page.GetByTestId("return-deposit-form").CountAsync()).ShouldBe(0, "a returned deposit offers nothing more");
        await CheckPageAsync(page, styled, null, "deposit-returned-mobile-390.png");
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