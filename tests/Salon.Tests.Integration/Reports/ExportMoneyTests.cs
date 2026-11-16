using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Appointments;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Payments;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Reports;

/// <summary>
/// The money lines of a period as a file for a spreadsheet (ADR 0042): one row per line that moved money, oldest
/// first, money out as a negative number, written in the reader's language with their separator and decimal sign,
/// text that looks like a formula made harmless, only the asked period, only this salon, only for people who may see
/// the reports.
/// </summary>
[Collection(SharedDatabase.Name)]
public class ExportMoneyTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string Header = "Date,Time,Client,What it was for,Service,How it was paid,Amount,Currency,Recorded by,Reference";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly Instant Now = SystemClock.Instance.GetCurrentInstant();
    private static readonly string Today = LocalDatePattern.Iso.Format(Now.InZone(London).Date);

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
    public async Task The_owner_downloads_every_line_that_moved_money_oldest_first_with_money_out_as_a_negative_number()
    {
        var upcoming = await VisitAsync(_studio.Id, _clientId, _staffId, _serviceId, Now + Duration.FromDays(2), AppointmentStatus.Booked);
        await AccountAsync(_studio.Id, s => s.RecordDepositAsync(upcoming, 5m, PaymentMethod.Cash, null));
        var arrived = await VisitAsync(_studio.Id, _clientId, _staffId, _serviceId, Now - Duration.FromMinutes(40), AppointmentStatus.Arrived);
        await AccountAsync(_studio.Id, s => s.PayBillAsync(arrived, PaymentMethod.CardAtSalonTerminal, [], null));
        await AccountAsync(_studio.Id, s => s.ReturnDepositAsync(upcoming, PaymentMethod.Cash, null));
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var report = WebUtility.HtmlDecode(await (await owner.GetAsync(new Uri($"/admin/reports?from={Today}&to={Today}", UriKind.Relative))).Content.ReadAsStringAsync());
        report.ShouldContain($"href=\"/admin/reports/money.csv?from={Today}&to={Today}\"");
        report.ShouldContain("Download for a spreadsheet");

        var response = await owner.GetAsync(new Uri($"/admin/reports/money.csv?from={Today}&to={Today}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType.ShouldNotBeNull().ToString().ShouldBe("text/csv; charset=utf-8");
        response.Content.Headers.ContentDisposition.ShouldNotBeNull().FileName.ShouldBe($"money-{Today}-to-{Today}.csv");
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Take(3).ShouldBe(Encoding.UTF8.GetPreamble(), "Excel needs the byte order mark to read the letters");
        var lines = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n");
        lines[0].ShouldBe(Header);
        lines.Length.ShouldBe(5, "a header, three rows and the empty end after the last line break");
        lines[1].ShouldStartWith($"{Today},");
        lines[1].ShouldContain(",Olivia Brown,Deposit received,Cut,Cash,5.00,GBP,");
        lines[2].ShouldContain(",Olivia Brown,Visit paid,Cut,Card at the salon's terminal,20.00,GBP,");
        lines[3].ShouldContain(",Olivia Brown,Deposit returned,Cut,Cash,-5.00,GBP,");
        lines[3].ShouldContain("manual:deposit-return:");
        lines[4].ShouldBeEmpty();
    }

    [Fact]
    public async Task Only_the_asked_days_are_in_the_file_and_dates_that_cannot_be_read_give_this_month()
    {
        var visit = await VisitAsync(_studio.Id, _clientId, _staffId, _serviceId, Now - Duration.FromMinutes(40), AppointmentStatus.Arrived);
        await AccountAsync(_studio.Id, s => s.PayBillAsync(visit, PaymentMethod.Cash, [], null));
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            // A fee line moved no money; a payment of 45 days ago is outside any "today".
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            db.ClientAccountEntries.Add(new ClientAccountEntry { ClientId = _clientId, Kind = ClientAccountEntryKind.FeePayment, Amount = 9m, Currency = "GBP", Method = PaymentMethod.Cash, At = Now - Duration.FromDays(45) });
            db.ClientAccountEntries.Add(new ClientAccountEntry { ClientId = _clientId, Kind = ClientAccountEntryKind.NoShowFee, Amount = 10m, Currency = "GBP", At = Now });
            await db.SaveChangesAsync();
        }

        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var today = await RowsAsync(owner, $"/admin/reports/money.csv?from={Today}&to={Today}");
        today.ShouldHaveSingleItem().ShouldContain(",Visit paid,");

        var month = Now.InZone(London).Date;
        var fallback = await owner.GetAsync(new Uri("/admin/reports/money.csv?from=yesterday&to=", UriKind.Relative));
        fallback.Content.Headers.ContentDisposition.ShouldNotBeNull().FileName.ShouldNotBeNull().ShouldStartWith($"money-{month.Year:0000}-{month.Month:00}-01-to-");
    }

    [Fact]
    public async Task A_name_that_looks_like_a_formula_or_holds_a_comma_cannot_break_the_file()
    {
        var trick = await VisitSeed.ClientAsync(factory, _studio.Id, "=HYPERLINK(\"http://evil.example\")", "Brown, Olivia");
        var visit = await VisitAsync(_studio.Id, trick, _staffId, _serviceId, Now - Duration.FromMinutes(40), AppointmentStatus.Arrived);
        await AccountAsync(_studio.Id, s => s.PayBillAsync(visit, PaymentMethod.Cash, [], null));
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var row = (await RowsAsync(owner, $"/admin/reports/money.csv?from={Today}&to={Today}")).ShouldHaveSingleItem();

        // An apostrophe in front makes it text; the quotes keep the comma inside one cell.
        row.ShouldContain(",\"'=HYPERLINK(\"\"http://evil.example\"\") Brown, Olivia\",Visit paid,");
    }

    [Fact]
    public async Task A_Polish_salon_gets_its_own_words_a_semicolon_and_a_decimal_comma_and_only_its_own_lines()
    {
        var studioVisit = await VisitAsync(_studio.Id, _clientId, _staffId, _serviceId, Now - Duration.FromMinutes(40), AppointmentStatus.Arrived);
        await AccountAsync(_studio.Id, s => s.PayBillAsync(studioVisit, PaymentMethod.Cash, [], null));
        var (staff, service) = await VisitSeed.SalonAsync(factory, _polish.Id);
        var anna = await VisitSeed.ClientAsync(factory, _polish.Id, "Anna", "Nowak", "601 234 567");
        var visit = await VisitAsync(_polish.Id, anna, staff, service, Now - Duration.FromMinutes(40), AppointmentStatus.Arrived);
        await AccountAsync(_polish.Id, s => s.PayBillAsync(visit, PaymentMethod.Cash, [], null));
        using var owner = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        var warsawToday = LocalDatePattern.Iso.Format(Now.InZone(DateTimeZoneProviders.Tzdb["Europe/Warsaw"]).Date);

        var response = await owner.GetAsync(new Uri($"/admin/reports/money.csv?from={warsawToday}&to={warsawToday}", UriKind.Relative));
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var lines = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        lines[0].ShouldBe("Data;Godzina;Klient;Za co;Usługa;Forma płatności;Kwota;Waluta;Zapisał(a);Numer");
        lines.Length.ShouldBe(2);
        lines[1].ShouldContain(";Anna Nowak;Płatność za wizytę;Cut;Gotówka;20,00;");
        lines[1].ShouldNotContain("Olivia");
    }

    [Fact]
    public async Task The_file_is_for_people_who_may_see_the_reports()
    {
        const string path = "/admin/reports/money.csv";
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        using var manager = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Manager);
        using var anonymous = factory.ClientFor(HostA);
        using var platform = factory.CreateClient();
        platform.DefaultRequestHeaders.Host = "localhost";

        var denied = await desk.GetAsync(new Uri(path, UriKind.Relative));
        denied.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        var login = await anonymous.GetAsync(new Uri(path, UriKind.Relative));
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        login.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");
        (await platform.GetAsync(new Uri(path, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var empty = await manager.GetAsync(new Uri(path, UriKind.Relative));
        empty.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bytes = await empty.Content.ReadAsByteArrayAsync();
        Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).ShouldBe(Header + "\r\n", "a period without money is a file with its header only");

        var page = WebUtility.HtmlDecode(await (await manager.GetAsync(new Uri("/admin/reports", UriKind.Relative))).Content.ReadAsStringAsync());
        page.ShouldNotContain("data-testid=\"export-money\"", Case.Sensitive, "nothing to download while no money was recorded");
    }

    private static async Task<List<string>> RowsAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();
    }

    private Task<Guid> VisitAsync(Guid tenantId, Guid clientId, Guid staffId, Guid serviceId, Instant start, AppointmentStatus status)
        => VisitSeed.AppointmentAsync(factory, tenantId, clientId, staffId, serviceId, start, status);

    private async Task<T> AccountAsync<T>(Guid tenantId, Func<IClientAccountService, Task<T>> act)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await act(scope.ServiceProvider.GetRequiredService<IClientAccountService>());
    }
}