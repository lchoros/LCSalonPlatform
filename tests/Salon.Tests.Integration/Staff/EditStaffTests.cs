using System.Globalization;
using System.Net;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NodaTime;

using Salon.Core.Identity;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Staff;

/// <summary>
/// The EditStaff slice end to end over HTTP as a signed-in Owner: form in both languages, anti-forgery, pipeline
/// validation with localized messages, the row in the right tenant with normalised fields, the flash on the list, and the gates (Staff.Manage, sign-in, tenant).
/// </summary>
[Collection(SharedDatabase.Name)]
public class EditStaffTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string FormPath = "/admin/staff/{0}/edit";

    private static string FormPathFor(Guid id) => FormPath.Replace("{0}", id.ToString(), StringComparison.Ordinal);

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private StaffMember _existing = null!;
    private StaffMember _polishStaff = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        _existing = new StaffMember { DisplayName = "Existing", SortOrder = 3 };
        db.StaffMembers.Add(_existing);
        await db.SaveChangesAsync();

        await using var polishScope = factory.CreateTenantScope(_polish.Id);
        var polishDb = polishScope.ServiceProvider.GetRequiredService<SalonDbContext>();
        _polishStaff = new StaffMember { DisplayName = "Istniejący", SortOrder = 1 };
        polishDb.StaffMembers.Add(_polishStaff);
        await polishDb.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_form_renders_for_a_salon_in_its_language_with_the_default_values()
    {
        using var english = await OwnerClientAsync("studio-20.localhost", _studio.Id);
        using var polish = await OwnerClientAsync("salon-pl.localhost", _polish.Id);

        var englishHtml = await GetFormAsync(english, _existing.Id);
        var polishHtml = await GetFormAsync(polish, _polishStaff.Id);

        englishHtml.ShouldContain("data-testid=\"edit-staff-form\"");
        englishHtml.ShouldContain("Edit staff member");
        englishHtml.ShouldContain("__RequestVerificationToken");
        englishHtml.ShouldContain("type=\"color\"");
        englishHtml.ShouldContain("value=\"#78716C\"");
        englishHtml.ShouldContain("Clients can book this person");
        polishHtml.ShouldContain("Edytuj pracownika");
        polishHtml.ShouldContain("Imię i nazwisko");
        polishHtml.ShouldContain("Kolor w kalendarzu");
    }

    [Fact]
    public async Task A_valid_post_updates_a_normalised_row_for_that_tenant_only()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var response = await PostAsync(client, _existing.Id, new()
        {
            ["DisplayName"] = "  Anna Kowalska  ",
            ["Email"] = " Anna.Kowalska@Example.COM ",
            ["Phone"] = " +44 7700 900123 ",
            ["Colour"] = "#be185d",
            ["IsBookable"] = "false",
            ["IsActive"] = "false",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe("/admin/staff");

        var updated = (await StaffAsync(_studio.Id, _existing.Id)).ShouldNotBeNull();
        updated.DisplayName.ShouldBe("Anna Kowalska");
        updated.Email.ShouldBe("anna.kowalska@example.com");
        updated.Phone.ShouldBe("+44 7700 900123");
        updated.Colour.ShouldBe("#BE185D");
        updated.IsBookable.ShouldBeFalse();
        updated.IsActive.ShouldBeFalse();
        (await StaffAsync(_polish.Id, _existing.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task Blank_optional_fields_are_stored_as_null_and_the_list_shows_the_confirmation()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var response = await PostAsync(client, _existing.Id, new() { ["DisplayName"] = "Tom Reed", ["Email"] = "  ", ["Phone"] = "", ["Colour"] = "#1D4ED8", ["IsBookable"] = "true", ["IsActive"] = "true" });

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var updated = (await StaffAsync(_studio.Id, _existing.Id)).ShouldNotBeNull();
        updated.Email.ShouldBeNull();
        updated.Phone.ShouldBeNull();
        updated.IsBookable.ShouldBeTrue();
        updated.IsActive.ShouldBeTrue();

        var list = await client.GetAsync(response.Headers.Location!);
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await list.Content.ReadAsStringAsync();
        html.ShouldContain("data-testid=\"flash\"");
        html.ShouldContain("Changes to “Tom Reed” saved.");
        html.ShouldContain("data-testid=\"staff-name\">Tom Reed</h2>");
    }

    [Fact]
    public async Task An_invalid_post_re_renders_the_form_with_messages_in_the_salon_language()
    {
        using var client = await OwnerClientAsync("salon-pl.localhost", _polish.Id);

        var response = await PostAsync(client, _polishStaff.Id, new() { ["DisplayName"] = "   ", ["Email"] = "nope", ["Phone"] = new string('1', 31), ["Colour"] = "red", ["IsBookable"] = "true", ["IsActive"] = "true" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("Podaj imię i nazwisko pracownika.");
        html.ShouldContain("Podaj poprawny adres e-mail.");
        html.ShouldContain("Numer telefonu może mieć najwyżej 30 znaków.");
        html.ShouldContain("Wybierz kolor.");
        html.ShouldContain("input-validation-error");
        (await StaffAsync(_polish.Id, _polishStaff.Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_post_without_the_anti_forgery_token_is_refused()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var response = await client.PostAsync(
            new Uri(FormPathFor(_existing.Id), UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string> { ["DisplayName"] = "Sneaky", ["Colour"] = "#000000" }));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await StaffAsync(_studio.Id, _existing.Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_manager_may_edit_staff_but_a_receptionist_is_denied()
    {
        using var manager = await factory.SignedInClientAsync("studio-20.localhost", _studio.Id, Role.Manager);
        using var receptionist = await factory.SignedInClientAsync("studio-20.localhost", _studio.Id, Role.Receptionist);

        (await manager.GetAsync(new Uri(FormPathFor(_existing.Id), UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var denied = await receptionist.GetAsync(new Uri(FormPathFor(_existing.Id), UriKind.Relative));
        denied.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
    }

    [Fact]
    public async Task Without_a_sign_in_the_screen_asks_for_login_and_the_bare_domain_has_nothing()
    {
        using var anonymous = factory.ClientFor("studio-20.localhost");
        using var platform = factory.ClientFor("localhost");

        var response = await anonymous.GetAsync(new Uri(FormPathFor(_existing.Id), UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");

        (await platform.GetAsync(new Uri(FormPathFor(_existing.Id), UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_random_id_returns_404()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var response = await client.GetAsync(new Uri(FormPathFor(Guid.NewGuid()), UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<string> GetFormAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync(new Uri(FormPathFor(id), UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>GETs the form for its anti-forgery token (the cookie half rides on the client) and POSTs the fields with it.</summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, Guid id, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(await GetFormAsync(client, id));
        return await client.PostAsync(new Uri(FormPathFor(id), UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    private Task<HttpClient> OwnerClientAsync(string host, Guid tenantId) => factory.SignedInClientAsync(host, tenantId, Role.Owner);

    private async Task<StaffMember?> StaffAsync(Guid tenantId, Guid id)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().StaffMembers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, CancellationToken.None);
    }
}