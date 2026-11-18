using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Core.Reports;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Payments;
using Salon.Services.Reports;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Payments;

/// <summary>
/// Gift vouchers (ADR 0057): sold for money today as a line with no client; used later toward a visit as a line that
/// moves no money and leaves the visit that much less to pay; what is left is what the salon owes. Nobody uses more
/// than a voucher has, even from two tills at once; the money goes back only for one nobody touched; another salon's
/// code does not exist here. A cut is £20.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class GiftVoucherTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string ListPath = "/admin/payments/vouchers";
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly Instant Now = SystemClock.Instance.GetCurrentInstant();
    private static readonly LocalDate Today = Now.InZone(London).Date;

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _staffId;
    private Guid _serviceId;
    private Guid _clientId;
    private int _visits;

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
    public async Task Selling_a_voucher_is_money_in_today_with_no_client_and_a_code_to_hand_over()
    {
        var user = await factory.CreateUserAsync(_studio.Id, "desk@studio-20.test", Role.Receptionist);

        var voucher = await VouchersAsync(s => s.SellAsync(50m, PaymentMethod.CardAtSalonTerminal, Today.PlusMonths(12), "  For Mia  ", user.Id));

        voucher.Code.ShouldMatch("^[2-9A-HJKMNP-Z]{5}-[2-9A-HJKMNP-Z]{5}$");
        (voucher.Initial.Amount, voucher.Left.Amount, voucher.Status, voucher.ExpiresOn, voucher.Note).ShouldBe((50m, 50m, GiftVoucherStatus.Active, Today.PlusMonths(12), "For Mia"));
        await InScopeAsync(async db =>
        {
            var entry = await db.ClientAccountEntries.AsNoTracking().SingleAsync();
            (entry.Kind, entry.ClientId, entry.AppointmentId, entry.Amount, entry.Method, entry.ByUserId)
                .ShouldBe((ClientAccountEntryKind.VoucherSale, null, null, 50m, PaymentMethod.CardAtSalonTerminal, user.Id));
            (await db.GiftVouchers.AsNoTracking().SingleAsync()).SaleEntryId.ShouldBe(entry.Id);
        });

        var till = await ScopedAsync<ICashUpService, CashDay>(s => s.GetDayAsync(null));
        till.Totals.Single(t => t.Method == PaymentMethod.CardAtSalonTerminal).Amount.Amount.ShouldBe(50m);
        var list = await VouchersAsync(s => s.ListAsync(false, null, null));
        (list.Outstanding.Amount, list.ActiveCount).ShouldBe((50m, 1));
        (await VouchersAsync(s => s.FindByCodeAsync(voucher.Code.ToLowerInvariant().Replace('-', ' ')))).ShouldNotBeNull().Id.ShouldBe(voucher.Id);
    }

    [Fact]
    public async Task A_voucher_pays_for_visits_until_it_is_used_up_and_the_money_is_counted_once()
    {
        var voucher = await SellAsync(50m);
        var first = await VisitAsync(AppointmentStatus.Arrived);
        var second = await VisitAsync(AppointmentStatus.Arrived);
        var third = await VisitAsync(AppointmentStatus.Arrived);

        var one = await VouchersAsync(s => s.ApplyAsync(first, voucher.Code, null));
        (one.Amount.Amount, one.Left.Amount, one.Bill.Outstanding.Amount, one.Bill.VoucherApplied?.Amount).ShouldBe((20m, 30m, 0m, 20m));
        var two = await VouchersAsync(s => s.ApplyAsync(second, voucher.Code, null));
        (two.Amount.Amount, two.Left.Amount).ShouldBe((20m, 10m));
        var three = await VouchersAsync(s => s.ApplyAsync(third, voucher.Code, null));
        (three.Amount.Amount, three.Left.Amount, three.Bill.Outstanding.Amount).ShouldBe((10m, 0m, 10m), "the last ten; ten is left to pay");

        // The rest of the third visit is paid in cash, for what is left and no more.
        var paid = await ScopedAsync<IClientAccountService, VisitBill>(s => s.PayBillAsync(third, PaymentMethod.Cash, [], null));
        paid.Payment.ShouldNotBeNull().Amount.Amount.ShouldBe(10m);

        var card = (await VouchersAsync(s => s.GetAsync(voucher.Id))).ShouldNotBeNull();
        (card.Voucher.Status, card.Voucher.Left.Amount, card.CanRefund).ShouldBe((GiftVoucherStatus.Used, 0m, false));
        card.Uses.Select(u => (u.ClientName, u.Amount.Amount)).ShouldBe([("Olivia Brown", 20m), ("Olivia Brown", 20m), ("Olivia Brown", 10m)]);
        (await VouchersAsync(s => s.ListAsync(false, null, null))).Outstanding.Amount.ShouldBe(0m);

        // Sixty pounds of work, sixty pounds of money: fifty when the voucher was sold, ten in cash. Nothing twice.
        await SetAsync([first, second, third], AppointmentStatus.Completed);
        var report = await ScopedAsync<IReportService, SalonReport>(s => s.GetAsync(ReportPreset.ThisMonth, null, null));
        report.Takings.Total.Amount.ShouldBe(60m);
        report.Takings.ByKind.Select(k => (k.Kind, k.Amount.Amount)).ShouldBe([(ClientAccountEntryKind.VisitPayment, 10m), (ClientAccountEntryKind.VoucherSale, 50m)]);
        report.UnpaidCount.ShouldBe(0, "a visit a voucher covered is not a visit nobody paid for");
        var till = await ScopedAsync<ICashUpService, CashDay>(s => s.GetDayAsync(null));
        till.Movements.Count.ShouldBe(2, "using a voucher moves no money, so it is not on the till");
    }

    [Fact]
    public async Task A_voucher_and_a_deposit_both_count_toward_the_same_visit()
    {
        var voucher = await SellAsync(10m);
        var visit = await VisitAsync(AppointmentStatus.Arrived);
        await InScopeAsync(async db =>
        {
            db.ClientAccountEntries.Add(new ClientAccountEntry { ClientId = _clientId, AppointmentId = visit, Kind = ClientAccountEntryKind.DepositPaid, Amount = 5m, Currency = "GBP", Method = PaymentMethod.Cash, At = Now });
            await db.SaveChangesAsync();
        });

        var applied = await VouchersAsync(s => s.ApplyAsync(visit, voucher.Code, null));

        (applied.Amount.Amount, applied.Bill.DepositHeld?.Amount, applied.Bill.VoucherApplied?.Amount, applied.Bill.Outstanding.Amount).ShouldBe((10m, 5m, 10m, 5m));
    }

    [Fact]
    public async Task What_cannot_be_used_is_refused_with_its_reason_and_nothing_changes()
    {
        var good = await SellAsync(50m);
        var expired = await SellAsync(50m);
        var spent = await SellAsync(20m);
        var theirs = await ScopedAsync<IGiftVoucherService, VoucherSummary>(s => s.SellAsync(50m, PaymentMethod.Cash, null, null, null), _polish.Id);
        await InScopeAsync(async db =>
        {
            await db.GiftVouchers.Where(v => v.Id == expired.Id).ExecuteUpdateAsync(s => s.SetProperty(v => v.ExpiresOn, Today.PlusDays(-1)));
        });
        var arrived = await VisitAsync(AppointmentStatus.Arrived);
        var booked = await VisitAsync(AppointmentStatus.Booked);
        var other = await VisitAsync(AppointmentStatus.Arrived);
        await VouchersAsync(s => s.ApplyAsync(other, spent.Code, null));

        (await RefusalAsync(s => s.ApplyAsync(arrived, "ABCDE-FGHJK", null))).ShouldBe(GiftVoucherRefusal.NotFound);
        (await RefusalAsync(s => s.ApplyAsync(arrived, "not a code", null))).ShouldBe(GiftVoucherRefusal.NotFound);
        (await RefusalAsync(s => s.ApplyAsync(arrived, theirs.Code, null))).ShouldBe(GiftVoucherRefusal.NotFound, "another salon's voucher does not exist here");
        (await RefusalAsync(s => s.ApplyAsync(arrived, expired.Code, null))).ShouldBe(GiftVoucherRefusal.Expired);
        (await RefusalAsync(s => s.ApplyAsync(arrived, spent.Code, null))).ShouldBe(GiftVoucherRefusal.NothingLeft);
        (await RefusalAsync(s => s.ApplyAsync(booked, good.Code, null))).ShouldBe(GiftVoucherRefusal.VisitNotPayable);
        (await RefusalAsync(s => s.ApplyAsync(Guid.NewGuid(), good.Code, null))).ShouldBe(GiftVoucherRefusal.VisitNotPayable);
        (await RefusalAsync(s => s.ApplyAsync(other, good.Code, null))).ShouldBe(GiftVoucherRefusal.NothingToPay, "the other voucher covered it");

        (await RefusalAsync(s => s.SellAsync(0m, PaymentMethod.Cash, null, null, null))).ShouldBe(GiftVoucherRefusal.AmountInvalid);
        (await RefusalAsync(s => s.SellAsync(50m, PaymentMethod.Voucher, null, null, null))).ShouldBe(GiftVoucherRefusal.MethodNotAllowed);
        (await RefusalAsync(s => s.SellAsync(50m, PaymentMethod.Cash, Today.PlusDays(-1), null, null))).ShouldBe(GiftVoucherRefusal.ExpiryPast);
        (await RefusalAsync(s => s.SellAsync(50m, PaymentMethod.Cash, null, new string('x', GiftVoucher.NoteMaxLength + 1), null))).ShouldBe(GiftVoucherRefusal.NoteTooLong);

        (await VouchersAsync(s => s.GetAsync(good.Id))).ShouldNotBeNull().Voucher.Left.Amount.ShouldBe(50m);
        await InScopeAsync(async db => (await db.ClientAccountEntries.AsNoTracking().CountAsync(e => e.Kind == ClientAccountEntryKind.VoucherRedeemed)).ShouldBe(1));
    }

    [Fact]
    public async Task Two_tills_cannot_both_take_a_vouchers_money_for_the_same_visit()
    {
        var voucher = await SellAsync(100m);
        var visit = await VisitAsync(AppointmentStatus.Arrived);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            try
            {
                await VouchersAsync(s => s.ApplyAsync(visit, voucher.Code, null));
                return true;
            }
            catch (GiftVoucherException ex) when (ex.Refusal is GiftVoucherRefusal.AlreadyOnThisBill or GiftVoucherRefusal.NothingToPay)
            {
                return false;
            }
        }));

        attempts.Count(done => done).ShouldBe(1);
        (await VouchersAsync(s => s.GetAsync(voucher.Id))).ShouldNotBeNull().Voucher.Left.Amount.ShouldBe(80m, "twenty went onto the bill, once");
        await InScopeAsync(async db => (await db.ClientAccountEntries.AsNoTracking().CountAsync(e => e.Kind == ClientAccountEntryKind.VoucherRedeemed)).ShouldBe(1));
    }

    [Fact]
    public async Task The_money_goes_back_for_a_voucher_nobody_touched_once_and_not_for_one_that_was_used()
    {
        var untouched = await SellAsync(50m);
        var used = await SellAsync(50m);
        var visit = await VisitAsync(AppointmentStatus.Arrived);
        await VouchersAsync(s => s.ApplyAsync(visit, used.Code, null));

        var back = await VouchersAsync(s => s.RefundAsync(untouched.Id, PaymentMethod.Cash, null));

        back.Amount.ShouldBe(50m);
        var card = (await VouchersAsync(s => s.GetAsync(untouched.Id))).ShouldNotBeNull();
        (card.Voucher.Status, card.Voucher.Left.Amount, card.CanRefund, card.RefundedOn).ShouldBe((GiftVoucherStatus.Refunded, 0m, false, Today));
        await InScopeAsync(async db =>
        {
            var line = await db.ClientAccountEntries.AsNoTracking().SingleAsync(e => e.Kind == ClientAccountEntryKind.VoucherRefund);
            (line.ClientId, line.AppointmentId, line.Amount, line.Method).ShouldBe((null, null, 50m, PaymentMethod.Cash));
        });
        (await RefusalAsync(s => s.RefundAsync(untouched.Id, PaymentMethod.Cash, null))).ShouldBe(GiftVoucherRefusal.NotRefundable, "given back already");
        (await RefusalAsync(s => s.RefundAsync(used.Id, PaymentMethod.Cash, null))).ShouldBe(GiftVoucherRefusal.NotRefundable, "part of it was used");
        (await RefusalAsync(s => s.RefundAsync(Guid.NewGuid(), PaymentMethod.Cash, null))).ShouldBe(GiftVoucherRefusal.NotFound);
        var another = await VisitAsync(AppointmentStatus.Arrived);
        (await RefusalAsync(s => s.ApplyAsync(another, untouched.Code, null))).ShouldBe(GiftVoucherRefusal.Refunded);

        var till = await ScopedAsync<ICashUpService, CashDay>(s => s.GetDayAsync(null));
        till.Totals.Single(t => t.Method == PaymentMethod.CardAtSalonTerminal).Amount.Amount.ShouldBe(100m, "two sold by card");
        till.Totals.Single(t => t.Method == PaymentMethod.Cash).Amount.Amount.ShouldBe(-50m, "fifty given back out of the drawer");
    }

    [Fact]
    public async Task The_desk_sells_a_voucher_on_the_page_and_the_same_form_sent_twice_is_one_voucher()
    {
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        (await GetAsync(desk, "/admin/payments/cash-up")).ShouldContain($"href=\"{ListPath}\" class=\"btn btn-compact btn-secondary\" data-testid=\"gift-vouchers\"");
        var form = await GetAsync(desk, ListPath + "/new");
        form.ShouldContain("Sell a gift voucher");
        var saleId = SaleId().Match(form).Groups["id"].Value;
        KeyValuePair<string, string>[] Fields(string amount, string method = "Cash") =>
        [
            new("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(form)),
            new("SaleId", saleId),
            new("Amount", amount),
            new("Method", method),
            new("ExpiresOn", string.Empty),
            new("Note", "For Mia"),
        ];

        foreach (var wrong in new[] { "fifty", "0", "5000.01", "12.345" })
        {
            var refused = await desk.PostAsync(new Uri(ListPath + "/new", UriKind.Relative), new FormUrlEncodedContent(Fields(wrong)));
            refused.StatusCode.ShouldBe(HttpStatusCode.OK, wrong);
            WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()).ShouldContain("Enter an amount from 0.01 to 5000, with at most two decimal places.");
        }

        (await desk.PostAsync(new Uri(ListPath + "/new", UriKind.Relative), new FormUrlEncodedContent(Fields("50", "Voucher")))).StatusCode.ShouldBe(HttpStatusCode.OK, "a voucher is not bought with a voucher");
        await InScopeAsync(async db => (await db.GiftVouchers.AsNoTracking().AnyAsync()).ShouldBeFalse());

        var sold = await desk.PostAsync(new Uri(ListPath + "/new", UriKind.Relative), new FormUrlEncodedContent(Fields("50")));
        var again = await desk.PostAsync(new Uri(ListPath + "/new", UriKind.Relative), new FormUrlEncodedContent(Fields("50")));

        sold.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        again.Headers.Location!.OriginalString.ShouldBe(sold.Headers.Location!.OriginalString, "the second answer is the voucher that was sold");
        var cardPage = await GetAsync(desk, sold.Headers.Location.OriginalString);
        cardPage.ShouldContain("A voucher worth £50.00 has been sold. Its code is ");
        cardPage.ShouldContain("data-testid=\"voucher-initial\">£50.00<");
        cardPage.ShouldContain("No expiry date");
        cardPage.ShouldContain("For Mia");
        cardPage.ShouldContain("Not used yet.");
        await InScopeAsync(async db =>
        {
            (await db.GiftVouchers.AsNoTracking().CountAsync()).ShouldBe(1);
            (await db.ClientAccountEntries.AsNoTracking().SingleAsync()).Id.ShouldBe(Guid.Parse(saleId));
        });

        var list = await GetAsync(desk, ListPath);
        list.ShouldContain("data-testid=\"vouchers-outstanding\">£50.00<");
        list.ShouldContain("Can be used (1)");
    }

    [Fact]
    public async Task A_code_typed_at_the_checkout_covers_the_visit_and_ends_it()
    {
        var voucher = await SellAsync(50m);
        var visit = await VisitAsync(AppointmentStatus.Arrived);
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        var checkout = $"/admin/payments/checkout/{visit}";

        var bill = await GetAsync(desk, checkout);
        bill.ShouldContain("Use a gift voucher");
        var wrong = await PostCodeAsync(desk, checkout, bill, "ABCDE-FGHJK");
        wrong.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await GetAsync(desk, checkout)).ShouldContain("There is no voucher with this code.");

        var used = await PostCodeAsync(desk, checkout, bill, voucher.Code.ToLowerInvariant());

        used.Headers.Location!.OriginalString.ShouldBe(checkout);
        var after = await GetAsync(desk, checkout);
        after.ShouldContain($"£20.00 from voucher {voucher.Code} went toward this visit. £30.00 is left on the voucher.");
        after.ShouldContain("data-testid=\"bill-voucher\">−£20.00<");
        after.ShouldContain("data-testid=\"settled-reason\">A gift voucher covered this visit. Nothing is left to pay.<");
        after.ShouldContain("data-status=\"Completed\"", Case.Sensitive, "nothing was left to pay, so the visit is over");
        after.ShouldNotContain("data-testid=\"voucher-step\"");

        var card = await GetAsync(desk, $"{ListPath}/{voucher.Id}");
        card.ShouldContain("data-testid=\"voucher-left\">£30.00<");
        card.ShouldContain($"href=\"{checkout}\"");
        card.ShouldContain("Olivia Brown");
        card.ShouldNotContain("data-testid=\"voucher-refund\"", Case.Sensitive, "a voucher somebody used is not given back");
    }

    [Fact]
    public async Task The_list_finds_a_voucher_by_its_code_and_the_refund_is_recorded_on_its_page()
    {
        var kept = await SellAsync(50m);
        var back = await SellAsync(30m);
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var found = await GetAsync(owner, $"{ListPath}?q={kept.Code[..4].ToLowerInvariant()}");
        found.ShouldContain(kept.Code);
        found.ShouldNotContain(back.Code);

        var page = await GetAsync(owner, $"{ListPath}/{back.Id}");
        var refund = await owner.PostAsync(new Uri($"{ListPath}/{back.Id}/refund", UriKind.Relative), new FormUrlEncodedContent(
        [
            new("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(page)),
            new("method", "Cash"),
        ]));

        refund.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var after = await GetAsync(owner, $"{ListPath}/{back.Id}");
        after.ShouldContain("£30.00 has been given back. The voucher can no longer be used.");
        after.ShouldContain("Money given back");
        var active = await GetAsync(owner, ListPath);
        active.ShouldContain("data-testid=\"vouchers-outstanding\">£50.00<");
        active.ShouldNotContain(back.Code);
        (await GetAsync(owner, $"{ListPath}?show=all")).ShouldContain(back.Code);
    }

    [Fact]
    public async Task Whoever_takes_payments_gets_in_and_another_salon_sees_none_of_it()
    {
        var voucher = await SellAsync(50m);
        using var staff = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Staff);
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        using var anonymous = factory.ClientFor(HostA);
        using var polish = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);

        foreach (var path in new[] { ListPath, ListPath + "/new", $"{ListPath}/{voucher.Id}" })
        {
            var denied = await staff.GetAsync(new Uri(path, UriKind.Relative));
            denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied", path);
            (await anonymous.GetAsync(new Uri(path, UriKind.Relative))).Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login", path);
        }

        (await polish.GetAsync(new Uri($"{ListPath}/{voucher.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var theirs = await GetAsync(polish, ListPath);
        theirs.ShouldNotContain(voucher.Code);
        theirs.ShouldContain("data-testid=\"no-vouchers\"");
        theirs.ShouldContain("Bony podarunkowe");

        var noToken = await desk.PostAsync(new Uri($"{ListPath}/{voucher.Id}/refund", UriKind.Relative), new FormUrlEncodedContent([new("method", "Cash")]));
        noToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await VouchersAsync(s => s.GetAsync(voucher.Id))).ShouldNotBeNull().Voucher.Left.Amount.ShouldBe(50m);
    }

    private static Task<HttpResponseMessage> PostCodeAsync(HttpClient client, string checkout, string pageWithToken, string code)
        => client.PostAsync(new Uri(checkout + "/voucher", UriKind.Relative), new FormUrlEncodedContent(
        [
            new("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(pageWithToken)),
            new("code", code),
        ]));

    private Task<VoucherSummary> SellAsync(decimal amount)
        => VouchersAsync(s => s.SellAsync(amount, PaymentMethod.CardAtSalonTerminal, null, null, null));

    /// <summary>A visit earlier today; each one a little earlier than the last so no two overlap.</summary>
    private Task<Guid> VisitAsync(AppointmentStatus status)
        => VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, Now - Duration.FromMinutes(40 * ++_visits), status);

    private Task SetAsync(Guid[] visits, AppointmentStatus status)
        => InScopeAsync(db => db.Appointments.Where(a => visits.Contains(a.Id)).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, status)));

    private async Task<GiftVoucherRefusal> RefusalAsync<T>(Func<IGiftVoucherService, Task<T>> act)
        => (await Should.ThrowAsync<GiftVoucherException>(() => VouchersAsync(act))).Refusal;

    private Task<T> VouchersAsync<T>(Func<IGiftVoucherService, Task<T>> act) => ScopedAsync(act);

    private async Task<T> ScopedAsync<TService, T>(Func<TService, Task<T>> act, Guid? tenantId = null)
        where TService : notnull
    {
        await using var scope = factory.CreateTenantScope(tenantId ?? _studio.Id);
        return await act(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task InScopeAsync(Func<SalonDbContext, Task> act)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await act(scope.ServiceProvider.GetRequiredService<SalonDbContext>());
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    [GeneratedRegex("name=\"SaleId\" value=\"(?<id>[0-9a-f-]{36})\"")]
    private static partial Regex SaleId();
}