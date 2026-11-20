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
    [InlineData("/admin", "nav-dashboard")]
    [InlineData("/admin/services", "nav-services")]
    [InlineData("/admin/services/new", "nav-services")]
    [InlineData("/admin/clients", "nav-clients")]
    [InlineData("/admin/clients/new", "nav-clients")]
    [InlineData("/admin/appointments", "nav-appointments")]
    [InlineData("/admin/appointments/calendar", "nav-calendar")]
    [InlineData("/admin/payments/cash-up", "nav-cash-up")]
    [InlineData("/admin/products", "nav-products")]
    [InlineData("/admin/products/new", "nav-products")]
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
    public async Task A_public_page_shows_no_shell_to_signed_in_staff_only_a_way_back_to_the_desk()
    {
        using var client = await factory.SignedInClientAsync("studio-20.localhost", _studio.Id, Role.Owner);

        var html = await GetAsync(client, "/");

        // The dashboard shell belongs to /admin only (ADR 0080): staff see the client's page as the client does.
        html.ShouldNotContain("data-testid=\"admin-nav\"");
        html.ShouldNotContain("data-testid=\"bottom-bar\"");
        html.ShouldContain("data-testid=\"back-to-admin\"");
        CurrentLinks(html).ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_page_carries_the_brand_line_and_the_menu_links_share_one_class()
    {
        using var client = await factory.SignedInClientAsync("studio-20.localhost", _studio.Id, Role.Owner);

        var html = await GetAsync(client, "/admin/staff");

        html.ShouldContain("data-testid=\"sidebar\"", customMessage: "signed-in pages live in the dashboard shell (ADR 0080)");
        html.ShouldContain("class=\"brand-line ");
        MenuLinkPattern().Count(html).ShouldBe(10, "an owner sees the dashboard and nine areas, products among them (ADR 0036, ADR 0084)");
        html.ShouldContain("<h1 class=\"page-title\">");
    }

    [Fact]
    public async Task The_dashboard_is_the_first_place_in_the_menu_and_the_bottom_bar_and_the_brand_leads_to_it()
    {
        using var client = await factory.SignedInClientAsync("studio-20.localhost", _studio.Id, Role.Owner);

        var html = await GetAsync(client, "/admin/staff");

        // Today at a glance is where the desk starts (ADR 0084): first in the sidebar, first under the thumb.
        MenuLinkPattern().Match(html).Value.ShouldContain("data-testid=\"nav-dashboard\"");
        html.ShouldContain("<a href=\"/admin\" data-testid=\"nav-dashboard\" class=\"nav-link\">Dashboard</a>");
        html.ShouldContain("href=\"/admin\" data-testid=\"brand-link\"");
        html.IndexOf("data-testid=\"bottom-dashboard\"", StringComparison.Ordinal).ShouldBeGreaterThan(0);
        html.IndexOf("data-testid=\"bottom-dashboard\"", StringComparison.Ordinal)
            .ShouldBeLessThan(html.IndexOf("data-testid=\"bottom-appointments\"", StringComparison.Ordinal));

        // On the dashboard itself both links say so; one address deeper neither does.
        var dashboard = await GetAsync(client, "/admin");
        dashboard.ShouldContain("data-testid=\"nav-dashboard\" aria-current=\"true\" class=\"nav-link\"");
        dashboard.ShouldContain("aria-current=\"true\" class=\"bottom-link\" data-testid=\"bottom-dashboard\"");
        html.ShouldNotContain("aria-current=\"true\" class=\"bottom-link\" data-testid=\"bottom-dashboard\"");
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