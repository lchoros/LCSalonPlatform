using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Domain;
using Salon.Core.Payments;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Clients;
using Salon.Services.Payments;
using Salon.Services.Settings;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Payments;

/// <summary>
/// The client account (ADR 0022): the policy proposes a fee for a missed or late-cancelled visit, a person adds it, and
/// it is settled once, by a payment recorded through the manual plugin or by a waiver. Per salon, never edited.
/// </summary>
[Collection(SharedDatabase.Name)]
public class ClientAccountServiceTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private static readonly Instant Now = SystemClock.Instance.GetCurrentInstant();

    private Tenant _studio = null!;
    private Tenant _other = null!;
    private Guid _staffId;
    private Guid _serviceId;
    private Guid _clientId;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("fees-a", name: "Studio 20");
        _other = await factory.CreateTenantAsync("fees-b", name: "Other Salon");
        (_staffId, _serviceId) = await VisitSeed.SalonAsync(factory, _studio.Id);
        _clientId = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123");
        await SavePolicyAsync(_studio.Id, hours: 24, latePercent: 50m, noShowPercent: 100m);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_missed_visit_is_proposed_the_no_show_fee_and_adding_it_puts_it_on_the_account()
    {
        var visit = await VisitAsync(Now - Duration.FromHours(1), AppointmentStatus.NoShow);
        var user = await factory.CreateUserAsync(_studio.Id, "anna@fees-a.test", Salon.Core.Identity.Role.Receptionist, displayName: "Anna Desk");

        var before = (await FeeAsync(_studio.Id, visit)).ShouldNotBeNull();
        before.ClientName.ShouldBe("Olivia Brown");
        before.ServiceName.ShouldBe(VisitSeed.ServiceName);
        before.Price.ShouldBe(new Money(20m, "GBP"));
        before.Fee.ShouldBeNull();
        var proposal = before.Proposal.ShouldNotBeNull();
        proposal.Kind.ShouldBe(ClientAccountEntryKind.NoShowFee);
        proposal.Amount.ShouldBe(new Money(20m, "GBP"));
        (await AccountAsync(_studio.Id, _clientId)).ShouldBe(ClientAccount.Empty, "a proposal is not a debt");

        var fee = await ChargeAsync(_studio.Id, visit, null, user.Id);

        fee.Kind.ShouldBe(ClientAccountEntryKind.NoShowFee);
        fee.Amount.ShouldBe(new Money(20m, "GBP"));
        fee.Status.ShouldBe(FeeStatus.Owed);
        fee.ByName.ShouldBe("Anna Desk");
        fee.ServiceName.ShouldBe(VisitSeed.ServiceName);
        var after = (await FeeAsync(_studio.Id, visit)).ShouldNotBeNull();
        after.Proposal.ShouldBeNull("the fee is on the account now");
        after.Fee.ShouldNotBeNull().EntryId.ShouldBe(fee.EntryId);
        var account = await AccountAsync(_studio.Id, _clientId);
        account.Owed.ShouldBe(new Money(20m, "GBP"));
        account.Fees.ShouldHaveSingleItem().EntryId.ShouldBe(fee.EntryId);
    }

    [Fact]
    public async Task A_late_cancellation_is_proposed_half_the_price_and_one_made_in_time_nothing()
    {
        var late = await VisitAsync(Now + Duration.FromHours(3), AppointmentStatus.CancelledByClient, cancelledAt: Now);
        var bySalon = await VisitAsync(Now + Duration.FromHours(5), AppointmentStatus.CancelledBySalon, cancelledAt: Now);
        var inTime = await VisitAsync(Now + Duration.FromHours(72), AppointmentStatus.CancelledByClient, cancelledAt: Now);

        var proposal = (await FeeAsync(_studio.Id, late)).ShouldNotBeNull().Proposal.ShouldNotBeNull();
        proposal.Kind.ShouldBe(ClientAccountEntryKind.LateCancellationFee);
        proposal.Amount.ShouldBe(new Money(10m, "GBP"));
        proposal.Percent.ShouldBe(50m);
        (await FeeAsync(_studio.Id, bySalon)).ShouldNotBeNull().Proposal.ShouldNotBeNull("a phone call is recorded as the salon's cancellation; the desk decides");
        (await FeeAsync(_studio.Id, inTime)).ShouldNotBeNull().Proposal.ShouldBeNull();

        (await RefusalAsync(() => ChargeAsync(_studio.Id, inTime, null))).ShouldBe(ClientAccountRefusal.NoFeeUnderPolicy);
        (await ChargeAsync(_studio.Id, late, null)).Amount.ShouldBe(new Money(10m, "GBP"));
    }

    [Theory]
    [InlineData(AppointmentStatus.Booked)]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.Arrived)]
    [InlineData(AppointmentStatus.Completed)]
    public async Task A_visit_that_is_on_or_took_place_cannot_be_charged_a_fee(AppointmentStatus status)
    {
        var visit = await VisitAsync(Now + Duration.FromHours(2), status);

        (await FeeAsync(_studio.Id, visit)).ShouldNotBeNull().Proposal.ShouldBeNull();
        (await RefusalAsync(() => ChargeAsync(_studio.Id, visit, 5m))).ShouldBe(ClientAccountRefusal.NoFeeUnderPolicy);
        (await EntriesAsync(_studio.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_policy_nothing_is_proposed_or_charged()
    {
        await SavePolicyAsync(_studio.Id, hours: 0, latePercent: 0m, noShowPercent: 0m);
        var visit = await VisitAsync(Now - Duration.FromHours(1), AppointmentStatus.NoShow);

        (await FeeAsync(_studio.Id, visit)).ShouldNotBeNull().Proposal.ShouldBeNull();
        (await RefusalAsync(() => ChargeAsync(_studio.Id, visit, null))).ShouldBe(ClientAccountRefusal.NoFeeUnderPolicy);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(20.01)]
    [InlineData(5.005)]
    public async Task The_fee_can_be_lowered_but_never_to_nothing_above_the_price_or_past_the_penny(decimal amount)
    {
        var visit = await VisitAsync(Now - Duration.FromHours(1), AppointmentStatus.NoShow);

        (await RefusalAsync(() => ChargeAsync(_studio.Id, visit, amount))).ShouldBe(ClientAccountRefusal.AmountOutOfRange);
        (await EntriesAsync(_studio.Id)).ShouldBeEmpty();

        (await ChargeAsync(_studio.Id, visit, 7.50m)).Amount.ShouldBe(new Money(7.50m, "GBP"));
    }

    [Fact]
    public async Task A_visit_carries_one_fee_only()
    {
        var visit = await VisitAsync(Now - Duration.FromHours(1), AppointmentStatus.NoShow);
        await ChargeAsync(_studio.Id, visit, null);

        (await RefusalAsync(() => ChargeAsync(_studio.Id, visit, null))).ShouldBe(ClientAccountRefusal.AlreadyCharged);

        (await EntriesAsync(_studio.Id)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_database_refuses_a_second_fee_or_a_second_settlement_even_past_the_service()
    {
        var visit = await VisitAsync(Now - Duration.FromHours(1), AppointmentStatus.NoShow);
        var fee = await ChargeAsync(_studio.Id, visit, null);
        await WaiveAsync(_studio.Id, visit, "First visit, told at the desk");

        // Two tills, same moment: whatever the code checked first, the unique indexes have the last word.
        await Should.ThrowAsync<DbUpdateException>(() => InsertRawAsync(_studio.Id, new ClientAccountEntry
        {
            ClientId = _clientId,
            AppointmentId = visit,
            Kind = ClientAccountEntryKind.LateCancellationFee,
            Amount = 5m,
            Currency = "GBP",
            At = Now,
        }));
        await Should.ThrowAsync<DbUpdateException>(() => InsertRawAsync(_studio.Id, new ClientAccountEntry
        {
            ClientId = _clientId,
            AppointmentId = visit,
            Kind = ClientAccountEntryKind.FeePayment,
            Amount = 20m,
            Currency = "GBP",
            RelatedEntryId = fee.EntryId,
            At = Now,
        }));
        await Should.ThrowAsync<DbUpdateException>(() => InsertRawAsync(_studio.Id, new ClientAccountEntry
        {
            ClientId = _clientId,
            Kind = ClientAccountEntryKind.FeeWaived,
            Amount = 0m,
            Currency = "GBP",
            At = Now,
        }));
        (await EntriesAsync(_studio.Id)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_payment_is_recorded_through_the_manual_plugin_and_settles_the_fee_once()
    {
        var visit = await VisitAsync(Now - Duration.FromHours(1), AppointmentStatus.NoShow);
        await ChargeAsync(_studio.Id, visit, 15m);

        var paid = await PayAsync(_studio.Id, visit, PaymentMethod.CardAtSalonTerminal);

        paid.Status.ShouldBe(FeeStatus.Paid);
        var settlement = paid.Settlement.ShouldNotBeNull();
        settlement.Kind.ShouldBe(ClientAccountEntryKind.FeePayment);
        settlement.Method.ShouldBe(PaymentMethod.CardAtSalonTerminal);
        settlement.ProviderReference.ShouldNotBeNull().ShouldStartWith("manual:no-show-fee:");
        (await AccountAsync(_studio.Id, _clientId)).Owed.ShouldBeNull("paid in full");
        var rows = await EntriesAsync(_studio.Id);
        rows.Count.ShouldBe(2);
        var payment = rows.Single(r => r.Kind == ClientAccountEntryKind.FeePayment);
        payment.Amount.ShouldBe(15m);
        payment.RelatedEntryId.ShouldBe(paid.EntryId);
        payment.ProviderSystemName.ShouldBe("Payments.Manual");
        payment.AppointmentId.ShouldBe(visit);

        (await RefusalAsync(() => PayAsync(_studio.Id, visit, PaymentMethod.Cash))).ShouldBe(ClientAccountRefusal.AlreadySettled);
        (await RefusalAsync(() => WaiveAsync(_studio.Id, visit, "Changed my mind"))).ShouldBe(ClientAccountRefusal.AlreadySettled);
        (await EntriesAsync(_studio.Id)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_waiver_needs_a_reason_and_keeps_it_with_the_fee()
    {
        var visit = await VisitAsync(Now - Duration.FromHours(1), AppointmentStatus.NoShow);
        await ChargeAsync(_studio.Id, visit, null);

        (await RefusalAsync(() => WaiveAsync(_studio.Id, visit, "   "))).ShouldBe(ClientAccountRefusal.ReasonRequired);
        var waived = await WaiveAsync(_studio.Id, visit, "  Her train was cancelled  ");

        waived.Status.ShouldBe(FeeStatus.Waived);
        waived.Settlement.ShouldNotBeNull().Note.ShouldBe("Her train was cancelled");
        waived.Settlement.Method.ShouldBeNull();
        var account = await AccountAsync(_studio.Id, _clientId);
        account.Owed.ShouldBeNull();
        account.Fees.ShouldHaveSingleItem().Status.ShouldBe(FeeStatus.Waived);
        (await RefusalAsync(() => PayAsync(_studio.Id, visit, PaymentMethod.Cash))).ShouldBe(ClientAccountRefusal.AlreadySettled);
    }

    [Fact]
    public async Task Settling_needs_a_fee_and_an_unknown_visit_is_not_found()
    {
        var visit = await VisitAsync(Now - Duration.FromHours(1), AppointmentStatus.NoShow);

        (await RefusalAsync(() => PayAsync(_studio.Id, visit, PaymentMethod.Cash))).ShouldBe(ClientAccountRefusal.NoFee);
        (await RefusalAsync(() => WaiveAsync(_studio.Id, visit, "No fee yet"))).ShouldBe(ClientAccountRefusal.NoFee);
        (await RefusalAsync(() => ChargeAsync(_studio.Id, Guid.CreateVersion7(), null))).ShouldBe(ClientAccountRefusal.NotFound);
        (await FeeAsync(_studio.Id, Guid.CreateVersion7())).ShouldBeNull();
    }

    [Fact]
    public async Task What_a_client_owes_is_the_sum_of_their_open_fees_newest_first()
    {
        var first = await VisitAsync(Now - Duration.FromDays(9), AppointmentStatus.NoShow);
        var second = await VisitAsync(Now - Duration.FromDays(2), AppointmentStatus.NoShow);
        var third = await VisitAsync(Now - Duration.FromDays(1), AppointmentStatus.NoShow);
        await ChargeAsync(_studio.Id, first, null);
        await ChargeAsync(_studio.Id, second, 12.50m);
        await ChargeAsync(_studio.Id, third, 5m);
        await PayAsync(_studio.Id, first, PaymentMethod.Cash);

        var account = await AccountAsync(_studio.Id, _clientId);

        account.Owed.ShouldBe(new Money(17.50m, "GBP"));
        account.Fees.Select(f => (f.AppointmentId, f.Status)).ShouldBe([(third, FeeStatus.Owed), (second, FeeStatus.Owed), (first, FeeStatus.Paid)]);
    }

    [Fact]
    public async Task Another_salon_sees_and_touches_nothing_of_the_account()
    {
        var visit = await VisitAsync(Now - Duration.FromHours(1), AppointmentStatus.NoShow);
        await ChargeAsync(_studio.Id, visit, null);
        await SavePolicyAsync(_other.Id, hours: 24, latePercent: 50m, noShowPercent: 100m);

        (await FeeAsync(_other.Id, visit)).ShouldBeNull();
        (await AccountAsync(_other.Id, _clientId)).ShouldBe(ClientAccount.Empty);
        (await RefusalAsync(() => PayAsync(_other.Id, visit, PaymentMethod.Cash))).ShouldBe(ClientAccountRefusal.NotFound);
        (await RefusalAsync(() => WaiveAsync(_other.Id, visit, "Not mine"))).ShouldBe(ClientAccountRefusal.NotFound);
        (await EntriesAsync(_other.Id)).ShouldBeEmpty();
        (await EntriesAsync(_studio.Id)).ShouldHaveSingleItem().TenantId.ShouldBe(_studio.Id);
    }

    [Fact]
    public async Task Merging_two_cards_moves_what_the_duplicate_owes_to_the_kept_card()
    {
        var duplicate = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Browne");
        var visit = await VisitSeed.AppointmentAsync(factory, _studio.Id, duplicate, _staffId, _serviceId, Now - Duration.FromHours(1), AppointmentStatus.NoShow);
        await ChargeAsync(_studio.Id, visit, null);

        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            await scope.ServiceProvider.GetRequiredService<IClientService>().MergeAsync(_clientId, duplicate, null);
        }

        (await AccountAsync(_studio.Id, duplicate)).ShouldBe(ClientAccount.Empty);
        var kept = await AccountAsync(_studio.Id, _clientId);
        kept.Owed.ShouldBe(new Money(20m, "GBP"));
        kept.Fees.ShouldHaveSingleItem().AppointmentId.ShouldBe(visit);
    }

    private async Task<Guid> VisitAsync(Instant start, AppointmentStatus status, Instant? cancelledAt = null)
    {
        var id = await VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, start, status);
        if (cancelledAt is { } at)
        {
            await using var scope = factory.CreateTenantScope(_studio.Id);
            await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Appointments
                .Where(a => a.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.CancelledAt, at));
        }

        return id;
    }

    private async Task SavePolicyAsync(Guid tenantId, int hours, decimal latePercent, decimal noShowPercent)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        await scope.ServiceProvider.GetRequiredService<ISettingService>().SaveAsync(new CancellationPolicySettings
        {
            FreeChangeHours = hours,
            LateCancellationFeePercent = latePercent,
            NoShowFeePercent = noShowPercent,
        });
    }

    private async Task<T> AccountServiceAsync<T>(Guid tenantId, Func<IClientAccountService, Task<T>> act)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await act(scope.ServiceProvider.GetRequiredService<IClientAccountService>());
    }

    private Task<AppointmentFee?> FeeAsync(Guid tenantId, Guid appointmentId) => AccountServiceAsync(tenantId, s => s.GetFeeAsync(appointmentId));

    private Task<ClientAccount> AccountAsync(Guid tenantId, Guid clientId) => AccountServiceAsync(tenantId, s => s.GetAccountAsync(clientId));

    private Task<FeeLine> ChargeAsync(Guid tenantId, Guid appointmentId, decimal? amount, Guid? byUserId = null)
        => AccountServiceAsync(tenantId, s => s.ChargeFeeAsync(appointmentId, amount, byUserId));

    private Task<FeeLine> PayAsync(Guid tenantId, Guid appointmentId, PaymentMethod method)
        => AccountServiceAsync(tenantId, s => s.RecordFeePaymentAsync(appointmentId, method, null));

    private Task<FeeLine> WaiveAsync(Guid tenantId, Guid appointmentId, string reason)
        => AccountServiceAsync(tenantId, s => s.WaiveFeeAsync(appointmentId, reason, null));

    private static async Task<ClientAccountRefusal> RefusalAsync(Func<Task> act)
        => (await Should.ThrowAsync<ClientAccountException>(act)).Refusal;

    private async Task<List<ClientAccountEntry>> EntriesAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().ClientAccountEntries.AsNoTracking().OrderBy(e => e.At).ToListAsync();
    }

    private async Task InsertRawAsync(Guid tenantId, ClientAccountEntry entry)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        entry.TenantId = tenantId;
        db.ClientAccountEntries.Add(entry);
        await db.SaveChangesAsync();
    }
}