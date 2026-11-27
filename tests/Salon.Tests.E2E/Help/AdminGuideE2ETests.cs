using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using NodaTime;
using Salon.Core.Identity;
using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Help;

/// <summary>
/// The guide for the salon's own people (ADR 0099): reached from the foot of the menu on a phone, one section for each
/// job with its steps, a picture that loads and a link to the page itself; axe clean, no sideways scroll.
/// <para>
/// The second test is not a check but the guide's camera: with <c>GUIDE_SHOTS=1</c> in the environment it walks the
/// admin of a made-up salon on a desk-sized screen and writes the guide's pictures into
/// <c>src/Presentation/Salon.Web/wwwroot/img/guide</c>. Run it when a screen the guide shows has changed, look at the
/// pictures, and commit them. Without the variable it does nothing, so an ordinary run never rewrites committed files.
/// </para>
/// </summary>
public class AdminGuideE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";
    private const string Email = "guide-owner@studio20.example";
    private const string Password = "Guide-walk-2026!";

    private static readonly (string Name, int Minutes, decimal Price)[] Services =
    [
        ("Cut & blow-dry", 45, 45m),
        ("Full head colour", 90, 85m),
        ("Beard trim", 20, 15m),
    ];

    /// <summary>The guide's sections and the page each picture is taken of (the controller owns the list; this mirrors it).</summary>
    private static readonly (string Key, string Path)[] Shots =
    [
        ("day", "/admin/appointments?date={0}"),
        ("book", "/admin/appointments/new?date={0}"),
        ("services", "/admin/services"),
        ("staff", "/admin/staff"),
        ("online", "/book"),
    ];

    private static string PictureDirectory
        => Path.GetFullPath(Path.Combine(KestrelAppFixture.ScreenshotDirectory, "..", "..", "..", "src", "Presentation", "Salon.Web", "wwwroot", "img", "guide"));

    [Fact]
    public async Task The_owner_opens_the_guide_from_the_foot_of_the_menu_on_a_phone()
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", Services);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 390, Height = 844 } });
        var page = await context.NewPageAsync();
        await SignInAsync(page, "/admin/appointments");

        await page.OpenNavAsync("nav-guide");
        await page.ReachAsync("**/admin/guide");

        (await page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Level = 1 }).TextContentAsync()).ShouldBe("Guide");
        (await page.GetByTestId("guide-section").CountAsync()).ShouldBe(Shots.Length);
        (await page.GetByTestId("guide-link").AllTextContentsAsync()).ShouldContain("Today's appointments");
        (await page.GetByTestId("guide-section").First.Locator("ol > li").CountAsync()).ShouldBe(5);
        (await page.ContentAsync()).ShouldNotContain("Guide.", Case.Sensitive, "every word is there, not its key");

        // Every picture the guide points at exists and is a picture.
        var pictures = await page.EvaluateAsync<string[]>("async () => Promise.all([...document.querySelectorAll('.guide-shot img')].map(async img => { const answer = await fetch(img.src); return answer.status + ' ' + answer.headers.get('content-type'); }))");
        pictures.Length.ShouldBe(Shots.Length);
        pictures.ShouldAllBe(answer => answer == "200 image/jpeg");

        if (await page.EvaluateAsync<bool>("() => Array.from(document.styleSheets).some(s => s.href && s.cssRules.length > 0)"))
        {
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).ShouldBeLessThanOrEqualTo(390);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, "guide-mobile-390.png"), FullPage = true });
        var axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {v.Help}")));

        // "Open this page" leads to the page the section is about.
        await page.GetByTestId("guide-open").First.ClickAsync();
        await page.ReachAsync("**/admin/appointments");
    }

    [Fact]
    public async Task The_guides_pictures_are_taken_when_asked_for()
    {
        if (Environment.GetEnvironmentVariable("GUIDE_SHOTS") != "1")
        {
            return;
        }

        await app.ClearBookingDataAsync(Slug);
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", Services);
        await app.EnsureDemoStaffAsync(tenantId);
        await app.UseDefaultSettingsAsync(tenantId);
        await app.EnsureUserAsync(tenantId, Email, "Studio 20 Owner", Role.Owner, Password);
        // Made-up clients with visits tomorrow morning, so the diary in the picture is a working day whatever the
        // hour the pictures are taken at.
        var london = DateTimeZoneProviders.Tzdb["Europe/London"];
        var now = SystemClock.Instance.GetCurrentInstant();
        var tomorrow = now.InZone(london).Date.PlusDays(1);
        Duration Until(int hour, int minute) => tomorrow.At(new LocalTime(hour, minute)).InZoneLeniently(london).ToInstant() - now;
        await app.AddVisitAsync(tenantId, "Olivia", "Brown", Until(10, 0), deposit: 0m);
        await app.AddVisitAsync(tenantId, "James", "Carter", Until(11, 30), deposit: 0m);
        await app.AddVisitAsync(tenantId, "Mia", "Patel", Until(14, 0), deposit: 0m);
        try
        {
            await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1200, Height = 800 } });
            var page = await context.NewPageAsync();
            await SignInAsync(page, "/admin/appointments");
            Directory.CreateDirectory(PictureDirectory);
            foreach (var (key, path) in Shots)
            {
                var address = string.Format(System.Globalization.CultureInfo.InvariantCulture, path, NodaTime.Text.LocalDatePattern.Iso.Format(tomorrow));
                (await page.GotoAsync(app.TenantUrl(Slug) + address)).ShouldNotBeNull().Ok.ShouldBeTrue(address);
                await page.WaitForFunctionAsync("() => document.readyState === 'complete' && !document.getAnimations().some(a => a.playState === 'running')");
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(PictureDirectory, key + ".jpg"), Type = ScreenshotType.Jpeg, Quality = 72 });
            }
        }
        finally
        {
            await app.ClearBookingDataAsync(Slug);
        }
    }

    private async Task SignInAsync(IPage page, string path)
    {
        await page.GotoAsync(app.TenantUrl(Slug) + path);
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.ReachAsync("**" + path);
    }
}