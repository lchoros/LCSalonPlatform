using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Catalog;
using Salon.Core.Catalog.Events;
using Salon.Core.Events;
using Salon.Core.Identity;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Catalog;

/// <summary>
/// The StaffOverrides slice over HTTP as a signed-in Owner (ADR 0018): one row per bookable staff member with the
/// service defaults as placeholders, a post that stores an override per person (and drops a row set back to the
/// defaults), field rules in the salon language, another salon's service a 404, and the gates (Catalog.Manage, sign-in,
/// anti-forgery). Each salon has Anna and Tom (bookable), Nina (not bookable) and a 45-minute £40 "Cut".
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class StaffOverridesTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _cutId;
    private Guid _annaId;
    private Guid _tomId;
    private Guid _polishCutId;
    private Guid _polishAnnaId;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        _cutId = await AddServiceAsync(_studio.Id, "Cut & blow-dry", 45, 40m, "GBP");
        _annaId = await AddStaffAsync(_studio.Id, "Anna Kowalska", 1);
        _tomId = await AddStaffAsync(_studio.Id, "Tom Reed", 2);
        await AddStaffAsync(_studio.Id, "Nina Owner", 3, bookable: false);
        await AddStaffAsync(_studio.Id, "Left Lastyear", 4, active: false);
        _polishCutId = await AddServiceAsync(_polish.Id, "Strzyżenie damskie", 45, 80m, "PLN");
        _polishAnnaId = await AddStaffAsync(_polish.Id, "Anna Nowak", 1);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_form_lists_the_bookable_staff_with_the_service_defaults_and_any_existing_override()
    {
        await AddOverrideAsync(_studio.Id, _annaId, _cutId, 60, 45m, offers: true);
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var html = await GetFormAsync(client, _cutId);

        html.ShouldContain("data-testid=\"staff-overrides-form\"");
        html.ShouldContain("data-testid=\"service-name\">Cut &amp; blow-dry<");
        html.ShouldContain("__RequestVerificationToken");
        html.ShouldContain("Staff prices and durations");
        WebUtility.HtmlDecode(html).ShouldContain("Default: 45 min, £40.00");
        Rows(html).ShouldBe([_annaId, _tomId], "bookable, active staff in display order; Nina and the leaver are not bookable");
        html.ShouldContain("Anna Kowalska");
        html.ShouldContain("Tom Reed");
        html.ShouldNotContain("Nina Owner");
        html.ShouldNotContain("Left Lastyear");

        InputTag(html, "Rows[0].Offers", "checkbox").ShouldContain("checked");
        InputTag(html, "Rows[0].DurationMinutes").ShouldContain("value=\"60\"");
        InputTag(html, "Rows[0].Price").ShouldContain("value=\"45.00\"");
        InputTag(html, "Rows[1].Offers", "checkbox").ShouldContain("checked");
        InputTag(html, "Rows[1].DurationMinutes").ShouldNotContain("value=\"4");
        InputTag(html, "Rows[1].DurationMinutes").ShouldContain("placeholder=\"45\"");
        InputTag(html, "Rows[1].Price").ShouldContain("placeholder=\"40.00\"");
    }

    [Fact]
    public async Task A_valid_post_stores_one_override_per_changed_person_queues_ServiceUpdated_and_lands_on_the_list()
    {
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var response = await PostAsync(client, _cutId, Fields(
            (_annaId, "true", "60", "45.00"),
            (_tomId, "false", "", "")));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe("/admin/services");

        var rows = await OverridesAsync(_studio.Id);
        rows.Count.ShouldBe(2);
        var anna = rows.Single(o => o.StaffMemberId == _annaId);
        anna.ServiceId.ShouldBe(_cutId);
        anna.TenantId.ShouldBe(_studio.Id);
        anna.Offers.ShouldBeTrue();
        anna.Duration.ShouldBe(Duration.FromMinutes(60));
        anna.Price.ShouldBe(45m);
        var tom = rows.Single(o => o.StaffMemberId == _tomId);
        tom.Offers.ShouldBeFalse();
        tom.Duration.ShouldBeNull();
        tom.Price.ShouldBeNull();

        var outbox = await OutboxAsync();
        var row = outbox.ShouldHaveSingleItem();
        row.EventType.ShouldBe(typeof(ServiceUpdated).FullName);
        row.TenantId.ShouldBe(_studio.Id);

        var list = await client.GetAsync(response.Headers.Location!);
        var html = await list.Content.ReadAsStringAsync();
        html.ShouldContain("data-testid=\"flash\"");
        html.ShouldContain("Staff prices and durations for “Cut &amp; blow-dry” have been saved.");
    }

    [Fact]
    public async Task Posting_the_defaults_again_removes_the_override_row_and_changes_an_existing_one_in_place()
    {
        await AddOverrideAsync(_studio.Id, _annaId, _cutId, 60, 45m, offers: true);
        var tomRowId = await AddOverrideAsync(_studio.Id, _tomId, _cutId, null, 35m, offers: true);
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var response = await PostAsync(client, _cutId, Fields(
            (_annaId, "true", "", ""),
            (_tomId, "true", "30", "35.00")));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var rows = await OverridesAsync(_studio.Id);
        var tom = rows.ShouldHaveSingleItem();
        tom.Id.ShouldBe(tomRowId, "the existing row is updated, not replaced");
        tom.StaffMemberId.ShouldBe(_tomId);
        tom.Duration.ShouldBe(Duration.FromMinutes(30));
        tom.Price.ShouldBe(35m);
    }

    [Fact]
    public async Task Field_errors_come_in_the_salon_language_next_to_their_field_and_nothing_changes()
    {
        using var client = await OwnerClientAsync(HostB, _polish.Id);

        var response = await PostAsync(client, _polishCutId, Fields((_polishAnnaId, "true", "47", "-1")));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("Czas trwania musi być wielokrotnością 5 minut.");
        html.ShouldContain("Cena musi wynosić od 0 do");
        html.ShouldContain("Ceny i czasy pracowników");
        InputTag(html, "Rows[0].DurationMinutes").ShouldContain("input-validation-error");
        InputTag(html, "Rows[0].Price").ShouldContain("input-validation-error");
        html.ShouldContain("Anna Nowak", Case.Sensitive, "the re-rendered form still names the person");
        (await OverridesAsync(_polish.Id)).ShouldBeEmpty();
        (await OutboxAsync()).ShouldBeEmpty();

        var english = await PostAsync(await OwnerClientAsync(HostA, _studio.Id), _cutId, Fields((_annaId, "true", "700", "45.005")));
        var englishHtml = await english.Content.ReadAsStringAsync();
        englishHtml.ShouldContain("Duration must be between 5 and 600 minutes.");
        englishHtml.ShouldContain("Price can have at most two decimal places.");
    }

    [Fact]
    public async Task A_row_for_a_person_the_salon_cannot_book_is_ignored()
    {
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var response = await PostAsync(client, _cutId, Fields(
            (_annaId, "true", "60", ""),
            (_polishAnnaId, "false", "", ""),
            (Guid.NewGuid(), "false", "", "")));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var row = (await OverridesAsync(_studio.Id)).ShouldHaveSingleItem();
        row.StaffMemberId.ShouldBe(_annaId);
        (await OverridesAsync(_polish.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Another_salons_service_id_and_an_unknown_id_are_not_found_on_both_verbs()
    {
        using var client = await OwnerClientAsync(HostB, _polish.Id);

        (await client.GetAsync(new Uri($"/admin/services/{_cutId}/staff", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync(new Uri($"/admin/services/{Guid.NewGuid()}/staff", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var fields = Fields((_annaId, "false", "", ""));
        fields["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(await GetFormAsync(client, _polishCutId));
        var response = await client.PostAsync(new Uri($"/admin/services/{_cutId}/staff", UriKind.Relative), new FormUrlEncodedContent(fields));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await OverridesAsync(_studio.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_post_without_the_anti_forgery_token_is_refused()
    {
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var response = await client.PostAsync(new Uri($"/admin/services/{_cutId}/staff", UriKind.Relative), new FormUrlEncodedContent(Fields((_annaId, "false", "", ""))));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await OverridesAsync(_studio.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_receptionist_is_denied_and_an_anonymous_visitor_is_asked_to_sign_in()
    {
        using var receptionist = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        using var anonymous = factory.ClientFor(HostA);

        var denied = await receptionist.GetAsync(new Uri($"/admin/services/{_cutId}/staff", UriKind.Relative));
        denied.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");

        var login = await anonymous.GetAsync(new Uri($"/admin/services/{_cutId}/staff", UriKind.Relative));
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        login.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");
    }

    [Fact]
    public async Task The_service_edit_page_links_to_the_staff_form()
    {
        using var client = await OwnerClientAsync(HostA, _studio.Id);

        var response = await client.GetAsync(new Uri($"/admin/services/{_cutId}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain($"href=\"/admin/services/{_cutId}/staff\"");
        html.ShouldContain("data-testid=\"staff-overrides\"");
    }

    /// <summary>The form as posted: one row per tuple (staff id, Offers, minutes, price), blanks meaning "use the default".</summary>
    private static Dictionary<string, string> Fields(params (Guid StaffId, string Offers, string Minutes, string Price)[] rows)
    {
        var fields = new Dictionary<string, string>();
        for (var i = 0; i < rows.Length; i++)
        {
            fields[$"Rows[{i}].StaffMemberId"] = rows[i].StaffId.ToString();
            fields[$"Rows[{i}].Offers"] = rows[i].Offers;
            fields[$"Rows[{i}].DurationMinutes"] = rows[i].Minutes;
            fields[$"Rows[{i}].Price"] = rows[i].Price;
        }

        return fields;
    }

    private static List<Guid> Rows(string html)
        => RowPattern().Matches(html).Select(m => Guid.Parse(m.Groups["id"].Value)).ToList();

    private static string InputTag(string html, string name, string type = "")
    {
        var match = Regex.Match(html, $"<input[^>]*name=\"{Regex.Escape(name)}\"[^>]*>");
        match.Success.ShouldBeTrue($"no input named {name}");
        if (type.Length > 0)
        {
            match.Value.ShouldContain($"type=\"{type}\"");
        }

        return match.Value;
    }

    private static async Task<string> GetFormAsync(HttpClient client, Guid serviceId)
    {
        var response = await client.GetAsync(new Uri($"/admin/services/{serviceId}/staff", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, Guid serviceId, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(await GetFormAsync(client, serviceId));
        return await client.PostAsync(new Uri($"/admin/services/{serviceId}/staff", UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    private Task<HttpClient> OwnerClientAsync(string host, Guid tenantId) => factory.SignedInClientAsync(host, tenantId, Role.Owner);

    private async Task<Guid> AddServiceAsync(Guid tenantId, string name, int minutes, decimal price, string currency)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var service = new Service { Name = name, Duration = Duration.FromMinutes(minutes), Price = price, Currency = currency, SortOrder = 1 };
        db.Services.Add(service);
        await db.SaveChangesAsync();
        return service.Id;
    }

    private async Task<Guid> AddStaffAsync(Guid tenantId, string name, int sortOrder, bool bookable = true, bool active = true)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var member = new StaffMember { DisplayName = name, SortOrder = sortOrder, IsBookable = bookable, IsActive = active, CreatedAt = SystemClock.Instance.GetCurrentInstant() };
        db.StaffMembers.Add(member);
        await db.SaveChangesAsync();
        return member.Id;
    }

    private async Task<Guid> AddOverrideAsync(Guid tenantId, Guid staffId, Guid serviceId, int? minutes, decimal? price, bool offers)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var row = new StaffServiceOverride
        {
            StaffMemberId = staffId,
            ServiceId = serviceId,
            Duration = minutes is { } m ? Duration.FromMinutes(m) : null,
            Price = price,
            Offers = offers,
        };
        db.StaffServiceOverrides.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    private async Task<List<StaffServiceOverride>> OverridesAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().StaffServiceOverrides.AsNoTracking().ToListAsync();
    }

    private async Task<List<OutboxMessage>> OutboxAsync()
    {
        await using var scope = factory.CreateTenantScope(null);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().OutboxMessages.AsNoTracking().ToListAsync();
    }

    [GeneratedRegex("data-testid=\"override-row\"[^>]*data-staff=\"(?<id>[0-9a-f-]{36})\"")]
    private static partial Regex RowPattern();
}