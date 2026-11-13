using System.Net;

using Microsoft.Extensions.DependencyInjection;

using Salon.Core.Identity;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Services.Settings;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Settings;

/// <summary>The cancellation policy page (ADR 0022): owner only, "no policy" by default, saved per salon, rules in the salon's language.</summary>
[Collection(SharedDatabase.Name)]
public class CancellationPolicyPageTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string Path = "/admin/settings/cancellation-policy";
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
    public async Task The_owner_sees_no_policy_by_default_and_both_settings_pages_link_to_each_other()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var html = await GetAsync(client, Path);

        html.ShouldContain("data-testid=\"cancellation-policy-form\"");
        Input(html, "FreeChangeHours").ShouldContain("value=\"0\"");
        Input(html, "LateCancellationFeePercent").ShouldContain("value=\"0\"");
        Input(html, "NoShowFeePercent").ShouldContain("value=\"0\"");
        html.ShouldContain("Nothing is charged automatically.");
        html.ShouldContain("href=\"/admin/settings/notifications\" data-testid=\"settings-nav-notifications\"");
        html.ShouldContain("href=\"/admin/settings/cancellation-policy\" data-testid=\"settings-nav-cancellation-policy\" aria-current=\"page\"");
        (await GetAsync(client, "/admin/settings/notifications")).ShouldContain("href=\"/admin/settings/cancellation-policy\" data-testid=\"settings-nav-cancellation-policy\"");
    }

    [Fact]
    public async Task Saving_stores_the_policy_for_this_salon_only_and_confirms()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var response = await PostAsync(client, "24", "50", "100");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe(Path);
        var after = await GetAsync(client, Path);
        after.ShouldContain("The cancellation policy has been saved.");
        Input(after, "FreeChangeHours").ShouldContain("value=\"24\"");
        Input(after, "LateCancellationFeePercent").ShouldContain("value=\"50\"");
        Input(after, "NoShowFeePercent").ShouldContain("value=\"100\"");

        var saved = await LoadAsync(_studio.Id);
        saved.FreeChangeHours.ShouldBe(24);
        saved.LateCancellationFeePercent.ShouldBe(50m);
        saved.NoShowFeePercent.ShouldBe(100m);
        var other = await LoadAsync(_polish.Id);
        other.FreeChangeHours.ShouldBe(0, "the other salon keeps having no policy");
        other.NoShowFeePercent.ShouldBe(0m);
    }

    [Theory]
    [InlineData("", "0", "0", "Enter a number of hours.")]
    [InlineData("169", "0", "0", "Hours must be between 0 and 168.")]
    [InlineData("-1", "0", "0", "Hours must be between 0 and 168.")]
    [InlineData("24", "", "0", "Enter a percentage.")]
    [InlineData("24", "101", "0", "The percentage must be between 0 and 100.")]
    [InlineData("24", "50", "150", "The percentage must be between 0 and 100.")]
    [InlineData("0", "50", "0", "A late cancellation fee needs a notice period: set the hours above 0.")]
    public async Task Invalid_input_re_renders_with_the_message_and_saves_nothing(string hours, string late, string noShow, string message)
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var response = await PostAsync(client, hours, late, noShow);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()).ShouldContain(message);
        var saved = await LoadAsync(_studio.Id);
        saved.FreeChangeHours.ShouldBe(0);
        saved.LateCancellationFeePercent.ShouldBe(0m);
        saved.NoShowFeePercent.ShouldBe(0m);
    }

    [Fact]
    public async Task A_no_show_fee_alone_needs_no_notice_period()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var response = await PostAsync(client, "0", "0", "50");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await LoadAsync(_studio.Id)).NoShowFeePercent.ShouldBe(50m);
    }

    [Fact]
    public async Task The_page_speaks_the_salon_language()
    {
        using var client = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);

        var html = await GetAsync(client, Path);

        html.ShouldContain("Zadatki i odwołania");
        html.ShouldContain("Opłata za nieobecność (% ceny)");
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
        var post = await client.PostAsync(new Uri(Path, UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FreeChangeHours"] = "24",
            ["LateCancellationFeePercent"] = "50",
            ["NoShowFeePercent"] = "100",
            ["__RequestVerificationToken"] = token,
        }));

        // A signed-in role without the permission is sent to the access-denied page (ADR 0009), for reads and writes alike.
        get.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        get.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        post.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        post.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        (await LoadAsync(_studio.Id)).FreeChangeHours.ShouldBe(0);
    }

    [Fact]
    public async Task A_post_without_the_anti_forgery_token_is_refused()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var response = await client.PostAsync(new Uri(Path, UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FreeChangeHours"] = "24",
            ["LateCancellationFeePercent"] = "50",
            ["NoShowFeePercent"] = "100",
        }));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await LoadAsync(_studio.Id)).FreeChangeHours.ShouldBe(0);
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string hours, string late, string noShow)
    {
        var token = IdentityTestClient.AntiForgeryToken(await GetAsync(client, Path));
        return await client.PostAsync(new Uri(Path, UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FreeChangeHours"] = hours,
            ["LateCancellationFeePercent"] = late,
            ["NoShowFeePercent"] = noShow,
            ["__RequestVerificationToken"] = token,
        }));
    }

    /// <summary>The whole input tag with this name, so a test can look at its attributes.</summary>
    private static string Input(string html, string name)
    {
        var start = html.IndexOf($"<input id=\"{name}\"", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, $"no input named {name}");
        return html[start..(html.IndexOf('>', start) + 1)];
    }

    private async Task<CancellationPolicySettings> LoadAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<ISettingService>().LoadAsync<CancellationPolicySettings>();
    }
}