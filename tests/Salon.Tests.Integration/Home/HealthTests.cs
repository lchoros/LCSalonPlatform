using System.Net;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Home;

/// <summary>
/// /health is what an outside monitor asks (ADR 0093): it answers without a login, on a salon's host and on the bare
/// platform host alike, and says "Healthy" only with the database reachable.
/// </summary>
[Collection(SharedDatabase.Name)]
public class HealthTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        await factory.CreateTenantAsync("studio-20", name: "Studio 20");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("studio-20.localhost")]
    [InlineData("localhost")]
    public async Task Health_answers_anyone_and_says_healthy_while_the_database_is_reachable(string host)
    {
        using var client = factory.ClientFor(host);

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }
}