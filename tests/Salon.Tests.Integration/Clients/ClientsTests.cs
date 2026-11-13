using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Clients;
using Salon.Core.Clients.Events;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Clients;

/// <summary>
/// The three Clients slices over HTTP (ADR 0011): the create form with localized messages, the normalised and encrypted
/// row, the ClientCreated outbox row without personal data, duplicate phone as a form message, search by name prefix and
/// by digits that never shows health notes, the card that does, and the gates (tenant, Clients.View, anonymous).
/// </summary>
[Collection(SharedDatabase.Name)]
public class ClientsTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string SearchPath = "/admin/clients";
    private const string FormPath = "/admin/clients/new";
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string HealthText = "Allergic to PPD; patch test negative";

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
    public async Task The_form_renders_for_a_salon_in_its_language_with_the_sensitive_data_hint()
    {
        using var english = await OwnerClientAsync(HostA, _studio.Id);
        using var polish = await OwnerClientAsync(HostB, _polish.Id);

        var englishHtml = await GetAsync(english, FormPath);
        var polishHtml = await GetAsync(polish, FormPath);

        englishHtml.ShouldContain("data-testid=\"create-client-form\"");
        englishHtml.ShouldContain("Add a client");
        englishHtml.ShouldContain("stored encrypted");
        englishHtml.ShouldContain("__RequestVerificationToken");
        polishHtml.ShouldContain("Dodaj klienta");
        polishHtml.ShouldContain("Numer telefonu");
        polishHtml.ShouldContain("Uwagi zdrowotne (alergie, test uczuleniowy)");
        polishHtml.ShouldContain("Ostatni test uczuleniowy");
        polishHtml.ShouldContain("Zgoda marketingowa");
    }

    [Fact]
    public async Task A_valid_post_creates_a_normalised_encrypted_row_and_queues_ClientCreated_without_personal_data()
    {
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var response = await PostAsync(client, new()
        {
            ["FirstName"] = "  Olivia ",
            ["LastName"] = "Brown",
            ["Phone"] = "07700 900123",
            ["Email"] = "Olivia@Example.com",
            ["Notes"] = "Prefers afternoons.",
            ["HealthNotes"] = HealthText,
            ["LastPatchTestOn"] = "2026-10-01",
            ["MarketingConsent"] = "true",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location.ShouldNotBeNull().OriginalString;
        location.ShouldStartWith("/admin/clients/");
        var id = Guid.Parse(location["/admin/clients/".Length..]);

        var row = (await ClientsAsync(_studio.Id)).ShouldHaveSingleItem();
        row.Id.ShouldBe(id);
        row.TenantId.ShouldBe(_studio.Id);
        row.FirstName.ShouldBe("Olivia");
        row.LastName.ShouldBe("Brown");
        row.Phone.ShouldBe("+447700900123");
        row.Email.ShouldBe("olivia@example.com");
        row.Notes.ShouldBe("Prefers afternoons.");
        row.HealthNotesProtected.ShouldNotBeNull();
        row.HealthNotesProtected.ShouldNotBe(HealthText);
        row.HealthNotesProtected.ShouldNotContain("Allergic to PPD", Case.Sensitive);
        row.LastPatchTestOn.ShouldBe(new LocalDate(2026, 10, 1));
        row.MarketingConsent.ShouldBeTrue();
        row.MarketingConsentAt.ShouldNotBeNull();
        row.MarketingConsentWording.ShouldNotBeNull().ShouldContain("Studio 20");
        row.IsActive.ShouldBeTrue();
        (await ClientsAsync(_polish.Id)).ShouldBeEmpty();

        await using var scope = factory.CreateTenantScope(null);
        var outbox = await scope.ServiceProvider.GetRequiredService<SalonDbContext>().OutboxMessages.AsNoTracking().ToListAsync();
        var message = outbox.ShouldHaveSingleItem();
        message.EventType.ShouldBe(typeof(ClientCreated).FullName);
        message.TenantId.ShouldBe(_studio.Id);
        using var payload = JsonDocument.Parse(message.Payload);
        payload.RootElement.GetProperty("clientId").GetGuid().ShouldBe(id);
        payload.RootElement.GetProperty("tenantId").GetGuid().ShouldBe(_studio.Id);
        payload.RootElement.EnumerateObject().Count().ShouldBe(2, "events carry ids only");
        message.Payload.ShouldNotContain("Olivia");
        message.Payload.ShouldNotContain("Brown");
        message.Payload.ShouldNotContain("7700900");
        message.Payload.ShouldNotContain("7700 900");
        message.Payload.ShouldNotContain("Allergic to PPD", Case.Sensitive);

        var card = await GetAsync(client, location);
        card.ShouldContain("data-testid=\"flash\"");
        card.ShouldContain("Olivia Brown");
    }

    [Fact]
    public async Task A_duplicate_phone_is_a_form_message_in_the_salon_language()
    {
        await CreateAsync(_studio.Id, new CreateClientRequest("Olivia", "Brown", Phone: "+44 7700 900123"));
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var response = await PostAsync(client, new()
        {
            ["FirstName"] = "Oliver",
            ["LastName"] = "Twist",
            ["Phone"] = "07700 900123",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("A client with this phone number already exists.");
        html.ShouldContain("input-validation-error");
        (await ClientsAsync(_studio.Id)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_invalid_post_re_renders_the_form_with_messages_in_the_salon_language()
    {
        using var client = await OwnerClientAsync(HostB, _polish.Id);

        var response = await PostAsync(client, new()
        {
            ["FirstName"] = "",
            ["LastName"] = "",
            ["Phone"] = "07700 900123",
            ["Email"] = "nie-email",
            ["LastPatchTestOn"] = "wczoraj",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("Podaj imię.");
        html.ShouldContain("Podaj nazwisko.");
        html.ShouldContain("Podaj prawidłowy numer telefonu");
        html.ShouldContain("Podaj prawidłowy adres e-mail.");
        html.ShouldContain("RRRR-MM-DD");
        (await ClientsAsync(_polish.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_matches_name_prefixes_and_phone_digits_and_never_shows_health_notes()
    {
        await CreateAsync(_studio.Id, new CreateClientRequest("Olivia", "Brown", Phone: "07700 900123", HealthNotes: HealthText));
        await CreateAsync(_studio.Id, new CreateClientRequest("Oliver", "Twist", Phone: "07700 900999"));
        await CreateAsync(_studio.Id, new CreateClientRequest("Anna", "Nowak", Email: "anna@example.com"));
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var byPrefix = await GetAsync(client, SearchPath + "?q=oli");
        byPrefix.ShouldContain("data-testid=\"client-search-form\"");
        byPrefix.ShouldContain("Olivia Brown");
        byPrefix.ShouldContain("Oliver Twist");
        byPrefix.ShouldNotContain("Anna Nowak");
        byPrefix.IndexOf("Olivia Brown", StringComparison.Ordinal).ShouldBeLessThan(byPrefix.IndexOf("Oliver Twist", StringComparison.Ordinal), "ordered by last name");
        byPrefix.ShouldContain("+447700900123");
        byPrefix.ShouldNotContain("Allergic to PPD", Case.Sensitive);
        byPrefix.ShouldNotContain(HealthText);

        var byTwoWords = await GetAsync(client, SearchPath + "?q=oli%20bro");
        byTwoWords.ShouldContain("Olivia Brown");
        byTwoWords.ShouldNotContain("Oliver Twist");

        var byLocalDigits = await GetAsync(client, SearchPath + "?q=07700%20900");
        byLocalDigits.ShouldContain("Olivia Brown");
        byLocalDigits.ShouldContain("Oliver Twist");
        byLocalDigits.ShouldNotContain("Anna Nowak");

        var byTail = await GetAsync(client, SearchPath + "?q=0999");
        byTail.ShouldContain("Oliver Twist");
        byTail.ShouldNotContain("Olivia Brown");

        var all = await GetAsync(client, SearchPath);
        all.ShouldContain("Olivia Brown");
        all.ShouldContain("Oliver Twist");
        all.ShouldContain("Anna Nowak");
        all.ShouldNotContain("Allergic to PPD", Case.Sensitive);

        var none = await GetAsync(client, SearchPath + "?q=zzz");
        none.ShouldContain("data-testid=\"empty-state\"");
        none.ShouldContain("href=\"/admin/clients/new\"");
    }

    [Fact]
    public async Task The_card_shows_decrypted_health_notes_and_the_consent_with_its_date()
    {
        var id = await CreateAsync(_studio.Id, new CreateClientRequest(
            "Olivia", "Brown", Phone: "07700 900123", Email: "olivia@example.com", Notes: "Prefers afternoons.",
            HealthNotes: HealthText, LastPatchTestOn: new LocalDate(2026, 10, 1), MarketingConsent: true, MarketingConsentWording: "I agree to Studio 20 contacting me."));
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var html = await GetAsync(client, $"{SearchPath}/{id}");

        html.ShouldContain("data-testid=\"client-card\"");
        html.ShouldContain("Olivia Brown");
        html.ShouldContain("+447700900123");
        html.ShouldContain("olivia@example.com");
        html.ShouldContain("Prefers afternoons.");
        html.ShouldContain("data-testid=\"health-notes\"");
        html.ShouldContain(HealthText);
        html.ShouldContain("stored encrypted");
        html.ShouldContain("01/10/2026");
        html.ShouldContain("data-testid=\"consent-status\"");
        html.ShouldContain("Granted on");
        html.ShouldContain("I agree to Studio 20 contacting me.");
    }

    [Fact]
    public async Task Another_salon_sees_neither_the_search_result_nor_the_card()
    {
        var id = await CreateAsync(_studio.Id, new CreateClientRequest("Olivia", "Brown", Phone: "07700 900123", HealthNotes: HealthText));
        using var client = await OwnerClientAsync(HostB, _polish.Id);

        var search = await GetAsync(client, SearchPath + "?q=Olivia");
        search.ShouldNotContain("Olivia Brown");
        search.ShouldContain("data-testid=\"empty-state\"");

        (await client.GetAsync(new Uri($"{SearchPath}/{id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_receptionist_is_let_in_a_staff_member_is_denied_and_an_anonymous_visitor_is_sent_to_login()
    {
        var id = await CreateAsync(_studio.Id, new CreateClientRequest("Olivia", "Brown"));
        string[] paths = [SearchPath, FormPath, $"{SearchPath}/{id}"];

        using var receptionist = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        foreach (var path in paths)
        {
            (await receptionist.GetAsync(new Uri(path, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK, path);
        }

        using var staff = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Staff);
        foreach (var path in paths)
        {
            var denied = await staff.GetAsync(new Uri(path, UriKind.Relative));
            denied.StatusCode.ShouldBe(HttpStatusCode.Redirect, path);
            denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        }

        using var anonymous = factory.ClientFor(HostA);
        foreach (var path in paths)
        {
            var challenge = await anonymous.GetAsync(new Uri(path, UriKind.Relative));
            challenge.StatusCode.ShouldBe(HttpStatusCode.Redirect, path);
            challenge.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");
        }

        using var platform = factory.ClientFor("localhost");
        (await platform.GetAsync(new Uri(SearchPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_post_without_the_anti_forgery_token_is_refused()
    {
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var response = await client.PostAsync(
            new Uri(FormPath, UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string> { ["FirstName"] = "Sneaky", ["LastName"] = "Post" }));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ClientsAsync(_studio.Id)).ShouldBeEmpty();
    }

    /// <summary>The page as text: entities decoded, because the encoder writes "+" as &amp;#x2B; and phones start with one.</summary>
    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>GETs the form for its anti-forgery token (the cookie half rides on the client) and POSTs the fields with it.</summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(await GetAsync(client, FormPath));
        return await client.PostAsync(new Uri(FormPath, UriKind.Relative), new FormUrlEncodedContent(fields));
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
}