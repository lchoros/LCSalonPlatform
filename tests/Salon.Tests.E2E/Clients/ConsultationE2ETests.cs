using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Clients;

/// <summary>
/// The demo owner on a phone takes a consultation form before a colour (ADR 0058): opens the client's card, answers
/// the six questions, forgets one and is told, then the client ticks the box and types their name. The kept form
/// shows the one "yes" marked and what was said; the card lists the form by day and purpose only and shows the new
/// patch test date. The form and the kept record are captured for the PR, pass axe and never scroll sideways at 390 px.
/// </summary>
public class ConsultationE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "owner@studio-20.local";
    private const string Password = "Studio20!e2e";
    private const string Said = "Itchy scalp for two days after a home colour last spring.";

    [Fact]
    public async Task The_owner_takes_a_consultation_form_on_a_phone_and_finds_it_on_the_card()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m), ("Colour (roots)", 90, 65m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        // Three days ago: the demo data books its own visits into the first free times from now on, so a visit placed
        // a few hours ahead lands on one of them at some hours of the day. The form needs a client, not a time.
        var (_, clientId, _) = await app.AddVisitAsync(tenantId, "Harriet", "Colour", -Duration.FromDays(3), 0m);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync(app.TenantUrl(Slug) + $"/admin/clients/{clientId}");
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync($"**/admin/clients/{clientId}");
        (await page.GetByTestId("no-consultations").IsVisibleAsync()).ShouldBeTrue();
        (await page.GetByTestId("patch-test").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("Not recorded");

        await page.GetByTestId("new-consultation").ClickAsync();
        await page.ReachAsync("**/consultations/new");
        (await page.GetByTestId("consultation-client").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("Harriet Colour");
        var questions = page.GetByTestId("question");
        (await questions.CountAsync()).ShouldBe(6);
        var styled = await StylesheetLoadedAsync(page);

        // Yes to the first, no to the rest but the last, which is forgotten.
        await page.GetByLabel("What is it for?").FillAsync("Colour (roots)");
        await questions.Nth(0).GetByTestId("answer-yes").CheckAsync();
        for (var i = 1; i < 5; i++)
        {
            await questions.Nth(i).GetByTestId("answer-no").CheckAsync();
        }

        await page.GetByTestId("consultation-details").FillAsync(Said);
        var today = SystemClock.Instance.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb["Europe/London"]).Date;
        await page.GetByTestId("consultation-patch-test").FillAsync(NodaTime.Text.LocalDatePattern.Iso.Format(today.PlusDays(-2)));
        await page.GetByTestId("consultation-agreed").CheckAsync();
        await page.GetByTestId("consultation-confirmed-by").FillAsync("Harriet Colour");
        await page.GetByTestId("consultation-submit").ClickAsync();
        await page.GetByTestId("answers-error").Filter(new LocatorFilterOptions { HasText = "Answer every question with Yes or No." }).WaitForAsync();

        // What was typed is still there; only the forgotten answer is missing.
        (await questions.Nth(0).GetByTestId("answer-yes").IsCheckedAsync()).ShouldBeTrue();
        (await page.GetByTestId("consultation-details").InputValueAsync()).ShouldBe(Said);
        if (styled)
        {
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "consultation-form-mobile-390.png"),
            FullPage = true,
        });
        var formAxe = await page.RunAxe();
        formAxe.Violations.ShouldBeEmpty(string.Join("; ", formAxe.Violations.Select(v => $"{v.Id}: {v.Help}")));

        await questions.Nth(5).GetByTestId("answer-no").CheckAsync();
        await page.GetByTestId("consultation-submit").ClickAsync();
        await page.ReachAsync(url => url.Contains("/consultations/", StringComparison.Ordinal) && !url.EndsWith("/new", StringComparison.Ordinal));

        (await page.GetByTestId("flash").TextContentAsync()).ShouldNotBeNull().ShouldContain("The consultation form is saved.");
        var lines = page.GetByTestId("answer-line");
        (await lines.CountAsync()).ShouldBe(6);
        (await page.Locator("[data-testid='answer-line'][data-concern='true']").CountAsync()).ShouldBe(1);
        (await lines.Nth(0).GetByTestId("answer").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("Yes");
        (await lines.Nth(5).GetByTestId("answer").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("No");
        (await page.GetByTestId("consultation-details").TextContentAsync()).ShouldNotBeNull().ShouldContain(Said);
        (await page.GetByTestId("confirmed-by").TextContentAsync()).ShouldNotBeNull().ShouldContain("Harriet Colour");
        (await page.GetByTestId("consent-wording").TextContentAsync()).ShouldNotBeNull().ShouldContain("I confirm that these answers are true and complete.");
        (await page.GetByTestId("consultation-taken").TextContentAsync()).ShouldNotBeNull().ShouldContain("Studio 20 Owner");
        if (styled)
        {
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "consultation-record-mobile-390.png"),
            FullPage = true,
        });
        var recordAxe = await page.RunAxe();
        recordAxe.Violations.ShouldBeEmpty(string.Join("; ", recordAxe.Violations.Select(v => $"{v.Id}: {v.Help}")));

        // Back on the card: the form is listed by day and purpose, the patch test date moved, nothing said is shown.
        await page.GetByTestId("back").ClickAsync();
        await page.ReachAsync($"**/admin/clients/{clientId}");
        var rows = page.GetByTestId("consultation-row");
        (await rows.CountAsync()).ShouldBe(1);
        (await rows.First.TextContentAsync()).ShouldNotBeNull().ShouldContain("Colour (roots)");
        (await page.GetByTestId("patch-test").TextContentAsync()).ShouldNotBeNull().Trim().ShouldNotBe("Not recorded");
        (await page.GetByTestId("client-card").TextContentAsync()).ShouldNotBeNull().ShouldNotContain("Itchy scalp");
        var cardAxe = await page.RunAxe();
        cardAxe.Violations.ShouldBeEmpty(string.Join("; ", cardAxe.Violations.Select(v => $"{v.Id}: {v.Help}")));
    }

    private static Task<bool> StylesheetLoadedAsync(IPage page)
        => page.EvaluateAsync<bool>("() => Array.from(document.styleSheets).some(s => s.href && s.cssRules.length > 0)");
}