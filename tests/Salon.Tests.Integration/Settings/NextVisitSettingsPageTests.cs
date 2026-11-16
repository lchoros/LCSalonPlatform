using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Salon.Core.Identity;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Services.Settings;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Settings;

/// <summary>
/// The "Next visit" settings page (ADR 0039): the owner sets how many weeks ahead the desk offers the next booking
/// after a payment. Owner only, four, six and eight weeks by default, saved per salon, in the salon's language.
/// </summary>
[Collection(SharedDatabase.Name)]
public class NextVisitSettingsPageTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string Path = "/admin/settings/next-visit";
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";

    private Tenant _studio = null!;
    private Tenant _polish = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_owner_sees_four_six_and_eight_weeks_by_default_and_the_page_is_in_the_settings_menu()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var html = await GetAsync(client, Path);

        html.ShouldContain("data-testid=\"next-visit-form\"");
        Input(html, "FirstWeeks").ShouldContain("value=\"4\"");
        Input(html, "SecondWeeks").ShouldContain("value=\"6\"");
        Input(html, "ThirdWeeks").ShouldContain("value=\"8\"");
        Offered(html).ShouldBe(["In 4 weeks", "In 6 weeks", "In 8 weeks"]);
        html.ShouldContain("href=\"/admin/settings/next-visit\" data-testid=\"settings-nav-next-visit\" aria-current=\"page\"");
        (await GetAsync(client, "/admin/settings/notifications")).ShouldContain("href=\"/admin/settings/next-visit\" data-testid=\"settings-nav-next-visit\"");
    }

    [Fact]
    public async Task Saving_stores_the_weeks_for_this_salon_only_and_confirms()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var response = await PostAsync(client, "3", "5", "12");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe(Path);
        var after = await GetAsync(client, Path);
        after.ShouldContain("The choices for the next visit have been saved.");
        Input(after, "FirstWeeks").ShouldContain("value=\"3\"");
        Input(after, "SecondWeeks").ShouldContain("value=\"5\"");
        Input(after, "ThirdWeeks").ShouldContain("value=\"12\"");
        Offered(after).ShouldBe(["In 3 weeks", "In 5 weeks", "In 12 weeks"]);

        var saved = await LoadAsync(_studio.Id);
        (saved.FirstWeeks, saved.SecondWeeks, saved.ThirdWeeks).ShouldBe((3, 5, 12));
        var other = await LoadAsync(_polish.Id);
        (other.FirstWeeks, other.SecondWeeks, other.ThirdWeeks).ShouldBe((4, 6, 8), "the other salon keeps its own choices");
    }

    [Fact]
    public async Task The_page_shows_what_the_desk_will_see_shortest_first_each_once_and_without_the_zeros()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        (await PostAsync(client, "8", "0", "1")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        Offered(await GetAsync(client, Path)).ShouldBe(["In 1 week", "In 8 weeks"]);

        (await PostAsync(client, "6", "6", "6")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        Offered(await GetAsync(client, Path)).ShouldBe(["In 6 weeks"]);
    }

    [Fact]
    public async Task Three_zeros_switch_the_offer_off_and_the_page_says_so()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var response = await PostAsync(client, "0", "0", "0");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var after = await GetAsync(client, Path);
        Offered(after).ShouldBeEmpty();
        after.ShouldContain("data-testid=\"nothing-offered\"");
        after.ShouldContain("No next visit is offered after a payment.");
        var saved = await LoadAsync(_studio.Id);
        (saved.FirstWeeks, saved.SecondWeeks, saved.ThirdWeeks).ShouldBe((0, 0, 0));
    }

    [Theory]
    [InlineData("", "6", "8", "Enter a number of weeks.")]
    [InlineData("4", "", "8", "Enter a number of weeks.")]
    [InlineData("4", "6", "53", "Weeks must be between 0 and 52.")]
    [InlineData("-1", "6", "8", "Weeks must be between 0 and 52.")]
    [InlineData("4", "six", "8", "Enter a number of weeks.")]
    public async Task Invalid_input_re_renders_with_the_message_keeps_what_was_typed_and_saves_nothing(string first, string second, string third, string message)
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var response = await PostAsync(client, first, second, third);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        html.ShouldContain(message);
        Input(html, "ThirdWeeks").ShouldContain($"value=\"{third}\"");
        var saved = await LoadAsync(_studio.Id);
        (saved.FirstWeeks, saved.SecondWeeks, saved.ThirdWeeks).ShouldBe((4, 6, 8));
    }

    [Fact]
    public async Task The_page_speaks_the_salon_language()
    {
        using var client = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);

        var html = await GetAsync(client, Path);

        html.ShouldContain("Następna wizyta");
        Offered(html).ShouldBe(["Za 4 tyg.", "Za 6 tyg.", "Za 8 tyg."]);
    }

    [Theory]
    [InlineData(Role.Manager)]
    [InlineData(Role.Receptionist)]
    public async Task Only_the_owner_may_open_or_post_the_page(Role role)
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner, "owner@studio-20.test");
        var token = IdentityTestClient.AntiForgeryToken(await GetAsync(owner, Path));
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, role, $"{role}@studio-20.test");

        var get = await client.GetAsync(new Uri(Path, UriKind.Relative));
        var post = await client.PostAsync(new Uri(Path, UriKind.Relative), Form("1", "2", "3", token));

        // A signed-in role without the permission is sent to the access-denied page (ADR 0009), for reads and writes alike.
        get.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        get.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        post.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        post.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        (await LoadAsync(_studio.Id)).FirstWeeks.ShouldBe(4);
    }

    [Fact]
    public async Task A_post_without_the_anti_forgery_token_is_refused()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var response = await client.PostAsync(new Uri(Path, UriKind.Relative), Form("1", "2", "3", null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await LoadAsync(_studio.Id)).FirstWeeks.ShouldBe(4);
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string first, string second, string third)
    {
        var token = IdentityTestClient.AntiForgeryToken(await GetAsync(client, Path));
        return await client.PostAsync(new Uri(Path, UriKind.Relative), Form(first, second, third, token));
    }

    private static FormUrlEncodedContent Form(string first, string second, string third, string? token)
    {
        var fields = new Dictionary<string, string>
        {
            ["FirstWeeks"] = first,
            ["SecondWeeks"] = second,
            ["ThirdWeeks"] = third,
        };
        if (token is not null)
        {
            fields["__RequestVerificationToken"] = token;
        }

        return new FormUrlEncodedContent(fields);
    }

    /// <summary>The whole input tag with this name, so a test can look at its attributes.</summary>
    private static string Input(string html, string name)
    {
        var start = html.IndexOf($"<input id=\"{name}\"", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, $"no input named {name}");
        return html[start..(html.IndexOf('>', start) + 1)];
    }

    /// <summary>The choices the page says the desk will see, in the order shown.</summary>
    private static string[] Offered(string html)
    {
        const string marker = "data-testid=\"offered-choice\">";
        var found = new List<string>();
        for (var at = html.IndexOf(marker, StringComparison.Ordinal); at >= 0; at = html.IndexOf(marker, at + 1, StringComparison.Ordinal))
        {
            var start = at + marker.Length;
            found.Add(html[start..html.IndexOf('<', start)].Trim());
        }

        return [.. found];
    }

    private async Task<RebookSettings> LoadAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<ISettingService>().LoadAsync<RebookSettings>();
    }
}