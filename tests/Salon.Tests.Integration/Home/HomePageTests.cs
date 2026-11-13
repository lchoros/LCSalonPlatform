using System.Net;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Home;

[Collection(SharedDatabase.Name)]
public class HomePageTests(SalonWebApplicationFactory factory)
{
    [Fact]
    public async Task Home_page_renders()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("Salon Platform");
        html.ShouldContain("<html lang=\"");
    }

    [Fact]
    public async Task Unknown_route_returns_404()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/does-not-exist", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}