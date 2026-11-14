using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Services.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Clients;

/// <summary>
/// The visit history of ADR 0017 through the service and on the card: upcoming visits soonest first, past ones newest
/// first with their status, names resolved through the tenant filter, counters over every row, and the last completed
/// visit as "last visit". Another salon sees nothing.
/// </summary>
[Collection(SharedDatabase.Name)]
public class ClientHistoryTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string Host = "studio-20.localhost";
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];

    private Tenant _studio = null!;
    private Tenant _other = null!;
    private Guid _staffId;
    private Guid _serviceId;
    private Guid _clientId;
    private Instant _now;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _other = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        (_staffId, _serviceId) = await VisitSeed.SalonAsync(factory, _studio.Id);
        _clientId = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123");
        // Whole seconds: Postgres keeps microseconds, NodaTime ticks, and the test compares instants after a round trip.
        _now = Instant.FromUnixTimeSeconds(SystemClock.Instance.GetCurrentInstant().ToUnixTimeSeconds());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_service_splits_visits_into_upcoming_and_past_and_counts_them()
    {
        await SeedVisitsAsync();

        var history = await HistoryAsync(_studio.Id, _clientId);

        history.Upcoming.Select(v => v.Status).ShouldBe([AppointmentStatus.Booked, AppointmentStatus.Confirmed]);
        history.Upcoming[0].Start.ToInstant().ShouldBeLessThan(history.Upcoming[1].Start.ToInstant(), "soonest first");
        history.Past.Select(v => v.Status).ShouldBe([AppointmentStatus.CancelledByClient, AppointmentStatus.NoShow, AppointmentStatus.Completed, AppointmentStatus.Completed]);
        history.Past[0].Start.ToInstant().ShouldBeGreaterThan(history.Past[1].Start.ToInstant(), "newest first");
        history.Past.ShouldAllBe(v => v.ServiceName == VisitSeed.ServiceName && v.StaffName == VisitSeed.StaffName);
        history.Past.ShouldAllBe(v => v.Start.Zone == London);
        history.Past.ShouldAllBe(v => v.PriceAmount == 20m && v.Currency == "GBP" && v.DurationMinutes == 30);
        history.VisitsTotal.ShouldBe(6);
        history.VisitsCompleted.ShouldBe(2);
        history.NoShows.ShouldBe(1);
        history.LastVisitOn.ShouldBe(DaysAgo(3).InZone(London).Date);
    }

    [Fact]
    public async Task A_client_without_visits_has_empty_lists_and_zero_counters()
    {
        var history = await HistoryAsync(_studio.Id, _clientId);

        history.Upcoming.ShouldBeEmpty();
        history.Past.ShouldBeEmpty();
        history.VisitsTotal.ShouldBe(0);
        history.VisitsCompleted.ShouldBe(0);
        history.NoShows.ShouldBe(0);
        history.LastVisitOn.ShouldBeNull();
    }

    [Fact]
    public async Task Take_caps_each_list_but_not_the_counters()
    {
        for (var i = 1; i <= 3; i++)
        {
            await VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, DaysAgo(i), AppointmentStatus.Completed);
            await VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, InDays(i), AppointmentStatus.Booked);
        }

        await using var scope = factory.CreateTenantScope(_studio.Id);
        var history = (await scope.ServiceProvider.GetRequiredService<IClientService>().GetHistoryAsync(_clientId, take: 2)).ShouldNotBeNull();

        history.Upcoming.Count.ShouldBe(2);
        history.Past.Count.ShouldBe(2);
        history.Past[0].Start.ToInstant().ShouldBe(DaysAgo(1));
        history.VisitsTotal.ShouldBe(6);
        history.VisitsCompleted.ShouldBe(3);
    }

    [Fact]
    public async Task Another_salon_gets_null_for_the_same_id()
    {
        await SeedVisitsAsync();

        await using var scope = factory.CreateTenantScope(_other.Id);
        (await scope.ServiceProvider.GetRequiredService<IClientService>().GetHistoryAsync(_clientId)).ShouldBeNull();
    }

    [Fact]
    public async Task The_card_lists_both_sections_with_statuses_counters_last_visit_and_day_links()
    {
        await SeedVisitsAsync();
        using var client = await factory.SignedInClientAsync(Host, _studio.Id, Role.Owner);

        var html = await GetAsync(client, $"/admin/clients/{_clientId}");

        html.ShouldContain("data-testid=\"history\"");
        html.ShouldContain("Visit history");
        html.ShouldContain("data-testid=\"upcoming-list\"");
        html.ShouldContain("data-testid=\"past-list\"");
        Count(html, "data-testid=\"visit\"").ShouldBe(6);
        html.ShouldContain("data-status=\"Completed\"");
        html.ShouldContain("data-status=\"NoShow\"");
        html.ShouldContain("data-status=\"CancelledByClient\"");
        html.ShouldContain(">Completed<");
        html.ShouldContain(">No-show<");
        html.ShouldContain(">Cancelled by client<");
        html.ShouldContain($"{VisitSeed.ServiceName} · {VisitSeed.StaffName}");
        var lastVisitDay = DaysAgo(3).InZone(London).Date;
        html.ShouldContain("href=\"/admin/appointments?date=" + lastVisitDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "\"");
        html.ShouldContain("data-testid=\"visits-completed\">2<");
        html.ShouldContain("data-testid=\"no-shows\">1<");
        html.ShouldContain("data-testid=\"last-visit\">" + lastVisitDay.ToString("d", new CultureInfo("en-GB")) + "<");
        html.ShouldContain("£20.00");
    }

    [Fact]
    public async Task A_card_without_visits_shows_the_empty_states_in_the_salon_language()
    {
        await VisitSeed.SalonAsync(factory, _other.Id);
        var polishClient = await VisitSeed.ClientAsync(factory, _other.Id, "Anna", "Nowak");
        using var client = await factory.SignedInClientAsync("salon-pl.localhost", _other.Id, Role.Owner);

        var html = await GetAsync(client, $"/admin/clients/{polishClient}");

        html.ShouldContain("Historia wizyt");
        html.ShouldContain("Nadchodzące");
        html.ShouldContain("Poprzednie");
        html.ShouldContain("Brak nadchodzących wizyt.");
        html.ShouldContain("Brak wcześniejszych wizyt.");
        html.ShouldContain("data-testid=\"visits-completed\">0<");
        html.ShouldContain("data-testid=\"last-visit\">—<");
    }

    private async Task SeedVisitsAsync()
    {
        await VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, DaysAgo(30), AppointmentStatus.Completed);
        await VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, DaysAgo(3), AppointmentStatus.Completed);
        await VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, DaysAgo(2), AppointmentStatus.NoShow);
        await VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, DaysAgo(1), AppointmentStatus.CancelledByClient);
        await VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, InDays(1), AppointmentStatus.Booked);
        await VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, InDays(2), AppointmentStatus.Confirmed);
    }

    private Instant DaysAgo(int days) => _now.Minus(Duration.FromDays(days));

    private Instant InDays(int days) => _now.Plus(Duration.FromDays(days));

    private async Task<ClientHistory> HistoryAsync(Guid tenantId, Guid clientId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return (await scope.ServiceProvider.GetRequiredService<IClientService>().GetHistoryAsync(clientId)).ShouldNotBeNull();
    }

    private static int Count(string html, string needle)
    {
        var count = 0;
        for (var index = html.IndexOf(needle, StringComparison.Ordinal); index >= 0; index = html.IndexOf(needle, index + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}