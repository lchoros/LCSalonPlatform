using System.Net;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NodaTime;

using Salon.Core.Catalog;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Booking;

/// <summary>
/// The public price list over HTTP: only the tenant's active services in SortOrder, prices and durations formatted for
/// the salon's culture and currency, the empty state, the home-page link, and no list on the bare platform domain.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class ListServicesTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private Tenant _studio = null!;
    private Tenant _polish = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        await factory.CreateTenantAsync("empty-salon", name: "Empty Salon");

        await AddServiceAsync(_studio.Id, "Colour", 90, 85m, "GBP", sortOrder: 2, description: "Full head.");
        await AddServiceAsync(_studio.Id, "Cut & blow-dry", 45, 45m, "GBP", sortOrder: 1);
        await AddServiceAsync(_studio.Id, "Retired perm", 120, 60m, "GBP", sortOrder: 0, isActive: false);
        await AddServiceAsync(_polish.Id, "Strzyżenie damskie", 45, 45m, "PLN", sortOrder: 1);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Shows_only_the_salons_active_services_in_sort_order_with_British_prices()
    {
        var html = await GetHtmlAsync("studio-20.localhost");

        html.ShouldContain("data-testid=\"salon-name\">Studio 20<");
        html.ShouldContain("<html lang=\"en-GB\"");
        CardNames(html).ShouldBe(["Cut &amp; blow-dry", "Colour"]);
        html.ShouldNotContain("Retired perm");
        html.ShouldNotContain("Strzyżenie damskie");
        html.ShouldContain("£45.00");
        html.ShouldContain("£85.00");
        html.ShouldContain("45 min");
        html.ShouldContain("90 min");
        html.ShouldContain("Full head.");
        html.ShouldNotContain("data-testid=\"empty-state\"");
    }

    [Fact]
    public async Task A_Polish_salon_shows_Polish_labels_and_zloty_prices()
    {
        var html = await GetHtmlAsync("salon-pl.localhost");

        html.ShouldContain("<html lang=\"pl-PL\"");
        html.ShouldContain("Usługi");
        html.ShouldContain("Nasze usługi, czas trwania i ceny.");
        CardNames(html).ShouldBe(["Strzyżenie damskie"]);
        // The encoder writes the no-break space as &#xA0;; the reader sees "45,00 zł".
        WebUtility.HtmlDecode(html).ShouldContain("45,00\u00A0zł");
        html.ShouldContain("45 min");
    }

    [Fact]
    public async Task A_salon_without_services_shows_the_empty_state()
    {
        var html = await GetHtmlAsync("empty-salon.localhost");

        html.ShouldContain("data-testid=\"empty-state\"");
        html.ShouldContain("This salon has not added any services yet.");
        html.ShouldNotContain("data-testid=\"service-card\"");
    }

    [Fact]
    public async Task The_salon_home_page_links_to_the_list()
    {
        using var client = ClientFor("studio-20.localhost");

        var response = await client.GetAsync(new Uri("/", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("href=\"/services\"");
        html.ShouldContain("View services");
    }

    [Fact]
    public async Task The_bare_platform_domain_has_no_list()
    {
        using var client = ClientFor("localhost");

        (await client.GetAsync(new Uri("/services", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Without_categories_the_list_is_flat_and_with_them_it_is_grouped_with_other_services_last()
    {
        var flat = await GetHtmlAsync("studio-20.localhost");
        flat.ShouldNotContain("data-testid=\"group-heading\"");
        flat.ShouldContain("<h2 class=\"text-lg font-semibold leading-snug\">Cut &amp; blow-dry</h2>");

        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            var hair = new ServiceCategory { Name = "Hair", SortOrder = 1 };
            db.ServiceCategories.Add(hair);
            (await db.Services.SingleAsync(s => s.Name == "Colour")).CategoryId = hair.Id;
            await db.SaveChangesAsync();
        }

        var grouped = await GetHtmlAsync("studio-20.localhost");

        GroupHeadings(grouped).ShouldBe(["Hair", "Other services"]);
        CardNames(grouped).ShouldBe(["Colour", "Cut &amp; blow-dry"]);
        grouped.ShouldContain("<h3 class=\"text-lg font-semibold leading-snug\">Colour</h3>");
        grouped.ShouldNotContain("Retired perm");

        var polish = await GetHtmlAsync("salon-pl.localhost");
        // Another salon's categories do not leak into this list.
        polish.ShouldNotContain("data-testid=\"group-heading\"");
    }

    private static List<string> GroupHeadings(string html)
        => GroupHeadingPattern().Matches(html).Select(m => m.Groups["name"].Value).ToList();

    private static List<string> CardNames(string html)
        => CardNamePattern().Matches(html).Select(m => m.Groups["name"].Value).ToList();

    private async Task<string> GetHtmlAsync(string host)
    {
        using var client = ClientFor(host);
        var response = await client.GetAsync(new Uri("/services", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private HttpClient ClientFor(string host)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        return client;
    }

    private async Task AddServiceAsync(Guid tenantId, string name, int minutes, decimal price, string currency, int sortOrder, string? description = null, bool isActive = true)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        db.Services.Add(new Service
        {
            Name = name,
            Description = description,
            Duration = Duration.FromMinutes(minutes),
            Price = price,
            Currency = currency,
            SortOrder = sortOrder,
            IsActive = isActive,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>The heading inside each service card, in document order (h2 on a flat list, h3 under a category heading).</summary>
    [GeneratedRegex("data-testid=\"service-card\">\\s*<div[^>]*>\\s*<h[23][^>]*>(?<name>[^<]+)</h[23]>")]
    private static partial Regex CardNamePattern();

    /// <summary>The text of each category heading, in document order.</summary>
    [GeneratedRegex("data-testid=\"group-heading\">(?<name>[^<]+)</h2>")]
    private static partial Regex GroupHeadingPattern();
}