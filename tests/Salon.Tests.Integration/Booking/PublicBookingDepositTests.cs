using System.Net;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NodaTime;

using Salon.Data;
using Salon.Services.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Booking;

/// <summary>
/// The deposit a booking asks for (ADR 0023): said before the client books, snapshotted on the appointment like the
/// price, and decided by the most specific rule: the client's, else the service's, else the salon's. SMS verification
/// is off here so that one client can book three times within the hour.
/// </summary>
public partial class PublicBookingTests
{
    [Fact]
    public async Task Without_a_deposit_rule_nothing_is_asked_for()
    {
        await SaveSettingsAsync(_studio.Id, s => s.RequireSmsVerification = false);
        using var client = factory.ClientFor(HostA);

        (await GetAsync(client, DetailsPath(At(10, 0), _annaId))).ShouldNotContain("data-testid=\"deposit-terms\"");
        var (deposit, done) = await BookAndReadDepositAsync(client, At(10, 0));

        deposit.ShouldBe(0m);
        done.ShouldNotContain("data-testid=\"done-deposit\"");
    }

    [Fact]
    public async Task The_salon_rule_asks_every_booking_for_a_deposit_and_says_so_first()
    {
        await SaveSettingsAsync(_studio.Id, s =>
        {
            s.RequireSmsVerification = false;
            s.DefaultDepositPercent = 25m;
        });
        using var client = factory.ClientFor(HostA);

        var details = await GetAsync(client, DetailsPath(At(10, 0), _annaId));
        var (deposit, done) = await BookAndReadDepositAsync(client, At(10, 0));

        details.ShouldContain("A deposit of £5.00 secures this visit. The salon will tell you how to pay it.");
        deposit.ShouldBe(5m, "a quarter of the £20 cut");
        done.ShouldContain("data-testid=\"done-deposit\"");
        done.ShouldContain("£5.00");

        // The snapshot stays when the rule changes later, like the price.
        await SaveSettingsAsync(_studio.Id, s => s.DefaultDepositPercent = 80m);
        (await AppointmentsAsync(_studio.Id)).ShouldHaveSingleItem().DepositAmount.ShouldBe(5m);
    }

    [Fact]
    public async Task The_service_rule_beats_the_salon_and_the_client_rule_beats_both()
    {
        await SaveSettingsAsync(_studio.Id, s =>
        {
            s.RequireSmsVerification = false;
            s.DefaultDepositPercent = 25m;
        });
        using var client = factory.ClientFor(HostA);

        // The service asks for half.
        await SetServiceDepositAsync(50m);
        (await GetAsync(client, DetailsPath(At(10, 0), _annaId))).ShouldContain("A deposit of £10.00 secures this visit.");
        (await BookAndReadDepositAsync(client, At(10, 0))).Deposit.ShouldBe(10m);

        // The client is trusted: never a deposit, whatever the service says.
        var clientId = (await ClientsAsync(_studio.Id)).ShouldHaveSingleItem().Id;
        await SetClientDepositAsync(clientId, 0m);
        (await BookAndReadDepositAsync(client, At(12, 0))).Deposit.ShouldBe(0m);

        // The client missed visits before: always in full.
        await SetClientDepositAsync(clientId, 100m);
        (await BookAndReadDepositAsync(client, At(14, 0))).Deposit.ShouldBe(20m);

        // Back to the usual rules; a service that opts out asks for nothing although the salon does.
        await SetClientDepositAsync(clientId, null);
        await SetServiceDepositAsync(0m);
        (await GetAsync(client, DetailsPath(At(15, 0), _annaId))).ShouldNotContain("data-testid=\"deposit-terms\"");
        (await BookAndReadDepositAsync(client, At(15, 0))).Deposit.ShouldBe(0m);
    }

    private async Task<(decimal Deposit, string DonePage)> BookAndReadDepositAsync(HttpClient client, Instant start)
    {
        var response = await PostDetailsAsync(client, Fields(start, _annaId));
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
        var donePath = response.Headers.Location.ShouldNotBeNull().OriginalString;
        donePath.ShouldStartWith("/book/done/");
        var appointment = (await AppointmentsAsync(_studio.Id)).Single(a => a.Start == start);
        return (appointment.DepositAmount, await GetAsync(client, donePath));
    }

    private async Task SetServiceDepositAsync(decimal? percent)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Services
            .Where(s => s.Id == _serviceId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DepositPercent, percent));
    }

    private async Task SetClientDepositAsync(Guid clientId, decimal? percent)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await scope.ServiceProvider.GetRequiredService<IClientService>().SetDepositPercentAsync(clientId, percent);
    }
}