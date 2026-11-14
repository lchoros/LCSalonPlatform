using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Catalog;
using Salon.Core.Catalog.Events;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Catalog;

/// <summary>
/// The CreateService slice end to end over HTTP as a signed-in Owner: form, anti-forgery, pipeline validation with
/// localized messages, the service row in the right tenant with the tenant's currency, the ServiceCreated outbox row, and
/// the two gates (tenant required, Catalog.Manage permission — the role matrix itself is covered in Identity/PermissionTests).
/// </summary>
[Collection(SharedDatabase.Name)]
public class CreateServiceTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string FormPath = "/admin/services/new";

    private Tenant _studio = null!;
    private Tenant _polish = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        await AddServiceAsync(_studio.Id, "Existing", sortOrder: 3);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_form_renders_for_a_salon_in_its_language()
    {
        using var english = await OwnerClientAsync("studio-20.localhost", _studio.Id);
        using var polish = await OwnerClientAsync("salon-pl.localhost", _polish.Id);

        var englishHtml = await GetFormAsync(english);
        var polishHtml = await GetFormAsync(polish);

        englishHtml.ShouldContain("data-testid=\"create-service-form\"");
        englishHtml.ShouldContain("Add a service");
        englishHtml.ShouldContain("__RequestVerificationToken");
        polishHtml.ShouldContain("Dodaj usługę");
    }

    [Fact]
    public async Task A_valid_post_creates_the_service_for_that_tenant_only_and_queues_ServiceCreated()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var response = await PostAsync(client, new()
        {
            ["Name"] = "  Cut & blow-dry  ",
            ["Description"] = "Wash, cut and finish.",
            ["DurationMinutes"] = "45",
            ["Price"] = "45.50",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe("/admin/services");

        var created = (await ServicesAsync(_studio.Id)).Single(s => s.Name == "Cut & blow-dry");
        created.TenantId.ShouldBe(_studio.Id);
        created.Description.ShouldBe("Wash, cut and finish.");
        created.Duration.ShouldBe(Duration.FromMinutes(45));
        created.Price.ShouldBe(45.50m);
        created.Currency.ShouldBe("GBP");
        created.SortOrder.ShouldBe(4);
        created.IsActive.ShouldBeTrue();
        (await ServicesAsync(_polish.Id)).ShouldBeEmpty();

        await using var scope = factory.CreateTenantScope(null);
        var outbox = await scope.ServiceProvider.GetRequiredService<SalonDbContext>().OutboxMessages.AsNoTracking().ToListAsync();
        var row = outbox.ShouldHaveSingleItem();
        row.EventType.ShouldBe(typeof(ServiceCreated).FullName);
        row.TenantId.ShouldBe(_studio.Id);
        using var payload = JsonDocument.Parse(row.Payload);
        payload.RootElement.GetProperty("serviceId").GetGuid().ShouldBe(created.Id);
        payload.RootElement.GetProperty("tenantId").GetGuid().ShouldBe(_studio.Id);
        payload.RootElement.GetProperty("name").GetString().ShouldBe("Cut & blow-dry");
    }

    [Fact]
    public async Task The_form_offers_the_salons_active_categories_and_stores_category_buffer_and_processing_time()
    {
        Guid hairId;
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            var hair = new ServiceCategory { Name = "Hair", SortOrder = 1 };
            db.ServiceCategories.Add(hair);
            db.ServiceCategories.Add(new ServiceCategory { Name = "Retired", SortOrder = 2, IsActive = false });
            await db.SaveChangesAsync();
            hairId = hair.Id;
        }
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var form = await GetFormAsync(client);
        form.ShouldContain($"value=\"{hairId}\">Hair</option>");
        form.ShouldNotContain("Retired");
        form.ShouldContain("data-testid=\"buffer-input\"");

        var response = await PostAsync(client, new()
        {
            ["Name"] = "Colour (roots)",
            ["DurationMinutes"] = "90",
            ["Price"] = "65",
            ["CategoryId"] = hairId.ToString(),
            ["BufferAfterMinutes"] = "15",
            ["ProcessingMinutes"] = "30",
            ["ProcessingStartsAfterMinutes"] = "20",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var created = (await ServicesAsync(_studio.Id)).Single(s => s.Name == "Colour (roots)");
        created.CategoryId.ShouldBe(hairId);
        created.BufferAfterMinutes.ShouldBe(15);
        created.ProcessingMinutes.ShouldBe(30);
        created.ProcessingStartsAfterMinutes.ShouldBe(20);

        var list = await client.GetAsync(response.Headers.Location!);
        var html = await list.Content.ReadAsStringAsync();
        html.ShouldContain("Service “Colour (roots)” has been added.");
        html.ShouldContain("30 min processing");
    }

    [Fact]
    public async Task Timing_fields_off_the_grid_or_outside_the_duration_are_refused()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var response = await PostAsync(client, new()
        {
            ["Name"] = "Perm",
            ["DurationMinutes"] = "60",
            ["Price"] = "60",
            ["BufferAfterMinutes"] = "125",
            ["ProcessingMinutes"] = "45",
            ["ProcessingStartsAfterMinutes"] = "20",
            ["CategoryId"] = Guid.NewGuid().ToString(),
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("The buffer must be between 0 and 120 minutes.");
        html.ShouldContain("The processing time must fit inside the duration");
        // The encoder writes the apostrophe as &#x27;.
        html.ShouldContain("Choose one of the salon&#x27;s categories.");
        (await ServicesAsync(_studio.Id)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_invalid_post_re_renders_the_form_with_messages_in_the_salon_language()
    {
        using var client = await OwnerClientAsync("salon-pl.localhost", _polish.Id);

        var response = await PostAsync(client, new()
        {
            ["Name"] = "",
            ["Description"] = "",
            ["DurationMinutes"] = "47",
            ["Price"] = "45,005",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("Podaj nazwę usługi.");
        html.ShouldContain("Czas trwania musi być wielokrotnością 5 minut.");
        html.ShouldContain("Cena może mieć najwyżej dwa miejsca po przecinku.");
        html.ShouldContain("input-validation-error");
        (await ServicesAsync(_polish.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_duplicate_name_is_a_form_message_not_a_server_error()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var response = await PostAsync(client, new() { ["Name"] = "existing ", ["DurationMinutes"] = "30", ["Price"] = "20" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("A service with this name already exists.");
        (await ServicesAsync(_studio.Id)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_post_without_the_anti_forgery_token_is_refused()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var response = await client.PostAsync(
            new Uri(FormPath, UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string> { ["Name"] = "Sneaky", ["DurationMinutes"] = "30", ["Price"] = "20" }));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ServicesAsync(_studio.Id)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_bare_platform_domain_has_no_catalogue()
    {
        using var client = factory.ClientFor("localhost");

        (await client.GetAsync(new Uri(FormPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Without_a_sign_in_the_screen_asks_for_login()
    {
        using var client = factory.ClientFor("studio-20.localhost");

        var response = await client.GetAsync(new Uri(FormPath, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");
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

    /// <summary>A client signed in as an Owner of the tenant. Redirects are asserted, not followed: the public list is another slice.</summary>
    private Task<HttpClient> OwnerClientAsync(string host, Guid tenantId) => factory.SignedInClientAsync(host, tenantId, Role.Owner);

    private async Task AddServiceAsync(Guid tenantId, string name, int sortOrder)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        db.Services.Add(new Service { Name = name, Duration = Duration.FromMinutes(30), Price = 20m, Currency = "GBP", SortOrder = sortOrder });
        await db.SaveChangesAsync();
    }

    private async Task<List<Service>> ServicesAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Services.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync();
    }
}