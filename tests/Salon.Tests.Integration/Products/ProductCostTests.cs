using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Salon.Core.Identity;
using Salon.Core.Products;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Products;

/// <summary>
/// What a product costs the salon (ADR 0049): kept on the product form, shown with the margin on the product's page
/// and as the worth of the shelves on the list, read from the cost column of a product file. Only the owner and the
/// managers see costs; the desk sees the shelves without them. Nothing of another salon.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class ProductCostTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string Host = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string ListPath = "/admin/products";
    private const string NewPath = "/admin/products/new";
    private const string ImportPath = "/admin/products/import";

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
    public async Task The_manager_keeps_the_cost_on_the_form_and_the_products_page_says_what_the_salon_keeps()
    {
        using var manager = await factory.SignedInClientAsync(Host, _studio.Id, Role.Manager);
        (await GetAsync(manager, NewPath)).ShouldContain("What it costs you");

        var response = await PostAsync(manager, NewPath, NewPath, new()
        {
            ["Name"] = "Shampoo 250 ml",
            ["RetailPrice"] = "12.50",
            ["CostPrice"] = "6.00",
            ["IsActive"] = "true",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var product = (await ProductsAsync(_studio.Id)).ShouldHaveSingleItem();
        product.CostPrice.ShouldBe(6m);
        var card = await GetAsync(manager, $"{ListPath}/{product.Id}");
        card.ShouldContain("data-testid=\"product-cost\">Costs you £6.00. You keep £6.50 of each one sold (52%).<");
        (await GetAsync(manager, $"{ListPath}/{product.Id}/edit")).ShouldContain("value=\"6.00\"");

        // The cost is cleared like any other optional field.
        await PostAsync(manager, $"{ListPath}/{product.Id}/edit", $"{ListPath}/{product.Id}/edit", new()
        {
            ["Name"] = "Shampoo 250 ml",
            ["RetailPrice"] = "12.50",
            ["CostPrice"] = string.Empty,
            ["IsActive"] = "true",
        });
        (await ProductsAsync(_studio.Id)).ShouldHaveSingleItem().CostPrice.ShouldBeNull();
        (await GetAsync(manager, $"{ListPath}/{product.Id}")).ShouldNotContain("data-testid=\"product-cost\"");
    }

    [Fact]
    public async Task A_loss_is_said_plainly_and_a_product_that_is_not_for_sale_has_a_cost_and_no_margin()
    {
        var loss = await AddAsync(_studio.Id, "Clearance wax", price: 12.50m, cost: 14m, onHand: 1);
        var salonUse = await AddAsync(_studio.Id, "Colour tube 60 ml", price: null, cost: 4m, onHand: 10);
        using var owner = await factory.SignedInClientAsync(Host, _studio.Id, Role.Owner);

        (await GetAsync(owner, $"{ListPath}/{loss}")).ShouldContain("Costs you £14.00. Each one is sold for £1.50 less than it costs you.");
        var card = await GetAsync(owner, $"{ListPath}/{salonUse}");
        card.ShouldContain("data-testid=\"product-cost\">Costs you £4.00.<");
        card.ShouldNotContain("of each one sold");
    }

    [Fact]
    public async Task A_cost_below_zero_or_with_three_decimals_is_refused_and_nothing_is_saved()
    {
        using var owner = await factory.SignedInClientAsync(Host, _studio.Id, Role.Owner);

        foreach (var cost in new[] { "-1", "6.005", "100000.01" })
        {
            var response = await PostAsync(owner, NewPath, NewPath, new()
            {
                ["Name"] = "Shampoo 250 ml",
                ["RetailPrice"] = "12.50",
                ["CostPrice"] = cost,
                ["IsActive"] = "true",
            });

            response.StatusCode.ShouldBe(HttpStatusCode.OK, cost);
            WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()).ShouldContain("Enter a cost from 0 to 100000, with at most two decimal places, or leave it empty.");
        }

        (await ProductsAsync(_studio.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_list_says_what_the_shelves_are_worth_and_how_many_products_have_no_cost_yet()
    {
        await AddAsync(_studio.Id, "Shampoo 250 ml", price: 12.50m, cost: 6m, onHand: 8);
        await AddAsync(_studio.Id, "Colour tube 60 ml", price: null, cost: 4m, onHand: 10);
        await AddAsync(_studio.Id, "Hair oil 50 ml", price: 18m, cost: null, onHand: 2);
        await AddAsync(_polish.Id, "Szampon 250 ml", price: 45.90m, cost: 21.50m, onHand: 100, currency: "PLN");
        using var owner = await factory.SignedInClientAsync(Host, _studio.Id, Role.Owner);

        var html = await GetAsync(owner, ListPath);

        html.ShouldContain("data-testid=\"worth-cost\">£88.00<");
        html.ShouldContain("data-testid=\"worth-retail\">£136.00<");
        html.ShouldContain("1 product on the shelves has no cost yet and is not counted at cost.");

        using var polish = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        var polishHtml = await GetAsync(polish, ListPath);
        // The thousands sign and the gap before "zł" are no-break spaces; the digits are what is checked.
        var polishWorth = Worth().Match(polishHtml).Groups["value"].Value;
        string.Concat(polishWorth.Where(c => char.IsDigit(c) || c == ',')).ShouldBe("2150,00", "its own shelves only");
        polishWorth.ShouldEndWith("zł", Case.Sensitive, "in its own money");
        polishHtml.ShouldNotContain("data-testid=\"worth-without-cost\"");
    }

    [Fact]
    public async Task The_desk_sees_the_shelves_without_what_they_cost()
    {
        var id = await AddAsync(_studio.Id, "Shampoo 250 ml", price: 12.50m, cost: 6m, onHand: 8);
        using var desk = await factory.SignedInClientAsync(Host, _studio.Id, Role.Receptionist);

        var list = await GetAsync(desk, ListPath);
        var card = await GetAsync(desk, $"{ListPath}/{id}");

        list.ShouldContain("Shampoo 250 ml");
        list.ShouldNotContain("data-testid=\"shelf-worth\"");
        card.ShouldContain("£12.50");
        card.ShouldNotContain("data-testid=\"product-cost\"");
        card.ShouldNotContain("£6.00");
    }

    [Fact]
    public async Task A_product_file_with_a_cost_column_shows_the_cost_in_the_preview_and_saves_it()
    {
        const string csv =
            "Name,Price,Cost,Stock\n" +
            "Shampoo 250 ml,12.50,6.00,8\n" +
            "Hair oil 50 ml,18,,2\n" +
            "Styling wax,9,six,1\n";
        using var owner = await factory.SignedInClientAsync(Host, _studio.Id, Role.Owner);

        var previewPath = await UploadAsync(owner, csv);
        var preview = await GetAsync(owner, previewPath);

        preview.ShouldContain("£12.50 · costs £6.00 · on the shelf: 8");
        preview.ShouldContain("The cost is not an amount of money such as 6.50.");
        var token = IdentityTestClient.AntiForgeryToken(preview);
        var confirm = await owner.PostAsync(
            new Uri(previewPath + "/confirm", UriKind.Relative),
            new FormUrlEncodedContent([new KeyValuePair<string, string>("__RequestVerificationToken", token)]));
        confirm.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var products = await ProductsAsync(_studio.Id);
        products.Select(p => (p.Name, p.RetailPrice, p.CostPrice)).ShouldBe([("Hair oil 50 ml", 18m, null), ("Shampoo 250 ml", 12.50m, 6m)]);
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>GETs <paramref name="tokenPage"/> for its anti-forgery token and POSTs the fields with it.</summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string tokenPage, string path, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(await GetAsync(client, tokenPage));
        return await client.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    /// <summary>Uploads the file to the import and returns the address of its preview.</summary>
    private static async Task<string> UploadAsync(HttpClient client, string csv)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(IdentityTestClient.AntiForgeryToken(await GetAsync(client, ImportPath))), "__RequestVerificationToken");
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "File", "products.csv");
        var upload = await client.PostAsync(new Uri(ImportPath, UriKind.Relative), form);
        upload.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return upload.Headers.Location!.OriginalString;
    }

    private async Task<Guid> AddAsync(Guid tenantId, string name, decimal? price, decimal? cost, int onHand, string currency = "GBP")
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var product = new Product { Name = name, RetailPrice = price, CostPrice = cost, Currency = currency, StockOnHand = onHand };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    private async Task<List<Product>> ProductsAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Products.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
    }

    [GeneratedRegex("data-testid=\"worth-cost\">(?<value>[^<]*)<")]
    private static partial Regex Worth();
}