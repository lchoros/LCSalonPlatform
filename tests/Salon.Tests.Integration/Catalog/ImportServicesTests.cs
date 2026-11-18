using System.Net;
using System.Net.Http.Headers;
using System.Text;
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
/// The price list from a file, and as a file (ADR 0066), over HTTP: upload, a preview that says for every row whether
/// it adds a service, changes one (the old and the new time and price), changes nothing or is left out and why, and a
/// confirm that does exactly that. Nothing is removed and a service the file does not name is left alone. The salon's
/// own file can be edited and read back, in English and in Polish. The token is the salon's own, and only who may
/// change the catalogue gets in. Studio 20 has "Cut" (45 minutes, £45, under Hair), "Colour" (120 minutes, £80, a
/// 20% deposit, under Hair) and "Old perm" (90 minutes, £60, not in use, no heading).
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class ImportServicesTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string ImportPath = "/admin/services/import";
    private const string FilePath = "/admin/services/price-list.csv";
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string FileName = "prices.csv";

    /// <summary>One changed, one the same, three new (one under a heading the salon has, one under a new one, one under none), one said twice, two that cannot be read.</summary>
    private const string Csv =
        "Category,Service,Minutes,Price,Deposit %\n" +
        "Hair,cut,45,47.50,\n" +
        "Hair,Colour,120,£80.00,20\n" +
        "hair,Blow-dry,30,25,\n" +
        "Nails,Gel nails,60,30.00,10\n" +
        ",Fringe trim,15,8,\n" +
        "Hair,CUT,60,50,\n" +
        "Nails,Manicure,47,20,\n" +
        "Nails,Pedicure,45,,\n";

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _hair;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var hair = new ServiceCategory { Name = "Hair", SortOrder = 1 };
        db.ServiceCategories.Add(hair);
        db.Services.AddRange(
            new Service { Name = "Cut", Duration = Duration.FromMinutes(45), Price = 45m, Currency = "GBP", SortOrder = 1, CategoryId = hair.Id },
            new Service { Name = "Colour", Duration = Duration.FromMinutes(120), Price = 80m, Currency = "GBP", SortOrder = 2, CategoryId = hair.Id, DepositPercent = 20m },
            new Service { Name = "Old perm", Duration = Duration.FromMinutes(90), Price = 60m, Currency = "GBP", SortOrder = 3, IsActive = false });
        await db.SaveChangesAsync();
        _hair = hair.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_services_page_links_to_the_file_both_ways_and_the_form_says_what_happens()
    {
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var list = await GetOkAsync(client, "/admin/services");
        list.ShouldContain($"href=\"{ImportPath}\" data-testid=\"import-services\"");
        list.ShouldContain($"href=\"{FilePath}\" data-testid=\"download-price-list\"");

        var form = await GetOkAsync(client, ImportPath);
        form.ShouldContain("Price list from a file");
        form.ShouldContain("Category,Service,Minutes,Price,Deposit %");
        form.ShouldContain("A service you already have (the same name) gets the time, the price and the deposit from the file.");
        form.ShouldContain("Nothing is removed.");
        form.ShouldContain("Visits already booked keep the price they were booked at.");
        form.ShouldContain($"href=\"{FilePath}\"");

        // The price list is one of the files the salon can take away (ADR 0051).
        var yours = await GetOkAsync(client, "/admin/settings/your-data");
        yours.ShouldContain("3 services.");
        yours.ShouldContain($"href=\"{FilePath}\"");
    }

    [Fact]
    public async Task The_preview_says_what_each_row_would_do_and_changes_nothing()
    {
        using var client = await OwnerClientAsync(HostA, _studio.Id);
        var before = await ServicesAsync(_studio.Id);

        var upload = await UploadAsync(client, Encoding.UTF8.GetBytes(Csv), FileName);

        upload.StatusCode.ShouldBe(HttpStatusCode.Redirect, await upload.Content.ReadAsStringAsync());
        var previewPath = upload.Headers.Location!.OriginalString;
        TokenPattern().IsMatch(previewPath[(ImportPath.Length + 1)..]).ShouldBeTrue(previewPath);
        var html = await GetOkAsync(client, previewPath);
        html.ShouldContain("prices.csv · rows: 8");
        (Count(html, "count-new"), Count(html, "count-changed"), Count(html, "count-unchanged"), Count(html, "count-left-out")).ShouldBe(("3", "1", "1", "3"));

        // What changes is shown as it is and as it will be; what is added, as it will be saved.
        html.ShouldContain("£45.00 → £47.50");
        html.ShouldNotContain("45 min → 45 min");
        html.ShouldContain("Nails · 60 min · £30.00 · deposit 10%");
        html.ShouldContain("hair · 30 min · £25.00");
        html.ShouldContain("15 min · £8.00");
        html.ShouldContain("120 min · £80.00");

        // Why a row is left out.
        html.ShouldContain("Repeats an earlier row of this file; the earlier row is used.");
        html.ShouldContain("The time must be whole minutes from 5 to 600, in steps of 5, such as 45.");
        html.ShouldContain("A new service needs its price.");
        html.ShouldContain("Add 3 and change 1");
        html.ShouldContain("Nothing has changed yet.");

        (await ServicesAsync(_studio.Id)).Select(Shape).ShouldBe(before.Select(Shape));
        (await CategoriesAsync(_studio.Id)).ShouldBe(["Hair"]);
    }

    [Fact]
    public async Task Confirming_adds_the_new_services_under_their_headings_and_changes_the_time_and_price_of_the_others()
    {
        using var client = await OwnerClientAsync(HostA, _studio.Id);
        var previewPath = (await UploadAsync(client, Encoding.UTF8.GetBytes(Csv), FileName)).Headers.Location!.OriginalString;

        var confirm = await ConfirmAsync(client, previewPath);

        confirm.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var result = await GetOkAsync(client, confirm.Headers.Location!.OriginalString);
        result.ShouldContain("The price list is read in");
        (Count(result, "result-added"), Count(result, "result-updated")).ShouldBe(("3", "1"));
        result.ShouldContain("1 category was added with them.");
        result.ShouldContain("Stayed as they were: 1. Rows left out: 3.");

        var services = await ServicesAsync(_studio.Id);
        var categories = await CategoryIdsAsync(_studio.Id);
        // "hair" in the file is the heading the salon has; "Nails" is new.
        categories.Keys.Order().ToArray().ShouldBe(["Hair", "Nails"]);
        services.Select(s => (s.Name, Minutes: (int)s.Duration.TotalMinutes, s.Price, s.DepositPercent, s.CategoryId, s.IsActive, s.Currency)).ShouldBe(
        [
            ("Cut", 45, 47.50m, null, _hair, true, "GBP"),
            ("Colour", 120, 80m, 20m, _hair, true, "GBP"),
            ("Old perm", 90, 60m, null, null, false, "GBP"),
            ("Blow-dry", 30, 25m, null, _hair, true, "GBP"),
            ("Gel nails", 60, 30m, 10m, categories["Nails"], true, "GBP"),
            ("Fringe trim", 15, 8m, null, null, true, "GBP"),
        ]);
        services.Select(s => s.SortOrder).ShouldBe([1, 2, 3, 4, 5, 6], "new services go after the ones the salon has, in the file's order");
    }

    [Fact]
    public async Task The_same_file_again_has_nothing_to_do_and_confirming_twice_changes_nothing_more()
    {
        using var client = await OwnerClientAsync(HostA, _studio.Id);
        var previewPath = (await UploadAsync(client, Encoding.UTF8.GetBytes(Csv), FileName)).Headers.Location!.OriginalString;
        (await ConfirmAsync(client, previewPath)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var after = (await ServicesAsync(_studio.Id)).Select(Shape).ToList();

        // A second press of "confirm" (the page was open twice): the result again, nothing run.
        var again = await client.PostAsync(new Uri(previewPath + "/confirm", UriKind.Relative), await AntiForgeryFormAsync(client));
        again.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await GetOkAsync(client, again.Headers.Location!.OriginalString)).ShouldContain("The price list is read in");

        // The same file once more: every row is known and says nothing new.
        var second = (await UploadAsync(client, Encoding.UTF8.GetBytes(Csv), FileName)).Headers.Location!.OriginalString;
        var html = await GetOkAsync(client, second);
        (Count(html, "count-new"), Count(html, "count-changed"), Count(html, "count-unchanged"), Count(html, "count-left-out")).ShouldBe(("0", "0", "5", "3"));
        html.ShouldContain("The file would add nothing and change nothing.");
        html.ShouldNotContain("data-testid=\"import-confirm\"");
        (await ServicesAsync(_studio.Id)).Select(Shape).ShouldBe(after);
        (await CategoriesAsync(_studio.Id)).ShouldBe(["Hair", "Nails"]);
    }

    [Fact]
    public async Task A_service_added_by_hand_after_the_preview_is_updated_not_added_twice()
    {
        using var client = await OwnerClientAsync(HostA, _studio.Id);
        var previewPath = (await UploadAsync(client, Encoding.UTF8.GetBytes(Csv), FileName)).Headers.Location!.OriginalString;
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            db.Services.Add(new Service { Name = "gel nails", Duration = Duration.FromMinutes(30), Price = 20m, Currency = "GBP", SortOrder = 9 });
            await db.SaveChangesAsync();
        }

        var confirm = await ConfirmAsync(client, previewPath);

        var result = await GetOkAsync(client, confirm.Headers.Location!.OriginalString);
        (Count(result, "result-added"), Count(result, "result-updated")).ShouldBe(("2", "2"));
        var gel = (await ServicesAsync(_studio.Id)).Where(s => string.Equals(s.Name, "gel nails", StringComparison.OrdinalIgnoreCase)).ShouldHaveSingleItem();
        (gel.Name, (int)gel.Duration.TotalMinutes, gel.Price, gel.DepositPercent, gel.CategoryId).ShouldBe(("gel nails", 60, 30m, 10m, null), "its time, price and deposit from the file; its name and heading as typed by hand");
    }

    [Fact]
    public async Task The_salons_own_file_is_downloaded_edited_and_read_back()
    {
        using var manager = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Manager);

        var response = await manager.GetAsync(new Uri(FilePath, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType.ShouldNotBeNull().ToString().ShouldBe("text/csv; charset=utf-8");
        var today = SystemClock.Instance.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb["Europe/London"]).Date;
        response.Content.Headers.ContentDisposition.ShouldNotBeNull().FileName.ShouldBe($"price-list-{today:yyyy-MM-dd}.csv");
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Take(3).ShouldBe(Encoding.UTF8.GetPreamble(), "Excel needs the byte order mark to read the letters");
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        text.Split("\r\n").ShouldBe(
        [
            "Category,Service,Minutes,Price,Deposit %,In use",
            "Hair,Cut,45,45.00,,Yes",
            "Hair,Colour,120,80.00,20,Yes",
            ",Old perm,90,60.00,,No",
            string.Empty,
        ]);

        // Read back as it is: nothing to do. With a price rise typed into it: one service changes.
        var same = await GetOkAsync(manager, (await UploadAsync(manager, bytes, "price-list.csv")).Headers.Location!.OriginalString);
        (Count(same, "count-new"), Count(same, "count-changed"), Count(same, "count-unchanged"), Count(same, "count-left-out")).ShouldBe(("0", "0", "3", "0"));
        var edited = Encoding.UTF8.GetBytes(text.Replace("Hair,Colour,120,80.00,20,Yes", "Hair,Colour,135,84.00,25,Yes", StringComparison.Ordinal));
        var previewPath = (await UploadAsync(manager, edited, "price-list.csv")).Headers.Location!.OriginalString;
        var preview = await GetOkAsync(manager, previewPath);
        (Count(preview, "count-new"), Count(preview, "count-changed"), Count(preview, "count-unchanged")).ShouldBe(("0", "1", "2"));
        preview.ShouldContain("120 min → 135 min · £80.00 → £84.00 · deposit 20% → deposit 25%");
        await ConfirmAsync(manager, previewPath);

        var colour = (await ServicesAsync(_studio.Id)).Single(s => s.Name == "Colour");
        ((int)colour.Duration.TotalMinutes, colour.Price, colour.DepositPercent, colour.IsActive).ShouldBe((135, 84m, 25m, true));
        (await ServicesAsync(_studio.Id)).Single(s => s.Name == "Old perm").IsActive.ShouldBeFalse("the file does not bring a service back into use");
    }

    [Fact]
    public async Task A_Polish_salon_gets_its_own_headings_a_semicolon_and_a_decimal_comma_and_reads_its_file_back()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var owner = await OwnerClientAsync(HostB, _polish.Id);

        // A price list typed in a spreadsheet and saved in Windows-1250, as Polish Excel does.
        var typed = Encoding.GetEncoding(1250).GetBytes(
            "Kategoria;Usługa;Czas trwania;Cena;Zadatek\n" +
            "Włosy;Strzyżenie;45;120,50 zł;\n" +
            "Włosy;Koloryzacja;120;350,00;20%\n");
        var previewPath = (await UploadAsync(owner, typed, "cennik.csv")).Headers.Location!.OriginalString;
        var html = await GetOkAsync(owner, previewPath);
        html.ShouldContain("Strzyżenie");

        // The price is written with a no-break space before the currency, as everywhere else.
        html.ShouldContain("Włosy · 45 min · 120,50 zł");
        html.ShouldContain("Dodaj 2 i zmień 0");
        await ConfirmAsync(owner, previewPath);
        (await ServicesAsync(_polish.Id)).Select(s => (s.Name, (int)s.Duration.TotalMinutes, s.Price, s.DepositPercent, s.Currency))
            .ShouldBe([("Strzyżenie", 45, 120.50m, null, "PLN"), ("Koloryzacja", 120, 350m, 20m, "PLN")]);

        var bytes = await (await owner.GetAsync(new Uri(FilePath, UriKind.Relative))).Content.ReadAsByteArrayAsync();
        Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ShouldBe(
        [
            "Kategoria;Usługa;Minuty;Cena;Zadatek %;W użyciu",
            "Włosy;Strzyżenie;45;120,50;;Tak",
            "Włosy;Koloryzacja;120;350,00;20;Tak",
        ]);
        var back = await GetOkAsync(owner, (await UploadAsync(owner, bytes, "cennik.csv")).Headers.Location!.OriginalString);
        (Count(back, "count-new"), Count(back, "count-changed"), Count(back, "count-unchanged"), Count(back, "count-left-out")).ShouldBe(("0", "0", "2", "0"));
        (await ServicesAsync(_studio.Id)).Count.ShouldBe(3, "the other salon's price list is its own");
    }

    [Theory]
    [InlineData("Price,Minutes\n45,45\n", "We could not find a column with the name of the service. Call it \"Service\" or \"Name\". Columns found: Price, Minutes")]
    [InlineData("Service,Price\n", "The file has no service rows.")]
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

        var spreadsheet = await UploadAsync(client, [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00], "prices.xlsx");
        WebUtility.HtmlDecode(await spreadsheet.Content.ReadAsStringAsync()).ShouldContain("The file is not a text CSV file. Save the list as CSV, not as a spreadsheet.");

        var nothing = await client.PostAsync(new Uri(ImportPath, UriKind.Relative), await AntiForgeryFormAsync(client));
        nothing.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await nothing.Content.ReadAsStringAsync()).ShouldContain("Choose a CSV file.");
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
        (await ServicesAsync(_polish.Id)).ShouldBeEmpty();
        (await ServicesAsync(_studio.Id)).Count.ShouldBe(3);

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

        foreach (var path in new[] { ImportPath, previewPath, FilePath })
        {
            var get = await receptionist.GetAsync(new Uri(path, UriKind.Relative));
            get.StatusCode.ShouldBe(HttpStatusCode.Redirect, path);
            get.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied", path);
        }

        (await GetOkAsync(manager, ImportPath)).ShouldContain("Price list from a file");

        var noToken = await owner.PostAsync(new Uri(previewPath + "/confirm", UriKind.Relative), new FormUrlEncodedContent([]));
        noToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ServicesAsync(_studio.Id)).Count.ShouldBe(3);
        (await ServicesAsync(_studio.Id)).Single(s => s.Name == "Cut").Price.ShouldBe(45m);
    }

    private static (string Name, Duration Duration, decimal Price, decimal? DepositPercent, Guid? CategoryId, bool IsActive, int SortOrder) Shape(Service s)
        => (s.Name, s.Duration, s.Price, s.DepositPercent, s.CategoryId, s.IsActive, s.SortOrder);

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

    private async Task<List<Service>> ServicesAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Services.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToListAsync();
    }

    private async Task<List<string>> CategoriesAsync(Guid tenantId) => [.. (await CategoryIdsAsync(tenantId)).Keys.Order(StringComparer.Ordinal)];

    private async Task<Dictionary<string, Guid>> CategoryIdsAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().ServiceCategories.AsNoTracking().ToDictionaryAsync(c => c.Name, c => c.Id);
    }

    [GeneratedRegex("^[a-f0-9]{64}$")]
    private static partial Regex TokenPattern();
}