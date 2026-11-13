using System.Net;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NodaTime;

using Salon.Core.Appointments;
using Salon.Core.Domain;
using Salon.Core.Identity;
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
/// Deposits on the client account (ADR 0023): the desk records the deposit a booking asks for and, when needed, its
/// return, both through the manual plugin; a deposit the salon holds counts toward the fee of a missed visit; the rules
/// are editable where they belong (policy page, service form, client form).
/// </summary>
[Collection(SharedDatabase.Name)]
public class DepositTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";

    private static readonly Instant Now = SystemClock.Instance.GetCurrentInstant();

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _staffId;
    private Guid _serviceId;
    private Guid _clientId;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        (_staffId, _serviceId) = await VisitSeed.SalonAsync(factory, _studio.Id);
        _clientId = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_deposit_asked_for_is_recorded_through_the_manual_plugin_once()
    {
        var visit = await VisitAsync(Now + Duration.FromDays(2), AppointmentStatus.Booked, deposit: 5m);

        var before = (await AccountAsync(_studio.Id, s => s.GetDepositAsync(visit))).ShouldNotBeNull();
        before.Due.ShouldBe(new Money(5m, "GBP"));
        before.Deposit.ShouldBeNull();

        var line = await AccountAsync(_studio.Id, s => s.RecordDepositAsync(visit, null, PaymentMethod.BankTransfer, null));

        line.Amount.ShouldBe(new Money(5m, "GBP"));
        line.Method.ShouldBe(PaymentMethod.BankTransfer);
        line.IsHeld.ShouldBeTrue();
        line.ProviderReference.ShouldNotBeNull().ShouldStartWith("manual:deposit:");
        var row = (await EntriesAsync(_studio.Id)).ShouldHaveSingleItem();
        row.Kind.ShouldBe(ClientAccountEntryKind.DepositPaid);
        row.ProviderSystemName.ShouldBe("Payments.Manual");
        row.ClientId.ShouldBe(_clientId);

        (await RefusalAsync(() => AccountAsync(_studio.Id, s => s.RecordDepositAsync(visit, null, PaymentMethod.Cash, null)))).ShouldBe(ClientAccountRefusal.DepositAlreadyRecorded);
        (await EntriesAsync(_studio.Id)).ShouldHaveSingleItem();
        var account = await AccountAsync(_studio.Id, s => s.GetAccountAsync(_clientId));
        account.Owed.ShouldBeNull("a deposit is not a debt");
        account.Deposits.ShouldHaveSingleItem().EntryId.ShouldBe(line.EntryId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(20.01)]
    [InlineData(1.005)]
    public async Task The_desk_may_take_another_amount_but_never_nothing_or_more_than_the_price(decimal amount)
    {
        var visit = await VisitAsync(Now + Duration.FromDays(2), AppointmentStatus.Booked, deposit: 5m);

        (await RefusalAsync(() => AccountAsync(_studio.Id, s => s.RecordDepositAsync(visit, amount, PaymentMethod.Cash, null)))).ShouldBe(ClientAccountRefusal.AmountOutOfRange);
        (await EntriesAsync(_studio.Id)).ShouldBeEmpty();

        (await AccountAsync(_studio.Id, s => s.RecordDepositAsync(visit, 12m, PaymentMethod.Cash, null))).Amount.ShouldBe(new Money(12m, "GBP"));
    }

    [Fact]
    public async Task A_visit_that_asks_for_none_can_still_take_one_when_the_amount_is_given_and_a_finished_visit_cannot()
    {
        var plain = await VisitAsync(Now + Duration.FromDays(2), AppointmentStatus.Booked);
        var cancelled = await VisitAsync(Now + Duration.FromDays(3), AppointmentStatus.CancelledByClient, deposit: 5m);

        (await RefusalAsync(() => AccountAsync(_studio.Id, s => s.RecordDepositAsync(plain, null, PaymentMethod.Cash, null)))).ShouldBe(ClientAccountRefusal.AmountOutOfRange, "nothing asked for and nothing typed");
        (await AccountAsync(_studio.Id, s => s.RecordDepositAsync(plain, 10m, PaymentMethod.Cash, null))).Amount.Amount.ShouldBe(10m);
        (await RefusalAsync(() => AccountAsync(_studio.Id, s => s.RecordDepositAsync(cancelled, null, PaymentMethod.Cash, null)))).ShouldBe(ClientAccountRefusal.VisitNotActive);
    }

    [Fact]
    public async Task A_deposit_goes_back_once_in_full_with_its_own_reference()
    {
        var visit = await VisitAsync(Now + Duration.FromDays(2), AppointmentStatus.Booked, deposit: 5m);
        (await RefusalAsync(() => AccountAsync(_studio.Id, s => s.ReturnDepositAsync(visit, PaymentMethod.Cash, null)))).ShouldBe(ClientAccountRefusal.NoDepositHeld);
        var deposit = await AccountAsync(_studio.Id, s => s.RecordDepositAsync(visit, null, PaymentMethod.Cash, null));

        var returned = await AccountAsync(_studio.Id, s => s.ReturnDepositAsync(visit, PaymentMethod.BankTransfer, null));

        returned.EntryId.ShouldBe(deposit.EntryId);
        returned.IsHeld.ShouldBeFalse();
        returned.Return.ShouldNotBeNull().Method.ShouldBe(PaymentMethod.BankTransfer);
        returned.Return.ProviderReference.ShouldNotBeNull().ShouldStartWith("manual:deposit-return:");
        var rows = await EntriesAsync(_studio.Id);
        rows.Select(r => r.Kind).ShouldBe([ClientAccountEntryKind.DepositPaid, ClientAccountEntryKind.DepositReturned]);
        rows[1].RelatedEntryId.ShouldBe(deposit.EntryId);
        rows[1].Amount.ShouldBe(5m);

        (await RefusalAsync(() => AccountAsync(_studio.Id, s => s.ReturnDepositAsync(visit, PaymentMethod.Cash, null)))).ShouldBe(ClientAccountRefusal.NoDepositHeld);
        (await RefusalAsync(() => AccountAsync(_studio.Id, s => s.RecordDepositAsync(visit, null, PaymentMethod.Cash, null)))).ShouldBe(ClientAccountRefusal.DepositAlreadyRecorded, "one deposit per visit, returned or not");
        (await EntriesAsync(_studio.Id)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_deposit_the_salon_holds_counts_toward_the_fee_of_a_missed_visit()
    {
        await SavePolicyAsync(_studio.Id, hours: 24, latePercent: 50m, noShowPercent: 100m);
        var visit = await VisitAsync(Now + Duration.FromHours(1), AppointmentStatus.Booked, deposit: 5m);
        await AccountAsync(_studio.Id, s => s.RecordDepositAsync(visit, null, PaymentMethod.Cash, null));
        await SetStatusAsync(visit, AppointmentStatus.NoShow);

        var fee = (await AccountAsync(_studio.Id, s => s.GetFeeAsync(visit))).ShouldNotBeNull();
        fee.Proposal.ShouldNotBeNull().Amount.ShouldBe(new Money(20m, "GBP"), "the policy's fee in full");
        fee.DepositHeld.ShouldBe(new Money(5m, "GBP"));
        fee.Chargeable.ShouldBe(new Money(15m, "GBP"));
        fee.MaxFee.ShouldBe(15m);

        (await RefusalAsync(() => AccountAsync(_studio.Id, s => s.ChargeFeeAsync(visit, 16m, null)))).ShouldBe(ClientAccountRefusal.AmountOutOfRange, "deposit and fee together never exceed the price");
        var charged = await AccountAsync(_studio.Id, s => s.ChargeFeeAsync(visit, null, null));

        charged.Amount.ShouldBe(new Money(15m, "GBP"));
        (await AccountAsync(_studio.Id, s => s.GetAccountAsync(_clientId))).Owed.ShouldBe(new Money(15m, "GBP"));
    }

    [Fact]
    public async Task A_deposit_that_covers_the_fee_leaves_nothing_to_add_and_a_returned_one_counts_for_nothing()
    {
        await SavePolicyAsync(_studio.Id, hours: 24, latePercent: 50m, noShowPercent: 100m);
        var covered = await VisitAsync(Now + Duration.FromHours(3), AppointmentStatus.Booked, deposit: 10m);
        await AccountAsync(_studio.Id, s => s.RecordDepositAsync(covered, null, PaymentMethod.Cash, null));
        await SetStatusAsync(covered, AppointmentStatus.CancelledByClient, cancelledAt: Now);
        var returned = await VisitAsync(Now + Duration.FromHours(5), AppointmentStatus.Booked, deposit: 10m);
        await AccountAsync(_studio.Id, s => s.RecordDepositAsync(returned, null, PaymentMethod.Cash, null));
        await AccountAsync(_studio.Id, s => s.ReturnDepositAsync(returned, PaymentMethod.Cash, null));
        await SetStatusAsync(returned, AppointmentStatus.CancelledByClient, cancelledAt: Now);

        // Late cancellation: half of £20 is £10, exactly the deposit the salon keeps.
        var fee = (await AccountAsync(_studio.Id, s => s.GetFeeAsync(covered))).ShouldNotBeNull();
        fee.Proposal.ShouldNotBeNull();
        fee.Chargeable.ShouldBeNull();
        (await RefusalAsync(() => AccountAsync(_studio.Id, s => s.ChargeFeeAsync(covered, null, null)))).ShouldBe(ClientAccountRefusal.CoveredByDeposit);

        var other = (await AccountAsync(_studio.Id, s => s.GetFeeAsync(returned))).ShouldNotBeNull();
        other.DepositHeld.ShouldBeNull();
        other.Chargeable.ShouldBe(new Money(10m, "GBP"));
    }

    [Fact]
    public async Task Another_salon_sees_and_touches_no_deposit()
    {
        var visit = await VisitAsync(Now + Duration.FromDays(2), AppointmentStatus.Booked, deposit: 5m);
        await AccountAsync(_studio.Id, s => s.RecordDepositAsync(visit, null, PaymentMethod.Cash, null));

        (await AccountAsync(_polish.Id, s => s.GetDepositAsync(visit))).ShouldBeNull();
        (await RefusalAsync(() => AccountAsync(_polish.Id, s => s.RecordDepositAsync(visit, 5m, PaymentMethod.Cash, null)))).ShouldBe(ClientAccountRefusal.NotFound);
        (await RefusalAsync(() => AccountAsync(_polish.Id, s => s.ReturnDepositAsync(visit, PaymentMethod.Cash, null)))).ShouldBe(ClientAccountRefusal.NotFound);
        (await EntriesAsync(_polish.Id)).ShouldBeEmpty();
        (await EntriesAsync(_studio.Id)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_desk_records_and_returns_a_deposit_from_the_visit_card()
    {
        var visit = await VisitAsync(Now + Duration.FromDays(2), AppointmentStatus.Booked, deposit: 5m);
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        var card = await GetAsync(client, $"/admin/appointments/calendar/{visit}");
        card.ShouldContain("£5.00 due, not received");
        card.ShouldContain($"href=\"{DepositPath(visit)}\" data-testid=\"card-deposit-link\"");
        var page = await GetAsync(client, DepositPath(visit));
        page.ShouldContain("Olivia Brown");
        page.ShouldContain("name=\"amount\" type=\"text\" inputmode=\"decimal\" value=\"5.00\"");

        var recorded = await PostAsync(client, DepositPath(visit), new() { ["amount"] = "5.00", ["method"] = "BankTransfer" }, page);

        recorded.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        recorded.Headers.Location!.OriginalString.ShouldBe(DepositPath(visit));
        var held = await GetAsync(client, DepositPath(visit));
        held.ShouldContain("The deposit has been recorded.");
        held.ShouldContain("data-testid=\"deposit\" data-held=\"true\"");
        held.ShouldContain("Received: Bank transfer. Recorded by receptionist@studio-20.localhost");
        held.ShouldContain("Reference: manual:deposit:");
        (await GetAsync(client, $"/admin/appointments/calendar/{visit}")).ShouldContain("£5.00 received");
        var clientCard = await GetAsync(client, $"/admin/clients/{_clientId}");
        clientCard.ShouldContain("data-testid=\"deposit-row\" data-held=\"true\"");
        clientCard.ShouldContain("Nothing owed");

        // A stale form cannot record it twice.
        var again = await PostAsync(client, DepositPath(visit), new() { ["amount"] = "5.00", ["method"] = "Cash" }, page);
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await again.Content.ReadAsStringAsync()).ShouldContain("This visit already has a deposit.");

        var returned = await PostAsync(client, DepositPath(visit) + "/return", new() { ["method"] = "Cash" }, held);

        returned.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var after = await GetAsync(client, DepositPath(visit));
        after.ShouldContain("The return of the deposit has been recorded.");
        after.ShouldContain("data-testid=\"deposit\" data-held=\"false\"");
        after.ShouldContain("Returned: Cash. Recorded by receptionist@studio-20.localhost");
        after.ShouldNotContain("data-testid=\"return-deposit-form\"");
        (await EntriesAsync(_studio.Id)).Select(r => r.Kind).ShouldBe([ClientAccountEntryKind.DepositPaid, ClientAccountEntryKind.DepositReturned]);
    }

    [Fact]
    public async Task The_fee_page_takes_the_deposit_off_what_is_left_to_add()
    {
        await SavePolicyAsync(_studio.Id, hours: 24, latePercent: 50m, noShowPercent: 100m);
        var visit = await VisitAsync(Now + Duration.FromHours(1), AppointmentStatus.Booked, deposit: 5m);
        await AccountAsync(_studio.Id, s => s.RecordDepositAsync(visit, null, PaymentMethod.Cash, null));
        await SetStatusAsync(visit, AppointmentStatus.NoShow);
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var page = await GetAsync(client, $"/admin/payments/fees/{visit}");

        page.ShouldContain("Your policy sets a no-show fee of 100% of the price: £20.00.");
        page.ShouldContain("A deposit of £5.00 was taken for this visit and stays with the salon. It counts toward the fee.");
        page.ShouldContain("name=\"amount\" type=\"text\" inputmode=\"decimal\" value=\"15.00\"");
        var over = await PostAsync(client, $"/admin/payments/fees/{visit}", new() { ["amount"] = "20.00" }, page);
        over.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await over.Content.ReadAsStringAsync()).ShouldContain("The amount must be above zero and no more than the limit shown under the field.");
    }

    [Fact]
    public async Task Staff_another_salon_and_a_post_without_a_token_get_nowhere()
    {
        var visit = await VisitAsync(Now + Duration.FromDays(2), AppointmentStatus.Booked, deposit: 5m);
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var page = await GetAsync(owner, DepositPath(visit));

        using var staff = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Staff);
        var get = await staff.GetAsync(new Uri(DepositPath(visit), UriKind.Relative));
        get.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        var post = await PostAsync(staff, DepositPath(visit), new() { ["method"] = "Cash" }, page);
        post.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");

        using var other = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        (await other.GetAsync(new Uri(DepositPath(visit), UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var otherPage = await GetAsync(other, "/admin/settings/cancellation-policy");
        (await PostAsync(other, DepositPath(visit), new() { ["amount"] = "5", ["method"] = "Cash" }, otherPage)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(other, DepositPath(visit) + "/return", new() { ["method"] = "Cash" }, otherPage)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var bare = await owner.PostAsync(new Uri(DepositPath(visit), UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string> { ["method"] = "Cash" }));
        bare.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await EntriesAsync(_studio.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_rules_are_edited_on_the_policy_page_the_service_form_and_the_client_form()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        // The salon's default, next to the cancellation policy.
        var policy = await GetAsync(client, "/admin/settings/cancellation-policy");
        policy.ShouldContain("<input id=\"DepositPercent\" name=\"DepositPercent\" type=\"number\"");
        var saved = await PostAsync(client, "/admin/settings/cancellation-policy", new()
        {
            ["DepositPercent"] = "20",
            ["FreeChangeHours"] = "24",
            ["LateCancellationFeePercent"] = "50",
            ["NoShowFeePercent"] = "100",
        }, policy);
        saved.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var settings = scope.ServiceProvider.GetRequiredService<ISettingService>();
            (await settings.LoadAsync<BookingSettings>()).DefaultDepositPercent.ShouldBe(20m);
            (await settings.LoadAsync<BookingSettings>()).RequireSmsVerification.ShouldBeTrue("the other booking rules keep their values");
        }

        // The service's own rule.
        var serviceForm = await GetAsync(client, $"/admin/services/{_serviceId}");
        serviceForm.ShouldContain("data-testid=\"deposit-percent\"");
        var serviceSaved = await PostAsync(client, $"/admin/services/{_serviceId}", new()
        {
            ["Name"] = VisitSeed.ServiceName,
            ["DurationMinutes"] = "30",
            ["Price"] = "20",
            ["IsActive"] = "true",
            ["DepositPercent"] = "50",
        }, serviceForm);
        serviceSaved.StatusCode.ShouldBe(HttpStatusCode.Redirect, WebUtility.HtmlDecode(await serviceSaved.Content.ReadAsStringAsync()));
        var tooMuch = await PostAsync(client, $"/admin/services/{_serviceId}", new()
        {
            ["Name"] = VisitSeed.ServiceName,
            ["DurationMinutes"] = "30",
            ["Price"] = "20",
            ["IsActive"] = "true",
            ["DepositPercent"] = "101",
        }, serviceForm);
        tooMuch.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await tooMuch.Content.ReadAsStringAsync()).ShouldContain("The deposit must be between 0 and 100 percent.");

        // The client's own rule.
        var clientForm = await GetAsync(client, $"/admin/clients/{_clientId}/edit");
        clientForm.ShouldContain("data-testid=\"client-deposit-percent\"");
        var clientSaved = await PostAsync(client, $"/admin/clients/{_clientId}/edit", new()
        {
            ["FirstName"] = "Olivia",
            ["LastName"] = "Brown",
            ["Phone"] = "+447700900123",
            ["DepositPercent"] = "0",
        }, clientForm);
        clientSaved.StatusCode.ShouldBe(HttpStatusCode.Redirect, WebUtility.HtmlDecode(await clientSaved.Content.ReadAsStringAsync()));

        await using var check = factory.CreateTenantScope(_studio.Id);
        var db = check.ServiceProvider.GetRequiredService<SalonDbContext>();
        (await db.Services.AsNoTracking().SingleAsync(s => s.Id == _serviceId)).DepositPercent.ShouldBe(50m);
        (await check.ServiceProvider.GetRequiredService<IClientService>().GetCardAsync(_clientId)).ShouldNotBeNull().DepositPercent.ShouldBe(0m);
        (await GetAsync(client, $"/admin/clients/{_clientId}/edit")).ShouldContain("data-testid=\"client-deposit-percent\"");
    }

    private static string DepositPath(Guid appointmentId) => $"/admin/payments/deposits/{appointmentId}";

    private async Task<Guid> VisitAsync(Instant start, AppointmentStatus status, decimal deposit = 0m)
    {
        var id = await VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, start, status);
        if (deposit > 0m)
        {
            await using var scope = factory.CreateTenantScope(_studio.Id);
            await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Appointments
                .Where(a => a.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.DepositAmount, deposit));
        }

        return id;
    }

    private async Task SetStatusAsync(Guid appointmentId, AppointmentStatus status, Instant? cancelledAt = null)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Appointments
            .Where(a => a.Id == appointmentId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, status).SetProperty(a => a.CancelledAt, cancelledAt));
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

    private async Task<T> AccountAsync<T>(Guid tenantId, Func<IClientAccountService, Task<T>> act)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await act(scope.ServiceProvider.GetRequiredService<IClientAccountService>());
    }

    private static async Task<ClientAccountRefusal> RefusalAsync(Func<Task> act)
        => (await Should.ThrowAsync<ClientAccountException>(act)).Refusal;

    private async Task<List<ClientAccountEntry>> EntriesAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().ClientAccountEntries.AsNoTracking().OrderBy(e => e.At).ThenBy(e => e.Id).ToListAsync();
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, Dictionary<string, string> fields, string pageWithToken)
    {
        fields["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(pageWithToken);
        return await client.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent(fields));
    }
}