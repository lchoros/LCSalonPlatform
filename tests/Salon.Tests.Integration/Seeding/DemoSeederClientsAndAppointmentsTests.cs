using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NodaTime;

using Salon.Core.Appointments;
using Salon.Core.Clients;
using Salon.Data;
using Salon.Services.Clients;
using Salon.Services.Seeding;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Seeding;

/// <summary>
/// The demo clients and appointments of the admin booking slice: twelve clients with normalised phones and encrypted
/// health notes, a handful of appointments over the next working days plus one completed yesterday and one cancelled,
/// all booked through the booking service so the exclusion constraint and the state machine apply; added once to a fresh
/// salon and once to an existing demo salon that has none, never twice.
/// </summary>
[Collection(SharedDatabase.Name)]
public class DemoSeederClientsAndAppointmentsTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private const string JamesHealthNotes = "PPD sensitivity — patch test required before any colour.";

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_fresh_demo_salon_gets_twelve_clients_with_encrypted_health_notes()
    {
        (await SeedAsync()).ShouldBeTrue();
        var tenantId = await TenantIdAsync();

        var clients = await ClientsAsync(tenantId);
        clients.Count.ShouldBe(12);
        clients.ShouldAllBe(c => c.TenantId == tenantId && c.IsActive);
        clients.Select(c => c.FullName).ShouldContain("Sophie Smith");
        clients.Select(c => c.FullName).ShouldContain("Katarzyna Nowak");
        clients.Select(c => c.Phone).ShouldAllBe(p => p != null && p.StartsWith("+447700900", StringComparison.Ordinal));
        clients.Select(c => c.Phone).Distinct().Count().ShouldBe(12);
        clients.Select(c => c.Email).ShouldAllBe(e => e != null && e.EndsWith("@example.com", StringComparison.Ordinal) && string.Equals(e, e.ToLowerInvariant(), StringComparison.Ordinal));

        var sophie = clients.Single(c => c.FirstName == "Sophie");
        sophie.Notes.ShouldBe("Prefers morning slots.");
        sophie.HealthNotesProtected.ShouldBeNull();
        sophie.MarketingConsent.ShouldBeTrue();
        sophie.MarketingConsentAt.ShouldNotBeNull();
        sophie.MarketingConsentWording.ShouldNotBeNull().ShouldContain("Studio 20");

        var james = clients.Single(c => c.FirstName == "James");
        james.HealthNotesProtected.ShouldNotBeNull();
        james.HealthNotesProtected.ShouldNotContain("PPD");
        james.LastPatchTestOn.ShouldBe(new LocalDate(2026, 5, 15));
        james.MarketingConsent.ShouldBeFalse();
        james.MarketingConsentWording.ShouldBeNull();
        (await CardAsync(tenantId, james.Id)).HealthNotes.ShouldBe(JamesHealthNotes);

        clients.Count(c => c.HealthNotesProtected != null).ShouldBe(5);
        clients.Count(c => c.MarketingConsent).ShouldBe(8);
    }

    [Fact]
    public async Task A_fresh_demo_salon_gets_appointments_booked_through_the_engine_with_a_spread_of_statuses()
    {
        await SeedAsync();
        var tenantId = await TenantIdAsync();
        var now = SystemClock.Instance.GetCurrentInstant();
        var today = now.InZone(London).Date;

        var appointments = await AppointmentsAsync(tenantId);
        var clients = await ClientsAsync(tenantId);
        appointments.Count.ShouldBeInRange(2, 6);
        appointments.ShouldAllBe(a => a.TenantId == tenantId && a.Source == AppointmentSource.Admin && a.Currency == "GBP" && a.PriceAmount > 0m && a.DurationMinutes > 0);
        appointments.ShouldAllBe(a => clients.Any(c => c.Id == a.ClientId));
        appointments.Select(a => a.Status).Distinct().ShouldBeSubsetOf([AppointmentStatus.Booked, AppointmentStatus.Confirmed, AppointmentStatus.Completed, AppointmentStatus.CancelledByClient]);

        var completed = appointments.Single(a => a.Status == AppointmentStatus.Completed);
        completed.Start.InZone(London).Date.ShouldBe(today.PlusDays(-1));
        completed.End.ShouldBe(completed.Start.Plus(Duration.FromMinutes(completed.DurationMinutes)));

        var upcoming = appointments.Where(a => a.Status != AppointmentStatus.Completed).ToList();
        upcoming.ShouldAllBe(a => a.Start > now, "only slots the engine offers now are booked");
        upcoming.Select(a => a.Start.InZone(London).Date).Distinct().Count().ShouldBeLessThanOrEqualTo(3, "today and the next two working days");
        upcoming.ShouldAllBe(a => a.Start.InZone(London).Date <= today.PlusDays(14));
        upcoming.Where(a => a.Status == AppointmentStatus.CancelledByClient).ShouldAllBe(a => a.CancelledAt != null);
        appointments.Count(a => a.Status == AppointmentStatus.Confirmed).ShouldBeGreaterThanOrEqualTo(1);

        await using var scope = factory.CreateTenantScope(tenantId);
        var changes = await scope.ServiceProvider.GetRequiredService<SalonDbContext>().AppointmentStatusChanges.AsNoTracking().ToListAsync();
        changes.Where(c => c.AppointmentId == completed.Id).OrderBy(c => c.At).Select(c => c.To)
            .ShouldBe([AppointmentStatus.Booked, AppointmentStatus.Confirmed, AppointmentStatus.Arrived, AppointmentStatus.Completed]);
        foreach (var appointment in appointments)
        {
            changes.Where(c => c.AppointmentId == appointment.Id).OrderBy(c => c.At).Last().To.ShouldBe(appointment.Status);
        }
    }

    [Fact]
    public async Task Seeding_again_adds_no_clients_and_no_appointments()
    {
        await SeedAsync();
        var tenantId = await TenantIdAsync();
        var clientIds = (await ClientsAsync(tenantId)).Select(c => c.Id).Order().ToList();
        var appointmentIds = (await AppointmentsAsync(tenantId)).Select(a => a.Id).Order().ToList();

        (await SeedAsync()).ShouldBeFalse();
        (await SeedAsync()).ShouldBeFalse();

        (await ClientsAsync(tenantId)).Select(c => c.Id).Order().ShouldBe(clientIds);
        (await AppointmentsAsync(tenantId)).Select(a => a.Id).Order().ShouldBe(appointmentIds);
    }

    [Fact]
    public async Task An_existing_demo_salon_without_clients_or_appointments_receives_them_once()
    {
        await SeedAsync();
        var tenantId = await TenantIdAsync();
        await using (var scope = factory.CreateTenantScope(tenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            db.Appointments.RemoveRange(await db.Appointments.ToListAsync());
            await db.SaveChangesAsync();
            db.Clients.RemoveRange(await db.Clients.ToListAsync());
            await db.SaveChangesAsync();
        }

        (await SeedAsync()).ShouldBeFalse("the tenant already existed");

        (await ClientsAsync(tenantId)).Count.ShouldBe(12);
        (await AppointmentsAsync(tenantId)).Count.ShouldBeInRange(2, 6);
        (await SeedAsync()).ShouldBeFalse();
        (await ClientsAsync(tenantId)).Count.ShouldBe(12);
    }

    [Fact]
    public async Task A_salon_that_already_has_a_client_or_an_appointment_is_left_alone()
    {
        await SeedAsync();
        var tenantId = await TenantIdAsync();
        await using (var scope = factory.CreateTenantScope(tenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            var keep = await db.Appointments.OrderBy(a => a.Start).FirstAsync();
            db.Appointments.RemoveRange(await db.Appointments.Where(a => a.Id != keep.Id).ToListAsync());
            await db.SaveChangesAsync();
            db.Clients.RemoveRange(await db.Clients.Where(c => c.Id != keep.ClientId).ToListAsync());
            await db.SaveChangesAsync();
        }

        (await SeedAsync()).ShouldBeFalse();

        (await ClientsAsync(tenantId)).Count.ShouldBe(1);
        (await AppointmentsAsync(tenantId)).Count.ShouldBe(1);
    }

    private async Task<bool> SeedAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DemoSeeder>().SeedAsync();
    }

    private async Task<Guid> TenantIdAsync()
    {
        await using var scope = factory.CreateTenantScope(null);
        return (await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Tenants.AsNoTracking().SingleAsync(t => t.Slug == DemoSeeder.Slug)).Id;
    }

    private async Task<List<Client>> ClientsAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Clients.AsNoTracking().OrderBy(c => c.LastName).ThenBy(c => c.FirstName).ToListAsync();
    }

    private async Task<List<Appointment>> AppointmentsAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Appointments.AsNoTracking().OrderBy(a => a.Start).ToListAsync();
    }

    private async Task<ClientCard> CardAsync(Guid tenantId, Guid clientId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return (await scope.ServiceProvider.GetRequiredService<IClientService>().GetCardAsync(clientId)).ShouldNotBeNull();
    }
}