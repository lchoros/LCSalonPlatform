using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Settings;

/// <summary>
/// The demo owner on a phone opens Settings, Messages, rewrites the booking confirmation text, previews it without
/// saving, saves it, sees it marked as the salon's own wording on the list, and goes back to the standard wording
/// (ADR 0030). 390 px, axe clean, no horizontal scroll.
/// </summary>
public class MessageTemplatesE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_owner_rewrites_a_message_previews_it_saves_it_and_goes_back_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        // A word no earlier run can have left behind, so the assertions below prove this save.
        var marker = "Ref" + Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var wording = "Hello {{ClientFirstName}}, see you on {{AppointmentStart}}. " + marker;
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync(app.TenantUrl(Slug) + "/admin/appointments");
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**/admin/appointments");

        // 1. Settings, the Messages tab, the confirmation text.
        await page.GetByTestId("nav-settings").ClickAsync();
        await page.ReachAsync("**/admin/settings/notifications");
        await page.GetByTestId("settings-nav-messages").ClickAsync();
        await page.ReachAsync("**/admin/settings/messages");
        var styled = await StylesheetLoadedAsync(page);
        (await page.GetByTestId("message").CountAsync()).ShouldBe(5);
        await CheckPageAsync(page, styled, "edit-Booking.Confirmation-Sms", "messages-mobile-390.png");
        await page.GetByTestId("edit-Booking.Confirmation-Sms").ClickAsync();
        await page.ReachAsync("**/admin/settings/messages/Booking.Confirmation/sms");

        // 2. A preview shows the typed text with made-up details and says it is not saved.
        await page.GetByTestId("body").FillAsync(wording);
        await page.GetByTestId("preview-message").ClickAsync();
        await page.ReachAsync("**/admin/settings/messages/Booking.Confirmation/sms/preview");
        (await page.GetByTestId("preview-unsaved").IsVisibleAsync()).ShouldBeTrue();
        (await page.GetByTestId("preview-body").TextContentAsync()).ShouldNotBeNull().ShouldStartWith("Hello Olivia, see you on ");
        (await page.GetByTestId("preview-length").GetAttributeAsync("data-segments")).ShouldBe("1");
        (await page.GetByTestId("body").InputValueAsync()).ShouldBe(wording);

        // 3. A placeholder the message does not have is refused next to the field.
        await page.GetByTestId("body").FillAsync(wording + " {{Price}}");
        await page.GetByTestId("save-message").ClickAsync();
        (await page.GetByTestId("body-error").TextContentAsync()).ShouldNotBeNull().ShouldStartWith("This message cannot use {{Price}}.");
        await CheckPageAsync(page, styled, "save-message", "message-edit-error-mobile-390.png");

        // 4. Saved: the page says so and offers the way back to the standard wording.
        await page.GetByTestId("body").FillAsync(wording);
        await page.GetByTestId("save-message").ClickAsync();
        (await page.GetByTestId("flash").TextContentAsync()).ShouldBe("The message has been saved.");
        (await page.GetByTestId("status").TextContentAsync()).ShouldBe("Your own wording");
        (await page.GetByTestId("preview-body").TextContentAsync()).ShouldNotBeNull().ShouldEndWith(marker);
        await CheckPageAsync(page, styled, "save-message", "message-edit-mobile-390.png");

        // 5. The list marks that one message as the salon's own.
        await page.GetByTestId("back-to-messages").ClickAsync();
        await page.ReachAsync("**/admin/settings/messages");
        (await page.GetByTestId("status-Booking.Confirmation-Sms").TextContentAsync()).ShouldBe("Your own wording");

        // 6. Back to the standard wording, so the salon is left as it was found.
        await page.GetByTestId("edit-Booking.Confirmation-Sms").ClickAsync();
        await page.ReachAsync("**/admin/settings/messages/Booking.Confirmation/sms");
        await page.GetByTestId("reset-message").ClickAsync();
        (await page.GetByTestId("flash").TextContentAsync()).ShouldBe("The standard wording is in use again.");
        (await page.GetByTestId("status").TextContentAsync()).ShouldBe("Standard wording");
        (await page.GetByTestId("body").InputValueAsync()).ShouldStartWith("Hi {{ClientFirstName}}, your {{ServiceName}}");
        (await page.GetByTestId("reset-form").CountAsync()).ShouldBe(0);
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