using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Clients;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Clients;

/// <summary>
/// IClientService against the real database (ADR 0011): normalisation per salon country, duplicates named by field,
/// consent stamping and clearing, health notes encrypted at rest and decrypted only on the card, and tenant isolation.
/// </summary>
[Collection(SharedDatabase.Name)]
public class ClientServiceTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HealthText = "Allergic to PPD; patch test negative";

    private Tenant _uk = null!;
    private Tenant _pl = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _uk = await factory.CreateTenantAsync("clients-uk", name: "Studio 20");
        _pl = await factory.CreateTenantAsync("clients-pl", "pl-PL", "PLN", "Salon PL");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_normalises_contact_details_for_the_salon_country_and_encrypts_health_notes()
    {
        Guid ukId;
        await using (var scope = factory.CreateTenantScope(_uk.Id))
        {
            ukId = await Clients(scope).CreateAsync(new CreateClientRequest("  Olivia ", " Brown ", "07700 900123", " Olivia@Example.COM ", " Prefers afternoons. ", HealthText));
        }

        Guid plId;
        await using (var scope = factory.CreateTenantScope(_pl.Id))
        {
            plId = await Clients(scope).CreateAsync(new CreateClientRequest("Anna", "Nowak", "601 234 567"));
        }

        var ukRow = await RowAsync(_uk.Id, ukId);
        ukRow.TenantId.ShouldBe(_uk.Id);
        ukRow.FirstName.ShouldBe("Olivia");
        ukRow.LastName.ShouldBe("Brown");
        ukRow.Phone.ShouldBe("+447700900123");
        ukRow.Email.ShouldBe("olivia@example.com");
        ukRow.Notes.ShouldBe("Prefers afternoons.");
        ukRow.HealthNotesProtected.ShouldNotBeNull().ShouldNotContain("Allergic to PPD", Case.Sensitive);
        ukRow.MarketingConsent.ShouldBeFalse();
        ukRow.MarketingConsentAt.ShouldBeNull();
        ukRow.MarketingConsentWording.ShouldBeNull();
        ukRow.CreatedAt.ShouldBe(ukRow.UpdatedAt);

        (await RowAsync(_pl.Id, plId)).Phone.ShouldBe("+48601234567");

        await using (var scope = factory.CreateTenantScope(_uk.Id))
        {
            var card = (await Clients(scope).GetCardAsync(ukId)).ShouldNotBeNull();
            card.HealthNotes.ShouldBe(HealthText);
            card.HealthNotesUnreadable.ShouldBeFalse();
            card.FullName.ShouldBe("Olivia Brown");
            card.ToString().ShouldNotContain("Allergic to PPD", Case.Sensitive);
        }
    }

    [Fact]
    public async Task A_duplicate_phone_or_email_is_refused_naming_the_field_but_only_within_the_salon()
    {
        await using var scope = factory.CreateTenantScope(_uk.Id);
        var clients = Clients(scope);
        await clients.CreateAsync(new CreateClientRequest("Olivia", "Brown", "+44 7700 900123", "olivia@example.com"));

        var byPhone = await Should.ThrowAsync<DuplicateClientException>(() => clients.CreateAsync(new CreateClientRequest("Oliver", "Twist", "07700 900123")));
        byPhone.Field.ShouldBe("Phone");
        byPhone.Message.ShouldContain("phone");
        byPhone.Message.ShouldNotContain("7700");

        var byEmail = await Should.ThrowAsync<DuplicateClientException>(() => clients.CreateAsync(new CreateClientRequest("Oliver", "Twist", Email: "OLIVIA@example.com")));
        byEmail.Field.ShouldBe("Email");

        await using var other = factory.CreateTenantScope(_pl.Id);
        await Clients(other).CreateAsync(new CreateClientRequest("Olivia", "Brown", "+44 7700 900123", "olivia@example.com"));
        (await Clients(other).SearchAsync("Olivia")).ShouldHaveSingleItem().Phone.ShouldBe("+447700900123");
        (await clients.SearchAsync("Olivia")).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Invalid_input_is_refused_before_anything_is_written()
    {
        await using var scope = factory.CreateTenantScope(_pl.Id);
        var clients = Clients(scope);
        CreateClientRequest[] invalid =
        [
            new("Anna", ""),
            new("", "Nowak"),
            new(new string('a', Client.NameMaxLength + 1), "Nowak"),
            new("Anna", "Nowak", "07700 900123"),
            new("Anna", "Nowak", "call me"),
            new("Anna", "Nowak", Email: "not-an-email"),
            new("Anna", "Nowak", Notes: new string('n', Client.NotesMaxLength + 1)),
            new("Anna", "Nowak", HealthNotes: new string('h', Client.HealthNotesMaxLength + 1)),
            new("Anna", "Nowak", MarketingConsent: true, MarketingConsentWording: " "),
        ];

        foreach (var request in invalid)
        {
            await Should.ThrowAsync<ArgumentException>(() => clients.CreateAsync(request), request.ToString());
        }

        (await clients.SearchAsync(null)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Consent_records_the_wording_and_the_instant_and_withdrawal_clears_both()
    {
        await using var scope = factory.CreateTenantScope(_uk.Id);
        var clients = Clients(scope);
        var id = await clients.CreateAsync(new CreateClientRequest("Olivia", "Brown"));

        await clients.SetMarketingConsentAsync(id, true, " I agree to Studio 20 contacting me. ");

        var granted = (await clients.GetCardAsync(id)).ShouldNotBeNull();
        granted.MarketingConsent.ShouldBeTrue();
        granted.MarketingConsentAt.ShouldNotBeNull();
        granted.MarketingConsentWording.ShouldBe("I agree to Studio 20 contacting me.");
        granted.UpdatedAt.ShouldBeGreaterThanOrEqualTo(granted.CreatedAt);

        await clients.SetMarketingConsentAsync(id, false, null);

        var withdrawn = (await clients.GetCardAsync(id)).ShouldNotBeNull();
        withdrawn.MarketingConsent.ShouldBeFalse();
        withdrawn.MarketingConsentAt.ShouldBeNull();
        withdrawn.MarketingConsentWording.ShouldBeNull();

        await Should.ThrowAsync<ArgumentException>(() => clients.SetMarketingConsentAsync(id, true, ""));
    }

    [Fact]
    public async Task Health_notes_update_round_trips_through_encryption_and_null_clears_them()
    {
        await using var scope = factory.CreateTenantScope(_uk.Id);
        var clients = Clients(scope);
        var id = await clients.CreateAsync(new CreateClientRequest("Olivia", "Brown", HealthNotes: HealthText));
        var before = (await RowAsync(_uk.Id, id)).HealthNotesProtected.ShouldNotBeNull();

        await clients.UpdateHealthNotesAsync(id, " Lash tint: mild redness in 2026. ", new LocalDate(2026, 11, 1));

        var updated = (await clients.GetCardAsync(id)).ShouldNotBeNull();
        updated.HealthNotes.ShouldBe("Lash tint: mild redness in 2026.");
        updated.LastPatchTestOn.ShouldBe(new LocalDate(2026, 11, 1));
        var after = (await RowAsync(_uk.Id, id)).HealthNotesProtected.ShouldNotBeNull();
        after.ShouldNotBe(before);
        after.ShouldNotContain("Lash");

        await clients.UpdateHealthNotesAsync(id, null, null);

        (await RowAsync(_uk.Id, id)).HealthNotesProtected.ShouldBeNull();
        var cleared = (await clients.GetCardAsync(id)).ShouldNotBeNull();
        cleared.HealthNotes.ShouldBeNull();
        cleared.LastPatchTestOn.ShouldBeNull();
    }

    [Fact]
    public async Task Details_update_normalises_refuses_another_clients_phone_or_email_and_ignores_the_clients_own()
    {
        await using var scope = factory.CreateTenantScope(_uk.Id);
        var clients = Clients(scope);
        await clients.CreateAsync(new CreateClientRequest("Olivia", "Brown", "+44 7700 900123", "olivia@example.com"));
        var id = await clients.CreateAsync(new CreateClientRequest("Oliver", "Twist", "07700 900999", "oliver@example.com", HealthNotes: HealthText));
        var before = await RowAsync(_uk.Id, id);

        var byPhone = await Should.ThrowAsync<DuplicateClientException>(() => clients.UpdateDetailsAsync(id, "Oliver", "Twist", "07700 900123", null, null));
        byPhone.Field.ShouldBe("Phone");
        var byEmail = await Should.ThrowAsync<DuplicateClientException>(() => clients.UpdateDetailsAsync(id, "Oliver", "Twist", null, "OLIVIA@example.com", null));
        byEmail.Field.ShouldBe("Email");
        await Should.ThrowAsync<ArgumentException>(() => clients.UpdateDetailsAsync(id, "", "Twist", null, null, null));
        await Should.ThrowAsync<ArgumentException>(() => clients.UpdateDetailsAsync(id, "Oliver", "Twist", "call me", null, null));
        (await RowAsync(_uk.Id, id)).Phone.ShouldBe("+447700900999", "nothing is written before the checks pass");

        await clients.UpdateDetailsAsync(id, " Ollie ", " Twist ", "07700 900999", " OLIVER@Example.com ", " Likes tea. ");

        var row = await RowAsync(_uk.Id, id);
        row.FirstName.ShouldBe("Ollie");
        row.LastName.ShouldBe("Twist");
        row.Phone.ShouldBe("+447700900999");
        row.Email.ShouldBe("oliver@example.com");
        row.Notes.ShouldBe("Likes tea.");
        row.HealthNotesProtected.ShouldBe(before.HealthNotesProtected, "details never touch the health notes");
        row.UpdatedAt.ShouldBeGreaterThan(before.UpdatedAt);

        await using var other = factory.CreateTenantScope(_pl.Id);
        await Should.ThrowAsync<InvalidOperationException>(() => Clients(other).UpdateDetailsAsync(id, "Hacked", "Twist", null, null, null));
    }

    [Fact]
    public async Task Cipher_text_the_key_ring_cannot_open_is_flagged_and_never_shown()
    {
        Guid id;
        await using (var scope = factory.CreateTenantScope(_uk.Id))
        {
            id = await Clients(scope).CreateAsync(new CreateClientRequest("Olivia", "Brown", HealthNotes: HealthText));
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            var row = await db.Clients.SingleAsync(c => c.Id == id);
            row.HealthNotesProtected = "CfDJ8-not-made-by-this-key-ring";
            await db.SaveChangesAsync();
        }

        await using var reader = factory.CreateTenantScope(_uk.Id);
        var card = (await Clients(reader).GetCardAsync(id)).ShouldNotBeNull();
        card.HealthNotesUnreadable.ShouldBeTrue();
        card.HealthNotes.ShouldBeNull();
    }

    [Fact]
    public async Task Another_salon_cannot_find_read_or_change_the_client()
    {
        Guid id;
        await using (var scope = factory.CreateTenantScope(_uk.Id))
        {
            id = await Clients(scope).CreateAsync(new CreateClientRequest("Olivia", "Brown", "07700 900123", HealthNotes: HealthText));
        }

        await using var other = factory.CreateTenantScope(_pl.Id);
        var clients = Clients(other);
        (await clients.GetCardAsync(id)).ShouldBeNull();
        (await clients.SearchAsync("Olivia")).ShouldBeEmpty();
        (await clients.SearchAsync("7700")).ShouldBeEmpty();
        await Should.ThrowAsync<InvalidOperationException>(() => clients.UpdateHealthNotesAsync(id, "changed", null));
        await Should.ThrowAsync<InvalidOperationException>(() => clients.SetMarketingConsentAsync(id, true, "wording"));

        await using var none = factory.CreateTenantScope(null);
        (await Clients(none).GetCardAsync(id)).ShouldBeNull();
    }

    [Fact]
    public async Task Search_orders_by_last_name_then_first_name_and_bounds_the_page()
    {
        await using var scope = factory.CreateTenantScope(_uk.Id);
        var clients = Clients(scope);
        await clients.CreateAsync(new CreateClientRequest("Zoe", "Adams"));
        await clients.CreateAsync(new CreateClientRequest("Ben", "Brown"));
        await clients.CreateAsync(new CreateClientRequest("Amy", "Brown"));

        (await clients.SearchAsync("  ")).Select(c => c.FullName).ShouldBe(["Zoe Adams", "Amy Brown", "Ben Brown"]);
        (await clients.SearchAsync("bro", take: 1)).Select(c => c.FullName).ShouldBe(["Amy Brown"]);
        (await clients.SearchAsync("b")).Select(c => c.FullName).ShouldBe(["Amy Brown", "Ben Brown"]);
        (await clients.SearchAsync("%")).ShouldBeEmpty("LIKE wildcards are matched literally");

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => clients.SearchAsync("a", take: 0));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => clients.SearchAsync("a", take: 1_000));
    }

    private static IClientService Clients(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IClientService>();

    private async Task<Client> RowAsync(Guid tenantId, Guid clientId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Clients.AsNoTracking().SingleAsync(c => c.Id == clientId);
    }
}