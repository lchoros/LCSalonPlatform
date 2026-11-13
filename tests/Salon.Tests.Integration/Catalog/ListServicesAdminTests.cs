using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Catalog;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Catalog;

/// <summary>
/// The admin catalogue over HTTP as a signed-in Owner (ADR 0013): every service grouped by category with the uncategorised
/// ones last, timing and price per card, inactive services greyed and badged, Polish labels on a Polish salon, the empty
/// state, nothing of another salon, and the gates (Catalog.Manage, sign-in, tenant).
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class ListServicesAdminTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string ListPath = "/admin/services";

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _cutId;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        await factory.CreateTenantAsync("empty-salon", name: "Empty Salon");

        var nails = await AddCategoryAsync(_studio.Id, "Nails", sortOrder: 2);
        var hair = await AddCategoryAsync(_studio.Id, "Hair", sortOrder: 1);
        await AddServiceAsync(_studio.Id, "Balayage", 150, 140m, "GBP", sortOrder: 2, categoryId: hair, buffer: 15, processing: 45, processingStartsAfter: 45);
        _cutId = await AddServiceAsync(_studio.Id, "Cut & blow-dry", 45, 45m, "GBP", sortOrder: 1, categoryId: hair, buffer: 5);
        await AddServiceAsync(_studio.Id, "Manicure", 45, 28m, "GBP", sortOrder: 3, categoryId: nails);
        await AddServiceAsync(_studio.Id, "Retired perm", 120, 60m, "GBP", sortOrder: 0, isActive: false);
        await AddServiceAsync(_polish.Id, "Strzyżenie damskie", 45, 45m, "PLN", sortOrder: 1);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Groups_every_service_by_category_with_the_uncategorised_ones_last()
    {
        using var client = await factory.SignedInClientAsync("studio-20.localhost", _studio.Id, Role.Owner);

        var html = await GetListAsync(client);

        html.ShouldContain("<html lang=\"en-GB\"");
        GroupHeadings(html).ShouldBe(["Hair", "Nails", "Uncategorised"]);
        CardNames(html).ShouldBe(["Cut &amp; blow-dry", "Balayage", "Manicure", "Retired perm"]);
        html.ShouldContain("£140.00");
        html.ShouldContain("45 min");
        html.ShouldContain("5 min buffer");
        html.ShouldContain("15 min buffer");
        html.ShouldContain("45 min processing");
        html.ShouldContain($"href=\"/admin/services/{_cutId}\"");
        html.ShouldContain("href=\"/admin/services/new\"");
        html.ShouldContain("href=\"/admin/services/categories\"");
        html.ShouldContain("data-testid=\"nav-services\"");
        html.ShouldNotContain("data-testid=\"empty-state\"");
        html.ShouldNotContain("Strzyżenie damskie");
    }

    [Fact]
    public async Task An_inactive_service_is_greyed_and_badged_and_a_service_without_processing_shows_none()
    {
        using var client = await factory.SignedInClientAsync("studio-20.localhost", _studio.Id, Role.Owner);

        var html = await GetListAsync(client);

        var cards = CardPattern().Matches(html).Select(m => m.Value).ToList();
        cards.Count.ShouldBe(4);
        cards[0].ShouldContain("data-active=\"true\"");
        cards[0].ShouldNotContain("opacity-60");
        cards[0].ShouldNotContain("data-testid=\"service-processing\"");
        cards[1].ShouldContain("data-testid=\"service-processing\"");
        cards[2].ShouldNotContain("data-testid=\"service-buffer\"");
        cards[3].ShouldContain("data-active=\"false\"");
        cards[3].ShouldContain("opacity-60");
        cards[3].ShouldContain("data-testid=\"badge-inactive\"");
        cards[3].ShouldContain("Inactive");
    }

    [Fact]
    public async Task A_Polish_salon_shows_Polish_labels()
    {
        using var client = await factory.SignedInClientAsync("salon-pl.localhost", _polish.Id, Role.Owner);

        var html = await GetListAsync(client);

        html.ShouldContain("<html lang=\"pl-PL\"");
        html.ShouldContain("Dodaj usługę");
        html.ShouldContain("Kategorie usług");
        html.ShouldContain("Edytuj");
        GroupHeadings(html).ShouldBe(["Bez kategorii"]);
        CardNames(html).ShouldBe(["Strzyżenie damskie"]);
        html.ShouldNotContain("Balayage");
    }

    [Fact]
    public async Task A_salon_without_services_shows_the_empty_state()
    {
        var empty = await factory.CreateUserAsync((await TenantIdAsync("empty-salon")), "owner@empty-salon.localhost", Role.Manager);
        using var client = factory.ClientFor("empty-salon.localhost");
        (await IdentityTestClient.LoginAsync(client, empty.Email, IdentityTestClient.Password)).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var html = await GetListAsync(client);

        html.ShouldContain("data-testid=\"empty-state\"");
        html.ShouldContain("No services yet.");
        html.ShouldNotContain("data-testid=\"service-card\"");
        html.ShouldContain("href=\"/admin/services/new\"");
    }

    [Fact]
    public async Task A_receptionist_is_sent_to_the_denied_page_and_an_anonymous_visitor_to_login()
    {
        using var receptionist = await factory.SignedInClientAsync("studio-20.localhost", _studio.Id, Role.Receptionist);
        using var anonymous = factory.ClientFor("studio-20.localhost");

        var denied = await receptionist.GetAsync(new Uri(ListPath, UriKind.Relative));
        denied.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");

        var login = await anonymous.GetAsync(new Uri(ListPath, UriKind.Relative));
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = login.Headers.Location.ShouldNotBeNull();
        location.AbsolutePath.ShouldBe("/account/login");
        location.Query.ShouldBe("?returnUrl=%2Fadmin%2Fservices");
    }

    [Fact]
    public async Task The_bare_platform_domain_has_no_catalogue()
    {
        using var client = factory.ClientFor("localhost");

        (await client.GetAsync(new Uri(ListPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static List<string> GroupHeadings(string html)
        => GroupHeadingPattern().Matches(html).Select(m => m.Groups["name"].Value.Trim()).ToList();

    private static List<string> CardNames(string html)
        => CardNamePattern().Matches(html).Select(m => m.Groups["name"].Value).ToList();

    private static async Task<string> GetListAsync(HttpClient client)
    {
        var response = await client.GetAsync(new Uri(ListPath, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<Guid> TenantIdAsync(string slug)
    {
        await using var scope = factory.CreateTenantScope(null);
        return (await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Tenants.AsNoTracking().SingleAsync(t => t.Slug == slug)).Id;
    }

    private async Task<Guid> AddCategoryAsync(Guid tenantId, string name, int sortOrder, bool isActive = true)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var category = new ServiceCategory { Name = name, SortOrder = sortOrder, IsActive = isActive };
        db.ServiceCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    private async Task<Guid> AddServiceAsync(
        Guid tenantId,
        string name,
        int minutes,
        decimal price,
        string currency,
        int sortOrder,
        Guid? categoryId = null,
        int buffer = 0,
        int processing = 0,
        int processingStartsAfter = 0,
        bool isActive = true)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var service = new Service
        {
            Name = name,
            Duration = Duration.FromMinutes(minutes),
            Price = price,
            Currency = currency,
            SortOrder = sortOrder,
            CategoryId = categoryId,
            BufferAfterMinutes = buffer,
            ProcessingMinutes = processing,
            ProcessingStartsAfterMinutes = processingStartsAfter,
            IsActive = isActive,
        };
        db.Services.Add(service);
        await db.SaveChangesAsync();
        return service.Id;
    }

    /// <summary>The text of the span inside each group heading, in document order.</summary>
    [GeneratedRegex("data-testid=\"group-heading\">\\s*<span>(?<name>[^<]+)</span>")]
    private static partial Regex GroupHeadingPattern();

    /// <summary>The name paragraph inside each service card, in document order.</summary>
    [GeneratedRegex("data-testid=\"service-name\">(?<name>[^<]+)</p>")]
    private static partial Regex CardNamePattern();

    /// <summary>Each card from its opening tag to its edit link.</summary>
    [GeneratedRegex("<li[^>]*data-testid=\"service-card\".*?data-testid=\"edit-service\"", RegexOptions.Singleline)]
    private static partial Regex CardPattern();
}