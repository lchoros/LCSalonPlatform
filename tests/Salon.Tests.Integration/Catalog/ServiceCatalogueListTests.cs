using System.Net;
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
/// A real-sized catalogue (ADR 0032) on the public booking page and in the admin: category tiles, a search in which
/// every word must occur, pages, and variants of one service under one title with a price range. The same shared
/// components serve both lists. A salon with a handful of services keeps the plain page, and one salon never sees
/// another's services.
/// </summary>
[Collection(SharedDatabase.Name)]
public class ServiceCatalogueListTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _hairId;
    private Guid _browsId;
    private Guid _longHairId;

    /// <summary>
    /// Studio 20: nine hair services, three Keratin variants (£70, £75, £85) and three brow services, all active, plus
    /// one inactive hair service. Thirteen entries for a client: page one holds twelve. The Polish salon: two services.
    /// </summary>
    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");

        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            var hair = new ServiceCategory { Name = "Hair", SortOrder = 1 };
            var brows = new ServiceCategory { Name = "Brows", SortOrder = 2 };
            db.ServiceCategories.AddRange(hair, brows);
            var order = 0;
            for (var i = 1; i <= 9; i++)
            {
                db.Services.Add(Service($"Hair {i:00}", hair.Id, ++order, 10m + i));
            }

            foreach (var (label, price) in new[] { ("Medium hair", 70m), ("Thick hair", 75m), ("Long hair", 85m) })
            {
                var variant = Service("Keratin" + ServiceRules.VariantSeparator + label, hair.Id, ++order, price);
                variant.VariantGroup = "Keratin";
                variant.VariantLabel = label;
                db.Services.Add(variant);
                if (label == "Long hair")
                {
                    _longHairId = variant.Id;
                }
            }

            for (var i = 1; i <= 3; i++)
            {
                db.Services.Add(Service($"Brows {i:00}", brows.Id, ++order, 20m + i));
            }

            var retired = Service("Hair retired", hair.Id, ++order, 99m);
            retired.IsActive = false;
            db.Services.Add(retired);
            await db.SaveChangesAsync();
            (_hairId, _browsId) = (hair.Id, brows.Id);
        }

        await using (var scope = factory.CreateTenantScope(_polish.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            db.Services.Add(Service("Strzyżenie", null, 1, 80m, "PLN"));
            db.Services.Add(Service("Koloryzacja", null, 2, 250m, "PLN"));
            await db.SaveChangesAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_first_page_shows_twelve_entries_with_tiles_a_search_and_a_pager()
    {
        using var client = factory.ClientFor(HostA);

        var page = await GetAsync(client, "/book");

        page.ShouldContain("data-testid=\"service-filters\"");
        page.ShouldContain("data-testid=\"search-input\"");
        Count(page, "data-testid=\"filter-tile\"").ShouldBe(3, "All, Hair and Brows");
        page.ShouldContain("aria-current=\"true\" data-testid=\"filter-tile\" data-key=\"all\">All<");
        page.ShouldContain($"href=\"/book?category={_hairId}\" class=\"filter-tile accent-1 \" data-testid=\"filter-tile\" data-key=\"{_hairId}\">Hair<");
        Count(page, "data-testid=\"book-service\"").ShouldBe(11, "nine hair services and two of the three brow services");
        Count(page, "data-testid=\"variant-group\"").ShouldBe(1);
        page.ShouldContain(">Hair 01<");
        page.ShouldContain(">Brows 02<");
        page.ShouldNotContain(">Brows 03<");
        page.ShouldNotContain("Hair retired");
        page.ShouldContain("data-testid=\"pager\" data-page=\"1\" data-pages=\"2\"");
        page.ShouldContain("href=\"/book?page=2\" rel=\"next\"");
        page.ShouldContain("data-testid=\"pager-status\">Page 1 of 2<");
        page.ShouldContain("data-testid=\"pager-summary\">1 to 12 of 13<");
        page.ShouldNotContain("data-testid=\"pager-previous\"");
    }

    [Theory]
    [InlineData("/book?page=2")]
    [InlineData("/book?page=99")]
    public async Task The_last_page_holds_the_rest_and_a_page_that_does_not_exist_leads_to_it(string path)
    {
        using var client = factory.ClientFor(HostA);

        var page = await GetAsync(client, path);

        Count(page, "data-testid=\"book-service\"").ShouldBe(1);
        page.ShouldContain(">Brows 03<");
        page.ShouldContain("data-testid=\"group-heading\">Brows<", Case.Sensitive, "a category split by the page break gets its heading again");
        page.ShouldContain("data-testid=\"pager\" data-page=\"2\" data-pages=\"2\"");
        page.ShouldContain("href=\"/book\" rel=\"prev\"", Case.Sensitive, "page one has the plain address");
        page.ShouldNotContain("data-testid=\"pager-next\"");
    }

    [Theory]
    [InlineData("/book?page=abc")]
    [InlineData("/book?page=0")]
    [InlineData("/book?category=not-an-id")]
    public async Task An_address_that_cannot_be_read_shows_the_first_page(string path)
    {
        using var client = factory.ClientFor(HostA);

        var page = await GetAsync(client, path);

        page.ShouldContain("data-testid=\"pager\" data-page=\"1\" data-pages=\"2\"");
        page.ShouldContain(">Hair 01<");
    }

    [Fact]
    public async Task A_category_tile_narrows_the_list_and_keeps_the_search()
    {
        using var client = factory.ClientFor(HostA);

        var brows = await GetAsync(client, $"/book?category={_browsId}");

        Count(brows, "data-testid=\"book-service\"").ShouldBe(3);
        brows.ShouldNotContain(">Hair 01<");
        brows.ShouldNotContain("data-testid=\"variant-group\"");
        brows.ShouldContain($"aria-current=\"true\" data-testid=\"filter-tile\" data-key=\"{_browsId}\">Brows<");
        brows.ShouldNotContain("data-testid=\"pager\"", Case.Sensitive, "three services fit one page");
        brows.ShouldContain($"<input type=\"hidden\" name=\"category\" value=\"{_browsId}\" />", Case.Sensitive, "a search stays inside the chosen category");

        var both = await GetAsync(client, $"/book?category={_browsId}&q=02");
        Count(both, "data-testid=\"book-service\"").ShouldBe(1);
        both.ShouldContain(">Brows 02<");
        both.ShouldContain($"href=\"/book?category={_hairId}&q=02\"", Case.Sensitive, "another tile keeps the search");
        both.ShouldContain($"href=\"/book?category={_browsId}\" class=\"search-clear\"", Case.Sensitive, "clearing the search keeps the category");
    }

    [Fact]
    public async Task Variants_sit_under_one_title_with_their_price_range_and_each_can_be_booked()
    {
        using var client = factory.ClientFor(HostA);

        var page = await GetAsync(client, $"/book?category={_hairId}");

        page.ShouldContain("class=\"variant-card accent-1\" data-testid=\"variant-group\">", Case.Sensitive, "closed until tapped");
        page.ShouldContain("data-testid=\"variant-title\">Keratin<");
        page.ShouldContain("data-testid=\"variant-prices\">£70.00 – £85.00<");
        page.ShouldContain("Options: 3");
        Count(page, "data-testid=\"book-variant\"").ShouldBe(3);
        page.ShouldContain($"href=\"/book/{_longHairId}\" data-testid=\"book-variant\"");
        page.ShouldContain(">Long hair<");
        page.ShouldNotContain(">Keratin · Long hair<", Case.Sensitive, "under its title a variant shows only what makes it different");

        // The next step names the whole service, so the client knows what they are booking.
        (await GetAsync(client, $"/book/{_longHairId}")).ShouldContain("Keratin · Long hair");
    }

    [Fact]
    public async Task A_search_needs_every_word_and_opens_the_variants_it_found()
    {
        using var client = factory.ClientFor(HostA);

        var found = await GetAsync(client, "/book?q=KERATIN+long");

        found.ShouldContain("data-testid=\"variant-group\" open=\"open\">");
        Count(found, "data-testid=\"book-variant\"").ShouldBe(1);
        found.ShouldContain("data-testid=\"variant-prices\">£85.00<", Case.Sensitive, "one variant has one price, not a range");
        found.ShouldNotContain("data-testid=\"book-service\"");
        found.ShouldContain("value=\"KERATIN long\"", Case.Sensitive, "the field shows what was searched for");
        found.ShouldContain("href=\"/book\" class=\"search-clear\"");

        var nothing = await GetAsync(client, "/book?q=keratin+brows");
        nothing.ShouldContain("data-testid=\"no-match\"");
        nothing.ShouldContain("No service matches.");
        nothing.ShouldContain("href=\"/book\" class=\"btn btn-secondary\" data-testid=\"show-all\"");
        nothing.ShouldNotContain("data-testid=\"pager\"");
    }

    [Fact]
    public async Task What_is_typed_into_the_search_is_shown_as_text_never_as_markup()
    {
        using var client = factory.ClientFor(HostA);

        var response = await client.GetAsync(new Uri("/book?q=%3Cscript%3Ealert(1)%3C%2Fscript%3E", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        raw.ShouldNotContain("<script>alert(1)</script>");
        raw.ShouldContain("&lt;script&gt;alert(1)&lt;/script&gt;");
    }

    [Fact]
    public async Task A_salon_with_a_handful_of_services_keeps_the_plain_page_in_its_language_and_sees_nothing_of_another()
    {
        using var client = factory.ClientFor(HostB);

        var page = await GetAsync(client, "/book");

        page.ShouldNotContain("data-testid=\"service-filters\"");
        page.ShouldNotContain("data-testid=\"pager\"");
        Count(page, "data-testid=\"book-service\"").ShouldBe(2);
        page.ShouldContain("Strzyżenie");
        page.ShouldNotContain("Keratin");
        page.ShouldNotContain("Hair 01");
        (await GetAsync(client, $"/book?category={_hairId}&q=keratin")).ShouldNotContain("Keratin", Case.Sensitive, "another salon's category and services mean nothing here");
    }

    [Fact]
    public async Task The_admin_list_uses_the_same_search_tiles_and_pager_and_shows_inactive_services_and_variants()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var all = await GetAsync(client, "/admin/services");
        Count(all, "data-testid=\"service-card\"").ShouldBe(16, "every service, the inactive one included, fits one page of 24");
        all.ShouldContain("data-testid=\"search-input\"");
        Count(all, "data-testid=\"filter-tile\"").ShouldBe(3);
        all.ShouldContain("Hair retired");
        all.ShouldNotContain("data-testid=\"pager\"");

        var keratin = await GetAsync(client, "/admin/services?q=keratin");
        Count(keratin, "data-testid=\"service-card\"").ShouldBe(3);
        Count(keratin, "data-testid=\"service-variant-of\">Variant of Keratin<").ShouldBe(3);
        keratin.ShouldContain("data-testid=\"service-name\">Keratin · Thick hair<");

        var brows = await GetAsync(client, $"/admin/services?category={_browsId}");
        Count(brows, "data-testid=\"service-card\"").ShouldBe(3);
        brows.ShouldContain($"href=\"/admin/services?category={_browsId}\" class=\"filter-tile accent-0 is-on\" aria-current=\"true\"");

        var nothing = await GetAsync(client, "/admin/services?q=zzz");
        nothing.ShouldContain("data-testid=\"no-match\"");
        nothing.ShouldContain("href=\"/admin/services\"");
    }

    [Fact]
    public async Task The_admin_list_pages_a_long_catalogue()
    {
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            for (var i = 1; i <= 10; i++)
            {
                db.Services.Add(Service($"Extra {i:00}", null, 100 + i, 5m));
            }

            await db.SaveChangesAsync();
        }

        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var first = await GetAsync(client, "/admin/services");
        Count(first, "data-testid=\"service-card\"").ShouldBe(24);
        first.ShouldContain("href=\"/admin/services?page=2\" rel=\"next\"");
        first.ShouldContain("data-testid=\"pager-summary\">1 to 24 of 26<");

        var second = await GetAsync(client, "/admin/services?page=2");
        Count(second, "data-testid=\"service-card\"").ShouldBe(2);
        second.ShouldContain("Extra 10");
    }

    [Fact]
    public async Task The_owner_makes_a_service_a_variant_on_the_edit_form_and_half_a_variant_is_refused()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var path = $"/admin/services/{_longHairId}";
        var form = await GetAsync(client, path);
        form.ShouldContain("data-testid=\"variant-fields\"");
        form.ShouldContain("value=\"Keratin\"");
        form.ShouldContain("value=\"Long hair\"");

        var half = await PostAsync(client, path, form, Fields("Keratin · Very long hair", "Keratin", "  "));
        half.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await half.Content.ReadAsStringAsync()).ShouldContain("Fill in the shared title and the difference, or leave both empty.");
        (await ServiceAsync(_longHairId)).VariantLabel.ShouldBe("Long hair", "nothing was saved");

        var saved = await PostAsync(client, path, form, Fields("Keratin · Very long hair", "  Keratin ", " Very long hair "));
        saved.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var service = await ServiceAsync(_longHairId);
        (service.Name, service.VariantGroup, service.VariantLabel).ShouldBe(("Keratin · Very long hair", "Keratin", "Very long hair"));

        // Emptying both fields takes the service out of the group: clients then see it on its own.
        (await PostAsync(client, path, form, Fields("Very long hair treatment", string.Empty, string.Empty))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var alone = await ServiceAsync(_longHairId);
        (alone.VariantGroup, alone.VariantLabel).ShouldBe(((string?)null, (string?)null));
        using var visitor = factory.ClientFor(HostA);
        (await GetAsync(visitor, "/book?q=very")).ShouldContain("data-testid=\"book-service\"");
    }

    private Dictionary<string, string> Fields(string name, string variantGroup, string variantLabel) => new()
    {
        ["Name"] = name,
        ["Description"] = string.Empty,
        ["DurationMinutes"] = "60",
        ["Price"] = "85.00",
        ["CategoryId"] = _hairId.ToString(),
        ["BufferAfterMinutes"] = "0",
        ["ProcessingMinutes"] = "0",
        ["ProcessingStartsAfterMinutes"] = "0",
        ["IsActive"] = "true",
        ["VariantGroup"] = variantGroup,
        ["VariantLabel"] = variantLabel,
    };

    private static Service Service(string name, Guid? categoryId, int sortOrder, decimal price, string currency = "GBP")
        => new() { Name = name, Currency = currency, Duration = Duration.FromMinutes(60), Price = price, SortOrder = sortOrder, CategoryId = categoryId };

    private async Task<Service> ServiceAsync(Guid id)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Services.AsNoTracking().SingleAsync(s => s.Id == id);
    }

    private static int Count(string html, string text)
    {
        var count = 0;
        for (var at = html.IndexOf(text, StringComparison.Ordinal); at >= 0; at = html.IndexOf(text, at + text.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string pageWithToken, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(pageWithToken);
        return await client.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent(fields));
    }
}