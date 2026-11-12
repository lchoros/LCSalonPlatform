using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Salon.Core.Clients;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Clients;

/// <summary>
/// The import slice over HTTP (ADR 0019): upload → preview counts and statuses → confirm creates only the new rows with
/// normalised phones and the consent source, a second run adds nothing, the token is the salon's own, Receptionist is
/// denied, the flag hides the screen, and the file-level refusals are localized messages.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class ImportClientsTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string ImportPath = "/admin/clients/import";
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string BooksyFileName = "booksy-customers.csv";

    private const string BooksyCsv =
        "First Name,Last Name,Phone Number,Email,Birthday,Customer notes,Accepts Marketing\n" +
        "Olivia,Brown,07700 900123,Olivia.Brown@Example.com,1990-05-04,\"Prefers afternoons; says \"\"no fringe\"\"\",Yes\n" +
        "Zoë,O'Neill,+44 7700 900456,,,,No\n" +
        "Amelia,Taylor,07700 900789,Amelia@Example.com,,Regular colour client,yes\n" +
        "Madonna,,07700 900000,,,,\n" +
        "Jack,Jones,not a phone,jack@@example,,,\n";

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
    public async Task The_upload_form_renders_in_the_salon_language_with_the_limits_and_what_is_not_imported()
    {
        using var english = await OwnerClientAsync(HostA, _studio.Id);
        using var polish = await OwnerClientAsync(HostB, _polish.Id);

        var englishHtml = await GetOkAsync(english, ImportPath);
        var polishHtml = await GetOkAsync(polish, ImportPath);

        englishHtml.ShouldContain("data-testid=\"import-upload-form\"");
        englishHtml.ShouldContain("Import clients");
        englishHtml.ShouldContain("CSV file");
        englishHtml.ShouldContain("5000 rows");
        englishHtml.ShouldContain("health information are not imported");
        englishHtml.ShouldContain("__RequestVerificationToken");
        englishHtml.ShouldContain("enctype=\"multipart/form-data\"");
        polishHtml.ShouldContain("Importuj klientów");
        polishHtml.ShouldContain("Plik CSV");
        polishHtml.ShouldContain("Booksy");
    }

    [Fact]
    public async Task Upload_shows_a_preview_with_counts_statuses_and_problems()
    {
        await CreateAsync(_studio.Id, new CreateClientRequest("Olivia", "Brown", Phone: "+44 7700 900123"));
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var response = await UploadAsync(client, Encoding.UTF8.GetBytes(BooksyCsv), BooksyFileName);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var previewPath = response.Headers.Location.ShouldNotBeNull().OriginalString;
        previewPath.ShouldStartWith(ImportPath + "/");
        TokenPattern().IsMatch(previewPath[(ImportPath.Length + 1)..]).ShouldBeTrue("the preview is addressed by a random token, not by the rows");

        var html = await GetOkAsync(client, previewPath);
        html.ShouldContain("data-testid=\"import-preview\"");
        html.ShouldContain(BooksyFileName);
        html.ShouldContain("Booksy");
        Count(html, "count-new").ShouldBe("2");
        Count(html, "count-duplicate").ShouldBe("1");
        Count(html, "count-invalid").ShouldBe("2");
        Regex.Count(html, "data-testid=\"import-row\"").ShouldBe(5);
        html.ShouldContain("data-status=\"New\"");
        html.ShouldContain("data-status=\"Duplicate\"");
        html.ShouldContain("data-status=\"Invalid\"");
        html.ShouldContain("Last name is missing.");
        html.ShouldContain("A client with this phone number already exists.");
        html.ShouldContain("The phone number is not valid");
        html.ShouldContain("+447700900456", Case.Sensitive, "the preview shows the number as it will be stored");
        html.ShouldContain("data-testid=\"import-confirm\"");
        html.ShouldContain("Import 2 new clients");
        html.ShouldNotContain("data-testid=\"import-more\"", Case.Sensitive, "five rows fit in the first fifty");
        (await ClientsAsync(_studio.Id)).Count.ShouldBe(1, "a preview writes nothing");
    }

    [Fact]
    public async Task Confirm_creates_only_the_new_rows_with_normalised_contact_details_and_the_consent_source()
    {
        await CreateAsync(_studio.Id, new CreateClientRequest("Olivia", "Brown", Phone: "+44 7700 900123"));
        using var client = await OwnerClientAsync(HostA, _studio.Id);
        var previewPath = (await UploadAsync(client, Encoding.UTF8.GetBytes(BooksyCsv), BooksyFileName)).Headers.Location!.OriginalString;

        var confirm = await ConfirmAsync(client, previewPath);

        confirm.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var resultPath = confirm.Headers.Location.ShouldNotBeNull().OriginalString;
        var html = await GetOkAsync(client, resultPath);
        html.ShouldContain("data-testid=\"import-result\"");
        Count(html, "result-created").ShouldBe("2");
        html.ShouldContain("1 already existed, 2 could not be imported.");
        html.ShouldContain("href=\"/admin/clients\"");

        var clients = await ClientsAsync(_studio.Id);
        clients.Count.ShouldBe(3);
        var zoe = clients.Single(c => c.FirstName == "Zoë");
        zoe.LastName.ShouldBe("O'Neill");
        zoe.Phone.ShouldBe("+447700900456");
        zoe.Email.ShouldBeNull();
        zoe.MarketingConsent.ShouldBeFalse();
        zoe.MarketingConsentWording.ShouldBeNull();
        var amelia = clients.Single(c => c.FirstName == "Amelia");
        amelia.Phone.ShouldBe("+447700900789");
        amelia.Email.ShouldBe("amelia@example.com");
        amelia.Notes.ShouldBe("Regular colour client");
        amelia.MarketingConsent.ShouldBeTrue();
        amelia.MarketingConsentAt.ShouldNotBeNull();
        amelia.MarketingConsentWording.ShouldNotBeNull().ShouldContain(BooksyFileName);
        amelia.MarketingConsentWording.ShouldContain("Booksy");
        clients.ShouldAllBe(c => c.HealthNotesProtected == null, "imports never write health data");
        clients.ShouldAllBe(c => c.TenantId == _studio.Id);
        (await ClientsAsync(_polish.Id)).ShouldBeEmpty();

        // The token is spent: the preview is gone and a second confirm cannot run the import again.
        (await client.PostAsync(new Uri(previewPath + "/confirm", UriKind.Relative), await AntiForgeryFormAsync(client))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await ClientsAsync(_studio.Id)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Running_the_same_file_twice_adds_nothing()
    {
        using var client = await OwnerClientAsync(HostA, _studio.Id);
        var first = (await UploadAsync(client, Encoding.UTF8.GetBytes(BooksyCsv), BooksyFileName)).Headers.Location!.OriginalString;
        await ConfirmAsync(client, first);
        (await ClientsAsync(_studio.Id)).Count.ShouldBe(3);

        var second = (await UploadAsync(client, Encoding.UTF8.GetBytes(BooksyCsv), BooksyFileName)).Headers.Location!.OriginalString;
        var preview = await GetOkAsync(client, second);
        Count(preview, "count-new").ShouldBe("0");
        Count(preview, "count-duplicate").ShouldBe("3");
        preview.ShouldContain("There is nothing new to import.");
        preview.ShouldNotContain("data-testid=\"import-confirm\"");

        var confirm = await client.PostAsync(new Uri(second + "/confirm", UriKind.Relative), await AntiForgeryFormAsync(client));
        confirm.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await ClientsAsync(_studio.Id)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_polish_salon_previews_a_versum_file_saved_in_windows_1250()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var versum = Encoding.GetEncoding(1250).GetBytes(
            "Imię;Nazwisko;Telefon komórkowy;E-mail;Opis;Zgoda marketingowa\n" +
            "Małgorzata;Łódźka;601 234 567;malgorzata@example.pl;Lubi kawę;Tak\n" +
            "Zbigniew;Źrebięcki;+48 512 345 678;;;Nie\n");
        using var client = await OwnerClientAsync(HostB, _polish.Id);

        var previewPath = (await UploadAsync(client, versum, "klienci.csv")).Headers.Location!.OriginalString;
        var html = await GetOkAsync(client, previewPath);

        html.ShouldContain("Małgorzata Łódźka");
        html.ShouldContain("Źrebięcki");
        html.ShouldContain("+48601234567");
        html.ShouldContain("Versum");
        Count(html, "count-new").ShouldBe("2");
        html.ShouldContain("Importuj 2 nowych klientów");

        await ConfirmAsync(client, previewPath);
        var clients = await ClientsAsync(_polish.Id);
        clients.Count.ShouldBe(2);
        clients.Single(c => c.FirstName == "Małgorzata").Phone.ShouldBe("+48601234567");
        clients.Single(c => c.FirstName == "Małgorzata").Notes.ShouldBe("Lubi kawę");
    }

    [Fact]
    public async Task Another_salon_cannot_see_or_confirm_the_token()
    {
        using var studio = await OwnerClientAsync(HostA, _studio.Id);
        using var polish = await OwnerClientAsync(HostB, _polish.Id);
        var previewPath = (await UploadAsync(studio, Encoding.UTF8.GetBytes(BooksyCsv), BooksyFileName)).Headers.Location!.OriginalString;

        (await polish.GetAsync(new Uri(previewPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await polish.PostAsync(new Uri(previewPath + "/confirm", UriKind.Relative), await AntiForgeryFormAsync(polish))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ClientsAsync(_polish.Id)).ShouldBeEmpty();
        (await ClientsAsync(_studio.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_or_expired_token_goes_back_to_the_upload_with_a_message()
    {
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var response = await client.GetAsync(new Uri(ImportPath + "/" + new string('0', 64), UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe(ImportPath);
        var html = await GetOkAsync(client, ImportPath);
        html.ShouldContain("The preview has expired. Upload the file again.");
    }

    [Fact]
    public async Task A_receptionist_is_denied_and_a_manager_is_not()
    {
        using var manager = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Manager);
        using var receptionist = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        (await manager.GetAsync(new Uri(ImportPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var denied = await receptionist.GetAsync(new Uri(ImportPath, UriKind.Relative));
        denied.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        (await receptionist.PostAsync(new Uri(ImportPath, UriKind.Relative), new MultipartFormDataContent())).StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task With_the_flag_off_the_screen_does_not_exist()
    {
        await factory.CreateUserAsync(_studio.Id, "owner@" + HostA, Role.Owner);
        using var flagOff = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection([new KeyValuePair<string, string?>("Features:ClientImport", "false")])));
        using var client = flagOff.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri($"https://{HostA}"),
        });
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
        (await IdentityTestClient.LoginAsync(client, "owner@" + HostA, IdentityTestClient.Password)).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        (await client.GetAsync(new Uri(ImportPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync(new Uri("/admin/clients", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK, "the rest of the clients module is unaffected");
    }

    [Fact]
    public async Task A_file_nobody_recognises_is_refused_with_the_columns_found()
    {
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var response = await UploadAsync(client, Encoding.UTF8.GetBytes("Foo,Bar\n1,2\n"), "export.csv");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("data-testid=\"import-error\"");
        html.ShouldContain("We could not recognise the columns");
        html.ShouldContain("Foo, Bar");
    }

    [Fact]
    public async Task An_oversized_file_and_a_missing_file_are_refused_with_messages()
    {
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var tooLarge = await UploadAsync(client, new byte[(2 * 1024 * 1024) + 1], "big.csv");
        tooLarge.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await tooLarge.Content.ReadAsStringAsync()).ShouldContain("The file is too large; the limit is 2 MB.");

        using var noFile = new MultipartFormDataContent { { new StringContent(await TokenAsync(client)), "__RequestVerificationToken" } };
        var missing = await client.PostAsync(new Uri(ImportPath, UriKind.Relative), noFile);
        missing.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await missing.Content.ReadAsStringAsync()).ShouldContain("Choose a CSV file.");
    }

    [Fact]
    public async Task Without_a_sign_in_the_screen_asks_for_login_and_the_bare_domain_has_nothing()
    {
        using var anonymous = factory.ClientFor(HostA);
        using var platform = factory.ClientFor("localhost");

        var response = await anonymous.GetAsync(new Uri(ImportPath, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");
        (await platform.GetAsync(new Uri(ImportPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<string> GetOkAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return await response.Content.ReadAsStringAsync();
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
        var preview = await GetOkAsync(client, previewPath);
        var token = IdentityTestClient.AntiForgeryToken(preview);
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

    private async Task<Guid> CreateAsync(Guid tenantId, CreateClientRequest request)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<IClientService>().CreateAsync(request);
    }

    private async Task<List<Client>> ClientsAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Clients.AsNoTracking().OrderBy(c => c.LastName).ToListAsync();
    }

    [GeneratedRegex("^[a-f0-9]{64}$")]
    private static partial Regex TokenPattern();
}
