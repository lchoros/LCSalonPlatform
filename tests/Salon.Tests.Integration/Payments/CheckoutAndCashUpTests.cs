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
using Salon.Services.Payments;
using Salon.Services.Settings;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Payments;

/// <summary>
/// The checkout and the till (ADR 0024): a visit the client turned up for is paid once, the deposit comes off the bill,
/// open fees ride on it, and the day adds up per method to exactly what was recorded, with the cash counted once at
/// the end. Nothing is charged: every payment is a record through the manual plugin.
/// </summary>
[Collection(SharedDatabase.Name)]
public class CheckoutAndCashUpTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string TillPath = "/admin/payments/cash-up";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];

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
    public async Task Paying_an_arrived_visit_records_it_through_the_manual_plugin_and_completes_the_visit()
    {
        var visit = await VisitAsync(Now - Duration.FromMinutes(30), AppointmentStatus.Arrived);
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        (await GetAsync(client, "/admin/appointments/calendar/" + visit)).ShouldContain($"href=\"{CheckoutPath(visit)}\" data-testid=\"card-checkout-link\"");
        var bill = await GetAsync(client, CheckoutPath(visit));
        bill.ShouldContain("Olivia Brown");
        bill.ShouldContain("data-testid=\"bill-outstanding\">£20.00<");
        bill.ShouldContain("data-testid=\"pay-bill-form\"");

        var paid = await PostAsync(client, CheckoutPath(visit), [new("method", "CardAtSalonTerminal")], bill);

        paid.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        paid.Headers.Location!.OriginalString.ShouldBe(CheckoutPath(visit));
        var after = await GetAsync(client, CheckoutPath(visit));
        after.ShouldContain("The payment has been recorded.");
        after.ShouldContain("data-testid=\"bill\" data-status=\"Completed\"");
        after.ShouldContain("Paid: Card at the salon's terminal");
        after.ShouldContain("data-testid=\"bill-outstanding\">£0.00<");
        after.ShouldContain("Recorded by receptionist@studio-20.localhost");
        after.ShouldContain("Reference: manual:payment:");
        after.ShouldContain("This bill is paid.");
        after.ShouldNotContain("data-testid=\"pay-bill-form\"");
        var row = (await EntriesAsync(_studio.Id)).ShouldHaveSingleItem();
        row.Kind.ShouldBe(ClientAccountEntryKind.VisitPayment);
        row.Amount.ShouldBe(20m);
        row.Method.ShouldBe(PaymentMethod.CardAtSalonTerminal);
        row.ProviderSystemName.ShouldBe("Payments.Manual");
        row.ByUserId.ShouldNotBeNull();
        (await StatusAsync(visit)).ShouldBe(AppointmentStatus.Completed);

        // A stale form cannot take the money twice.
        var again = await PostAsync(client, CheckoutPath(visit), [new("method", "Cash")], bill);
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await again.Content.ReadAsStringAsync()).ShouldContain("There is nothing left to pay on this bill.");
        (await EntriesAsync(_studio.Id)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_deposit_the_salon_holds_comes_off_the_bill_and_one_that_covers_it_leaves_nothing_to_pay()
    {
        var visit = await VisitAsync(Now - Duration.FromMinutes(30), AppointmentStatus.Booked);
        await AccountAsync(_studio.Id, s => s.RecordDepositAsync(visit, 5m, PaymentMethod.BankTransfer, null));
        var covered = await VisitAsync(Now - Duration.FromHours(3), AppointmentStatus.Booked);
        await AccountAsync(_studio.Id, s => s.RecordDepositAsync(covered, 20m, PaymentMethod.Cash, null));
        await SetStatusAsync(visit, AppointmentStatus.Arrived);
        await SetStatusAsync(covered, AppointmentStatus.Completed);

        var bill = (await AccountAsync(_studio.Id, s => s.GetBillAsync(visit))).ShouldNotBeNull();
        bill.DepositHeld.ShouldBe(new Money(5m, "GBP"));
        bill.Outstanding.ShouldBe(new Money(15m, "GBP"));
        var paid = await AccountAsync(_studio.Id, s => s.PayBillAsync(visit, PaymentMethod.Cash, [], null));
        paid.Payment.ShouldNotBeNull().Amount.ShouldBe(new Money(15m, "GBP"));
        paid.Outstanding.Amount.ShouldBe(0m);

        (await AccountAsync(_studio.Id, s => s.GetBillAsync(covered))).ShouldNotBeNull().Outstanding.Amount.ShouldBe(0m);
        (await RefusalAsync(() => AccountAsync(_studio.Id, s => s.PayBillAsync(covered, PaymentMethod.Cash, [], null)))).ShouldBe(ClientAccountRefusal.NothingToPay);
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var page = await GetAsync(client, CheckoutPath(covered));
        page.ShouldContain("The deposit covers this visit. There is nothing to pay.");
        page.ShouldContain("data-testid=\"bill-deposit\">−£20.00<");
    }

    [Fact]
    public async Task Open_fees_from_earlier_visits_ride_on_the_bill_unless_they_are_unticked()
    {
        await SavePolicyAsync(_studio.Id, noShowPercent: 50m);
        var first = await VisitAsync(Now - Duration.FromDays(9), AppointmentStatus.NoShow);
        var second = await VisitAsync(Now - Duration.FromDays(5), AppointmentStatus.NoShow);
        await AccountAsync(_studio.Id, s => s.ChargeFeeAsync(first, null, null));
        await AccountAsync(_studio.Id, s => s.ChargeFeeAsync(second, 7.50m, null));
        var visit = await VisitAsync(Now - Duration.FromMinutes(30), AppointmentStatus.Arrived);
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Manager);

        var bill = await GetAsync(client, CheckoutPath(visit));
        bill.ShouldContain("Fees from earlier visits");
        bill.ShouldContain($"name=\"fees\" type=\"checkbox\" value=\"{first}\" checked");
        bill.ShouldContain($"name=\"fees\" type=\"checkbox\" value=\"{second}\" checked");
        bill.ShouldContain("£37.50", Case.Sensitive, "the visit, £10 and £7.50");

        // The client pays the visit and the older fee today; the other fee stays on the account.
        var paid = await PostAsync(client, CheckoutPath(visit), [new("method", "Cash"), new("fees", first.ToString())], bill);

        paid.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var rows = await EntriesAsync(_studio.Id);
        rows.Where(r => r.Method is not null).Select(r => (r.Kind, r.Amount, r.AppointmentId)).ShouldBe(
            [(ClientAccountEntryKind.VisitPayment, 20m, visit), (ClientAccountEntryKind.FeePayment, 10m, first)], ignoreOrder: true);
        var account = await AccountAsync(_studio.Id, s => s.GetAccountAsync(_clientId));
        account.Owed.ShouldBe(new Money(7.50m, "GBP"));

        // The visit is paid; the remaining fee can still be paid from the same page later.
        var later = await GetAsync(client, CheckoutPath(visit));
        later.ShouldContain("data-testid=\"pay-bill-form\"");
        later.ShouldContain($"value=\"{second}\" checked");
        (await PostAsync(client, CheckoutPath(visit), [new("method", "Voucher"), new("fees", second.ToString())], later)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await AccountAsync(_studio.Id, s => s.GetAccountAsync(_clientId))).Owed.ShouldBeNull();
        (await GetAsync(client, CheckoutPath(visit))).ShouldContain("This bill is paid.");
    }

    [Theory]
    [InlineData(AppointmentStatus.Booked, "The client has not arrived yet.")]
    [InlineData(AppointmentStatus.Confirmed, "The client has not arrived yet.")]
    [InlineData(AppointmentStatus.NoShow, "This visit did not take place")]
    [InlineData(AppointmentStatus.CancelledByClient, "This visit did not take place")]
    public async Task A_visit_the_client_did_not_turn_up_for_is_not_paid_at_the_checkout(AppointmentStatus status, string message)
    {
        var visit = await VisitAsync(Now + Duration.FromHours(2), status);
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var page = await GetAsync(client, CheckoutPath(visit));

        page.ShouldContain(message);
        page.ShouldNotContain("data-testid=\"pay-bill-form\"");
        (await RefusalAsync(() => AccountAsync(_studio.Id, s => s.PayBillAsync(visit, PaymentMethod.Cash, [], null)))).ShouldBe(ClientAccountRefusal.VisitNotPayable);
        var forced = await PostAsync(client, CheckoutPath(visit), [new("method", "Cash")], await GetAsync(client, TillPath));
        forced.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await forced.Content.ReadAsStringAsync()).ShouldContain("Only a visit the client arrived for can be paid.");
        (await EntriesAsync(_studio.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_day_list_offers_the_checkout_for_an_arrived_visit_only()
    {
        // Midday today in the salon's zone, so both visits are on today's list whenever the test runs.
        var noon = London.AtStartOfDay(Now.InZone(London).Date).ToInstant() + Duration.FromHours(12);
        var arrived = await VisitAsync(noon, AppointmentStatus.Arrived);
        var booked = await VisitAsync(noon + Duration.FromHours(1), AppointmentStatus.Booked);
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        var board = await GetAsync(client, "/admin/appointments");

        board.ShouldContain($"href=\"{CheckoutPath(arrived)}\" data-testid=\"checkout-link\"");
        board.ShouldNotContain($"href=\"{CheckoutPath(booked)}\"");
        board.ShouldContain("data-testid=\"nav-cash-up\"");
    }

    [Fact]
    public async Task The_till_adds_the_day_up_per_method_with_returns_taken_off()
    {
        await SavePolicyAsync(_studio.Id, noShowPercent: 100m);
        await RecordADayAsync();

        var day = await TillAsync(_studio.Id, s => s.GetDayAsync(null));

        day.Day.ShouldBe(day.Today);
        day.Totals.Select(t => (t.Method, t.Amount.Amount)).ShouldBe(
        [
            (PaymentMethod.Cash, 15m),
            (PaymentMethod.CardAtSalonTerminal, 20m),
            (PaymentMethod.BankTransfer, 10m),
        ]);
        day.Takings.ShouldBe(new Money(45m, "GBP"));
        day.ExpectedCash.ShouldBe(new Money(15m, "GBP"));
        day.Movements.Select(m => (m.Kind, m.Method, m.Amount.Amount)).ShouldBe(
        [
            (ClientAccountEntryKind.DepositPaid, PaymentMethod.Cash, 5m),
            (ClientAccountEntryKind.VisitPayment, PaymentMethod.CardAtSalonTerminal, 20m),
            (ClientAccountEntryKind.DepositPaid, PaymentMethod.BankTransfer, 10m),
            (ClientAccountEntryKind.DepositReturned, PaymentMethod.Cash, -10m),
            (ClientAccountEntryKind.FeePayment, PaymentMethod.Cash, 20m),
        ]);
        day.Movements.ShouldAllBe(m => m.ClientName == "Olivia Brown");
        day.Closed.ShouldBeNull();

        // Yesterday and the other salon saw none of it.
        (await TillAsync(_studio.Id, s => s.GetDayAsync(day.Today.PlusDays(-1)))).Movements.ShouldBeEmpty();
        var other = await TillAsync(_polish.Id, s => s.GetDayAsync(null));
        other.Movements.ShouldBeEmpty();
        other.ExpectedCash.ShouldBe(new Money(0m, "PLN"));
    }

    [Fact]
    public async Task A_day_is_closed_once_with_the_cash_counted_and_money_recorded_later_is_flagged()
    {
        await SavePolicyAsync(_studio.Id, noShowPercent: 100m);
        await RecordADayAsync();
        var today = (await TillAsync(_studio.Id, s => s.GetDayAsync(null))).Today;

        (await TillAsync(_studio.Id, s => s.CloseAsync(today.PlusDays(1), 0m, null, null))).Outcome.ShouldBe(CashUpOutcome.FutureDay);
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => TillAsync(_studio.Id, s => s.CloseAsync(today, -1m, null, null)));
        var closed = await TillAsync(_studio.Id, s => s.CloseAsync(today, 14.50m, "  Fifty pence short, change given wrong  ", null));

        closed.Outcome.ShouldBe(CashUpOutcome.Closed);
        var record = closed.Day.Closed.ShouldNotBeNull();
        record.ExpectedCash.ShouldBe(new Money(15m, "GBP"));
        record.CountedCash.ShouldBe(new Money(14.50m, "GBP"));
        record.Difference.ShouldBe(new Money(-0.50m, "GBP"));
        record.Note.ShouldBe("Fifty pence short, change given wrong");
        closed.Day.ChangedSinceClose.ShouldBeFalse();

        var second = await TillAsync(_studio.Id, s => s.CloseAsync(today, 15m, null, null));
        second.Outcome.ShouldBe(CashUpOutcome.AlreadyClosed);
        second.Day.Closed.ShouldNotBeNull().CountedCash.Amount.ShouldBe(14.50m, "the first count stands");
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            (await scope.ServiceProvider.GetRequiredService<SalonDbContext>().CashUps.CountAsync()).ShouldBe(1);
        }

        // A late payment after the close: the count is kept, the page is told the drawer changed.
        var late = await VisitAsync(Now - Duration.FromMinutes(10), AppointmentStatus.Arrived);
        await AccountAsync(_studio.Id, s => s.PayBillAsync(late, PaymentMethod.Cash, [], null));
        var after = await TillAsync(_studio.Id, s => s.GetDayAsync(today));
        after.ChangedSinceClose.ShouldBeTrue();
        after.ExpectedCash.ShouldBe(new Money(35m, "GBP"));
        after.Closed.ShouldNotBeNull().ExpectedCash.ShouldBe(new Money(15m, "GBP"));

        // The other salon's day is its own.
        (await TillAsync(_polish.Id, s => s.GetDayAsync(today))).Closed.ShouldBeNull();
        (await TillAsync(_polish.Id, s => s.CloseAsync(today, 0m, null, null))).Outcome.ShouldBe(CashUpOutcome.Closed);
    }

    [Fact]
    public async Task The_till_page_shows_the_day_and_closes_it()
    {
        await SavePolicyAsync(_studio.Id, noShowPercent: 100m);
        await RecordADayAsync();
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        var page = await GetAsync(client, TillPath);
        page.ShouldContain("data-testid=\"total-Cash\">£15.00<");
        page.ShouldContain("data-testid=\"total-CardAtSalonTerminal\">£20.00<");
        page.ShouldContain("data-testid=\"total-BankTransfer\">£10.00<");
        page.ShouldContain("data-testid=\"takings\">£45.00<");
        page.ShouldContain("data-testid=\"expected-cash\">£15.00<");
        page.ShouldContain("Deposit returned");
        page.ShouldContain("data-testid=\"close-form\"");
        var today = (await TillAsync(_studio.Id, s => s.GetDayAsync(null))).Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var bad = await PostAsync(client, TillPath, [new("date", today), new("countedCash", "abc")], page);
        bad.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await bad.Content.ReadAsStringAsync()).ShouldContain("Enter the cash counted: zero or more, with at most two decimals.");

        var closed = await PostAsync(client, TillPath, [new("date", today), new("countedCash", "15.50"), new("note", "Tip left in the drawer")], page);

        closed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        closed.Headers.Location!.OriginalString.ShouldBe($"{TillPath}?date={today}");
        var after = await GetAsync(client, $"{TillPath}?date={today}");
        after.ShouldContain("The day has been closed.");
        after.ShouldContain("data-testid=\"difference-badge\">Over<");
        after.ShouldContain("data-testid=\"closed-counted\">£15.50<");
        after.ShouldContain("data-testid=\"closed-difference\">£0.50<");
        after.ShouldContain("Tip left in the drawer");
        after.ShouldContain("Closed by receptionist@studio-20.localhost");
        after.ShouldNotContain("data-testid=\"close-form\"");

        var again = await PostAsync(client, TillPath, [new("date", today), new("countedCash", "1")], page);
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await again.Content.ReadAsStringAsync()).ShouldContain("This day has already been closed. The first count stands.");
    }

    [Fact]
    public async Task The_pages_speak_the_salon_language()
    {
        using var client = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);

        var till = await GetAsync(client, TillPath);

        till.ShouldContain("Kasa");
        till.ShouldContain("Tego dnia nie zapisano żadnych płatności.");
        till.ShouldContain("Zamknij dzień");
    }

    [Fact]
    public async Task Staff_another_salon_and_a_post_without_a_token_get_nowhere()
    {
        var visit = await VisitAsync(Now - Duration.FromMinutes(30), AppointmentStatus.Arrived);
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var bill = await GetAsync(owner, CheckoutPath(visit));

        // The Staff role neither sees clients nor records payments (ADR 0009).
        using var staff = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Staff);
        foreach (var path in new[] { CheckoutPath(visit), TillPath })
        {
            var get = await staff.GetAsync(new Uri(path, UriKind.Relative));
            get.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        }

        (await PostAsync(staff, CheckoutPath(visit), [new("method", "Cash")], bill)).Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");

        // Another salon's visit does not exist here.
        using var other = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        (await other.GetAsync(new Uri(CheckoutPath(visit), UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(other, CheckoutPath(visit), [new("method", "Cash")], await GetAsync(other, TillPath))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // No anti-forgery token, no payment and no close.
        (await owner.PostAsync(new Uri(CheckoutPath(visit), UriKind.Relative), new FormUrlEncodedContent([new("method", "Cash")]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.PostAsync(new Uri(TillPath, UriKind.Relative), new FormUrlEncodedContent([new("countedCash", "1")]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await EntriesAsync(_studio.Id)).ShouldBeEmpty();
        (await StatusAsync(visit)).ShouldBe(AppointmentStatus.Arrived);
    }

    private static string CheckoutPath(Guid appointmentId) => $"/admin/payments/checkout/{appointmentId}";

    /// <summary>
    /// A deposit in cash (£5), a visit paid by card (£20), a deposit by transfer (£10) that goes back in cash, and a
    /// no-show fee paid in cash (£20): cash 5 - 10 + 20 = 15, card 20, transfer 10.
    /// </summary>
    private async Task RecordADayAsync()
    {
        var upcoming = await VisitAsync(Now + Duration.FromDays(2), AppointmentStatus.Booked);
        await AccountAsync(_studio.Id, s => s.RecordDepositAsync(upcoming, 5m, PaymentMethod.Cash, null));
        var arrived = await VisitAsync(Now - Duration.FromMinutes(45), AppointmentStatus.Arrived);
        await AccountAsync(_studio.Id, s => s.PayBillAsync(arrived, PaymentMethod.CardAtSalonTerminal, [], null));
        var cancelled = await VisitAsync(Now + Duration.FromDays(3), AppointmentStatus.Booked);
        await AccountAsync(_studio.Id, s => s.RecordDepositAsync(cancelled, 10m, PaymentMethod.BankTransfer, null));
        await AccountAsync(_studio.Id, s => s.ReturnDepositAsync(cancelled, PaymentMethod.Cash, null));
        var missed = await VisitAsync(Now - Duration.FromDays(4), AppointmentStatus.NoShow);
        await AccountAsync(_studio.Id, s => s.ChargeFeeAsync(missed, null, null));
        await AccountAsync(_studio.Id, s => s.RecordFeePaymentAsync(missed, PaymentMethod.Cash, null));
    }

    private Task<Guid> VisitAsync(Instant start, AppointmentStatus status)
        => VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, start, status);

    private async Task SetStatusAsync(Guid appointmentId, AppointmentStatus status)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Appointments
            .Where(a => a.Id == appointmentId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, status));
    }

    private async Task<AppointmentStatus> StatusAsync(Guid appointmentId)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Appointments.AsNoTracking().Where(a => a.Id == appointmentId).Select(a => a.Status).SingleAsync();
    }

    private async Task SavePolicyAsync(Guid tenantId, decimal noShowPercent)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        await scope.ServiceProvider.GetRequiredService<ISettingService>().SaveAsync(new CancellationPolicySettings { NoShowFeePercent = noShowPercent });
    }

    private async Task<T> AccountAsync<T>(Guid tenantId, Func<IClientAccountService, Task<T>> act)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await act(scope.ServiceProvider.GetRequiredService<IClientAccountService>());
    }

    private async Task<T> TillAsync<T>(Guid tenantId, Func<ICashUpService, Task<T>> act)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await act(scope.ServiceProvider.GetRequiredService<ICashUpService>());
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

    /// <summary>A list, not a dictionary: the fee checkboxes post the same name several times.</summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, List<KeyValuePair<string, string>> fields, string pageWithToken)
    {
        fields.Add(new("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(pageWithToken)));
        return await client.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent(fields));
    }
}