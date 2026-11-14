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
/// The ManageCategories slice over HTTP as a signed-in Owner (ADR 0013): the list with service counts in both languages,
/// adding (trimmed, unique case-insensitively, appended last), renaming (own name allowed), deactivating and restoring
/// (services keep their category), nothing of another salon, and the gates (Catalog.Manage, sign-in).
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class ManageCategoriesTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string Path = "/admin/services/categories";

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _hairId;
    private Guid _nailsId;
    private Guid _polishCategoryId;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        _hairId = await AddCategoryAsync(_studio.Id, "Hair", 1);
        _nailsId = await AddCategoryAsync(_studio.Id, "Nails", 2);
        _polishCategoryId = await AddCategoryAsync(_polish.Id, "Fryzjerstwo", 1);
        await AddServiceAsync(_studio.Id, "Cut & blow-dry", _hairId);
        await AddServiceAsync(_studio.Id, "Balayage", _hairId);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Lists_the_salons_categories_in_order_with_their_service_counts_and_forms()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var html = await GetPageAsync(client);

        html.ShouldContain("data-testid=\"add-category-form\"");
        html.ShouldContain("__RequestVerificationToken");
        RowNames(html).ShouldBe(["Hair", "Nails"]);
        html.ShouldContain("2 services");
        html.ShouldContain("0 services");
        html.ShouldContain($"action=\"/admin/services/categories/{_hairId}/rename\"");
        html.ShouldContain($"action=\"/admin/services/categories/{_hairId}/deactivate\"");
        html.ShouldNotContain("data-testid=\"activate-category\"");
        html.ShouldNotContain("Fryzjerstwo");
        html.ShouldNotContain("data-testid=\"empty-state\"");
    }

    [Fact]
    public async Task A_Polish_salon_shows_Polish_labels()
    {
        using var client = await OwnerClientAsync("salon-pl.localhost", _polish.Id);

        var html = await GetPageAsync(client);

        html.ShouldContain("<html lang=\"pl-PL\"");
        html.ShouldContain("Kategorie usług");
        html.ShouldContain("Dodaj kategorię");
        html.ShouldContain("Zmień nazwę");
        html.ShouldContain("Dezaktywuj");
        html.ShouldContain("Usługi: 0");
        RowNames(html).ShouldBe(["Fryzjerstwo"]);
    }

    [Fact]
    public async Task Adding_trims_the_name_appends_it_last_and_shows_a_flash()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var response = await PostAsync(client, Path, new() { ["Name"] = "  Barbering  " });

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe(Path);
        var categories = await CategoriesAsync(_studio.Id);
        categories.Select(c => (c.Name, c.SortOrder, c.IsActive)).ShouldBe([("Hair", 1, true), ("Nails", 2, true), ("Barbering", 3, true)]);
        categories.ShouldAllBe(c => c.TenantId == _studio.Id);
        (await CategoriesAsync(_polish.Id)).Count.ShouldBe(1);

        var html = await (await client.GetAsync(response.Headers.Location!)).Content.ReadAsStringAsync();
        html.ShouldContain("Category “Barbering” has been added.");
        RowNames(html).ShouldBe(["Hair", "Nails", "Barbering"]);
    }

    [Fact]
    public async Task A_duplicate_or_blank_name_is_refused_next_to_the_add_form()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var duplicate = await PostAsync(client, Path, new() { ["Name"] = "hair " });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await duplicate.Content.ReadAsStringAsync();
        html.ShouldContain("A category with this name already exists.");
        Regex.Match(html, "<input[^>]*data-testid=\"new-category-name\"[^>]*>").Value.ShouldContain("input-validation-error");
        html.ShouldContain("value=\"hair \"");

        var blank = await PostAsync(client, Path, new() { ["Name"] = "   " });
        (await blank.Content.ReadAsStringAsync()).ShouldContain("Enter the category name.");

        (await CategoriesAsync(_studio.Id)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Renaming_changes_the_row_refuses_another_rows_name_and_accepts_its_own_in_another_case()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var renamed = await PostAsync(client, $"{Path}/{_hairId}/rename", new() { ["Name"] = " Hairdressing " });
        renamed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await CategoriesAsync(_studio.Id)).Single(c => c.Id == _hairId).Name.ShouldBe("Hairdressing");
        (await (await client.GetAsync(renamed.Headers.Location!)).Content.ReadAsStringAsync()).ShouldContain("Category renamed to “Hairdressing”.");

        var duplicate = await PostAsync(client, $"{Path}/{_hairId}/rename", new() { ["Name"] = "NAILS" });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await duplicate.Content.ReadAsStringAsync();
        html.ShouldContain("A category with this name already exists.");
        Regex.Match(html, $"<input[^>]*id=\"rename-{_hairId}\"[^>]*>").Value.ShouldContain("input-validation-error");
        Regex.Match(html, $"<input[^>]*id=\"rename-{_nailsId}\"[^>]*>").Value.ShouldNotContain("input-validation-error");
        (await CategoriesAsync(_studio.Id)).Single(c => c.Id == _hairId).Name.ShouldBe("Hairdressing");

        (await PostAsync(client, $"{Path}/{_hairId}/rename", new() { ["Name"] = "HAIRDRESSING" })).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await CategoriesAsync(_studio.Id)).Single(c => c.Id == _hairId).Name.ShouldBe("HAIRDRESSING");
    }

    [Fact]
    public async Task Deactivating_and_restoring_toggle_the_flag_and_leave_the_services_where_they_are()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var deactivated = await PostAsync(client, $"{Path}/{_hairId}/deactivate", new());

        deactivated.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await CategoriesAsync(_studio.Id)).Single(c => c.Id == _hairId).IsActive.ShouldBeFalse();
        (await ServicesAsync(_studio.Id)).ShouldAllBe(s => s.CategoryId == _hairId);
        var html = await (await client.GetAsync(deactivated.Headers.Location!)).Content.ReadAsStringAsync();
        html.ShouldContain("The category has been deactivated.");
        html.ShouldContain("data-testid=\"badge-inactive\"");
        html.ShouldContain($"action=\"/admin/services/categories/{_hairId}/activate\"");

        var restored = await PostAsync(client, $"{Path}/{_hairId}/activate", new());

        restored.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await CategoriesAsync(_studio.Id)).Single(c => c.Id == _hairId).IsActive.ShouldBeTrue();
        (await (await client.GetAsync(restored.Headers.Location!)).Content.ReadAsStringAsync()).ShouldContain("The category has been restored.");
    }

    [Fact]
    public async Task Another_salons_category_id_is_not_found_on_every_post()
    {
        using var client = await OwnerClientAsync("salon-pl.localhost", _polish.Id);

        (await PostAsync(client, $"{Path}/{_hairId}/rename", new() { ["Name"] = "Hijacked" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(client, $"{Path}/{_hairId}/deactivate", new())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(client, $"{Path}/{Guid.NewGuid()}/activate", new())).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var hair = (await CategoriesAsync(_studio.Id)).Single(c => c.Id == _hairId);
        hair.Name.ShouldBe("Hair");
        hair.IsActive.ShouldBeTrue();
        (await CategoriesAsync(_polish.Id)).Single().Id.ShouldBe(_polishCategoryId);
    }

    [Fact]
    public async Task A_post_without_the_anti_forgery_token_is_refused()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var response = await client.PostAsync(new Uri(Path, UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string> { ["Name"] = "Sneaky" }));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CategoriesAsync(_studio.Id)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_receptionist_is_denied_and_an_anonymous_visitor_is_asked_to_sign_in()
    {
        using var receptionist = await factory.SignedInClientAsync("studio-20.localhost", _studio.Id, Role.Receptionist);
        using var anonymous = factory.ClientFor("studio-20.localhost");

        var denied = await receptionist.GetAsync(new Uri(Path, UriKind.Relative));
        denied.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");

        var login = await anonymous.GetAsync(new Uri(Path, UriKind.Relative));
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        login.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");
    }

    private static List<string> RowNames(string html)
        => RowNamePattern().Matches(html).Select(m => m.Groups["name"].Value).ToList();

    private static async Task<string> GetPageAsync(HttpClient client)
    {
        var response = await client.GetAsync(new Uri(Path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>GETs the page for its anti-forgery token (the cookie half rides on the client) and POSTs the fields with it.</summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(await GetPageAsync(client));
        return await client.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    private Task<HttpClient> OwnerClientAsync(string host, Guid tenantId) => factory.SignedInClientAsync(host, tenantId, Role.Owner);

    private async Task<Guid> AddCategoryAsync(Guid tenantId, string name, int sortOrder)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var category = new ServiceCategory { Name = name, SortOrder = sortOrder };
        db.ServiceCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    private async Task AddServiceAsync(Guid tenantId, string name, Guid categoryId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        db.Services.Add(new Service { Name = name, Duration = Duration.FromMinutes(45), Price = 45m, Currency = "GBP", CategoryId = categoryId });
        await db.SaveChangesAsync();
    }

    private async Task<List<ServiceCategory>> CategoriesAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().ServiceCategories.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync();
    }

    private async Task<List<Service>> ServicesAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Services.AsNoTracking().ToListAsync();
    }

    /// <summary>The value of the rename input in each row, in document order.</summary>
    [GeneratedRegex("<input id=\"rename-[^\"]+\" name=\"Name\" type=\"text\" value=\"(?<name>[^\"]*)\"")]
    private static partial Regex RowNamePattern();
}