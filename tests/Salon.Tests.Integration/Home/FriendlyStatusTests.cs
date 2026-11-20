using System.Net;
using System.Net.Http.Headers;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Home;

/// <summary>
/// The friendly status page (ADR 0087): an empty 404 or 403 becomes a sentence and a way on for a person in a
/// browser, the status code itself is kept, and a caller that did not ask for HTML still gets the bare status.
/// </summary>
[Collection(SharedDatabase.Name)]
public class FriendlyStatusTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string Host = "studio-20.localhost";

    private Tenant _studio = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_missing_page_tells_a_person_so_in_a_sentence_and_keeps_its_404()
    {
        using var client = factory.ClientFor(Host);

        using var response = await GetHtmlAsync(client, "/no-such-page");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        html.ShouldContain("data-testid=\"status-page\"");
        html.ShouldContain("We can't find that page");
        html.ShouldContain("data-testid=\"status-home\"");
        html.ShouldNotContain("data-testid=\"status-admin\"", customMessage: "a client is not offered the diary");
    }

    [Fact]
    public async Task A_caller_that_did_not_ask_for_html_keeps_the_bare_status()
    {
        using var client = factory.ClientFor(Host);
        client.DefaultRequestHeaders.Accept.Clear();

        // A script's fetch asks for data, not a page.
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/no-such-page", UriKind.Relative));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Signed_in_staff_are_offered_the_way_back_to_the_diary()
    {
        using var client = await factory.SignedInClientAsync(Host, _studio.Id, Role.Owner);

        using var response = await GetHtmlAsync(client, $"/admin/clients/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, "another salon's or a missing client is a 404 as before");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        html.ShouldContain("We can't find that page");
        html.ShouldContain("data-testid=\"status-admin\"");
    }

    private static async Task<HttpResponseMessage> GetHtmlAsync(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        return await client.SendAsync(request);
    }
}