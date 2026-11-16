using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Identity;
using Salon.Core.Products;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Products;

/// <summary>
/// The product list with today's counts as a file (ADR 0047): every product by name, the ones no longer in use marked
/// so, written in the reader's language with their separator and decimal sign, under the headings the import reads
/// (ADR 0045), so the file of one salon is the product list of another. Only this salon, only for who may change the
/// catalogue.
/// </summary>
[Collection(SharedDatabase.Name)]
public class ExportProductsTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string Path = "/admin/products/stock.csv";
    private const string ImportPath = "/admin/products/import";
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string HostC = "second-chair.localhost";
    private const string Header = "Name,Brand,Code,Price,Cost,Currency,In stock,Low stock at,In use";

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
    public async Task The_manager_downloads_every_product_by_name_with_what_is_on_the_shelf()
    {
        await ProductAsync(_studio.Id, "Shampoo 250 ml", "Salon Range", "SH-250", 1250.5m, 8, 2, cost: 1000.25m);
        await ProductAsync(_studio.Id, "colour tube 60 ml", null, null, null, 12, null, cost: 4m);
        await ProductAsync(_studio.Id, "Hair oil 50 ml", null, "HO-50", 18m, 0, 1, active: false);
        using var manager = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Manager);

        var page = WebUtility.HtmlDecode(await (await manager.GetAsync(new Uri("/admin/products", UriKind.Relative))).Content.ReadAsStringAsync());
        page.ShouldContain("href=\"/admin/products/stock.csv\" data-testid=\"export-stock\"");
        page.ShouldContain("Download the stock list");

        var response = await manager.GetAsync(new Uri(Path, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType.ShouldNotBeNull().ToString().ShouldBe("text/csv; charset=utf-8");
        var today = SystemClock.Instance.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb["Europe/London"]).Date;
        response.Content.Headers.ContentDisposition.ShouldNotBeNull().FileName.ShouldBe($"stock-{LocalDatePattern.Iso.Format(today)}.csv");
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Take(3).ShouldBe(Encoding.UTF8.GetPreamble(), "Excel needs the byte order mark to read the letters");
        var lines = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n");
        lines.ShouldBe(
        [
            Header,
            "colour tube 60 ml,,,,4.00,GBP,12,,Yes",
            "Hair oil 50 ml,,HO-50,18.00,,GBP,0,1,No",
            "Shampoo 250 ml,Salon Range,SH-250,1250.50,1000.25,GBP,8,2,Yes",
            string.Empty,
        ]);
    }

    [Fact]
    public async Task The_file_of_one_salon_is_read_by_the_import_of_another()
    {
        await ProductAsync(_studio.Id, "Shampoo 250 ml", "Salon Range", "SH-250", 12.5m, 8, 2, cost: 6m);
        await ProductAsync(_studio.Id, "Colour tube 60 ml", null, null, null, 12, null);
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var file = await (await owner.GetAsync(new Uri(Path, UriKind.Relative))).Content.ReadAsByteArrayAsync();
        var second = await factory.CreateTenantAsync("second-chair", name: "Second Chair");
        using var other = await factory.SignedInClientAsync(HostC, second.Id, Role.Owner);

        await ImportAsync(other, file);

        var products = await ProductsAsync(second.Id);
        products.Select(p => (p.Name, p.Brand, p.Code, p.RetailPrice, p.CostPrice, p.StockOnHand, p.LowStockAt)).ShouldBe(
        [
            ("Colour tube 60 ml", null, null, null, null, 12, null),
            ("Shampoo 250 ml", "Salon Range", "SH-250", 12.5m, 6m, 8, 2),
        ]);
    }

    [Fact]
    public async Task A_Polish_salon_gets_its_own_headings_a_semicolon_and_a_decimal_comma_and_its_import_reads_them()
    {
        await ProductAsync(_studio.Id, "Shampoo 250 ml", null, null, 12.5m, 8, null);
        await ProductAsync(_polish.Id, "Szampon 250 ml", "Seria salonowa", "SZ-250", 45.9m, 6, 2, currency: "PLN", cost: 21.5m);
        using var owner = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);

        var bytes = await (await owner.GetAsync(new Uri(Path, UriKind.Relative))).Content.ReadAsByteArrayAsync();
        var lines = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        lines.ShouldBe(
        [
            "Nazwa;Marka;Kod;Cena;Koszt;Waluta;Stan;Stan minimalny;W użyciu",
            "Szampon 250 ml;Seria salonowa;SZ-250;45,90;21,50;PLN;6;2;Tak",
        ]);

        // The salon's own file read back: the product is known, so nothing is added and nothing changes.
        await ImportAsync(owner, bytes);
        var product = (await ProductsAsync(_polish.Id)).ShouldHaveSingleItem();
        product.RetailPrice.ShouldBe(45.9m);
        product.StockOnHand.ShouldBe(6);
    }

    [Fact]
    public async Task A_name_that_looks_like_a_formula_or_holds_a_comma_cannot_break_the_file()
    {
        await ProductAsync(_studio.Id, "=SUM(A1:A9)", "Wash, care", null, 5m, 1, null);
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var bytes = await (await owner.GetAsync(new Uri(Path, UriKind.Relative))).Content.ReadAsByteArrayAsync();
        var row = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[1];

        // An apostrophe in front makes it text; the quotes keep the comma inside one cell.
        row.ShouldBe("'=SUM(A1:A9),\"Wash, care\",,5.00,,GBP,1,,Yes");
    }

    [Fact]
    public async Task The_file_is_for_people_who_may_change_the_catalogue()
    {
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        using var anonymous = factory.ClientFor(HostA);
        using var platform = factory.CreateClient();
        platform.DefaultRequestHeaders.Host = "localhost";

        var empty = await owner.GetAsync(new Uri(Path, UriKind.Relative));
        empty.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bytes = await empty.Content.ReadAsByteArrayAsync();
        Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).ShouldBe(Header + "\r\n", "a salon without products gets a file with its header only");
        (await PageAsync(owner)).ShouldNotContain("data-testid=\"export-stock\"", Case.Sensitive, "nothing to download while there is no product");

        await ProductAsync(_studio.Id, "Shampoo 250 ml", null, null, 12.5m, 8, null);

        var denied = await desk.GetAsync(new Uri(Path, UriKind.Relative));
        denied.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        (await PageAsync(desk)).ShouldNotContain("data-testid=\"export-stock\"", Case.Sensitive, "the desk sees the shelves, not the file");
        (await PageAsync(owner)).ShouldContain("data-testid=\"export-stock\"");
        var login = await anonymous.GetAsync(new Uri(Path, UriKind.Relative));
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        login.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");
        (await platform.GetAsync(new Uri(Path, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<string> PageAsync(HttpClient client)
    {
        var response = await client.GetAsync(new Uri("/admin/products", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Uploads the file to the import and confirms the preview (ADR 0045).</summary>
    private static async Task ImportAsync(HttpClient client, byte[] content)
    {
        var formPage = await client.GetAsync(new Uri(ImportPath, UriKind.Relative));
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(IdentityTestClient.AntiForgeryToken(await formPage.Content.ReadAsStringAsync())), "__RequestVerificationToken");
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "File", "stock.csv");
        var upload = await client.PostAsync(new Uri(ImportPath, UriKind.Relative), form);
        upload.StatusCode.ShouldBe(HttpStatusCode.Redirect, await upload.Content.ReadAsStringAsync());
        var previewPath = upload.Headers.Location!.OriginalString;
        previewPath.ShouldStartWith(ImportPath + "/", Case.Sensitive, "the file could not be read by the import");

        var preview = await client.GetAsync(new Uri(previewPath, UriKind.Relative));
        var token = IdentityTestClient.AntiForgeryToken(await preview.Content.ReadAsStringAsync());
        var confirm = await client.PostAsync(
            new Uri(previewPath + "/confirm", UriKind.Relative),
            new FormUrlEncodedContent([new KeyValuePair<string, string>("__RequestVerificationToken", token)]));
        confirm.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    private async Task ProductAsync(Guid tenantId, string name, string? brand, string? code, decimal? price, int onHand, int? lowAt, bool active = true, string currency = "GBP", decimal? cost = null)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        db.Products.Add(new Product { Name = name, Brand = brand, Code = code, RetailPrice = price, CostPrice = cost, Currency = currency, StockOnHand = onHand, LowStockAt = lowAt, IsActive = active });
        await db.SaveChangesAsync();
    }

    private async Task<List<Product>> ProductsAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Products.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
    }
}