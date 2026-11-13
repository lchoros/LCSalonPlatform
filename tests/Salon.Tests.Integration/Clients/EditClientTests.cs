using System.Net;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NodaTime;

using Salon.Core.Clients;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Clients;

/// <summary>
/// The edit-client slice over HTTP (ADR 0011): the form prefilled from the decrypted card, a valid post that rewrites
/// details, health notes (encrypted again) and consent (wording and instant) and lands on the card with a confirmation,
/// a duplicate phone as a field message, and the gates (unknown id, another salon's id, Clients.View, anti-forgery).
/// </summary>
[Collection(SharedDatabase.Name)]
public class EditClientTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string CardPath = "/admin/clients";
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string HealthText = "Allergic to PPD; patch test negative";
    private const string NewHealthText = "Lash tint: mild redness in 2026";

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
    public async Task The_form_is_prefilled_from_the_card_with_decrypted_health_notes_and_the_sensitive_data_hint()
    {
        var id = await CreateAsync(_studio.Id, new CreateClientRequest(
            "Olivia", "Brown", Phone: "07700 900123", Email: "olivia@example.com", Notes: "Prefers afternoons.",
            HealthNotes: HealthText, LastPatchTestOn: new LocalDate(2026, 10, 1), MarketingConsent: true, MarketingConsentWording: "I agree to Studio 20 contacting me."));
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var html = await GetAsync(client, EditPath(id));

        html.ShouldContain("data-testid=\"edit-client-form\"");
        html.ShouldContain("Edit client");
        html.ShouldContain("value=\"Olivia\"");
        html.ShouldContain("value=\"Brown\"");
        html.ShouldContain("value=\"+447700900123\"");
        html.ShouldContain("value=\"olivia@example.com\"");
        html.ShouldContain("Prefers afternoons.");
        html.ShouldContain(HealthText);
        html.ShouldContain("stored encrypted");
        html.ShouldContain("value=\"2026-10-01\"");
        html.ShouldContain("data-testid=\"marketing-consent\"");
        html.ShouldContain("checked=\"checked\"");
        html.ShouldContain("data-testid=\"consent-wording\"");
        html.ShouldContain("Studio 20");
        html.ShouldContain("Save changes");
        html.ShouldContain($"href=\"{CardPath}/{id}\"");
        html.ShouldContain("__RequestVerificationToken");
    }

    [Fact]
    public async Task The_form_renders_in_the_salon_language()
    {
        var id = await CreateAsync(_polish.Id, new CreateClientRequest("Anna", "Nowak"));
        using var client = await OwnerClientAsync(HostB, _polish.Id);

        var html = await GetAsync(client, EditPath(id));

        html.ShouldContain("Edytuj klienta");
        html.ShouldContain("Zapisz zmiany");
        html.ShouldContain("Anuluj");
        html.ShouldContain("Numer telefonu");
        html.ShouldContain("Uwagi zdrowotne (alergie, test uczuleniowy)");
    }

    [Fact]
    public async Task A_valid_post_rewrites_details_health_notes_and_consent_and_lands_on_the_card_with_a_confirmation()
    {
        var id = await CreateAsync(_studio.Id, new CreateClientRequest("Olivia", "Brown", Phone: "07700 900123", HealthNotes: HealthText));
        var before = await RowAsync(_studio.Id, id);
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var response = await PostAsync(client, id, new()
        {
            ["FirstName"] = "  Olive ",
            ["LastName"] = "Brown-Smith",
            ["Phone"] = "07700 900124",
            ["Email"] = "Olive@Example.com",
            ["Notes"] = "Prefers mornings now.",
            ["HealthNotes"] = NewHealthText,
            ["LastPatchTestOn"] = "2026-11-01",
            ["MarketingConsent"] = "true",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"{CardPath}/{id}");

        var row = await RowAsync(_studio.Id, id);
        row.FirstName.ShouldBe("Olive");
        row.LastName.ShouldBe("Brown-Smith");
        row.Phone.ShouldBe("+447700900124");
        row.Email.ShouldBe("olive@example.com");
        row.Notes.ShouldBe("Prefers mornings now.");
        row.HealthNotesProtected.ShouldNotBeNull();
        row.HealthNotesProtected.ShouldNotBe(before.HealthNotesProtected);
        row.HealthNotesProtected.ShouldNotBe(NewHealthText);
        row.HealthNotesProtected.ShouldNotContain("Lash");
        row.LastPatchTestOn.ShouldBe(new LocalDate(2026, 11, 1));
        row.MarketingConsent.ShouldBeTrue();
        row.MarketingConsentAt.ShouldNotBeNull();
        row.MarketingConsentWording.ShouldNotBeNull().ShouldContain("Studio 20");
        row.UpdatedAt.ShouldBeGreaterThan(before.UpdatedAt);
        row.CreatedAt.ShouldBe(before.CreatedAt);
        (await ClientsAsync(_studio.Id)).Count.ShouldBe(1);

        var card = await GetAsync(client, $"{CardPath}/{id}");
        card.ShouldContain("data-testid=\"flash\"");
        card.ShouldContain("Changes to Olive Brown-Smith saved.");
        card.ShouldContain(NewHealthText);
        card.ShouldNotContain(HealthText);
        card.ShouldContain("Granted on");
    }

    [Fact]
    public async Task Clearing_the_optional_fields_and_the_tick_withdraws_consent_and_removes_the_health_notes()
    {
        var id = await CreateAsync(_studio.Id, new CreateClientRequest(
            "Olivia", "Brown", Phone: "07700 900123", Email: "olivia@example.com", Notes: "Prefers afternoons.",
            HealthNotes: HealthText, LastPatchTestOn: new LocalDate(2026, 10, 1), MarketingConsent: true, MarketingConsentWording: "I agree to Studio 20 contacting me."));
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var response = await PostAsync(client, id, new()
        {
            ["FirstName"] = "Olivia",
            ["LastName"] = "Brown",
            ["Phone"] = "",
            ["Email"] = "",
            ["Notes"] = "",
            ["HealthNotes"] = "",
            ["LastPatchTestOn"] = "",
            ["MarketingConsent"] = "false",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var row = await RowAsync(_studio.Id, id);
        row.Phone.ShouldBeNull();
        row.Email.ShouldBeNull();
        row.Notes.ShouldBeNull();
        row.HealthNotesProtected.ShouldBeNull();
        row.LastPatchTestOn.ShouldBeNull();
        row.MarketingConsent.ShouldBeFalse();
        row.MarketingConsentAt.ShouldBeNull();
        row.MarketingConsentWording.ShouldBeNull();
    }

    [Fact]
    public async Task Keeping_the_tick_leaves_the_original_consent_stamp_untouched()
    {
        var id = await CreateAsync(_studio.Id, new CreateClientRequest("Olivia", "Brown", MarketingConsent: true, MarketingConsentWording: "I agree to Studio 20 contacting me."));
        var before = await RowAsync(_studio.Id, id);
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var response = await PostAsync(client, id, new()
        {
            ["FirstName"] = "Olivia",
            ["LastName"] = "Brown",
            ["Notes"] = "Prefers afternoons.",
            ["MarketingConsent"] = "true",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var row = await RowAsync(_studio.Id, id);
        row.Notes.ShouldBe("Prefers afternoons.");
        row.MarketingConsent.ShouldBeTrue();
        row.MarketingConsentAt.ShouldBe(before.MarketingConsentAt);
        row.MarketingConsentWording.ShouldBe(before.MarketingConsentWording);
    }

    [Fact]
    public async Task A_phone_already_used_by_another_client_is_a_field_message_in_the_salon_language()
    {
        await CreateAsync(_studio.Id, new CreateClientRequest("Olivia", "Brown", Phone: "+44 7700 900123"));
        var id = await CreateAsync(_studio.Id, new CreateClientRequest("Oliver", "Twist", Phone: "07700 900999"));
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var response = await PostAsync(client, id, new()
        {
            ["FirstName"] = "Oliver",
            ["LastName"] = "Twist",
            ["Phone"] = "07700 900123",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        html.ShouldContain("A client with this phone number already exists.");
        html.ShouldContain("input-validation-error");
        (await RowAsync(_studio.Id, id)).Phone.ShouldBe("+447700900999");
    }

    [Fact]
    public async Task Keeping_the_clients_own_phone_is_not_a_duplicate()
    {
        var id = await CreateAsync(_studio.Id, new CreateClientRequest("Olivia", "Brown", Phone: "+44 7700 900123", Email: "olivia@example.com"));
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var response = await PostAsync(client, id, new()
        {
            ["FirstName"] = "Olivia",
            ["LastName"] = "Brown",
            ["Phone"] = "07700 900123",
            ["Email"] = "OLIVIA@example.com",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var row = await RowAsync(_studio.Id, id);
        row.Phone.ShouldBe("+447700900123");
        row.Email.ShouldBe("olivia@example.com");
    }

    [Fact]
    public async Task An_invalid_post_re_renders_the_form_with_messages_in_the_salon_language()
    {
        var id = await CreateAsync(_polish.Id, new CreateClientRequest("Anna", "Nowak", Phone: "601 234 567"));
        using var client = await OwnerClientAsync(HostB, _polish.Id);

        var response = await PostAsync(client, id, new()
        {
            ["FirstName"] = "",
            ["LastName"] = "",
            ["Phone"] = "07700 900123",
            ["Email"] = "nie-email",
            ["LastPatchTestOn"] = "wczoraj",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        html.ShouldContain("Podaj imię.");
        html.ShouldContain("Podaj nazwisko.");
        html.ShouldContain("Podaj prawidłowy numer telefonu");
        html.ShouldContain("Podaj prawidłowy adres e-mail.");
        html.ShouldContain("RRRR-MM-DD");
        var row = await RowAsync(_polish.Id, id);
        row.FirstName.ShouldBe("Anna");
        row.Phone.ShouldBe("+48601234567");
    }

    [Fact]
    public async Task An_unknown_id_and_another_salons_client_are_not_found_on_both_verbs()
    {
        var id = await CreateAsync(_studio.Id, new CreateClientRequest("Olivia", "Brown", HealthNotes: HealthText));
        using var owner = await OwnerClientAsync(HostA, _studio.Id);
        using var other = await OwnerClientAsync(HostB, _polish.Id);

        (await owner.GetAsync(new Uri(EditPath(Guid.NewGuid()), UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var otherGet = await other.GetAsync(new Uri(EditPath(id), UriKind.Relative));
        otherGet.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await otherGet.Content.ReadAsStringAsync()).ShouldNotContain("Allergic to PPD", Case.Sensitive);

        var token = IdentityTestClient.AntiForgeryToken(await GetAsync(other, $"{CardPath}/new"));
        var otherPost = await other.PostAsync(
            new Uri(EditPath(id), UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string> { ["FirstName"] = "Hacked", ["LastName"] = "Brown", ["__RequestVerificationToken"] = token }));

        otherPost.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await RowAsync(_studio.Id, id)).FirstName.ShouldBe("Olivia");
    }

    [Fact]
    public async Task A_receptionist_is_let_in_a_staff_member_is_denied_and_an_anonymous_visitor_is_sent_to_login()
    {
        var id = await CreateAsync(_studio.Id, new CreateClientRequest("Olivia", "Brown"));
        var path = EditPath(id);

        using var receptionist = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        (await receptionist.GetAsync(new Uri(path, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var staff = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Staff);
        var denied = await staff.GetAsync(new Uri(path, UriKind.Relative));
        denied.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");

        using var anonymous = factory.ClientFor(HostA);
        var challenge = await anonymous.GetAsync(new Uri(path, UriKind.Relative));
        challenge.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        challenge.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");
    }

    [Fact]
    public async Task A_post_without_the_anti_forgery_token_is_refused()
    {
        var id = await CreateAsync(_studio.Id, new CreateClientRequest("Olivia", "Brown"));
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var response = await client.PostAsync(
            new Uri(EditPath(id), UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string> { ["FirstName"] = "Sneaky", ["LastName"] = "Post" }));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await RowAsync(_studio.Id, id)).FirstName.ShouldBe("Olivia");
    }

    private static string EditPath(Guid id) => $"{CardPath}/{id}/edit";

    /// <summary>The page as text: entities decoded, because the encoder writes "+" as &amp;#x2B; and phones start with one.</summary>
    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>GETs the form for its anti-forgery token (the cookie half rides on the client) and POSTs the fields with it.</summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, Guid id, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(await GetAsync(client, EditPath(id)));
        return await client.PostAsync(new Uri(EditPath(id), UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    private Task<HttpClient> OwnerClientAsync(string host, Guid tenantId) => factory.SignedInClientAsync(host, tenantId, Role.Owner);

    private async Task<Guid> CreateAsync(Guid tenantId, CreateClientRequest request)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<IClientService>().CreateAsync(request);
    }

    private async Task<Client> RowAsync(Guid tenantId, Guid clientId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Clients.AsNoTracking().SingleAsync(c => c.Id == clientId);
    }

    private async Task<List<Client>> ClientsAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Clients.AsNoTracking().OrderBy(c => c.LastName).ToListAsync();
    }
}