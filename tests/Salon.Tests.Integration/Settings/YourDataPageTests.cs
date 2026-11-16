using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Core.Products;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Settings;

/// <summary>
/// The "Your data" page (ADR 0051): one place where the owner takes away everything the salon has put in. It says how
/// much there is and links every file: the client list, the visits and the money of each year the salon has any, and
/// the stock list. Owner only, in the salon's language, nothing of another salon.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class YourDataPageTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string Path = "/admin/settings/your-data";
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly Instant Now = SystemClock.Instance.GetCurrentInstant();
    private static readonly int ThisYear = Now.InZone(London).Year;

    private Tenant _studio = null!;
    private Tenant _polish = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_owner_sees_how_much_there_is_and_a_file_for_every_year_since_the_first_visit()
    {
        var (staff, service) = await VisitSeed.SalonAsync(factory, _studio.Id);
        var olivia = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123");
        await VisitSeed.ClientAsync(factory, _studio.Id, "Noah", "Adams");
        var twoYearsAgo = London.AtStrictly(new LocalDateTime(ThisYear - 2, 6, 15, 10, 0)).ToInstant();
        await VisitSeed.AppointmentAsync(factory, _studio.Id, olivia, staff, service, twoYearsAgo, AppointmentStatus.Completed);
        await VisitSeed.AppointmentAsync(factory, _studio.Id, olivia, staff, service, Now - Duration.FromDays(1), AppointmentStatus.Completed);
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            db.Products.Add(new Product { Name = "Shampoo 250 ml", Currency = "GBP" });
            db.ClientAccountEntries.Add(new ClientAccountEntry { ClientId = olivia, Kind = ClientAccountEntryKind.VisitPayment, Amount = 20m, Currency = "GBP", Method = PaymentMethod.Cash, At = Now });
            db.ClientAccountEntries.Add(new ClientAccountEntry { ClientId = olivia, Kind = ClientAccountEntryKind.NoShowFee, Amount = 5m, Currency = "GBP", At = Now });
            await db.SaveChangesAsync();
        }

        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var html = await GetAsync(owner, Path);

        html.ShouldContain("Everything this salon has put in belongs to the salon.");
        html.ShouldContain("data-testid=\"your-clients-count\">2 client cards.<");
        html.ShouldContain("Visits: 2. Lines that moved money: 1.");
        html.ShouldContain("data-testid=\"your-products-count\">1 product.<");
        html.ShouldContain("href=\"/admin/clients/export.csv\"");
        html.ShouldContain("href=\"/admin/products/stock.csv\"");
        Years().Matches(html).Select(m => int.Parse(m.Groups["year"].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe([ThisYear, ThisYear - 1, ThisYear - 2], "newest first, back to the first visit");
        html.ShouldContain($"href=\"/admin/reports/visits.csv?from={ThisYear - 2}-01-01&to={ThisYear - 2}-12-31\"");
        html.ShouldContain($"href=\"/admin/reports/money.csv?from={ThisYear}-01-01&to={ThisYear}-12-31\"");
        html.ShouldContain("Health notes are not in any file.");
        html.ShouldContain("href=\"/admin/settings/your-data\" data-testid=\"settings-nav-your-data\" aria-current=\"page\"");

        // The links lead to files the owner may open.
        (await owner.GetAsync(new Uri($"/admin/reports/visits.csv?from={ThisYear - 2}-01-01&to={ThisYear - 2}-12-31", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.GetAsync(new Uri("/admin/clients/export.csv", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_new_salon_has_this_year_only_and_a_Polish_salon_reads_the_page_in_Polish_without_the_other_salons_numbers()
    {
        await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123");
        using var owner = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);

        var html = await GetAsync(owner, Path);

        html.ShouldContain("Twoje dane");
        html.ShouldContain("Karty klientów: 0.");
        html.ShouldContain("Wizyty: 0. Zapisy wpłat i wypłat: 0.");
        Years().Matches(html).Select(m => m.Groups["year"].Value).ShouldBe([ThisYear.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
    }

    [Fact]
    public async Task Only_the_owner_gets_in()
    {
        using var manager = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Manager);
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        using var anonymous = factory.ClientFor(HostA);

        foreach (var refused in new[] { manager, desk })
        {
            var denied = await refused.GetAsync(new Uri(Path, UriKind.Relative));
            denied.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        }

        (await anonymous.GetAsync(new Uri(Path, UriKind.Relative))).Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");
        (await GetAsync(manager, "/admin/settings/messages")).ShouldNotContain("settings-nav-your-data");
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    [GeneratedRegex("data-testid=\"year\" data-year=\"(?<year>\\d{4})\"")]
    private static partial Regex Years();
}