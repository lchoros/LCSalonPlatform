using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NodaTime;

using Salon.Core.Catalog;
using Salon.Core.Catalog.Events;
using Salon.Core.Events;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Catalog;

/// <summary>
/// The EditService slice end to end over HTTP as a signed-in Owner (ADR 0013): the form pre-filled from the row, a valid
/// post changing every field and queueing ServiceUpdated, deactivation hiding the service from the public list and queueing
/// ServiceDeactivated, a duplicate name refused with a localized message (its own name is fine), field rules in the salon
/// language, another salon's id a 404, and the gates (Catalog.Manage, sign-in).
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class EditServiceTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _hairId;
    private Guid _retiredId;
    private Guid _cutId;
    private Guid _polishCutId;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        _hairId = await AddCategoryAsync(_studio.Id, "Hair", 1);
        _retiredId = await AddCategoryAsync(_studio.Id, "Retired", 2, isActive: false);
        _cutId = await AddServiceAsync(_studio.Id, "Cut & blow-dry", 45, 45m, "GBP", 1, _hairId, buffer: 5);
        await AddServiceAsync(_studio.Id, "Colour", 90, 85m, "GBP", 2);
        _polishCutId = await AddServiceAsync(_polish.Id, "Strzyżenie damskie", 45, 45m, "PLN", 1);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_form_shows_the_current_values_and_only_active_categories()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var html = await GetFormAsync(client, _cutId);

        html.ShouldContain("data-testid=\"edit-service-form\"");
        html.ShouldContain("data-testid=\"service-name\">Cut &amp; blow-dry<");
        html.ShouldContain("__RequestVerificationToken");
        InputTag(html, "Name").ShouldContain("value=\"Cut &amp; blow-dry\"");
        InputTag(html, "DurationMinutes").ShouldContain("value=\"45\"");
        InputTag(html, "Price").ShouldContain("value=\"45.00\"");
        InputTag(html, "BufferAfterMinutes").ShouldContain("value=\"5\"");
        InputTag(html, "ProcessingMinutes").ShouldContain("value=\"0\"");
        InputTag(html, "IsActive").ShouldContain("checked");
        html.ShouldContain($"<option selected=\"selected\" value=\"{_hairId}\">Hair</option>");
        html.ShouldNotContain("Retired");
        html.ShouldContain("Clients can book this service");
        html.ShouldContain("Buffer after the service (minutes)");
        html.ShouldContain("Processing time (minutes)");
    }

    [Fact]
    public async Task A_service_in_an_inactive_category_keeps_it_in_the_picker()
    {
        var permId = await AddServiceAsync(_studio.Id, "Perm", 120, 60m, "GBP", 3, _retiredId);
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var html = await GetFormAsync(client, permId);

        html.ShouldContain($"<option selected=\"selected\" value=\"{_retiredId}\">Retired</option>");
        html.ShouldContain($"value=\"{_hairId}\">Hair</option>");
    }

    [Fact]
    public async Task A_valid_post_changes_every_field_queues_ServiceUpdated_and_lands_on_the_list_with_a_flash()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var response = await PostAsync(client, _cutId, Fields(
            name: "  Cut & finish  ", description: "  Wash, cut and finish.  ", duration: "60", price: "49.50",
            category: null, buffer: "10", processing: "15", processingStartsAfter: "30", isActive: "true"));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe("/admin/services");

        var service = await ServiceAsync(_studio.Id, _cutId);
        service.Name.ShouldBe("Cut & finish");
        service.Description.ShouldBe("Wash, cut and finish.");
        service.Duration.ShouldBe(Duration.FromMinutes(60));
        service.Price.ShouldBe(49.50m);
        service.Currency.ShouldBe("GBP", "the currency is not editable");
        service.CategoryId.ShouldBeNull();
        service.BufferAfterMinutes.ShouldBe(10);
        service.ProcessingMinutes.ShouldBe(15);
        service.ProcessingStartsAfterMinutes.ShouldBe(30);
        service.IsActive.ShouldBeTrue();
        service.SortOrder.ShouldBe(1, "the position is not editable");

        var outbox = await OutboxAsync();
        var row = outbox.ShouldHaveSingleItem();
        row.EventType.ShouldBe(typeof(ServiceUpdated).FullName);
        row.TenantId.ShouldBe(_studio.Id);
        using var payload = JsonDocument.Parse(row.Payload);
        payload.RootElement.GetProperty("serviceId").GetGuid().ShouldBe(_cutId);
        payload.RootElement.GetProperty("tenantId").GetGuid().ShouldBe(_studio.Id);

        var list = await client.GetAsync(response.Headers.Location!);
        var html = await list.Content.ReadAsStringAsync();
        html.ShouldContain("data-testid=\"flash\"");
        html.ShouldContain("Service “Cut &amp; finish” has been saved.");
        html.ShouldContain("10 min buffer");
        html.ShouldContain("15 min processing");
    }

    [Fact]
    public async Task Deactivating_hides_the_service_from_the_public_list_and_queues_ServiceDeactivated_too()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);
        using var visitor = factory.ClientFor("studio-20.localhost");
        (await (await visitor.GetAsync(new Uri("/services", UriKind.Relative))).Content.ReadAsStringAsync()).ShouldContain("Cut &amp; blow-dry");

        var response = await PostAsync(client, _cutId, Fields(isActive: "false"));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await ServiceAsync(_studio.Id, _cutId)).IsActive.ShouldBeFalse();
        var publicHtml = await (await visitor.GetAsync(new Uri("/services", UriKind.Relative))).Content.ReadAsStringAsync();
        publicHtml.ShouldNotContain("Cut &amp; blow-dry");
        publicHtml.ShouldContain("Colour");

        var outbox = await OutboxAsync();
        outbox.Select(o => o.EventType).Order().ShouldBe([typeof(ServiceDeactivated).FullName, typeof(ServiceUpdated).FullName]);
        outbox.ShouldAllBe(o => o.TenantId == _studio.Id);

        // Reactivating is an ordinary update: no second deactivation event.
        (await PostAsync(client, _cutId, Fields(isActive: "true"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await OutboxAsync()).Count(o => o.EventType == typeof(ServiceDeactivated).FullName).ShouldBe(1);
        (await ServiceAsync(_studio.Id, _cutId)).IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Renaming_to_another_services_name_is_refused_but_its_own_name_in_another_case_is_fine()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var duplicate = await PostAsync(client, _cutId, Fields(name: " colour "));

        duplicate.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await duplicate.Content.ReadAsStringAsync();
        html.ShouldContain("A service with this name already exists.");
        InputTag(html, "Name").ShouldContain("input-validation-error");
        (await ServiceAsync(_studio.Id, _cutId)).Name.ShouldBe("Cut & blow-dry");

        var ownName = await PostAsync(client, _cutId, Fields(name: "cut & BLOW-dry"));

        ownName.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await ServiceAsync(_studio.Id, _cutId)).Name.ShouldBe("cut & BLOW-dry");
    }

    [Fact]
    public async Task Field_errors_come_in_the_salon_language_next_to_their_field_and_nothing_changes()
    {
        using var client = await OwnerClientAsync("salon-pl.localhost", _polish.Id);

        var response = await PostAsync(client, _polishCutId, Fields(
            name: "", duration: "45", price: "45,005", category: Guid.NewGuid().ToString(), buffer: "7", processing: "30", processingStartsAfter: "20"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("Podaj nazwę usługi.");
        html.ShouldContain("Cena może mieć najwyżej dwa miejsca po przecinku.");
        html.ShouldContain("Wybierz jedną z kategorii salonu.");
        html.ShouldContain("Minuty muszą być wielokrotnością 5.");
        html.ShouldContain("Czas działania musi mieścić się w czasie trwania");
        html.ShouldContain("Czas działania (klient czeka)");
        html.ShouldContain("Przerwa techniczna po usłudze (minuty)");
        InputTag(html, "BufferAfterMinutes").ShouldContain("input-validation-error");
        InputTag(html, "ProcessingStartsAfterMinutes").ShouldContain("input-validation-error");
        var service = await ServiceAsync(_polish.Id, _polishCutId);
        service.Name.ShouldBe("Strzyżenie damskie");
        service.BufferAfterMinutes.ShouldBe(0);
        (await OutboxAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_processing_time_the_offset_is_stored_as_zero()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var response = await PostAsync(client, _cutId, Fields(processing: "0", processingStartsAfter: "20"));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var service = await ServiceAsync(_studio.Id, _cutId);
        service.ProcessingMinutes.ShouldBe(0);
        service.ProcessingStartsAfterMinutes.ShouldBe(0);
    }

    [Fact]
    public async Task Another_salons_service_id_and_an_unknown_id_are_not_found_on_both_verbs()
    {
        using var client = await OwnerClientAsync("salon-pl.localhost", _polish.Id);

        (await client.GetAsync(new Uri($"/admin/services/{_cutId}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync(new Uri($"/admin/services/{Guid.NewGuid()}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var fields = Fields(name: "Hijacked");
        fields["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(await GetFormAsync(client, _polishCutId));
        var response = await client.PostAsync(new Uri($"/admin/services/{_cutId}", UriKind.Relative), new FormUrlEncodedContent(fields));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ServiceAsync(_studio.Id, _cutId)).Name.ShouldBe("Cut & blow-dry");
    }

    [Fact]
    public async Task A_post_without_the_anti_forgery_token_is_refused()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var response = await client.PostAsync(new Uri($"/admin/services/{_cutId}", UriKind.Relative), new FormUrlEncodedContent(Fields(name: "Sneaky")));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ServiceAsync(_studio.Id, _cutId)).Name.ShouldBe("Cut & blow-dry");
    }

    [Fact]
    public async Task A_receptionist_is_denied_and_an_anonymous_visitor_is_asked_to_sign_in()
    {
        using var receptionist = await factory.SignedInClientAsync("studio-20.localhost", _studio.Id, Role.Receptionist);
        using var anonymous = factory.ClientFor("studio-20.localhost");

        var denied = await receptionist.GetAsync(new Uri($"/admin/services/{_cutId}", UriKind.Relative));
        denied.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");

        var login = await anonymous.GetAsync(new Uri($"/admin/services/{_cutId}", UriKind.Relative));
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        login.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");
    }

    /// <summary>The form as posted for "Cut &amp; blow-dry", with any field overridden; a null category posts an empty select.</summary>
    private Dictionary<string, string> Fields(
        string name = "Cut & blow-dry",
        string description = "",
        string duration = "45",
        string price = "45.00",
        string? category = "hair",
        string buffer = "5",
        string processing = "0",
        string processingStartsAfter = "0",
        string isActive = "true")
        => new()
        {
            ["Name"] = name,
            ["Description"] = description,
            ["DurationMinutes"] = duration,
            ["Price"] = price,
            ["CategoryId"] = category == "hair" ? _hairId.ToString() : category ?? string.Empty,
            ["BufferAfterMinutes"] = buffer,
            ["ProcessingMinutes"] = processing,
            ["ProcessingStartsAfterMinutes"] = processingStartsAfter,
            ["IsActive"] = isActive,
        };

    private static string InputTag(string html, string name)
    {
        var match = Regex.Match(html, $"<input[^>]*name=\"{Regex.Escape(name)}\"[^>]*>");
        match.Success.ShouldBeTrue($"no input named {name}");
        return match.Value;
    }

    private static async Task<string> GetFormAsync(HttpClient client, Guid serviceId)
    {
        var response = await client.GetAsync(new Uri($"/admin/services/{serviceId}", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>GETs the form for its anti-forgery token (the cookie half rides on the client) and POSTs the fields with it.</summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, Guid serviceId, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(await GetFormAsync(client, serviceId));
        return await client.PostAsync(new Uri($"/admin/services/{serviceId}", UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    private Task<HttpClient> OwnerClientAsync(string host, Guid tenantId) => factory.SignedInClientAsync(host, tenantId, Role.Owner);

    private async Task<Guid> AddCategoryAsync(Guid tenantId, string name, int sortOrder, bool isActive = true)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var category = new ServiceCategory { Name = name, SortOrder = sortOrder, IsActive = isActive };
        db.ServiceCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    private async Task<Guid> AddServiceAsync(Guid tenantId, string name, int minutes, decimal price, string currency, int sortOrder, Guid? categoryId = null, int buffer = 0)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var service = new Service { Name = name, Duration = Duration.FromMinutes(minutes), Price = price, Currency = currency, SortOrder = sortOrder, CategoryId = categoryId, BufferAfterMinutes = buffer };
        db.Services.Add(service);
        await db.SaveChangesAsync();
        return service.Id;
    }

    private async Task<Service> ServiceAsync(Guid tenantId, Guid serviceId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Services.AsNoTracking().SingleAsync(s => s.Id == serviceId);
    }

    private async Task<List<OutboxMessage>> OutboxAsync()
    {
        await using var scope = factory.CreateTenantScope(null);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().OutboxMessages.AsNoTracking().ToListAsync();
    }
}