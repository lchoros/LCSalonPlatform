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
/// The product import over HTTP (ADR 0045): upload, a preview with counts and reasons, and a confirm that adds only
/// the new rows, each with its opening count as a line of the stock history. A product the salon has is never
/// changed, a second run adds nothing, the token is the salon's own, and only who may change the catalogue gets in.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class ImportProductsTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string ImportPath = "/admin/products/import";
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string FileName = "products.csv";

    /// <summary>Four good rows, one that repeats an earlier one, and two that cannot be read.</summary>
    private const string Csv =
        "Name,Brand,Code,Price,Stock,Low stock at\n" +
        "Shampoo 250 ml,Salon Range,SH-250,12.50,8,2\n" +
        "Conditioner 250 ml,Salon Range,CO-250,£9.00,5,\n" +
        "Colour tube 60 ml,,,,12,3\n" +
        "Hair oil 50 ml,,HO-50,18,0,\n" +
        "shampoo 250 ml,,,11.00,1,\n" +
        ",Nameless,,5.00,1,\n" +
        "Styling wax,,,twelve,2,\n";

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
    public async Task The_products_page_links_to_the_import_and_the_form_says_what_a_file_looks_like()
    {
        using var english = await OwnerClientAsync(HostA, _studio.Id);
        using var polish = await OwnerClientAsync(HostB, _polish.Id);

        (await GetOkAsync(english, "/admin/products")).ShouldContain("href=\"/admin/products/import\" data-testid=\"import-products\"");
        var englishHtml = await GetOkAsync(english, ImportPath);
        var polishHtml = await GetOkAsync(polish, ImportPath);

        englishHtml.ShouldContain("Import products");
        englishHtml.ShouldContain("Up to 2000 rows and 2 MB.");
        englishHtml.ShouldContain("Name,Brand,Code,Price,Cost,Stock,Low stock at");
        englishHtml.ShouldContain("A product you already have is left as it is");
        polishHtml.ShouldContain("Import produktów");
        polishHtml.ShouldContain("Nazwa;Marka;Kod;Cena;Koszt;Stan;Stan minimalny");
    }

    [Fact]
    public async Task The_preview_counts_the_rows_and_says_why_a_row_is_left_out_and_changes_nothing()
    {
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var upload = await UploadAsync(client, Encoding.UTF8.GetBytes(Csv), FileName);

        upload.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var previewPath = upload.Headers.Location!.OriginalString;
        TokenPattern().IsMatch(previewPath[(ImportPath.Length + 1)..]).ShouldBeTrue(previewPath);
        var html = await GetOkAsync(client, previewPath);
        html.ShouldContain("products.csv · rows: 7");
        Count(html, "count-new").ShouldBe("4");
        Count(html, "count-duplicate").ShouldBe("1");
        Count(html, "count-invalid").ShouldBe("2");
        html.ShouldContain("Salon Range · SH-250 · £12.50 · on the shelf: 8");
        html.ShouldContain("Repeats an earlier row of this file.");
        html.ShouldContain("The name is missing.");
        html.ShouldContain("The price is not an amount of money such as 12.50.");
        html.ShouldContain("Import 4 new products");
        (await ProductsAsync(_studio.Id)).ShouldBeEmpty("a preview only reads");
    }

    [Fact]
    public async Task Confirming_adds_the_new_products_with_their_opening_counts_in_the_history()
    {
        using var client = await OwnerClientAsync(HostA, _studio.Id);
        var previewPath = (await UploadAsync(client, Encoding.UTF8.GetBytes(Csv), FileName)).Headers.Location!.OriginalString;

        var confirm = await ConfirmAsync(client, previewPath);

        confirm.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        confirm.Headers.Location!.OriginalString.ShouldBe(previewPath);
        var result = await GetOkAsync(client, previewPath);
        result.ShouldContain("data-testid=\"import-result\"");
        Count(result, "result-created").ShouldBe("4");
        result.ShouldContain("25 units were counted onto the shelves.");
        result.ShouldContain("1 already existed, 2 could not be imported.");

        var products = await ProductsAsync(_studio.Id);
        products.Select(p => p.Name).ShouldBe(["Colour tube 60 ml", "Conditioner 250 ml", "Hair oil 50 ml", "Shampoo 250 ml"]);
        var shampoo = products.Single(p => p.Name == "Shampoo 250 ml");
        (shampoo.Brand, shampoo.Code, shampoo.RetailPrice, shampoo.Currency, shampoo.LowStockAt, shampoo.StockOnHand, shampoo.IsActive)
            .ShouldBe(("Salon Range", "SH-250", 12.50m, "GBP", 2, 8, true), "the first row with a name wins, not the later one at £11");
        products.Single(p => p.Name == "Conditioner 250 ml").RetailPrice.ShouldBe(9m);
        var tube = products.Single(p => p.Name == "Colour tube 60 ml");
        (tube.RetailPrice, tube.StockOnHand, tube.LowStockAt).ShouldBe((null, 12, 3), "no price: for the chair, not for sale");
        products.Single(p => p.Name == "Hair oil 50 ml").StockOnHand.ShouldBe(0);

        var movements = await MovementsAsync(_studio.Id);
        movements.Count.ShouldBe(3, "one opening count per product that has units; none for an empty shelf");
        movements.ShouldAllBe(m => m.Kind == StockMovementKind.Counted && m.Note == "Imported from products.csv" && m.ByUserId != null);
        movements.Single(m => m.ProductId == shampoo.Id).Quantity.ShouldBe(8);
        (await ProductsAsync(_polish.Id)).ShouldBeEmpty("the other salon gets nothing");
    }

    [Fact]
    public async Task Running_the_same_file_twice_adds_nothing_and_changes_no_price_or_stock()
    {
        using var client = await OwnerClientAsync(HostA, _studio.Id);
        await ConfirmAsync(client, (await UploadAsync(client, Encoding.UTF8.GetBytes(Csv), FileName)).Headers.Location!.OriginalString);
        var dearer = Csv.Replace("12.50,8,2", "99.00,80,2", StringComparison.Ordinal);

        var second = (await UploadAsync(client, Encoding.UTF8.GetBytes(dearer), FileName)).Headers.Location!.OriginalString;
        var preview = await GetOkAsync(client, second);

        Count(preview, "count-new").ShouldBe("0");
        Count(preview, "count-duplicate").ShouldBe("5");
        preview.ShouldContain("You already have a product with this name.");
        preview.ShouldContain("There is nothing new to import.");
        preview.ShouldNotContain("data-testid=\"import-confirm\"");
        (await client.PostAsync(new Uri(second + "/confirm", UriKind.Relative), await AntiForgeryFormAsync(client))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var shampoo = (await ProductsAsync(_studio.Id)).Single(p => p.Name == "Shampoo 250 ml");
        (shampoo.RetailPrice, shampoo.StockOnHand).ShouldBe((12.50m, 8));
        (await ProductsAsync(_studio.Id)).Count.ShouldBe(4);
    }

    [Fact]
    public async Task A_product_added_by_hand_after_the_preview_is_not_added_twice()
    {
        using var client = await OwnerClientAsync(HostA, _studio.Id);
        var previewPath = (await UploadAsync(client, Encoding.UTF8.GetBytes(Csv), FileName)).Headers.Location!.OriginalString;
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            db.Products.Add(new Product { Name = "HAIR OIL 50 ML", Currency = "GBP", RetailPrice = 20m });
            await db.SaveChangesAsync();
        }

        await ConfirmAsync(client, previewPath);

        var result = await GetOkAsync(client, previewPath);
        Count(result, "result-created").ShouldBe("3");
        result.ShouldContain("2 already existed, 2 could not be imported.");
        (await ProductsAsync(_studio.Id)).Count(p => p.Name.Equals("Hair oil 50 ml", StringComparison.OrdinalIgnoreCase)).ShouldBe(1);
    }

    [Fact]
    public async Task Confirming_twice_shows_the_result_again_and_adds_nothing_more()
    {
        using var client = await OwnerClientAsync(HostA, _studio.Id);
        var previewPath = (await UploadAsync(client, Encoding.UTF8.GetBytes(Csv), FileName)).Headers.Location!.OriginalString;
        var form = await AntiForgeryFormAsync(client);
        await ConfirmAsync(client, previewPath);

        var again = await client.PostAsync(new Uri(previewPath + "/confirm", UriKind.Relative), form);

        again.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await ProductsAsync(_studio.Id)).Count.ShouldBe(4);
        (await MovementsAsync(_studio.Id)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_polish_salon_imports_a_file_with_polish_headings_saved_in_windows_1250()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var file = Encoding.GetEncoding(1250).GetBytes(
            "Nazwa;Marka;Cena;Stan\n" +
            "Odżywka 250 ml;Seria Salon;45,90 zł;4\n" +
            "Szampon 250 ml;Seria Salon;39,00;6\n");
        using var client = await OwnerClientAsync(HostB, _polish.Id);

        var previewPath = (await UploadAsync(client, file, "produkty.csv")).Headers.Location!.OriginalString;
        var html = await GetOkAsync(client, previewPath);

        html.ShouldContain("Odżywka 250 ml");
        // The price is written with a no-break space before the currency, as everywhere else.
        html.ShouldContain("45,90 zł");
        html.ShouldContain("Importuj 2 nowych produktów");
        await ConfirmAsync(client, previewPath);
        var products = await ProductsAsync(_polish.Id);
        products.Select(p => (p.Name, p.RetailPrice, p.Currency, p.StockOnHand)).ShouldBe([("Odżywka 250 ml", 45.90m, "PLN", 4), ("Szampon 250 ml", 39m, "PLN", 6)]);
        (await MovementsAsync(_polish.Id)).ShouldAllBe(m => m.Note == "Zaimportowano z produkty.csv");
    }

    [Theory]
    [InlineData("Price,Stock\n12.50,3\n", "We could not find a column with the product name. Call it \"Name\" or \"Product\". Columns found: Price, Stock")]
    [InlineData("Name,Price\n", "The file has no product rows.")]
    [InlineData("", "Choose a CSV file.")]
    public async Task A_file_that_cannot_be_used_is_refused_with_the_reason(string csv, string message)
    {
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var response = await UploadAsync(client, Encoding.UTF8.GetBytes(csv), FileName);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()).ShouldContain(message);
    }

    [Fact]
    public async Task A_spreadsheet_file_and_a_post_without_a_file_are_refused_with_the_reason()
    {
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var binary = await UploadAsync(client, [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x08], "products.xlsx");
        using var noFile = new MultipartFormDataContent { { new StringContent(await TokenAsync(client)), "__RequestVerificationToken" } };
        var empty = await client.PostAsync(new Uri(ImportPath, UriKind.Relative), noFile);

        WebUtility.HtmlDecode(await binary.Content.ReadAsStringAsync()).ShouldContain("The file is not a text CSV file.");
        WebUtility.HtmlDecode(await empty.Content.ReadAsStringAsync()).ShouldContain("Choose a CSV file.");
    }

    [Fact]
    public async Task Another_salon_cannot_see_or_confirm_the_token_and_an_unknown_token_goes_back_to_the_start()
    {
        using var studio = await OwnerClientAsync(HostA, _studio.Id);
        using var polish = await OwnerClientAsync(HostB, _polish.Id);
        var previewPath = (await UploadAsync(studio, Encoding.UTF8.GetBytes(Csv), FileName)).Headers.Location!.OriginalString;

        (await polish.GetAsync(new Uri(previewPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var polishForm = new FormUrlEncodedContent([new KeyValuePair<string, string>("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(await GetOkAsync(polish, ImportPath)))]);
        (await polish.PostAsync(new Uri(previewPath + "/confirm", UriKind.Relative), polishForm)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProductsAsync(_polish.Id)).ShouldBeEmpty();
        (await ProductsAsync(_studio.Id)).ShouldBeEmpty();

        var unknown = await studio.GetAsync(new Uri($"{ImportPath}/{new string('a', 64)}", UriKind.Relative));
        unknown.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        unknown.Headers.Location!.OriginalString.ShouldBe(ImportPath);
        (await GetOkAsync(studio, ImportPath)).ShouldContain("The preview has expired. Upload the file again.");
    }

    [Fact]
    public async Task Only_who_may_change_the_catalogue_gets_in_and_the_anti_forgery_token_is_needed()
    {
        using var owner = await OwnerClientAsync(HostA, _studio.Id);
        var previewPath = (await UploadAsync(owner, Encoding.UTF8.GetBytes(Csv), FileName)).Headers.Location!.OriginalString;
        using var receptionist = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist, "desk@studio-20.test");
        using var manager = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Manager, "manager@studio-20.test");

        var get = await receptionist.GetAsync(new Uri(ImportPath, UriKind.Relative));
        get.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        get.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        (await receptionist.GetAsync(new Uri(previewPath, UriKind.Relative))).Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        (await GetOkAsync(receptionist, "/admin/products")).ShouldNotContain("data-testid=\"import-products\"");
        (await GetOkAsync(manager, ImportPath)).ShouldContain("Import products");

        var noToken = await owner.PostAsync(new Uri(previewPath + "/confirm", UriKind.Relative), new FormUrlEncodedContent([]));
        noToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProductsAsync(_studio.Id)).ShouldBeEmpty();
    }

    private static async Task<string> GetOkAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static async Task<string> TokenAsync(HttpClient client)
        => IdentityTestClient.AntiForgeryToken(await GetOkAsync(client, ImportPath));

    private static async Task<FormUrlEncodedContent> AntiForgeryFormAsync(HttpClient client)
        => new([new KeyValuePair<string, string>("__RequestVerificationToken", await TokenAsync(client))]);

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] content, string fileName)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(await TokenAsync(client)), "__RequestVerificationToken");
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "File", fileName);
        return await client.PostAsync(new Uri(ImportPath, UriKind.Relative), form);
    }

    private static async Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string previewPath)
    {
        var token = IdentityTestClient.AntiForgeryToken(await GetOkAsync(client, previewPath));
        return await client.PostAsync(
            new Uri(previewPath + "/confirm", UriKind.Relative),
            new FormUrlEncodedContent([new KeyValuePair<string, string>("__RequestVerificationToken", token)]));
    }

    private static string Count(string html, string testId)
    {
        var match = Regex.Match(html, $"data-testid=\"{testId}\"[^>]*>\\s*(?<value>\\d+)\\s*<");
        match.Success.ShouldBeTrue($"no {testId} in the page");
        return match.Groups["value"].Value;
    }

    private Task<HttpClient> OwnerClientAsync(string host, Guid tenantId) => factory.SignedInClientAsync(host, tenantId, Role.Owner);

    private async Task<List<Product>> ProductsAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Products.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
    }

    private async Task<List<StockMovement>> MovementsAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().StockMovements.AsNoTracking().OrderBy(m => m.At).ThenBy(m => m.Id).ToListAsync();
    }

    [GeneratedRegex("^[a-f0-9]{64}$")]
    private static partial Regex TokenPattern();
}