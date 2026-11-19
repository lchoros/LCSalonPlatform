using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Payments;

/// <summary>
/// The desk on a phone takes the payment for a visit, opens its receipt from the paid bill and emails it to the
/// client (ADR 0070). The receipt says what the visit cost, how it was paid and where the salon is. 390 px, axe
/// clean, no horizontal scroll, a screenshot for the PR.
/// </summary>
public class ReceiptE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_desk_opens_the_receipt_of_a_paid_visit_and_emails_it_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await app.UseDefaultSettingsAsync(tenantId);
        var lastName = "Receipt" + Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var clientEmail = $"{lastName.ToLowerInvariant()}@example.com";
        var (appointmentId, _, _) = await app.AddVisitAsync(tenantId, "Rhea", lastName, -Duration.FromMinutes(20), deposit: 0m, email: clientEmail);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 390, Height = 844 } });
        var page = await context.NewPageAsync();

        (await page.GotoAsync($"{app.TenantUrl(Slug)}/admin/appointments/calendar/{appointmentId}")).ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync($"**/admin/appointments/calendar/{appointmentId}");
        var styled = await StylesheetLoadedAsync(page);

        // 1. The client arrived and paid in cash.
        await page.GetByTestId("action-arrived").GetByRole(AriaRole.Button).ClickAsync();
        await page.ReachAsync("**/admin/appointments?date=*");
        await page.GetByTestId("checkout-link").ClickAsync();
        await page.ReachAsync($"**/admin/payments/checkout/{appointmentId}");
        await page.GetByTestId("bill-method").SelectOptionAsync("Cash");
        await page.GetByTestId("pay-bill").ClickAsync();
        (await page.GetByTestId("flash").TextContentAsync()).ShouldBe("The payment has been recorded.");

        // 2. The paid bill leads to its receipt.
        await page.GetByTestId("receipt-link").ClickAsync();
        await page.ReachAsync($"**/admin/payments/receipt/{appointmentId}");
        (await page.GetByTestId("receipt-salon").TextContentAsync()).ShouldBe("Studio 20");
        (await page.GetByTestId("receipt-client").TextContentAsync()).ShouldBe($"Rhea {lastName}");
        (await page.GetByTestId("receipt-price").TextContentAsync()).ShouldBe("£45.00");
        (await page.GetByTestId("receipt-total").TextContentAsync()).ShouldBe("£45.00");
        await Assertions.Expect(page.GetByTestId("receipt-payment")).ToHaveTextAsync("Paid: Cash");
        await CheckPageAsync(page, styled, "email-receipt", "receipt-mobile-390.png");

        // 3. One tap sends it to the address on the client's card.
        var before = app.Emails.Messages.Count;
        await page.GetByTestId("email-receipt").ClickAsync();
        await page.ReachAsync($"**/admin/payments/receipt/{appointmentId}");
        (await page.GetByTestId("flash").TextContentAsync()).ShouldBe("The receipt is on its way to the client.");
        var sent = app.Emails.Messages.Skip(before).ShouldHaveSingleItem();
        sent.To.ShouldBe(clientEmail);
        sent.TextBody.ShouldNotBeNull().ShouldContain("Total: £45.00");
    }

    /// <summary>Touch target (when styled), no sideways scroll, a screenshot for the PR and an axe pass.</summary>
    private static async Task CheckPageAsync(IPage page, bool styled, string buttonTestId, string screenshot)
    {
        if (styled)
        {
            (await page.GetByTestId(buttonTestId).First.BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
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