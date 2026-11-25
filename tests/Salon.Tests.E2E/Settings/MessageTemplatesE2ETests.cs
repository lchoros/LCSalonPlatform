using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Settings;

/// <summary>
/// The demo owner on a phone opens Settings, Messages, rewrites the booking confirmation text, puts a tag in with one
/// tap where the cursor is and watches the example and the count of texts follow (ADR 0098), sees a tag the message
/// does not have marked and refused, saves, finds the wording kept after a reload, sees it marked as the salon's own
/// on the list, and puts back the standard text after saying yes (ADR 0030). 390 px, axe clean, no horizontal scroll.
/// </summary>
public class MessageTemplatesE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";

    [Fact]
    public async Task The_owner_rewrites_a_message_with_a_tapped_tag_saves_it_and_puts_back_the_standard_text_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        // A word no earlier run can have left behind, so the assertions below prove this save.
        var marker = "Ref" + Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var typed = "Hello, see you on {{AppointmentStart}}. " + marker;
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
        await page.OpenNavAsync("nav-settings");
        await page.ReachAsync("**/admin/settings/notifications");
        await page.GetByTestId("settings-nav-messages").ClickAsync();
        await page.ReachAsync("**/admin/settings/messages");
        var styled = await StylesheetLoadedAsync(page);
        (await page.GetByTestId("message").CountAsync()).ShouldBe(7, "four booking messages, the code and the two waitlist messages (ADR 0056, ADR 0065)");
        await CheckPageAsync(page, styled, "edit-Booking.Confirmation-Sms", "messages-mobile-390.png");
        await page.GetByTestId("edit-Booking.Confirmation-Sms").ClickAsync();
        await page.ReachAsync("**/admin/settings/messages/Booking.Confirmation/sms");

        // 2. The script is on: the tags are buttons, and the example follows the box, so the Preview button steps aside.
        var chip = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Client's first name" });
        await Assertions.Expect(chip).ToBeEnabledAsync();
        (await page.GetByTestId("preview-message").IsHiddenAsync()).ShouldBeTrue("the example is live, so the server's preview is only the way without script");
        (await page.GetByTestId("tag-Code").CountAsync()).ShouldBe(0, "the confirmation has no code to offer");
        var body = page.GetByTestId("body");
        await body.FillAsync(typed);
        (await page.GetByTestId("preview-body").TextContentAsync()).ShouldNotBeNull().ShouldStartWith("Hello, see you on ");
        var countBefore = await page.GetByTestId("preview-length").TextContentAsync();

        // 3. A tap in the box after "Hello", a tap on the tag: it goes in at the cursor with a space before it, the cursor
        // waits after it in the box, and the example and the count follow at once.
        await body.ClickAsync();
        await body.PressAsync("Control+Home");
        for (var i = 0; i < "Hello".Length; i++)
        {
            await body.PressAsync("ArrowRight");
        }

        await chip.ClickAsync();
        (await body.InputValueAsync()).ShouldBe(wording);
        (await page.EvaluateAsync<string>("() => document.activeElement?.id")).ShouldBe("body", "the focus goes back to the box");
        (await body.EvaluateAsync<int>("b => b.selectionStart")).ShouldBe("Hello {{ClientFirstName}}".Length);
        (await page.GetByTestId("preview-body").TextContentAsync()).ShouldNotBeNull().ShouldStartWith("Hello Olivia, see you on ");
        (await page.GetByTestId("preview-body").Locator("mark.tag-value").First.TextContentAsync()).ShouldBe("Olivia");
        var countAfter = await page.GetByTestId("preview-length").TextContentAsync();
        countAfter.ShouldNotBe(countBefore, "the name adds characters to the example");
        countAfter.ShouldNotBeNull().ShouldStartWith("Texts needed: 1.");
        (await page.GetByTestId("preview-length").GetAttributeAsync("data-segments")).ShouldBe("1");
        await CheckPageAsync(page, styled, "tag-ClientFirstName", "message-tags-mobile-390.png");

        // 4. A tag the message does not have is marked in the example as the person types, and refused on saving.
        await body.FillAsync(wording + " {{Price}}");
        (await page.GetByTestId("preview-body").Locator("mark.tag-unknown").TextContentAsync()).ShouldBe("{{Price}} unknown tag");
        await page.GetByTestId("save-message").ClickAsync();
        // The page that answers has the same address; wait for its refusal, then for the cross-fade to end.
        await Assertions.Expect(page.GetByTestId("body-error")).ToContainTextAsync("This message cannot use {{Price}}.");
        await page.ReachAsync("**/admin/settings/messages/Booking.Confirmation/sms");
        (await page.GetByTestId("body-error").TextContentAsync()).ShouldNotBeNull().ShouldStartWith("This message cannot use {{Price}}.");
        (await page.GetByTestId("preview-body").Locator("mark.tag-unknown").TextContentAsync()).ShouldBe("{{Price}} unknown tag", "the server marks the same tag");
        await CheckPageAsync(page, styled, "save-message", "message-edit-error-mobile-390.png");

        // 5. Saved: the page says so, and after a reload the wording, the example and the count are the ones typed.
        await body.FillAsync(wording);
        var countTyped = await page.GetByTestId("preview-length").TextContentAsync();
        await page.GetByTestId("save-message").ClickAsync();
        (await page.GetByTestId("flash").TextContentAsync()).ShouldBe("The message has been saved.");
        await page.ReloadAsync();
        await page.ReachAsync("**/admin/settings/messages/Booking.Confirmation/sms");
        (await page.GetByTestId("status").TextContentAsync()).ShouldBe("Your own wording");
        (await body.InputValueAsync()).ShouldBe(wording);
        (await page.GetByTestId("preview-body").TextContentAsync()).ShouldNotBeNull().ShouldEndWith(marker);
        (await page.GetByTestId("preview-length").TextContentAsync()).ShouldBe(countTyped, "the browser counts the way the server does");
        await CheckPageAsync(page, styled, "save-message", "message-edit-mobile-390.png");

        // 6. The list marks that one message as the salon's own.
        await page.GetByTestId("back-to-messages").ClickAsync();
        await page.ReachAsync("**/admin/settings/messages");
        (await page.GetByTestId("status-Booking.Confirmation-Sms").TextContentAsync()).ShouldBe("Your own wording");

        // 7. Put back the standard text, after saying yes, so the salon is left as it was found.
        await page.GetByTestId("edit-Booking.Confirmation-Sms").ClickAsync();
        await page.ReachAsync("**/admin/settings/messages/Booking.Confirmation/sms");
        await page.GetByTestId("reset-jump").ClickAsync();
        await page.GetByTestId("reset-message").ClickAsync();
        (await page.GetByTestId("reset-yes").IsVisibleAsync()).ShouldBeTrue("the first tap only asks");
        await page.GetByTestId("reset-yes").ClickAsync();
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