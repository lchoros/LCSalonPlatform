using System.Diagnostics;
using System.Globalization;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Booking;

/// <summary>
/// The M1 gate (ADR 0014): a client on a 390 px phone taps a service, taps a free time, types their details, reads the
/// SMS code from their "phone" (the recording sender) and types it, and lands on the confirmation — in under 60 seconds
/// of wall time. Each of the four screens is captured, passes axe and does not scroll sideways.
/// </summary>
public class PublicBookingE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const int Width = 390;
    private static readonly TimeSpan Gate = TimeSpan.FromSeconds(60);

    private static readonly (string Name, int Minutes, decimal Price)[] Services =
    [
        ("Cut & blow-dry", 45, 45m),
        ("Full head colour", 90, 85m),
        ("Beard trim", 20, 15m),
    ];

    [Fact]
    public async Task A_client_books_on_a_phone_in_under_60_seconds()
    {
        await app.ClearBookingDataAsync(Slug);
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", Services);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.UseDefaultSettingsAsync(tenantId);
        try
        {
            await BookAsync(suffixSeed: Random.Shared.Next(100_000, 999_999));
        }
        finally
        {
            // Other classes replace this salon's services and staff; the booking must not hold a foreign key on them.
            await app.ClearBookingDataAsync(Slug);
        }
    }

    private async Task BookAsync(int suffixSeed)
    {
        // Unique per run: the per-phone code limit is three an hour and earlier runs may have used the number.
        var suffix = suffixSeed.ToString(CultureInfo.InvariantCulture);
        var phoneE164 = "+447700" + suffix;

        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = Width, Height = 844 },
            IsMobile = true,
            HasTouch = true,
        });
        var page = await context.NewPageAsync();
        var interaction = new Stopwatch();
        var total = Stopwatch.StartNew();

        // Step 1: the service.
        interaction.Start();
        (await page.GotoAsync(app.TenantUrl(Slug) + "/book")).ShouldNotBeNull().Ok.ShouldBeTrue();
        (await page.GetByTestId("book-service").CountAsync()).ShouldBe(Services.Length);
        interaction.Stop();
        await CaptureAsync(page, "book-services-mobile-390.png");
        interaction.Start();
        await page.GetByTestId("book-service").First.TapAsync();
        await page.ReachAsync(url => url.Contains("/book/", StringComparison.Ordinal) && !url.EndsWith("/book", StringComparison.Ordinal));

        // Step 2: the first free time of the first day that has one ("anyone available" is selected by default).
        (await page.GetByTestId("service-name").TextContentAsync()).ShouldBe("Cut & blow-dry");
        (await page.GetByTestId("staff-any").GetAttributeAsync("aria-current")).ShouldBe("true");
        (await page.GetByTestId("day").CountAsync()).ShouldBe(14);
        var slots = page.GetByTestId("slot");
        (await slots.CountAsync()).ShouldBeGreaterThan(0, "the demo rota has free times within two weeks");
        var chosenTime = (await slots.First.TextContentAsync())!.Trim();
        interaction.Stop();
        await CaptureAsync(page, "book-slots-mobile-390.png");
        await CaptureDesktopAsync(page.Url, "book-slots-desktop-1280.png");
        interaction.Start();
        await slots.First.TapAsync();
        await page.ReachAsync("**/details?*");

        // Step 3: details.
        (await page.GetByTestId("summary-service").TextContentAsync()).ShouldNotBeNull().ShouldContain("Cut & blow-dry");
        (await page.GetByTestId("summary-staff").TextContentAsync()).ShouldNotBeNull().ShouldContain("Anyone available");
        (await page.GetByTestId("summary-when").TextContentAsync()).ShouldNotBeNull().ShouldContain(chosenTime);
        await page.GetByLabel("First name", new PageGetByLabelOptions { Exact = true }).FillAsync("Olivia");
        await page.GetByLabel("Last name", new PageGetByLabelOptions { Exact = true }).FillAsync("Brown");
        await page.GetByLabel("Mobile number", new PageGetByLabelOptions { Exact = true }).FillAsync("07700 " + suffix);
        await page.GetByTestId("marketing-consent").CheckAsync();
        interaction.Stop();
        await CaptureAsync(page, "book-details-mobile-390.png");
        interaction.Start();
        await page.GetByTestId("submit-details").TapAsync();
        await page.ReachAsync("**/book/verify/*");

        // Step 4: the code, read from the "phone".
        (await page.GetByTestId("code-sent-to").TextContentAsync()).ShouldNotBeNull().ShouldContain("+44•••••••" + suffix[^3..]);
        var code = app.Sms.LastCodeFor(phoneE164).ShouldNotBeNull("the code SMS went to the typed number");
        interaction.Stop();
        await CaptureAsync(page, "book-verify-mobile-390.png");
        interaction.Start();
        await page.GetByTestId("code").FillAsync(code);
        await page.GetByTestId("submit-code").TapAsync();
        await page.ReachAsync("**/book/done/*");
        (await page.GetByTestId("done-title").TextContentAsync()).ShouldBe("Your appointment is booked");
        interaction.Stop();
        total.Stop();

        (await page.GetByTestId("done-service").TextContentAsync()).ShouldBe("Cut & blow-dry");
        (await page.GetByTestId("done-when").TextContentAsync()).ShouldNotBeNull().ShouldContain(chosenTime);
        (await page.GetByTestId("done-salon").TextContentAsync()).ShouldBe("Studio 20");
        (await page.GetByTestId("cancel-link").GetAttributeAsync("href")).ShouldNotBeNull().ShouldStartWith("/book/cancel/");
        await CaptureAsync(page, "book-done-mobile-390.png");

        // A change of plan (ADR 0021): the link on the confirmation, one tap on another time, and the visit has moved.
        await page.GetByTestId("change-link").TapAsync();
        await page.ReachAsync("**/book/change/*");
        (await page.GetByTestId("change-when").TextContentAsync()).ShouldNotBeNull().ShouldContain(chosenTime);
        await CaptureAsync(page, "book-change-mobile-390.png");
        var newTime = (await page.GetByTestId("slot").First.TextContentAsync()).ShouldNotBeNull().Trim();
        newTime.ShouldNotBe(chosenTime, "the time the visit already has is not offered back");
        await page.GetByTestId("slot").First.TapAsync();
        (await page.GetByTestId("change-result").TextContentAsync()).ShouldNotBeNull().ShouldContain("Your appointment has been moved.");
        (await page.GetByTestId("change-when").TextContentAsync()).ShouldNotBeNull().ShouldContain(newTime);
        await CaptureAsync(page, "book-changed-mobile-390.png");

        // The same phone comes back: the details are already filled in from the device's own cookie, and can be cleared.
        await page.GetByTestId("book-another").TapAsync();
        await page.ReachAsync("**/book");
        await page.GetByTestId("book-service").First.TapAsync();
        await page.ReachAsync(url => url.Contains("/book/", StringComparison.Ordinal) && !url.EndsWith("/book", StringComparison.Ordinal));
        await page.GetByTestId("slot").First.TapAsync();
        await page.ReachAsync("**/details?*");
        (await page.GetByTestId("prefilled").TextContentAsync()).ShouldNotBeNull().ShouldContain("Welcome back, Olivia");
        (await page.GetByLabel("First name", new PageGetByLabelOptions { Exact = true }).InputValueAsync()).ShouldBe("Olivia");
        (await page.GetByLabel("Mobile number", new PageGetByLabelOptions { Exact = true }).InputValueAsync()).ShouldBe("07700 " + suffix);
        await CaptureAsync(page, "book-prefilled-mobile-390.png");
        await page.GetByTestId("not-you").TapAsync();
        await page.ReachAsync("**/details?*forget=true");
        (await page.GetByTestId("prefilled").CountAsync()).ShouldBe(0);
        (await page.GetByLabel("First name", new PageGetByLabelOptions { Exact = true }).InputValueAsync()).ShouldBe(string.Empty);

        interaction.Elapsed.ShouldBeLessThan(Gate, $"the client's taps and typing took {interaction.Elapsed.TotalSeconds:F1} s (whole test {total.Elapsed.TotalSeconds:F1} s including screenshots and axe)");
        await File.WriteAllTextAsync(
            Path.Combine(KestrelAppFixture.ScreenshotDirectory, "book-timing.txt"),
            $"interaction {interaction.Elapsed.TotalSeconds:F1} s; with screenshots and axe {total.Elapsed.TotalSeconds:F1} s");
    }

    /// <summary>The same step on a 1280 px desktop (goal 4 of the UX pass): a centred column of the same components, axe clean.</summary>
    private async Task CaptureDesktopAsync(string url, string fileName)
    {
        const int desktopWidth = 1280;
        await using var desktop = await app.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = desktopWidth, Height = 800 } });
        var page = await desktop.NewPageAsync();
        (await page.GotoAsync(url)).ShouldNotBeNull().Ok.ShouldBeTrue();
        await CaptureAsync(page, fileName, desktopWidth);
    }

    /// <summary>Full-page screenshot, no horizontal scroll (when the stylesheet is present), axe clean.</summary>
    private static async Task CaptureAsync(IPage page, string fileName, int width = Width)
    {
        if (await page.EvaluateAsync<bool>("() => Array.from(document.styleSheets).some(s => s.href && s.cssRules.length > 0)"))
        {
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(width, fileName);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, fileName), FullPage = true });
        var axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(fileName + ": " + string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {v.Help}")));
    }
}