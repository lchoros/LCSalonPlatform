using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Locations;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Settings;

/// <summary>
/// The salon details page and the main location behind it (ADR 0027): owner only, empty until the salon says where it
/// is, one row per salon however often it is saved, written the way the salon's country writes an address, and never
/// visible to another salon.
/// </summary>
[Collection(SharedDatabase.Name)]
public class SalonDetailsPageTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string Path = "/admin/settings/salon-details";
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";

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
    public async Task A_salon_has_no_location_until_it_says_where_it_is()
    {
        (await LocationsAsync(_studio.Id, s => s.GetMainAsync())).ShouldBeNull();
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var html = await GetAsync(client, Path);

        html.ShouldContain("data-testid=\"salon-details-form\"");
        html.ShouldContain("href=\"/admin/settings/salon-details\" data-testid=\"settings-nav-salon-details\" aria-current=\"page\"");
        Input(html, "AddressLine1").ShouldNotContain("value=\"");
        Input(html, "AddressLine1").ShouldContain("required");
        Input(html, "Phone").ShouldNotContain("required");
        (await GetAsync(client, "/admin/settings/notifications")).ShouldContain("href=\"/admin/settings/salon-details\" data-testid=\"settings-nav-salon-details\"");
    }

    [Fact]
    public async Task Saving_keeps_one_main_location_per_salon_trimmed_and_in_the_countrys_order()
    {
        var first = await LocationsAsync(_studio.Id, s => s.SaveMainAsync(new LocationDetails("  ", " 12 High Street ", " Floor 2 ", " London ", " N1 9GU ", " 020 7946 0020 ", "  ")));

        first.Name.ShouldBeNull();
        first.AddressLine2.ShouldBe("Floor 2");
        first.Email.ShouldBeNull();
        first.Address.ShouldBe("12 High Street, Floor 2, London N1 9GU");
        first.PhoneLink.ShouldBe("tel:02079460020");

        // A second save changes the same row.
        var second = await LocationsAsync(_studio.Id, s => s.SaveMainAsync(new LocationDetails("Studio 20 Angel", "14 High Street", null, "London", "N1 9GU", null, "hello@studio20.example")));
        second.Id.ShouldBe(first.Id);
        second.Address.ShouldBe("14 High Street, London N1 9GU");
        second.PhoneLink.ShouldBeNull();
        (await LocationsAsync(_studio.Id, s => s.GetMainAsync())).ShouldBe(second);
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            (await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Locations.CountAsync()).ShouldBe(1);
        }

        // The other salon has its own, written the Polish way, and sees nothing of the first.
        (await LocationsAsync(_polish.Id, s => s.GetMainAsync())).ShouldBeNull();
        var polish = await LocationsAsync(_polish.Id, s => s.SaveMainAsync(new LocationDetails(null, "ul. Piękna 12", null, "Warszawa", "00-549", "22 123 45 67", null)));
        polish.Address.ShouldBe("ul. Piękna 12, 00-549 Warszawa");
        (await LocationsAsync(_studio.Id, s => s.GetMainAsync())).ShouldBe(second);
    }

    [Fact]
    public async Task The_service_refuses_what_the_table_must_not_hold()
    {
        await Should.ThrowAsync<ArgumentException>(() => LocationsAsync(_studio.Id, s => s.SaveMainAsync(new LocationDetails(null, " ", null, "London", "N1 9GU", null, null))));
        await Should.ThrowAsync<ArgumentException>(() => LocationsAsync(_studio.Id, s => s.SaveMainAsync(new LocationDetails(null, "12 High Street", null, "London", "N1 9GU", "call me", null))));
        await Should.ThrowAsync<ArgumentException>(() => LocationsAsync(_studio.Id, s => s.SaveMainAsync(new LocationDetails(null, "12 High Street", null, "London", new string('9', 21), null, null))));

        (await LocationsAsync(_studio.Id, s => s.GetMainAsync())).ShouldBeNull();
    }

    [Fact]
    public async Task The_owner_saves_the_details_on_the_page_and_sees_them_again()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var response = await PostAsync(client, Fields(phone: "020 7946 0020", email: "hello@studio20.example"));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe(Path);
        var after = await GetAsync(client, Path);
        after.ShouldContain("The salon details have been saved.");
        Input(after, "AddressLine1").ShouldContain("value=\"12 High Street\"");
        Input(after, "City").ShouldContain("value=\"London\"");
        Input(after, "Postcode").ShouldContain("value=\"N1 9GU\"");
        Input(after, "Phone").ShouldContain("value=\"020 7946 0020\"");
        Input(after, "Email").ShouldContain("value=\"hello@studio20.example\"");
        var saved = (await LocationsAsync(_studio.Id, s => s.GetMainAsync())).ShouldNotBeNull();
        saved.Address.ShouldBe("12 High Street, London N1 9GU");
        (await LocationsAsync(_polish.Id, s => s.GetMainAsync())).ShouldBeNull();
    }

    [Theory]
    [InlineData("AddressLine1", "", "Enter the address.")]
    [InlineData("City", "   ", "Enter the town or city.")]
    [InlineData("Postcode", "", "Enter the postcode.")]
    [InlineData("Postcode", "123456789012345678901", "Use at most 20 characters.")]
    [InlineData("Phone", "call the desk", "Enter a phone number: digits, with spaces if you like, for example 020 7946 0020.")]
    [InlineData("Phone", "12345", "Enter a phone number: digits, with spaces if you like, for example 020 7946 0020.")]
    [InlineData("Email", "hello-at-studio", "Enter a valid email address.")]
    public async Task What_cannot_be_saved_is_named_next_to_its_field_and_nothing_is_stored(string field, string value, string message)
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var fields = Fields(phone: "020 7946 0020", email: "hello@studio20.example");
        fields[field] = value;

        var response = await PostAsync(client, fields);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        html.ShouldContain($"data-testid=\"{field}-error\">{message}<");
        Input(html, field).ShouldContain("aria-invalid=\"true\"");
        Input(html, "AddressLine1" == field ? "City" : "AddressLine1").ShouldContain("aria-invalid=\"false\"", Case.Sensitive, "what was typed elsewhere is kept and not flagged");
        (await LocationsAsync(_studio.Id, s => s.GetMainAsync())).ShouldBeNull();
    }

    [Fact]
    public async Task The_page_speaks_the_salon_language()
    {
        using var client = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);

        var html = await GetAsync(client, Path);
        html.ShouldContain("Dane salonu");
        html.ShouldContain("Kod pocztowy");

        var fields = Fields(phone: null, email: null);
        fields["AddressLine1"] = string.Empty;
        var refused = WebUtility.HtmlDecode(await (await PostAsync(client, fields)).Content.ReadAsStringAsync());
        refused.ShouldContain("Podaj adres.");
    }

    [Fact]
    public async Task Only_the_owner_changes_the_details_and_never_without_a_token()
    {
        // A manager runs the day but not the settings (ADR 0009).
        using var manager = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Manager);
        (await manager.GetAsync(new Uri(Path, UriKind.Relative))).Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");

        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var withoutToken = await owner.PostAsync(new Uri(Path, UriKind.Relative), new FormUrlEncodedContent(Fields(null, null)));
        withoutToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await LocationsAsync(_studio.Id, s => s.GetMainAsync())).ShouldBeNull();
    }

    private static Dictionary<string, string> Fields(string? phone, string? email) => new()
    {
        ["Name"] = string.Empty,
        ["AddressLine1"] = "12 High Street",
        ["AddressLine2"] = string.Empty,
        ["City"] = "London",
        ["Postcode"] = "N1 9GU",
        ["Phone"] = phone ?? string.Empty,
        ["Email"] = email ?? string.Empty,
    };

    /// <summary>The whole <c>&lt;input&gt;</c> tag of a field, so an assertion cannot match a neighbour.</summary>
    private static string Input(string html, string name)
    {
        var start = html.IndexOf($"<input id=\"{name}\"", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, name);
        return html[start..(html.IndexOf("/>", start, StringComparison.Ordinal) + 2)];
    }

    private async Task<T> LocationsAsync<T>(Guid tenantId, Func<ILocationService, Task<T>> act)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await act(scope.ServiceProvider.GetRequiredService<ILocationService>());
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(await GetAsync(client, Path));
        return await client.PostAsync(new Uri(Path, UriKind.Relative), new FormUrlEncodedContent(fields));
    }
}