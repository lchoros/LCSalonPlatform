using System.Net;

using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Localization;

/// <summary>
/// Request culture order (ADR 0006): ?culture → cookie → tenant culture → Accept-Language → en-GB. Polish text is
/// asserted on the raw response, which the widened HTML encoder serves as UTF-8 rather than numeric entities.
/// </summary>
[Collection(SharedDatabase.Name)]
public class RequestCultureTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string PolishBookingText = "Rezerwacje online ruszą tutaj w M1.";
    private const string EnglishBookingText = "Online booking opens here in M1.";
    private const string PolishTagline = "Rezerwacje online i zarządzanie salonem dla Wielkiej Brytanii i Polski.";
    private const string EnglishTagline = "Booking and salon management for the UK and Poland.";

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        await factory.CreateTenantAsync("salon-pl", culture: "pl-PL", currency: "PLN", name: "Salon PL");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_Polish_tenant_renders_in_Polish()
    {
        using var client = ClientFor("salon-pl.localhost");

        var html = await GetHtmlAsync(client, "/");

        html.ShouldContain("<html lang=\"pl-PL\"");
        html.ShouldContain(PolishBookingText);
        html.ShouldNotContain(EnglishBookingText);
    }

    [Fact]
    public async Task The_culture_query_string_wins_over_the_tenant_culture()
    {
        using var client = ClientFor("salon-pl.localhost");

        var html = await GetHtmlAsync(client, "/?culture=en-GB");

        html.ShouldContain("<html lang=\"en-GB\"");
        html.ShouldContain(EnglishBookingText);
    }

    [Fact]
    public async Task An_unsupported_culture_query_string_falls_back_to_the_tenant_culture()
    {
        using var client = ClientFor("salon-pl.localhost");

        var html = await GetHtmlAsync(client, "/?culture=de-DE");

        html.ShouldContain("<html lang=\"pl-PL\"");
        html.ShouldContain(PolishBookingText);
    }

    [Fact]
    public async Task The_platform_page_honours_a_neutral_Polish_Accept_Language_header()
    {
        using var client = ClientFor("localhost");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("pl");

        var html = await GetHtmlAsync(client, "/");

        html.ShouldContain("<html lang=\"pl-PL\"");
        html.ShouldContain(PolishTagline);
    }

    [Fact]
    public async Task The_platform_page_defaults_to_British_English()
    {
        using var client = ClientFor("localhost");

        var html = await GetHtmlAsync(client, "/");

        html.ShouldContain("<html lang=\"en-GB\"");
        html.ShouldContain(EnglishTagline);
        html.ShouldNotContain(PolishTagline);
    }

    [Fact]
    public async Task An_unsupported_Accept_Language_header_defaults_to_British_English()
    {
        using var client = ClientFor("localhost");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("de-DE");

        var html = await GetHtmlAsync(client, "/");

        html.ShouldContain("<html lang=\"en-GB\"");
    }

    private static async Task<string> GetHtmlAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private HttpClient ClientFor(string host)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        return client;
    }
}