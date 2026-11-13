using System.Net;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Tenancy;

[Collection(SharedDatabase.Name)]
public class TenantResolutionTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        await factory.CreateTenantAsync("studio-20", name: "Studio 20");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Salon_subdomain_shows_the_salon_page()
    {
        using var client = ClientFor("studio-20.localhost");

        var response = await client.GetAsync(new Uri("/", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("data-testid=\"tenant-name\"");
        html.ShouldContain("Studio 20");
    }

    [Fact]
    public async Task Unknown_subdomain_is_not_found()
    {
        using var client = ClientFor("someone-else.localhost");

        var response = await client.GetAsync(new Uri("/", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Base_domain_shows_the_platform_page()
    {
        using var client = ClientFor("localhost");

        var response = await client.GetAsync(new Uri("/", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("data-testid=\"tenant-name\"");
    }

    [Fact]
    public async Task A_subdomain_that_was_unknown_resolves_as_soon_as_the_tenant_exists()
    {
        using var client = ClientFor("new-salon.localhost");
        (await client.GetAsync(new Uri("/", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await factory.CreateTenantAsync("new-salon", name: "New Salon");

        var response = await client.GetAsync(new Uri("/", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("New Salon");
    }

    [Fact]
    public async Task Subdomain_lookup_is_case_insensitive()
    {
        using var client = ClientFor("STUDIO-20.localhost");

        (await client.GetAsync(new Uri("/", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private HttpClient ClientFor(string host)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        return client;
    }
}