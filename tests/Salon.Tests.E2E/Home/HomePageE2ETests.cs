using Deque.AxeCore.Playwright;

using Microsoft.Playwright;

using Salon.Tests.E2E.Infrastructure;

namespace Salon.Tests.E2E.Home;

public class HomePageE2ETests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    [Theory]
    [InlineData("mobile", 390, 844)]
    [InlineData("desktop", 1280, 800)]
    public async Task Home_page_loads_and_passes_axe(string label, int width, int height)
    {
        await using var context = await app.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync(app.BaseUrl + "/");

        response.ShouldNotBeNull();
        response.Ok.ShouldBeTrue();
        (await page.TitleAsync()).ShouldContain("Salon Platform");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(KestrelAppFixture.ScreenshotDirectory, $"home-{label}-{width}.png"),
            FullPage = true,
        });

        var axe = await page.RunAxe();
        axe.Violations.ShouldBeEmpty(string.Join("; ", axe.Violations.Select(v => $"{v.Id}: {v.Help}")));
    }
}