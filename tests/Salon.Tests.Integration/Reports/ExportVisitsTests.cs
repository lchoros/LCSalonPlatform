using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Appointments;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Reports;

/// <summary>
/// The visits of a period as a file for a spreadsheet (ADR 0047): one row per visit that starts in the period, oldest
/// first, whatever became of it, written in the reader's language with their separator and decimal sign, text that
/// looks like a formula made harmless, only the asked period, only this salon, only for people who may see the
/// reports. The file has as many rows as the report counts visits.
/// </summary>
[Collection(SharedDatabase.Name)]
public class ExportVisitsTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string Header = "Date,Time,Client,Service,With,Minutes,Status,Booked,Price,Currency";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly Instant Now = SystemClock.Instance.GetCurrentInstant();

    /// <summary>Noon yesterday in London: a whole day that is over, so three visits fit in it at any hour of a run.</summary>
    private static readonly LocalDate Yesterday = Now.InZone(London).Date.PlusDays(-1);
    private static readonly string Day = LocalDatePattern.Iso.Format(Yesterday);

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
    public async Task The_owner_downloads_every_visit_of_the_period_oldest_first_whatever_became_of_it()
    {
        var noah = await VisitSeed.ClientAsync(factory, _studio.Id, "Noah", "Green");
        await VisitAsync(_studio.Id, _clientId, At(14, 0), AppointmentStatus.NoShow);
        await VisitAsync(_studio.Id, noah, At(9, 30), AppointmentStatus.Completed, minutes: 45, price: 1234.5m);
        var online = await VisitAsync(_studio.Id, _clientId, At(11, 0), AppointmentStatus.CancelledByClient);
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            (await db.Appointments.SingleAsync(a => a.Id == online)).Source = AppointmentSource.Online;
            await db.SaveChangesAsync();
        }

        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var report = WebUtility.HtmlDecode(await (await owner.GetAsync(new Uri($"/admin/reports?from={Day}&to={Day}", UriKind.Relative))).Content.ReadAsStringAsync());
        report.ShouldContain($"href=\"/admin/reports/visits.csv?from={Day}&to={Day}\"");
        report.ShouldContain("Download the visits");
        report.ShouldContain("data-testid=\"visits-total\">3<");

        var response = await owner.GetAsync(new Uri($"/admin/reports/visits.csv?from={Day}&to={Day}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType.ShouldNotBeNull().ToString().ShouldBe("text/csv; charset=utf-8");
        response.Content.Headers.ContentDisposition.ShouldNotBeNull().FileName.ShouldBe($"visits-{Day}-to-{Day}.csv");
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Take(3).ShouldBe(Encoding.UTF8.GetPreamble(), "Excel needs the byte order mark to read the letters");
        var lines = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n");
        lines.Length.ShouldBe(5, "a header, three rows and the empty end after the last line break");
        lines[0].ShouldBe(Header);
        lines[1].ShouldBe($"{Day},09:30,Noah Green,Cut,Anna,45,Completed,At the salon,1234.50,GBP", "no thousands separator in a number");
        lines[2].ShouldBe($"{Day},11:00,Olivia Brown,Cut,Anna,30,Cancelled by client,Online,20.00,GBP");
        lines[3].ShouldBe($"{Day},14:00,Olivia Brown,Cut,Anna,30,No-show,At the salon,20.00,GBP");
        lines[4].ShouldBeEmpty();
        string.Join('\n', lines).ShouldNotContain("7700900123", Case.Sensitive, "no phone numbers in the file");
    }

    [Fact]
    public async Task Only_the_asked_days_are_in_the_file_and_dates_that_cannot_be_read_give_this_month()
    {
        await VisitAsync(_studio.Id, _clientId, At(10, 0), AppointmentStatus.Completed);
        await VisitAsync(_studio.Id, _clientId, At(10, 0) - Duration.FromDays(45), AppointmentStatus.Completed);
        await VisitAsync(_studio.Id, _clientId, At(10, 0) + Duration.FromDays(3), AppointmentStatus.Booked);
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var day = await RowsAsync(owner, $"/admin/reports/visits.csv?from={Day}&to={Day}");
        day.ShouldHaveSingleItem().ShouldStartWith($"{Day},10:00,");
        var ahead = LocalDatePattern.Iso.Format(Yesterday.PlusDays(3));
        (await RowsAsync(owner, $"/admin/reports/visits.csv?from={ahead}&to={ahead}")).ShouldHaveSingleItem().ShouldContain(",Booked,", Case.Sensitive, "a visit still to come is a visit of its day");

        var month = Now.InZone(London).Date;
        var fallback = await owner.GetAsync(new Uri("/admin/reports/visits.csv?from=yesterday&to=", UriKind.Relative));
        fallback.Content.Headers.ContentDisposition.ShouldNotBeNull().FileName.ShouldNotBeNull().ShouldStartWith($"visits-{month.Year:0000}-{month.Month:00}-01-to-");
    }

    [Fact]
    public async Task A_name_that_looks_like_a_formula_or_holds_a_comma_cannot_break_the_file()
    {
        var trick = await VisitSeed.ClientAsync(factory, _studio.Id, "=HYPERLINK(\"http://evil.example\")", "Brown, Olivia");
        await VisitAsync(_studio.Id, trick, At(10, 0), AppointmentStatus.Completed);
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var row = (await RowsAsync(owner, $"/admin/reports/visits.csv?from={Day}&to={Day}")).ShouldHaveSingleItem();

        // An apostrophe in front makes it text; the quotes keep the comma inside one cell.
        row.ShouldContain(",\"'=HYPERLINK(\"\"http://evil.example\"\") Brown, Olivia\",Cut,");
    }

    [Fact]
    public async Task A_Polish_salon_gets_its_own_words_a_semicolon_and_a_decimal_comma_and_only_its_own_visits()
    {
        await VisitAsync(_studio.Id, _clientId, At(10, 0), AppointmentStatus.Completed);
        var (staff, service) = await VisitSeed.SalonAsync(factory, _polish.Id);
        var anna = await VisitSeed.ClientAsync(factory, _polish.Id, "Anna", "Nowak", "601 234 567");
        var warsaw = DateTimeZoneProviders.Tzdb["Europe/Warsaw"];
        var warsawDay = Now.InZone(warsaw).Date.PlusDays(-1);
        var start = warsaw.AtStrictly(warsawDay + new LocalTime(12, 15)).ToInstant();
        await VisitSeed.AppointmentAsync(factory, _polish.Id, anna, staff, service, start, AppointmentStatus.Completed, price: 120.5m);
        using var owner = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        var iso = LocalDatePattern.Iso.Format(warsawDay);

        var response = await owner.GetAsync(new Uri($"/admin/reports/visits.csv?from={iso}&to={iso}", UriKind.Relative));
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var lines = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        lines[0].ShouldBe("Data;Godzina;Klient;Usługa;Pracownik;Minuty;Status;Rezerwacja;Cena;Waluta");
        lines.Length.ShouldBe(2);
        lines[1].ShouldBe($"{iso};12:15;Anna Nowak;Cut;Anna;30;Zakończona;W salonie;120,50;GBP");
    }

    [Fact]
    public async Task The_file_is_for_people_who_may_see_the_reports()
    {
        const string path = "/admin/reports/visits.csv";
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
        Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).ShouldBe(Header + "\r\n", "a period without visits is a file with its header only");

        var page = WebUtility.HtmlDecode(await (await manager.GetAsync(new Uri("/admin/reports", UriKind.Relative))).Content.ReadAsStringAsync());
        page.ShouldNotContain("data-testid=\"export-visits\"", Case.Sensitive, "nothing to download while the period has no visit");
    }

    /// <summary>A time of yesterday on the salon's clock.</summary>
    private static Instant At(int hour, int minute) => London.AtStrictly(Yesterday + new LocalTime(hour, minute)).ToInstant();

    private static async Task<List<string>> RowsAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();
    }

    private Task<Guid> VisitAsync(Guid tenantId, Guid clientId, Instant start, AppointmentStatus status, int minutes = 30, decimal price = 20m)
        => VisitSeed.AppointmentAsync(factory, tenantId, clientId, _staffId, _serviceId, start, status, minutes, price);
}