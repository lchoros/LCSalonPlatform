using System.Net;
using System.Text.RegularExpressions;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Home;

/// <summary>
/// The admin menu marks the area a page belongs to (ADR 0033): exactly one link carries <c>aria-current="true"</c>, the
/// calendar and the day list are told apart although one address lies under the other, a page deeper in an area keeps
/// its area marked, and a page outside every area marks none.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class AdminMenuTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private Tenant _studio = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("/admin/services", "nav-services")]
    [InlineData("/admin/services/new", "nav-services")]
    [InlineData("/admin/clients", "nav-clients")]
    [InlineData("/admin/clients/new", "nav-clients")]
    [InlineData("/admin/appointments", "nav-appointments")]
    [InlineData("/admin/appointments/calendar", "nav-calendar")]
    [InlineData("/admin/payments/cash-up", "nav-cash-up")]
    [InlineData("/admin/reports", "nav-reports")]
    [InlineData("/admin/staff", "nav-staff")]
    [InlineData("/admin/settings/salon-details", "nav-settings")]
    [InlineData("/admin/settings/messages", "nav-settings")]
    public async Task The_menu_marks_the_area_of_the_page_and_no_other(string path, string expected)
    {
        using var client = await factory.SignedInClientAsync("studio-20.localhost", _studio.Id, Role.Owner);

        var html = await GetAsync(client, path);

        CurrentLinks(html).ShouldBe([expected], $"{path} belongs to {expected}");
    }

    [Fact]
    public async Task A_page_outside_every_area_marks_no_link()
    {
        using var client = await factory.SignedInClientAsync("studio-20.localhost", _studio.Id, Role.Owner);

        var html = await GetAsync(client, "/");

        html.ShouldContain("data-testid=\"admin-nav\"");
        CurrentLinks(html).ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_page_carries_the_brand_line_and_the_menu_links_share_one_class()
    {
        using var client = await factory.SignedInClientAsync("studio-20.localhost", _studio.Id, Role.Owner);

        var html = await GetAsync(client, "/admin/staff");

        html.ShouldContain("<header class=\"brand-line ");
        MenuLinkPattern().Count(html).ShouldBe(8, "an owner sees eight areas");
        html.ShouldContain("<h1 class=\"page-title\">");
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return await response.Content.ReadAsStringAsync();
    }

    private static string[] CurrentLinks(string html)
        => [.. CurrentLinkPattern().Matches(html).Select(m => m.Groups[1].Value)];

    [GeneratedRegex("data-testid=\"(nav-[a-z-]+)\" aria-current=\"true\" class=\"nav-link\"")]
    private static partial Regex CurrentLinkPattern();

    [GeneratedRegex("data-testid=\"nav-[a-z-]+\"(?: aria-current=\"true\")? class=\"nav-link\"")]
    private static partial Regex MenuLinkPattern();
}