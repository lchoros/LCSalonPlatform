using System.Net;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NodaTime;

using Salon.Core.Appointments;
using Salon.Core.Clients;
using Salon.Core.Clients.Events;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Clients;

/// <summary>
/// Merging duplicates (ADR 0017) through the service and over HTTP: appointments and their history move, the rules of
/// ClientMergeRules apply to the real rows, the source is deactivated with the pointer and vanishes from search, the
/// event carries ids only, and the gates hold (self-merge 400, another salon 404, Manager yes, Receptionist no).
/// </summary>
[Collection(SharedDatabase.Name)]
public class MergeClientsTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string Host = "studio-20.localhost";
    private const string TargetHealth = "Allergic to PPD; patch test negative";
    private const string SourceHealth = "Sensitive scalp";

    private Tenant _studio = null!;
    private Tenant _other = null!;
    private Guid _staffId;
    private Guid _serviceId;
    private Guid _targetId;
    private Guid _sourceId;
    private Instant _now;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _other = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        (_staffId, _serviceId) = await VisitSeed.SalonAsync(factory, _studio.Id);
        _now = Instant.FromUnixTimeSeconds(SystemClock.Instance.GetCurrentInstant().ToUnixTimeSeconds());

        _targetId = await CreateAsync(_studio.Id, new CreateClientRequest(
            "Olivia", "Brown", Phone: "07700 900123", Email: "olivia@example.com", Notes: "Prefers afternoons.",
            HealthNotes: TargetHealth, LastPatchTestOn: new LocalDate(2026, 10, 1), MarketingConsent: true, MarketingConsentWording: "I agree to Studio 20 contacting me."));
        _sourceId = await CreateAsync(_studio.Id, new CreateClientRequest(
            "Olivia", "Browne", Phone: "07700 900999", Notes: "Allergic to dogs.", HealthNotes: SourceHealth));

        await VisitSeed.AppointmentAsync(factory, _studio.Id, _targetId, _staffId, _serviceId, _now.Minus(Duration.FromDays(10)), AppointmentStatus.Completed);
        await VisitSeed.AppointmentAsync(factory, _studio.Id, _sourceId, _staffId, _serviceId, _now.Minus(Duration.FromDays(3)), AppointmentStatus.Completed);
        await VisitSeed.AppointmentAsync(factory, _studio.Id, _sourceId, _staffId, _serviceId, _now.Plus(Duration.FromDays(2)), AppointmentStatus.Booked);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Merging_moves_appointments_with_their_history_applies_the_rules_and_deactivates_the_source()
    {
        var sourceAppointments = (await VisitSeed.AppointmentsOfAsync(factory, _studio.Id, _sourceId)).Select(a => a.Id).ToArray();
        sourceAppointments.Length.ShouldBe(2);

        await MergeAsync(_studio.Id, _targetId, _sourceId);

        (await VisitSeed.AppointmentsOfAsync(factory, _studio.Id, _sourceId)).ShouldBeEmpty();
        var moved = await VisitSeed.AppointmentsOfAsync(factory, _studio.Id, _targetId);
        moved.Count.ShouldBe(3);
        moved.Select(a => a.Id).ShouldBeSubsetOf(moved.Select(a => a.Id)); // sanity
        sourceAppointments.ShouldAllBe(id => moved.Any(a => a.Id == id));

        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        (await db.AppointmentStatusChanges.AsNoTracking().CountAsync(c => sourceAppointments.Contains(c.AppointmentId))).ShouldBe(3, "the Booked rows and one Completed transition follow the appointments");

        var target = await db.Clients.AsNoTracking().SingleAsync(c => c.Id == _targetId);
        target.Notes.ShouldBe("Prefers afternoons.\n---\nAllergic to dogs.");
        target.Phone.ShouldBe("+447700900123");
        target.Email.ShouldBe("olivia@example.com");
        target.LastPatchTestOn.ShouldBe(new LocalDate(2026, 10, 1));
        target.MarketingConsent.ShouldBeTrue();
        target.MarketingConsentWording.ShouldBe("I agree to Studio 20 contacting me.");
        target.IsActive.ShouldBeTrue();
        target.MergedIntoClientId.ShouldBeNull();

        var source = await db.Clients.AsNoTracking().SingleAsync(c => c.Id == _sourceId);
        source.IsActive.ShouldBeFalse();
        source.MergedIntoClientId.ShouldBe(_targetId);
        source.MergedAt.ShouldNotBeNull();
        source.Phone.ShouldBeNull("the number is unique per salon and must not keep pointing at a dead card");
        source.Email.ShouldBeNull();
        source.Notes.ShouldBe("Allergic to dogs.");

        var card = (await scope.ServiceProvider.GetRequiredService<IClientService>().GetCardAsync(_targetId)).ShouldNotBeNull();
        card.HealthNotes.ShouldBe(TargetHealth, "the kept card's health notes win");
        card.HealthNotesUnreadable.ShouldBeFalse();

        var history = (await scope.ServiceProvider.GetRequiredService<IClientService>().GetHistoryAsync(_targetId)).ShouldNotBeNull();
        history.VisitsTotal.ShouldBe(3);
        history.VisitsCompleted.ShouldBe(2);
        history.Upcoming.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_source_fills_gaps_only_and_consent_is_never_gained()
    {
        var bare = await CreateAsync(_studio.Id, new CreateClientRequest("Mia", "Green"));
        var rich = await CreateAsync(_studio.Id, new CreateClientRequest(
            "Mia", "Greene", Phone: "07700 900555", Email: "mia@example.com", HealthNotes: SourceHealth, LastPatchTestOn: new LocalDate(2026, 3, 10),
            MarketingConsent: true, MarketingConsentWording: "I agree to Studio 20 contacting me."));

        await MergeAsync(_studio.Id, bare, rich);

        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var target = await db.Clients.AsNoTracking().SingleAsync(c => c.Id == bare);
        target.Phone.ShouldBe("+447700900555");
        target.Email.ShouldBe("mia@example.com");
        target.LastPatchTestOn.ShouldBe(new LocalDate(2026, 3, 10));
        target.MarketingConsent.ShouldBeFalse("a merge never grants consent");
        target.MarketingConsentAt.ShouldBeNull();
        target.MarketingConsentWording.ShouldBeNull();

        var card = (await scope.ServiceProvider.GetRequiredService<IClientService>().GetCardAsync(bare)).ShouldNotBeNull();
        card.HealthNotes.ShouldBe(SourceHealth, "the source's cipher text moved as it is and still decrypts");

        // The number is free again: a new card may use it, and the public flow no longer finds the merged card.
        (await scope.ServiceProvider.GetRequiredService<IClientService>().FindByPhoneAsync("07700 900555")).ShouldNotBeNull().Id.ShouldBe(bare);
    }

    [Fact]
    public async Task The_event_carries_ids_only_and_the_source_leaves_the_search()
    {
        await MergeAsync(_studio.Id, _targetId, _sourceId);

        await using (var scope = factory.CreateTenantScope(null))
        {
            var messages = await scope.ServiceProvider.GetRequiredService<SalonDbContext>().OutboxMessages.AsNoTracking()
                .Where(m => m.EventType == typeof(ClientsMerged).FullName).ToListAsync();
            var message = messages.ShouldHaveSingleItem();
            message.TenantId.ShouldBe(_studio.Id);
            using var payload = JsonDocument.Parse(message.Payload);
            payload.RootElement.GetProperty("targetId").GetGuid().ShouldBe(_targetId);
            payload.RootElement.GetProperty("sourceId").GetGuid().ShouldBe(_sourceId);
            payload.RootElement.GetProperty("tenantId").GetGuid().ShouldBe(_studio.Id);
            payload.RootElement.EnumerateObject().Count().ShouldBe(3, "events carry ids only");
            message.Payload.ShouldNotContain("Olivia");
            message.Payload.ShouldNotContain("7700");
        }

        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var results = await scope.ServiceProvider.GetRequiredService<IClientService>().SearchAsync("Olivia");
            results.Select(r => r.Id).ShouldBe([_targetId]);
        }

        using var client = await factory.SignedInClientAsync(Host, _studio.Id, Role.Owner);
        var search = await GetAsync(client, "/admin/clients?q=Browne");
        search.ShouldContain("data-testid=\"empty-state\"");
        search.ShouldNotContain("Olivia Browne");

        var sourceCard = await GetAsync(client, $"/admin/clients/{_sourceId}");
        sourceCard.ShouldContain("data-testid=\"merged-banner\"");
        sourceCard.ShouldContain("This client was merged into Olivia Brown.");
        sourceCard.ShouldContain($"href=\"/admin/clients/{_targetId}\"");
        sourceCard.ShouldNotContain("data-testid=\"merge-link\"");
        sourceCard.ShouldContain(">Inactive<");
    }

    [Fact]
    public async Task Self_merge_and_another_salons_cards_are_refused()
    {
        await Should.ThrowAsync<ClientMergeRefusedException>(() => MergeAsync(_studio.Id, _targetId, _targetId));
        await Should.ThrowAsync<InvalidOperationException>(() => MergeAsync(_other.Id, _targetId, _sourceId));
        (await VisitSeed.AppointmentsOfAsync(factory, _studio.Id, _sourceId)).Count.ShouldBe(2, "nothing moved");

        using var owner = await factory.SignedInClientAsync(Host, _studio.Id, Role.Owner);
        (await owner.GetAsync(new Uri($"/admin/clients/{_sourceId}/merge/{_sourceId}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var selfPost = await PostAsync(owner, _sourceId, _sourceId, tokenFrom: $"/admin/clients/{_sourceId}/merge/{_targetId}");
        selfPost.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await selfPost.Content.ReadAsStringAsync()).ShouldContain("A client cannot be merged into itself.");

        using var foreign = await factory.SignedInClientAsync("salon-pl.localhost", _other.Id, Role.Owner);
        (await foreign.GetAsync(new Uri($"/admin/clients/{_sourceId}/merge", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await foreign.GetAsync(new Uri($"/admin/clients/{_sourceId}/merge/{_targetId}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var foreignPost = await foreign.PostAsync(
            new Uri($"/admin/clients/{_sourceId}/merge/{_targetId}", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(await GetAsync(foreign, "/admin/clients/new")) }));
        foreignPost.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await VisitSeed.AppointmentsOfAsync(factory, _studio.Id, _sourceId)).Count.ShouldBe(2);

        await MergeAsync(_studio.Id, _targetId, _sourceId);
        await Should.ThrowAsync<ClientMergeRefusedException>(() => MergeAsync(_studio.Id, _targetId, _sourceId));
        (await owner.GetAsync(new Uri($"/admin/clients/{_sourceId}/merge", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_owner_searches_confirms_and_lands_on_the_kept_card_with_a_flash()
    {
        using var owner = await factory.SignedInClientAsync(Host, _studio.Id, Role.Owner);

        var card = await GetAsync(owner, $"/admin/clients/{_sourceId}");
        card.ShouldContain($"href=\"/admin/clients/{_sourceId}/merge\"");
        card.ShouldContain("Merge into another client");

        var pick = await GetAsync(owner, $"/admin/clients/{_sourceId}/merge");
        pick.ShouldContain("data-testid=\"merge-pick\"");
        pick.ShouldContain("Olivia Browne will be merged into that card and deactivated.");
        pick.ShouldContain("data-testid=\"merge-hint\"");

        var results = await GetAsync(owner, $"/admin/clients/{_sourceId}/merge?q=Oli");
        results.ShouldContain("data-testid=\"merge-candidate\"");
        results.ShouldContain("Olivia Brown");
        results.ShouldNotContain($"/merge/{_sourceId}\"");
        results.ShouldContain($"href=\"/admin/clients/{_sourceId}/merge/{_targetId}\"");
        results.ShouldContain("Keep this one");

        var confirm = await GetAsync(owner, $"/admin/clients/{_sourceId}/merge/{_targetId}");
        confirm.ShouldContain("data-testid=\"merge-confirm\"");
        confirm.ShouldContain("data-testid=\"source-name\">Olivia Browne<");
        confirm.ShouldContain("data-testid=\"target-name\">Olivia Brown<");
        confirm.ShouldContain("What will happen");
        confirm.ShouldContain("All 2 appointments of Olivia Browne and their history move to Olivia Brown.");
        confirm.ShouldContain("This cannot be undone.");
        confirm.ShouldContain("__RequestVerificationToken");
        confirm.ShouldNotContain(TargetHealth);
        confirm.ShouldNotContain(SourceHealth);

        var response = await PostAsync(owner, _sourceId, _targetId);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"/admin/clients/{_targetId}");

        var kept = await GetAsync(owner, $"/admin/clients/{_targetId}");
        kept.ShouldContain("data-testid=\"flash\"");
        kept.ShouldContain("Olivia Browne has been merged into Olivia Brown.");
        kept.ShouldContain("data-testid=\"visits-completed\">2<");
        kept.ShouldContain("Prefers afternoons.");
        kept.ShouldContain("Allergic to dogs.");
    }

    [Fact]
    public async Task The_confirmation_speaks_the_salon_language()
    {
        await VisitSeed.SalonAsync(factory, _other.Id);
        var anna = await CreateAsync(_other.Id, new CreateClientRequest("Anna", "Nowak", Phone: "601 234 567"));
        var annaDuplicate = await CreateAsync(_other.Id, new CreateClientRequest("Anna", "Nowak", Phone: "601 234 568"));
        using var owner = await factory.SignedInClientAsync("salon-pl.localhost", _other.Id, Role.Owner);

        var pick = await GetAsync(owner, $"/admin/clients/{annaDuplicate}/merge?q=Now");
        pick.ShouldContain("Scalanie klienta");
        pick.ShouldContain("Zachowaj tę kartę");

        var confirm = await GetAsync(owner, $"/admin/clients/{annaDuplicate}/merge/{anna}");
        confirm.ShouldContain("Potwierdź scalenie");
        confirm.ShouldContain("Co się stanie");
        confirm.ShouldContain("Tej operacji nie można cofnąć.");
        confirm.ShouldContain("Scal klientów");
    }

    [Fact]
    public async Task A_manager_may_merge_a_receptionist_may_not_and_a_post_needs_the_token()
    {
        using var manager = await factory.SignedInClientAsync(Host, _studio.Id, Role.Manager);
        (await manager.GetAsync(new Uri($"/admin/clients/{_sourceId}/merge", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(manager, $"/admin/clients/{_sourceId}")).ShouldContain("data-testid=\"merge-link\"");

        using var receptionist = await factory.SignedInClientAsync(Host, _studio.Id, Role.Receptionist);
        (await GetAsync(receptionist, $"/admin/clients/{_sourceId}")).ShouldNotContain("data-testid=\"merge-link\"");
        foreach (var path in new[] { $"/admin/clients/{_sourceId}/merge", $"/admin/clients/{_sourceId}/merge/{_targetId}" })
        {
            var denied = await receptionist.GetAsync(new Uri(path, UriKind.Relative));
            denied.StatusCode.ShouldBe(HttpStatusCode.Redirect, path);
            denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        }

        var deniedPost = await receptionist.PostAsync(
            new Uri($"/admin/clients/{_sourceId}/merge/{_targetId}", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(await GetAsync(receptionist, "/admin/clients/new")) }));
        deniedPost.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        deniedPost.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");

        var noToken = await manager.PostAsync(new Uri($"/admin/clients/{_sourceId}/merge/{_targetId}", UriKind.Relative), new FormUrlEncodedContent([]));
        noToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await VisitSeed.AppointmentsOfAsync(factory, _studio.Id, _sourceId)).Count.ShouldBe(2, "nothing merged yet");

        var merged = await PostAsync(manager, _sourceId, _targetId);
        merged.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await VisitSeed.AppointmentsOfAsync(factory, _studio.Id, _sourceId)).ShouldBeEmpty();
    }

    private async Task<Guid> CreateAsync(Guid tenantId, CreateClientRequest request)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<IClientService>().CreateAsync(request);
    }

    private async Task MergeAsync(Guid tenantId, Guid targetId, Guid sourceId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        await scope.ServiceProvider.GetRequiredService<IClientService>().MergeAsync(targetId, sourceId, Guid.NewGuid());
    }

    /// <summary>GETs the confirmation page for its anti-forgery token (the cookie half rides on the client) and POSTs the merge.</summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, Guid sourceId, Guid targetId, string? tokenFrom = null)
    {
        var token = IdentityTestClient.AntiForgeryToken(await GetAsync(client, tokenFrom ?? $"/admin/clients/{sourceId}/merge/{targetId}"));
        return await client.PostAsync(
            new Uri($"/admin/clients/{sourceId}/merge/{targetId}", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}