using System.Net;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NodaTime;

using Salon.Core.Identity;
using Salon.Core.Staff;
using Salon.Core.Staff.Events;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Staff;

/// <summary>
/// The CreateStaff slice end to end over HTTP as a signed-in Owner: form in both languages, anti-forgery, pipeline
/// validation with localized messages, the row in the right tenant with normalised fields, the StaffMemberCreated outbox
/// row, the flash on the list, and the gates (Staff.Manage, sign-in, tenant).
/// </summary>
[Collection(SharedDatabase.Name)]
public class CreateStaffTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string FormPath = "/admin/staff/new";

    private Tenant _studio = null!;
    private Tenant _polish = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        db.StaffMembers.Add(new StaffMember { DisplayName = "Existing", SortOrder = 3 });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_form_renders_for_a_salon_in_its_language_with_the_default_colour()
    {
        using var english = await OwnerClientAsync("studio-20.localhost", _studio.Id);
        using var polish = await OwnerClientAsync("salon-pl.localhost", _polish.Id);

        var englishHtml = await GetFormAsync(english);
        var polishHtml = await GetFormAsync(polish);

        englishHtml.ShouldContain("data-testid=\"create-staff-form\"");
        englishHtml.ShouldContain("Add a staff member");
        englishHtml.ShouldContain("__RequestVerificationToken");
        englishHtml.ShouldContain("type=\"color\"");
        englishHtml.ShouldContain("value=\"#78716C\"");
        englishHtml.ShouldContain("Clients can book this person");
        polishHtml.ShouldContain("Dodaj pracownika");
        polishHtml.ShouldContain("Imię i nazwisko");
        polishHtml.ShouldContain("Kolor w kalendarzu");
    }

    [Fact]
    public async Task A_valid_post_creates_a_normalised_row_for_that_tenant_only_and_queues_StaffMemberCreated()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var response = await PostAsync(client, new()
        {
            ["DisplayName"] = "  Anna Kowalska  ",
            ["Email"] = " Anna.Kowalska@Example.COM ",
            ["Phone"] = " +44 7700 900123 ",
            ["Colour"] = "#be185d",
            ["IsBookable"] = "false",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe("/admin/staff");

        var created = (await StaffAsync(_studio.Id)).Single(s => s.DisplayName == "Anna Kowalska");
        created.TenantId.ShouldBe(_studio.Id);
        created.Email.ShouldBe("anna.kowalska@example.com");
        created.Phone.ShouldBe("+44 7700 900123");
        created.Colour.ShouldBe("#BE185D");
        created.IsBookable.ShouldBeFalse();
        created.IsActive.ShouldBeTrue();
        created.UserId.ShouldBeNull();
        created.SortOrder.ShouldBe(4);
        created.CreatedAt.ShouldBeGreaterThan(Instant.FromUtc(2026, 1, 1, 0, 0));
        (await StaffAsync(_polish.Id)).ShouldBeEmpty();

        await using var scope = factory.CreateTenantScope(null);
        var outbox = await scope.ServiceProvider.GetRequiredService<SalonDbContext>().OutboxMessages.AsNoTracking().ToListAsync();
        var row = outbox.ShouldHaveSingleItem();
        row.EventType.ShouldBe(typeof(StaffMemberCreated).FullName);
        row.TenantId.ShouldBe(_studio.Id);
        using var payload = JsonDocument.Parse(row.Payload);
        payload.RootElement.GetProperty("staffMemberId").GetGuid().ShouldBe(created.Id);
        payload.RootElement.GetProperty("tenantId").GetGuid().ShouldBe(_studio.Id);
        payload.RootElement.GetProperty("displayName").GetString().ShouldBe("Anna Kowalska");
    }

    [Fact]
    public async Task Blank_optional_fields_are_stored_as_null_and_the_list_shows_the_confirmation()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var response = await PostAsync(client, new() { ["DisplayName"] = "Tom Reed", ["Email"] = "  ", ["Phone"] = "", ["Colour"] = "#1D4ED8", ["IsBookable"] = "true" });

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var created = (await StaffAsync(_studio.Id)).Single(s => s.DisplayName == "Tom Reed");
        created.Email.ShouldBeNull();
        created.Phone.ShouldBeNull();
        created.IsBookable.ShouldBeTrue();

        var list = await client.GetAsync(response.Headers.Location!);
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await list.Content.ReadAsStringAsync();
        html.ShouldContain("data-testid=\"flash\"");
        html.ShouldContain("“Tom Reed” has been added.");
        html.ShouldContain("data-testid=\"staff-name\">Tom Reed</h2>");
    }

    [Fact]
    public async Task An_invalid_post_re_renders_the_form_with_messages_in_the_salon_language()
    {
        using var client = await OwnerClientAsync("salon-pl.localhost", _polish.Id);

        var response = await PostAsync(client, new() { ["DisplayName"] = "   ", ["Email"] = "nope", ["Phone"] = new string('1', 31), ["Colour"] = "red" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("Podaj imię i nazwisko pracownika.");
        html.ShouldContain("Podaj poprawny adres e-mail.");
        html.ShouldContain("Numer telefonu może mieć najwyżej 30 znaków.");
        html.ShouldContain("Wybierz kolor.");
        html.ShouldContain("input-validation-error");
        (await StaffAsync(_polish.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_post_without_the_anti_forgery_token_is_refused()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var response = await client.PostAsync(
            new Uri(FormPath, UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string> { ["DisplayName"] = "Sneaky", ["Colour"] = "#000000" }));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await StaffAsync(_studio.Id)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_manager_may_add_staff_but_a_receptionist_is_denied()
    {
        using var manager = await factory.SignedInClientAsync("studio-20.localhost", _studio.Id, Role.Manager);
        using var receptionist = await factory.SignedInClientAsync("studio-20.localhost", _studio.Id, Role.Receptionist);

        (await manager.GetAsync(new Uri(FormPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var denied = await receptionist.GetAsync(new Uri(FormPath, UriKind.Relative));
        denied.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
    }

    [Fact]
    public async Task Without_a_sign_in_the_screen_asks_for_login_and_the_bare_domain_has_nothing()
    {
        using var anonymous = factory.ClientFor("studio-20.localhost");
        using var platform = factory.ClientFor("localhost");

        var response = await anonymous.GetAsync(new Uri(FormPath, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");

        (await platform.GetAsync(new Uri(FormPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<string> GetFormAsync(HttpClient client)
    {
        var response = await client.GetAsync(new Uri(FormPath, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>GETs the form for its anti-forgery token (the cookie half rides on the client) and POSTs the fields with it.</summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(await GetFormAsync(client));
        return await client.PostAsync(new Uri(FormPath, UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    private Task<HttpClient> OwnerClientAsync(string host, Guid tenantId) => factory.SignedInClientAsync(host, tenantId, Role.Owner);

    private async Task<List<StaffMember>> StaffAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().StaffMembers.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync();
    }
}