using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Core.Products;
using Salon.Core.Reports;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Payments;
using Salon.Services.Products;
using Salon.Services.Reports;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Reports;

/// <summary>
/// Commission (ADR 0059): the owner sets a services and a products percent per person; the report shows, per person,
/// the products sold on their visits, the commission due and what is left. Two cuts at £20 and £59 of products on
/// their bills: 40% of £40 and 10% of £59 is £21.90, which leaves £77.10. Only the owner sets the rates.
/// </summary>
[Collection(SharedDatabase.Name)]
public class StaffCommissionTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string CommissionPath = "/admin/staff/commission";

    private static readonly Instant Now = SystemClock.Instance.GetCurrentInstant();

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _staffId;
    private Guid _serviceId;
    private Guid _clientId;
    private Guid _shampoo;
    private Guid _conditioner;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        (_staffId, _serviceId) = await VisitSeed.SalonAsync(factory, _studio.Id);
        _clientId = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123");
        _shampoo = await ProductAsync("Shampoo 250 ml", 12.50m);
        _conditioner = await ProductAsync("Conditioner 250 ml", 9m);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Without_rates_the_report_shows_the_products_and_no_commission()
    {
        await SellTwoBillsAsync();

        var line = (await ReportAsync()).Staff.Single(s => s.Visits > 0);

        (line.Visits, line.Value.Amount, line.Retail?.Amount).ShouldBe((2, 40m, 59m));
        (line.HasCommission, line.Commission, line.AfterCommission).ShouldBe((false, null, null));
    }

    [Fact]
    public async Task The_rates_earn_commission_on_the_work_and_the_products_and_the_rest_is_what_is_left()
    {
        await SellTwoBillsAsync();
        await RatesAsync(_staffId, 40m, 10m);

        var line = (await ReportAsync()).Staff.Single(s => s.Visits > 0);

        (line.ServiceRate, line.ProductRate).ShouldBe((40m, 10m));
        line.Commission.ShouldNotBeNull().Amount.ShouldBe(21.90m);
        line.AfterCommission.ShouldNotBeNull().Amount.ShouldBe(77.10m);
    }

    [Fact]
    public async Task A_product_brought_back_earns_no_commission()
    {
        var (visit, shampooLine) = await SellTwoBillsAsync();
        await RatesAsync(_staffId, null, 10m);
        await ScopedAsync<IClientAccountService, VisitBill>(_studio.Id, s => s.ReturnProductAsync(visit, shampooLine, 1, PaymentMethod.Cash, true, null));

        var line = (await ReportAsync()).Staff.Single(s => s.Visits > 0);

        line.Retail.ShouldNotBeNull().Amount.ShouldBe(46.50m);
        line.Commission.ShouldNotBeNull().Amount.ShouldBe(4.65m, "10% of the products kept, nothing on the services without a rate");
        line.AfterCommission.ShouldNotBeNull().Amount.ShouldBe(40m + 46.50m - 4.65m);
    }

    [Fact]
    public async Task A_visit_keeps_the_rate_of_the_day_it_was_completed_and_a_raise_does_not_rewrite_it()
    {
        await RatesAsync(_staffId, 40m, null);
        var first = await VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, Now - Duration.FromMinutes(90), AppointmentStatus.Arrived);
        await CompleteAsync(first);

        // The owner gives a raise: the visit already done stays at 40%.
        await RatesAsync(_staffId, 50m, null);
        var afterRaise = (await ReportAsync()).Staff.Single(s => s.Visits > 0);
        (afterRaise.ServiceRate, afterRaise.RatesVaried).ShouldBe((40m, false));
        afterRaise.Commission.ShouldNotBeNull().Amount.ShouldBe(8m);

        // A visit done after the raise earns 50%; the period now has two rates.
        var second = await VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, Now - Duration.FromMinutes(45), AppointmentStatus.Arrived);
        await CompleteAsync(second);
        var both = (await ReportAsync()).Staff.Single(s => s.Visits > 0);
        (both.ServiceRate, both.RatesVaried).ShouldBe((null, true));
        both.Commission.ShouldNotBeNull().Amount.ShouldBe(18m);

        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        (await GetAsync(owner, "/admin/reports")).ShouldContain("Commission (the rate changed in this period)");

        // Taking someone off commission does not take away what their past visits earned.
        await RatesAsync(_staffId, null, null);
        (await ReportAsync()).Staff.Single(s => s.Visits > 0).Commission.ShouldNotBeNull().Amount.ShouldBe(18m);
    }

    [Fact]
    public async Task A_visit_completed_while_the_person_had_no_rate_earns_nothing_after_they_get_one()
    {
        var visit = await VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, Now - Duration.FromMinutes(90), AppointmentStatus.Arrived);
        await CompleteAsync(visit);
        await RatesAsync(_staffId, 40m, null);

        var line = (await ReportAsync()).Staff.Single(s => s.Visits > 0);

        line.Commission.ShouldNotBeNull().Amount.ShouldBe(0m, "the person is on commission now, and that visit was done before");
        line.ServiceRate.ShouldBe(40m, "no visit of the period earned at a rate, so today's is named");
    }

    private async Task CompleteAsync(Guid appointmentId)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await scope.ServiceProvider.GetRequiredService<Salon.Services.Appointments.IBookingService>().CompleteAsync(appointmentId);
    }

    [Fact]
    public async Task A_person_with_a_rate_and_no_work_earned_nothing_and_is_not_listed_without_hours()
    {
        await RatesAsync(_staffId, 40m, 10m);

        var report = await ReportAsync();

        report.Staff.Where(s => s.Visits > 0).ShouldBeEmpty();
        report.Staff.Where(s => s.HasCommission).ShouldAllBe(s => s.Commission!.Value.Amount == 0m);
    }

    [Fact]
    public async Task The_owner_sets_the_rates_and_the_report_shows_them()
    {
        await SellTwoBillsAsync();
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var page = await GetAsync(owner, CommissionPath);
        page.ShouldContain("data-testid=\"commission-row\"");
        (await GetAsync(owner, "/admin/staff")).ShouldContain($"href=\"{CommissionPath}\"");

        var saved = await PostAsync(owner, page, ("Rates[0].StaffId", _staffId.ToString()), ("Rates[0].Services", "40"), ("Rates[0].Products", "10"));
        saved.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await GetAsync(owner, CommissionPath)).ShouldContain("The rates are saved.");
        (await StaffAsync()).ShouldBe((40m, 10m));

        var report = await GetAsync(owner, "/admin/reports");
        report.ShouldContain("Commission (40% services, 10% products)");
        report.ShouldContain("Left after commission");
        report.ShouldContain("£77.10");

        // An empty box takes the rate away.
        var cleared = await PostAsync(owner, await GetAsync(owner, CommissionPath), ("Rates[0].StaffId", _staffId.ToString()), ("Rates[0].Services", ""), ("Rates[0].Products", "10"));
        cleared.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await StaffAsync()).ShouldBe((null, 10m));
    }

    [Theory]
    [InlineData("101")]
    [InlineData("-5")]
    [InlineData("12.345")]
    [InlineData("forty")]
    public async Task A_rate_that_is_not_a_percent_is_refused_and_nothing_is_saved(string typed)
    {
        await RatesAsync(_staffId, 30m, null);
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var refused = await PostAsync(owner, await GetAsync(owner, CommissionPath), ("Rates[0].StaffId", _staffId.ToString()), ("Rates[0].Services", typed), ("Rates[0].Products", "10"));

        refused.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()).ShouldContain("Enter a percent from 0 to 100, with at most two decimals, or leave it empty.");
        (await StaffAsync()).ShouldBe((30m, null));
    }

    [Fact]
    public async Task Only_the_owner_reaches_the_rates_and_another_salons_staff_cannot_be_changed()
    {
        using var manager = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Manager);
        var denied = await manager.GetAsync(new Uri(CommissionPath, UriKind.Relative));
        denied.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        (await GetAsync(manager, "/admin/staff")).ShouldNotContain($"href=\"{CommissionPath}\"");

        var (theirStaff, _) = await VisitSeed.SalonAsync(factory, _polish.Id);
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var page = await GetAsync(owner, CommissionPath);
        var posted = await PostAsync(owner, page, ("Rates[0].StaffId", theirStaff.ToString()), ("Rates[0].Services", "90"), ("Rates[0].Products", "90"));
        posted.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        await using var scope = factory.CreateTenantScope(_polish.Id);
        var theirs = await scope.ServiceProvider.GetRequiredService<SalonDbContext>().StaffMembers.AsNoTracking().SingleAsync(s => s.Id == theirStaff);
        (theirs.ServiceCommissionPercent, theirs.ProductCommissionPercent).ShouldBe((null, null));

        var noToken = await owner.PostAsync(new Uri(CommissionPath, UriKind.Relative), new FormUrlEncodedContent([new("Rates[0].StaffId", _staffId.ToString()), new("Rates[0].Services", "50")]));
        noToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await StaffAsync()).ShouldBe((null, null));
    }

    [Fact]
    public async Task The_database_refuses_a_rate_above_100()
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var person = await db.StaffMembers.SingleAsync(s => s.Id == _staffId);
        person.ServiceCommissionPercent = 150m;

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private async Task<(Guid Visit, Guid ShampooLine)> SellTwoBillsAsync()
    {
        var first = await VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, Now - Duration.FromMinutes(60), AppointmentStatus.Arrived);
        await AccountAsync(s => s.AddProductAsync(first, _shampoo, 3, null));
        await AccountAsync(s => s.AddProductAsync(first, _conditioner, 1, null));
        await AccountAsync(s => s.PayBillAsync(first, PaymentMethod.CardAtSalonTerminal, [], null));
        var second = await VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, Now - Duration.FromMinutes(30), AppointmentStatus.Arrived);
        await AccountAsync(s => s.AddProductAsync(second, _shampoo, 1, null));
        await AccountAsync(s => s.PayBillAsync(second, PaymentMethod.Cash, [], null));

        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        // The desk ends both visits; only completed visits count as work done (ADR 0026).
        await db.Appointments.Where(a => a.Id == first || a.Id == second).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AppointmentStatus.Completed));
        var line = await db.VisitProductLines.AsNoTracking()
            .SingleAsync(l => l.AppointmentId == first && l.ProductId == _shampoo);
        return (first, line.Id);
    }

    private async Task RatesAsync(Guid staffId, decimal? services, decimal? products)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await scope.ServiceProvider.GetRequiredService<SalonDbContext>().StaffMembers.Where(s => s.Id == staffId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ServiceCommissionPercent, services).SetProperty(x => x.ProductCommissionPercent, products));
    }

    private async Task<(decimal? Services, decimal? Products)> StaffAsync()
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var person = await scope.ServiceProvider.GetRequiredService<SalonDbContext>().StaffMembers.AsNoTracking().SingleAsync(s => s.Id == _staffId);
        return (person.ServiceCommissionPercent, person.ProductCommissionPercent);
    }

    private async Task<Guid> ProductAsync(string name, decimal price)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var product = new Product { Name = name, RetailPrice = price, Currency = "GBP" };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IStockService>().RecordAsync(product.Id, StockMovementKind.Counted, 8, null, null)).Outcome.ShouldBe(StockOutcome.Recorded);
        return product.Id;
    }

    private Task<SalonReport> ReportAsync()
        => ScopedAsync<IReportService, SalonReport>(_studio.Id, s => s.GetAsync(ReportPreset.ThisMonth, null, null));

    private Task<T> AccountAsync<T>(Func<IClientAccountService, Task<T>> act) => ScopedAsync(_studio.Id, act);

    private async Task<T> ScopedAsync<TService, T>(Guid tenantId, Func<TService, Task<T>> act)
        where TService : notnull
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await act(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string pageWithToken, params (string Key, string Value)[] fields)
        => client.PostAsync(new Uri(CommissionPath, UriKind.Relative), new FormUrlEncodedContent(
            [new KeyValuePair<string, string>("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(pageWithToken)), .. fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))]));

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}