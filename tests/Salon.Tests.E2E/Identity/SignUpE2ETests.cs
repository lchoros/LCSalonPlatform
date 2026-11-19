using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Identity;

/// <summary>
/// A salon owner on a phone starts their salon themselves (ADR 0075): the sign-up form on the bare domain, the done
/// page, signing in at the salon's own address with what they just chose, and landing on getting started with the
/// steps ahead of them. The page fits 390 px and is axe clean.
/// </summary>
public class SignUpE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    [Fact]
    public async Task A_new_owner_signs_up_signs_in_and_stands_before_the_getting_started_steps()
    {
        // A slug no earlier run can have left behind: a slug is unique across the platform.
        var suffix = Random.Shared.Next(100_000, 999_999).ToString(CultureInfo.InvariantCulture);
        var slug = $"pilot-{suffix}";
        var email = $"owner@pilot-{suffix}.local";
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        // 1. The platform's home page offers the door; the form says what each field is for.
        (await page.GotoAsync($"{app.BaseUrl}/")).ShouldNotBeNull().Ok.ShouldBeTrue();
        await page.GetByTestId("start-signup").ClickAsync();
        await page.ReachAsync("**/signup");
        (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        var axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {v.Help}")));

        // 2. One form: the salon, its address, where it is, and the owner's own login.
        await page.GetByLabel("Salon name").FillAsync($"Pilot {suffix}");
        await page.GetByLabel("Your web address").FillAsync(slug);
        await page.GetByLabel("England").CheckAsync();
        await page.GetByLabel("Your name").FillAsync("Pat Owner");
        await page.GetByLabel("Your email").FillAsync(email);
        await page.GetByLabel("Choose a password").FillAsync("Correct-horse-9");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "signup-form-mobile-390.png"),
            FullPage = true,
        });
        await page.GetByTestId("signup-submit").ClickAsync();
        await page.ReachAsync("**/signup/done*");
        (await page.GetByTestId("signup-done").TextContentAsync()).ShouldBe($"Pilot {suffix} is ready");

        // 3. The sign-in link names the salon's own address (the E2E host has no fixed public port, so the test
        // walks there itself); the owner signs in with what they just chose.
        (await page.GetByTestId("signup-sign-in").GetAttributeAsync("href")).ShouldNotBeNull().ShouldContain($"//{slug}.localhost");
        await page.GotoAsync($"{app.TenantUrl(slug)}/admin/settings/getting-started");
        await page.ReachAsync("**/account/login*");
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Password").FillAsync("Correct-horse-9");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();

        // 4. Straight onto the steps: a new salon has reminders on and everything else ahead of it.
        await page.ReachAsync("**/admin/settings/getting-started");
        (await page.GetByTestId("setup-progress").TextContentAsync()).ShouldNotBeNull().Trim().ShouldBe("1 of 8 steps done.");
    }
}