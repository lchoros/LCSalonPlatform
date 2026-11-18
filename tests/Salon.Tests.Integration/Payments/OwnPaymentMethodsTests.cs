using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Core.Products;
using Salon.Core.Reports;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Payments;
using Salon.Services.Products;
using Salon.Services.Reports;
using Salon.Services.Settings;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Payments;

/// <summary>
/// The salon's own payment methods (ADR 0060): the owner adds "PayPal" and every place that takes or gives back money
/// offers it; a line paid by it says which method and what it was called that day; the till, the report and the money
/// file show it as its own line; a method the salon does not have, or took out of use, records nothing; another salon
/// never sees it. A cut is £20, a shampoo £12.50.
/// </summary>
[Collection(SharedDatabase.Name)]
public class OwnPaymentMethodsTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string SettingsPath = "/admin/settings/payment-methods";
    private const string TillPath = "/admin/payments/cash-up";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly Instant Now = SystemClock.Instance.GetCurrentInstant();
    private static readonly LocalDate Today = Now.InZone(London).Date;

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _staffId;
    private Guid _serviceId;
    private Guid _clientId;
    private Guid _shampoo;
    private int _visits;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        (_staffId, _serviceId) = await VisitSeed.SalonAsync(factory, _studio.Id);
        _clientId = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123");
        _shampoo = await ProductAsync("Shampoo 250 ml", 12.50m);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_owner_adds_a_method_and_every_list_offers_it_after_the_built_in_ones()
    {
        var added = await MethodsAsync(s => s.AddAsync("  Pay   Pal "));

        (added.Outcome, added.Name).ShouldBe((PaymentMethodOutcome.Saved, "Pay Pal"));
        var options = await MethodsAsync(s => s.OptionsAsync());
        options.Select(o => o.Name).ShouldBe(["Cash", "Card at the salon's terminal", "Bank transfer", "Voucher", "Pay Pal"]);
        options[^1].Choice.ShouldBe(PaymentChoice.Own(added.Id!.Value));
        options[^1].Value.ShouldBe(added.Id!.Value.ToString());
        (await MethodsAsync(s => s.OptionsAsync(forVoucher: true))).Select(o => o.Name).ShouldBe(["Cash", "Card at the salon's terminal", "Bank transfer", "Pay Pal"]);
        (await MethodsAsync(s => s.ListAsync())).Own.ShouldHaveSingleItem().ShouldBe(new OwnPaymentMethod(added.Id!.Value, "Pay Pal", true, false));

        // Without a method of its own a salon has exactly the four it always had.
        (await MethodsAsync(s => s.OptionsAsync(), _polish.Id)).Select(o => o.Choice)
            .ShouldBe([PaymentMethod.Cash, PaymentMethod.CardAtSalonTerminal, PaymentMethod.BankTransfer, PaymentMethod.Voucher]);
    }

    [Fact]
    public async Task Money_taken_by_an_own_method_is_its_own_line_in_the_till_the_report_and_the_file()
    {
        var payPal = await AddAsync("PayPal");
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var visit = await VisitAsync(AppointmentStatus.Arrived);
        var bill = await GetAsync(desk, CheckoutPath(visit));
        bill.ShouldContain($"<option value=\"{payPal}\">PayPal</option>");

        var paid = await PostAsync(desk, CheckoutPath(visit), bill, ("method", payPal.ToString()));

        paid.StatusCode.ShouldBe(HttpStatusCode.Redirect, await paid.Content.ReadAsStringAsync());
        var later = await VisitAsync(AppointmentStatus.Booked, Duration.FromDays(2));
        await AccountAsync(s => s.RecordDepositAsync(later, 5m, PaymentMethod.Cash, null));

        var line = (await EntriesAsync()).Single(e => e.Kind == ClientAccountEntryKind.VisitPayment);
        (line.Method, line.CustomMethodId, line.MethodName, line.Amount).ShouldBe((PaymentMethod.Other, payPal, "PayPal", 20m));

        var till = await ScopedAsync<ICashUpService, CashDay>(s => s.GetDayAsync(null));
        till.Totals.Select(t => (t.Method, t.MethodName, t.CustomMethodId, t.Amount.Amount)).ShouldBe(
        [
            (PaymentMethod.Cash, null, null, 5m),
            (PaymentMethod.Other, "PayPal", payPal, 20m),
        ]);
        (till.Takings.Amount, till.ExpectedCash.Amount).ShouldBe((25m, 5m), "PayPal money is takings, and is not in the drawer");
        till.Movements.Single(m => m.Kind == ClientAccountEntryKind.VisitPayment).MethodName.ShouldBe("PayPal");

        var report = await ScopedAsync<IReportService, SalonReport>(s => s.GetAsync(null, Today, Today));
        report.Takings.ByMethod.Select(t => (t.Method, t.MethodName, t.Amount.Amount)).ShouldBe([(PaymentMethod.Cash, null, 5m), (PaymentMethod.Other, "PayPal", 20m)]);
        report.Takings.Total.Amount.ShouldBe(25m);

        (await GetAsync(desk, CheckoutPath(visit))).ShouldContain("PayPal");
        (await GetAsync(desk, TillPath)).ShouldContain("PayPal");
        (await GetAsync(desk, $"/admin/reports?from={Today}&to={Today}")).ShouldContain("PayPal");
        var file = await GetAsync(desk, $"/admin/reports/money.csv?from={Today}&to={Today}");
        file.ShouldContain("PayPal");
        file.ShouldNotContain("Other");
    }

    [Fact]
    public async Task Every_way_money_moves_takes_an_own_method()
    {
        var payPal = PaymentChoice.Own(await AddAsync("PayPal"));
        await SavePolicyAsync(noShowPercent: 50m);

        // A deposit and its return.
        var booked = await VisitAsync(AppointmentStatus.Booked, Duration.FromDays(2));
        await AccountAsync(s => s.RecordDepositAsync(booked, 5m, payPal, null));
        var returned = await AccountAsync(s => s.ReturnDepositAsync(booked, payPal, null));
        (returned.MethodName, returned.Return.ShouldNotBeNull().MethodName, returned.PaidWith).ShouldBe(("PayPal", "PayPal", payPal));

        // A fee for a missed visit.
        var missed = await VisitAsync(AppointmentStatus.NoShow);
        await AccountAsync(s => s.ChargeFeeAsync(missed, null, null));
        var fee = await AccountAsync(s => s.RecordFeePaymentAsync(missed, payPal, null));
        fee.Settlement.ShouldNotBeNull().MethodName.ShouldBe("PayPal");

        // A visit with a product on its bill, and the product brought back.
        var arrived = await VisitAsync(AppointmentStatus.Arrived);
        await AccountAsync(s => s.AddProductAsync(arrived, _shampoo, 2, null));
        var bill = await AccountAsync(s => s.PayBillAsync(arrived, payPal, [], null));
        var product = bill.Products.ShouldHaveSingleItem();
        (product.PaidWith, product.PaidWithName, product.PaidBy).ShouldBe((PaymentMethod.Other, "PayPal", payPal));
        bill.Payment.ShouldNotBeNull().MethodName.ShouldBe("PayPal");
        await AccountAsync(s => s.ReturnProductAsync(arrived, product.LineId, 1, payPal, true, null));
        (await AccountAsync(s => s.GetPurchasesAsync(_clientId))).ShouldHaveSingleItem().MethodName.ShouldBe("PayPal");

        // A sale at the counter, and a unit of it brought back.
        var receipt = await ScopedAsync<ICounterSaleService, CounterSaleReceipt>(s => s.SellAsync([(_shampoo, 2)], payPal, null));
        (receipt.Method, receipt.MethodName).ShouldBe((PaymentMethod.Other, "PayPal"));
        var sale = (await ScopedAsync<ICounterSaleService, CounterSaleDay>(s => s.ListAsync(null))).Sales.ShouldHaveSingleItem();
        (sale.MethodName, sale.PaidWith).ShouldBe(("PayPal", payPal));
        await ScopedAsync<ICounterSaleService, Salon.Core.Domain.Money>(s => s.ReturnAsync(sale.Id, sale.Lines[0].Id, 1, payPal, true, null));

        // A gift voucher sold, and its money given back.
        var voucher = await ScopedAsync<IGiftVoucherService, VoucherSummary>(s => s.SellAsync(30m, payPal, null, null, null));
        var card = (await ScopedAsync<IGiftVoucherService, VoucherCard?>(s => s.GetAsync(voucher.Id))).ShouldNotBeNull();
        (card.SoldWith, card.SoldWithName, card.PaidWith).ShouldBe((PaymentMethod.Other, "PayPal", payPal));
        await ScopedAsync<IGiftVoucherService, Salon.Core.Domain.Money>(s => s.RefundAsync(voucher.Id, payPal, null));

        var entries = await EntriesAsync();
        var moved = entries.Where(e => e.Method is not null).ToArray();
        moved.Select(e => e.Kind).Order().ShouldBe(
        [
            ClientAccountEntryKind.FeePayment, ClientAccountEntryKind.DepositPaid, ClientAccountEntryKind.DepositReturned, ClientAccountEntryKind.VisitPayment,
            ClientAccountEntryKind.ProductSale, ClientAccountEntryKind.ProductSale, ClientAccountEntryKind.ProductReturn, ClientAccountEntryKind.ProductReturn,
            ClientAccountEntryKind.VoucherSale, ClientAccountEntryKind.VoucherRefund,
        ]);
        moved.ShouldAllBe(e => e.Method == PaymentMethod.Other && e.CustomMethodId == payPal.CustomMethodId && e.MethodName == "PayPal");
        entries.Where(e => e.Method is null).ShouldAllBe(e => e.CustomMethodId == null && e.MethodName == null);

        // One line in the till for all of it, and nothing of it in the drawer.
        var till = await ScopedAsync<ICashUpService, CashDay>(s => s.GetDayAsync(null));
        till.Totals.ShouldHaveSingleItem().MethodName.ShouldBe("PayPal");
        till.ExpectedCash.Amount.ShouldBe(0m);
        (await MethodsAsync(s => s.ListAsync())).Own.ShouldHaveSingleItem().IsUsed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_method_the_salon_does_not_have_records_nothing()
    {
        var retired = await AddAsync("Cheque");
        var arrived = await VisitAsync(AppointmentStatus.Arrived);
        await AccountAsync(s => s.PayBillAsync(arrived, PaymentChoice.Own(retired), [], null));
        (await MethodsAsync(s => s.RetireAsync(retired))).Outcome.ShouldBe(PaymentMethodOutcome.Saved);
        var theirs = (await MethodsAsync(s => s.AddAsync("Blik"), _polish.Id)).Id!.Value;
        var before = (await EntriesAsync()).Count;

        PaymentChoice[] notOurs = [PaymentChoice.Own(Guid.NewGuid()), PaymentChoice.Own(theirs), PaymentChoice.Own(retired), new(PaymentMethod.Other), new((PaymentMethod)42), new(PaymentMethod.Cash, retired)];
        foreach (var choice in notOurs)
        {
            var visit = await VisitAsync(AppointmentStatus.Arrived);
            (await Should.ThrowAsync<ClientAccountException>(() => AccountAsync(s => s.PayBillAsync(visit, choice, [], null)))).Refusal.ShouldBe(ClientAccountRefusal.MethodNotOffered, choice.ToString());
            (await Should.ThrowAsync<ClientAccountException>(() => AccountAsync(s => s.RecordDepositAsync(visit, 5m, choice, null)))).Refusal.ShouldBe(ClientAccountRefusal.MethodNotOffered);
            (await Should.ThrowAsync<CounterSaleException>(() => ScopedAsync<ICounterSaleService, CounterSaleReceipt>(s => s.SellAsync([(_shampoo, 1)], choice, null)))).Refusal.ShouldBe(CounterSaleRefusal.MethodNotOffered);
            (await Should.ThrowAsync<GiftVoucherException>(() => ScopedAsync<IGiftVoucherService, VoucherSummary>(s => s.SellAsync(30m, choice, null, null, null)))).Refusal.ShouldBe(GiftVoucherRefusal.MethodNotAllowed);
            (await MethodsAsync(s => s.ResolveAsync(choice))).ShouldBeNull();
        }

        (await EntriesAsync()).Count.ShouldBe(before);
        await InScopeAsync(async db => (await db.GiftVouchers.CountAsync()).ShouldBe(0));

        // At the desk: the page says so in words and the visit stays unpaid.
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        var unpaid = await VisitAsync(AppointmentStatus.Arrived);
        var bill = await GetAsync(desk, CheckoutPath(unpaid));
        bill.ShouldNotContain("Cheque");
        var refused = await PostAsync(desk, CheckoutPath(unpaid), bill, ("method", retired.ToString()));
        WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()).ShouldContain("The salon does not take money that way any more.");
        var nonsense = await PostAsync(desk, CheckoutPath(unpaid), bill, ("method", "PayPal"));
        nonsense.StatusCode.ShouldNotBe(HttpStatusCode.Redirect);
        (await EntriesAsync()).Count.ShouldBe(before);
    }

    [Fact]
    public async Task Renaming_keeps_the_name_on_the_lines_already_recorded_and_the_totals_use_the_latest()
    {
        var id = await AddAsync("Paypal");
        await AccountAsync(async s => await s.PayBillAsync(await VisitAsync(AppointmentStatus.Arrived), PaymentChoice.Own(id), [], null));

        (await MethodsAsync(s => s.RenameAsync(id, " PayPal "))).ShouldBe(new PaymentMethodResult(PaymentMethodOutcome.Saved, id, "PayPal"));
        await AccountAsync(async s => await s.PayBillAsync(await VisitAsync(AppointmentStatus.Arrived), PaymentChoice.Own(id), [], null));

        (await EntriesAsync()).OrderBy(e => e.At).Select(e => e.MethodName).ShouldBe(["Paypal", "PayPal"]);
        var till = await ScopedAsync<ICashUpService, CashDay>(s => s.GetDayAsync(null));
        till.Totals.Select(t => (t.MethodName, t.Amount.Amount)).ShouldBe([("PayPal", 40m)]);
        till.Movements.Select(m => m.MethodName).Order(StringComparer.Ordinal).ShouldBe(["PayPal", "Paypal"]);
    }

    [Fact]
    public async Task A_used_method_is_taken_out_of_use_an_unused_one_is_removed_and_the_same_name_brings_it_back()
    {
        var used = await AddAsync("PayPal");
        var unused = await AddAsync("Cheque");
        await AccountAsync(async s => await s.PayBillAsync(await VisitAsync(AppointmentStatus.Arrived), PaymentChoice.Own(used), [], null));

        (await MethodsAsync(s => s.RetireAsync(used))).Outcome.ShouldBe(PaymentMethodOutcome.Saved);
        (await MethodsAsync(s => s.RetireAsync(unused))).Outcome.ShouldBe(PaymentMethodOutcome.Saved);

        (await MethodsAsync(s => s.ListAsync())).Own.ShouldHaveSingleItem().ShouldBe(new OwnPaymentMethod(used, "PayPal", false, true));
        (await MethodsAsync(s => s.OptionsAsync())).Count.ShouldBe(4);
        (await ScopedAsync<ICashUpService, CashDay>(s => s.GetDayAsync(null))).Totals.ShouldHaveSingleItem().MethodName.ShouldBe("PayPal", "its money stays in the till");

        // Typed again, in other capitals: the same method comes back, so its history stays one line.
        var again = await MethodsAsync(s => s.AddAsync("paypal"));
        (again.Outcome, again.Id, again.Name).ShouldBe((PaymentMethodOutcome.Saved, used, "paypal"));
        (await MethodsAsync(s => s.OptionsAsync())).Count.ShouldBe(5);

        (await MethodsAsync(s => s.RetireAsync(used))).Outcome.ShouldBe(PaymentMethodOutcome.Saved);
        (await MethodsAsync(s => s.RestoreAsync(used))).Outcome.ShouldBe(PaymentMethodOutcome.Saved);
        (await MethodsAsync(s => s.ResolveAsync(PaymentChoice.Own(used)))).ShouldNotBeNull().MethodName.ShouldBe("paypal");
        (await MethodsAsync(s => s.RetireAsync(Guid.NewGuid()))).Outcome.ShouldBe(PaymentMethodOutcome.NotFound);
        (await MethodsAsync(s => s.RestoreAsync(unused))).Outcome.ShouldBe(PaymentMethodOutcome.NotFound);
    }

    [Fact]
    public async Task A_name_must_be_given_short_new_and_not_a_built_in_one()
    {
        var payPal = await AddAsync("PayPal");
        var cheque = await AddAsync("Cheque");

        (await MethodsAsync(s => s.AddAsync("   "))).Outcome.ShouldBe(PaymentMethodOutcome.NameInvalid);
        (await MethodsAsync(s => s.AddAsync(null))).Outcome.ShouldBe(PaymentMethodOutcome.NameInvalid);
        (await MethodsAsync(s => s.AddAsync(new string('a', SalonPaymentMethod.NameMaxLength + 1)))).Outcome.ShouldBe(PaymentMethodOutcome.NameInvalid);
        (await MethodsAsync(s => s.AddAsync("PAYPAL"))).Outcome.ShouldBe(PaymentMethodOutcome.NameTaken);
        // A built-in method's name, in either language, would be two lines the desk cannot tell apart.
        foreach (var builtIn in new[] { "Cash", "cash", "Bank transfer", "Card at the salon's terminal", "Voucher", "Other", "Gotówka", "przelew", "Bon", "Inny" })
        {
            (await MethodsAsync(s => s.AddAsync(builtIn))).Outcome.ShouldBe(PaymentMethodOutcome.NameTaken, builtIn);
        }

        (await MethodsAsync(s => s.RenameAsync(cheque, "paypal"))).Outcome.ShouldBe(PaymentMethodOutcome.NameTaken);
        (await MethodsAsync(s => s.RenameAsync(cheque, "Cash"))).Outcome.ShouldBe(PaymentMethodOutcome.NameTaken);
        (await MethodsAsync(s => s.RenameAsync(cheque, " "))).Outcome.ShouldBe(PaymentMethodOutcome.NameInvalid);
        (await MethodsAsync(s => s.RenameAsync(Guid.NewGuid(), "Klarna"))).Outcome.ShouldBe(PaymentMethodOutcome.NotFound);
        (await MethodsAsync(s => s.RenameAsync(payPal, "PAYPAL"))).Outcome.ShouldBe(PaymentMethodOutcome.Saved, "a method may change its own capitals");

        // Another salon may call its method the same.
        (await MethodsAsync(s => s.AddAsync("PayPal"), _polish.Id)).Outcome.ShouldBe(PaymentMethodOutcome.Saved);
    }

    [Fact]
    public async Task A_salon_has_at_most_twenty_methods_of_its_own_in_use()
    {
        var ids = new List<Guid>();
        for (var i = 1; i <= SalonPaymentMethodRules.MaxMethods; i++)
        {
            ids.Add(await AddAsync($"Method {i}"));
        }

        (await MethodsAsync(s => s.AddAsync("One too many"))).Outcome.ShouldBe(PaymentMethodOutcome.TooMany);

        // Taking one out of use makes room.
        (await MethodsAsync(s => s.RetireAsync(ids[0]))).Outcome.ShouldBe(PaymentMethodOutcome.Saved);
        (await MethodsAsync(s => s.AddAsync("One too many"))).Outcome.ShouldBe(PaymentMethodOutcome.Saved);
        (await MethodsAsync(s => s.OptionsAsync())).Count.ShouldBe(4 + SalonPaymentMethodRules.MaxMethods);
    }

    [Fact]
    public async Task The_owner_switches_off_built_in_methods_but_never_cash()
    {
        var payPal = await AddAsync("PayPal");
        await AccountAsync(async s => await s.PayBillAsync(await VisitAsync(AppointmentStatus.Arrived), PaymentMethod.BankTransfer, [], null));

        await MethodsAsync(async s =>
        {
            await s.SaveBuiltInAsync(new PaymentMethodSettings { CardAtSalonTerminal = false, BankTransfer = false, Voucher = false });
            return true;
        });

        (await MethodsAsync(s => s.OptionsAsync())).Select(o => o.Choice).ShouldBe([PaymentMethod.Cash, PaymentChoice.Own(payPal)]);
        (await MethodsAsync(s => s.OptionsAsync(), _polish.Id)).Count.ShouldBe(4, "another salon keeps its own switches");
        (await ScopedAsync<ICashUpService, CashDay>(s => s.GetDayAsync(null))).Totals.ShouldHaveSingleItem().Method.ShouldBe(PaymentMethod.BankTransfer, "what was recorded stays");

        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        var bill = await GetAsync(desk, CheckoutPath(await VisitAsync(AppointmentStatus.Arrived)));
        bill.ShouldContain("<option value=\"Cash\">Cash</option>");
        bill.ShouldContain(">PayPal</option>");
        bill.ShouldNotContain("<option value=\"BankTransfer\"");
        bill.ShouldNotContain("<option value=\"CardAtSalonTerminal\"");
    }

    [Fact]
    public async Task The_database_refuses_a_line_that_names_its_method_wrongly()
    {
        var payPal = await AddAsync("PayPal");
        var visit = await VisitAsync(AppointmentStatus.Arrived);

        ClientAccountEntry Line(PaymentMethod? method, Guid? customId, string? name) => new()
        {
            TenantId = _studio.Id,
            ClientId = _clientId,
            AppointmentId = visit,
            Kind = ClientAccountEntryKind.VisitPayment,
            Amount = 20m,
            Currency = "GBP",
            Method = method,
            CustomMethodId = customId,
            MethodName = name,
            At = Now,
        };

        (PaymentMethod? Method, Guid? Id, string? Name)[] wrong =
        [
            (PaymentMethod.Other, null, null),
            (PaymentMethod.Other, payPal, null),
            (PaymentMethod.Other, null, "PayPal"),
            (PaymentMethod.Cash, payPal, "PayPal"),
            (PaymentMethod.Cash, null, "PayPal"),
            (null, payPal, "PayPal"),
            (PaymentMethod.Other, Guid.NewGuid(), "Nobody's"),
        ];
        foreach (var (method, id, name) in wrong)
        {
            await using var scope = factory.CreateTenantScope(_studio.Id);
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            db.ClientAccountEntries.Add(Line(method, id, name));
            await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(), $"{method} {id} {name}");
        }

        (await EntriesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_settings_page_is_the_owners_and_another_salon_never_sees_the_methods()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var page = await GetAsync(owner, SettingsPath);
        page.ShouldContain("data-testid=\"no-own-methods\"");
        page.ShouldContain("data-testid=\"settings-nav-payment-methods\"");

        var added = await PostAsync(owner, SettingsPath + "/add", page, ("name", "PayPal"));
        added.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        page = await GetAsync(owner, SettingsPath);
        page.ShouldContain("PayPal is added. The desk can choose it now.");
        var id = (await MethodsAsync(s => s.ListAsync())).Own.ShouldHaveSingleItem().Id;

        (await PostAsync(owner, SettingsPath + "/add", page, ("name", "paypal"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        page = await GetAsync(owner, SettingsPath);
        page.ShouldContain("There is already a method with that name.");
        (await PostAsync(owner, SettingsPath + "/add", page, ("name", " "))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await GetAsync(owner, SettingsPath)).ShouldContain("Give the method a name of at most 40 characters.");

        (await PostAsync(owner, $"{SettingsPath}/{id}/rename", page, ("name", "PayPal Here"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await GetAsync(owner, SettingsPath)).ShouldContain("The method is now called PayPal Here.");
        (await PostAsync(owner, SettingsPath + "/built-in", page, ("CardAtSalonTerminal", "true"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var builtIn = (await MethodsAsync(s => s.ListAsync())).BuiltIn;
        (builtIn.CardAtSalonTerminal, builtIn.BankTransfer, builtIn.Voucher).ShouldBe((true, false, false));

        (await PostAsync(owner, $"{SettingsPath}/{id}/retire", page)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await MethodsAsync(s => s.ListAsync())).Own.ShouldBeEmpty("nothing was recorded with it, so it is removed");
        (await PostAsync(owner, $"{SettingsPath}/{id}/retire", page)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(owner, $"{SettingsPath}/{Guid.NewGuid()}/rename", page, ("name", "X"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // No token, no change.
        var noToken = await owner.PostAsync(new Uri(SettingsPath + "/add", UriKind.Relative), new FormUrlEncodedContent([new("name", "Klarna")]));
        noToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await MethodsAsync(s => s.ListAsync())).Own.ShouldBeEmpty();

        // A manager takes money but does not decide how the salon takes it.
        foreach (var role in new[] { Role.Manager, Role.Receptionist, Role.Staff })
        {
            using var other = await factory.SignedInClientAsync(HostA, _studio.Id, role);
            var denied = await other.GetAsync(new Uri(SettingsPath, UriKind.Relative));
            denied.StatusCode.ShouldBe(HttpStatusCode.Redirect, role.ToString());
            denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        }

        // Another salon: its page has none of ours, and our id is nothing there.
        var ours = await AddAsync("Klarna");
        using var theirOwner = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        var theirs = await GetAsync(theirOwner, SettingsPath);
        theirs.ShouldNotContain("Klarna");
        theirs.ShouldContain("Sposoby płatności");
        (await PostAsync(theirOwner, $"{SettingsPath}/{ours}/retire", theirs)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await MethodsAsync(s => s.ResolveAsync(PaymentChoice.Own(ours)), _polish.Id)).ShouldBeNull();
        (await MethodsAsync(s => s.ListAsync())).Own.ShouldHaveSingleItem().Name.ShouldBe("Klarna");
    }

    [Fact]
    public async Task Money_is_offered_back_the_way_it_came_while_the_salon_still_takes_it_that_way()
    {
        var payPal = await AddAsync("PayPal");
        await ScopedAsync<ICounterSaleService, CounterSaleReceipt>(s => s.SellAsync([(_shampoo, 1)], PaymentChoice.Own(payPal), null));
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        var sales = await GetAsync(desk, "/admin/payments/sales");
        sales.ShouldContain($"<option value=\"{payPal}\" selected=\"selected\">PayPal</option>");
        sales.ShouldContain("PayPal");

        // Once the salon stops taking PayPal the desk picks another way; the sale still says how it was paid.
        await MethodsAsync(s => s.RetireAsync(payPal));
        var after = await GetAsync(desk, "/admin/payments/sales");
        after.ShouldNotContain($"<option value=\"{payPal}\"");
        after.ShouldNotContain("selected=\"selected\"");
        after.ShouldContain("PayPal");
    }

    [Fact]
    public async Task A_name_with_markup_in_it_is_text_on_every_page_never_markup()
    {
        const string name = "<b>Pay</b> & \"Go\"";
        var id = await AddAsync(name);
        var visit = await VisitAsync(AppointmentStatus.Arrived);
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        // The raw page, not decoded: the name must arrive escaped.
        async Task<string> RawAsync(string path)
        {
            using var response = await owner.GetAsync(new Uri(path, UriKind.Relative));
            response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
            return await response.Content.ReadAsStringAsync();
        }

        var bill = await RawAsync(CheckoutPath(visit));
        bill.ShouldNotContain("<b>Pay</b>");
        bill.ShouldContain("&lt;b&gt;Pay&lt;/b&gt;");
        WebUtility.HtmlDecode(bill).ShouldContain($"<option value=\"{id}\">{name}</option>");

        await AccountAsync(s => s.PayBillAsync(visit, PaymentChoice.Own(id), [], null));
        foreach (var path in new[] { CheckoutPath(visit), TillPath, $"/admin/reports?from={Today}&to={Today}", SettingsPath })
        {
            var page = await RawAsync(path);
            page.ShouldNotContain("<b>Pay</b>", customMessage: path);
            WebUtility.HtmlDecode(page).ShouldContain(name, customMessage: path);
        }
    }

    private static string CheckoutPath(Guid appointmentId) => $"/admin/payments/checkout/{appointmentId}";

    private async Task<Guid> AddAsync(string name)
    {
        var added = await MethodsAsync(s => s.AddAsync(name));
        added.Outcome.ShouldBe(PaymentMethodOutcome.Saved, name);
        return added.Id!.Value;
    }

    /// <summary>A visit today, each a little earlier than the last so no two overlap; or so far from now.</summary>
    private Task<Guid> VisitAsync(AppointmentStatus status, Duration? fromNow = null)
        => VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, Now + (fromNow ?? Duration.Zero) - Duration.FromMinutes(35 * ++_visits), status);

    private async Task SavePolicyAsync(decimal noShowPercent)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await scope.ServiceProvider.GetRequiredService<ISettingService>().SaveAsync(new CancellationPolicySettings { NoShowFeePercent = noShowPercent });
    }

    private async Task<Guid> ProductAsync(string name, decimal price)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var product = new Product { Name = name, RetailPrice = price, Currency = "GBP" };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IStockService>().RecordAsync(product.Id, StockMovementKind.Counted, 20, null, null)).Outcome.ShouldBe(StockOutcome.Recorded);
        return product.Id;
    }

    private Task<T> MethodsAsync<T>(Func<IPaymentMethodService, Task<T>> act, Guid? tenantId = null) => ScopedAsync(act, tenantId);

    private Task<T> AccountAsync<T>(Func<IClientAccountService, Task<T>> act) => ScopedAsync(act);

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

    private async Task<List<ClientAccountEntry>> EntriesAsync()
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().ClientAccountEntries.AsNoTracking().OrderBy(e => e.At).ThenBy(e => e.Id).ToListAsync();
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string pageWithToken, params (string Key, string Value)[] fields)
        => client.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent(
            [new KeyValuePair<string, string>("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(pageWithToken)), .. fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))]));

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}