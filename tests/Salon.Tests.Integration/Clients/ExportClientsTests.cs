using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Appointments;
using Salon.Core.Clients;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Clients;

/// <summary>
/// The salon's client list as a file (ADR 0051): the salon owns its data, so the owner can take it away. Every card
/// with how to reach the client and how often they came, merged pointers left out, health notes never in it, written
/// in the reader's language, text that looks like a formula made harmless, only this salon, only for the owner. The
/// client import of another salon reads the file.
/// </summary>
[Collection(SharedDatabase.Name)]
public class ExportClientsTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string Path = "/admin/clients/export.csv";
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string HostC = "second-chair.localhost";
    private const string Header = "First name,Last name,Phone,Email,Notes,Marketing consent,Consent given on,Client since,Completed visits,Last visit,Next visit,Active";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly Instant Now = SystemClock.Instance.GetCurrentInstant();
    private static readonly LocalDate TodayDate = Now.InZone(London).Date;
    private static readonly string Today = LocalDatePattern.Iso.Format(TodayDate);

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _staffId;
    private Guid _serviceId;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        (_staffId, _serviceId) = await VisitSeed.SalonAsync(factory, _studio.Id);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_owner_downloads_every_client_with_how_to_reach_them_and_how_often_they_came()
    {
        var olivia = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123", "olivia@example.test", "Likes tea");
        var noah = await VisitSeed.ClientAsync(factory, _studio.Id, "Noah", "Adams");
        var merged = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Browne");
        var threeDaysAgo = London.AtStrictly(TodayDate.PlusDays(-3) + new LocalTime(10, 0)).ToInstant();
        var tenDaysAgo = London.AtStrictly(TodayDate.PlusDays(-10) + new LocalTime(10, 0)).ToInstant();
        var inFiveDays = London.AtStrictly(TodayDate.PlusDays(5) + new LocalTime(10, 0)).ToInstant();
        await VisitAsync(olivia, tenDaysAgo, AppointmentStatus.Completed);
        await VisitAsync(olivia, threeDaysAgo, AppointmentStatus.Completed);
        await VisitAsync(olivia, threeDaysAgo + Duration.FromDays(1), AppointmentStatus.NoShow);
        await VisitAsync(olivia, inFiveDays, AppointmentStatus.Booked);
        await VisitAsync(olivia, inFiveDays + Duration.FromDays(9), AppointmentStatus.Confirmed);
        await InScopeAsync(_studio.Id, async db =>
        {
            var card = await db.Clients.SingleAsync(c => c.Id == olivia);
            card.MarketingConsent = true;
            card.MarketingConsentAt = Now;
            card.HealthNotesProtected = "CIPHER-TEXT-OF-AN-ALLERGY";
            (await db.Clients.SingleAsync(c => c.Id == noah)).IsActive = false;
            var pointer = await db.Clients.SingleAsync(c => c.Id == merged);
            pointer.IsActive = false;
            pointer.MergedIntoClientId = olivia;
            pointer.MergedAt = Now;
        });
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var response = await owner.GetAsync(new Uri(Path, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType.ShouldNotBeNull().ToString().ShouldBe("text/csv; charset=utf-8");
        response.Content.Headers.ContentDisposition.ShouldNotBeNull().FileName.ShouldBe($"clients-{Today}.csv");
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Take(3).ShouldBe(Encoding.UTF8.GetPreamble(), "Excel needs the byte order mark to read the letters");
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var iso = LocalDatePattern.Iso;
        text.Split("\r\n").ShouldBe(
        [
            Header,
            $"Noah,Adams,,,,No,,{Today},0,,,No",
            $"Olivia,Brown,'+447700900123,olivia@example.test,Likes tea,Yes,{Today},{Today},2,{iso.Format(TodayDate.PlusDays(-3))},{iso.Format(TodayDate.PlusDays(5))},Yes",
            string.Empty,
        ]);
        text.ShouldNotContain("CIPHER", Case.Insensitive, "health notes are in no file");
        text.ShouldNotContain("Browne", Case.Sensitive, "a card merged into another is a pointer, not a client");
    }

    [Fact]
    public async Task The_file_of_one_salon_is_read_by_the_client_import_of_another()
    {
        await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123", "olivia@example.test", "Likes tea");
        await VisitSeed.ClientAsync(factory, _studio.Id, "Noah", "Adams", "+447700900456");
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var file = await (await owner.GetAsync(new Uri(Path, UriKind.Relative))).Content.ReadAsByteArrayAsync();
        var second = await factory.CreateTenantAsync("second-chair", name: "Second Chair");
        using var other = await factory.SignedInClientAsync(HostC, second.Id, Role.Owner);

        await ImportAsync(other, file);

        List<Client> clients = [];
        await InScopeAsync(second.Id, async db => clients = await db.Clients.AsNoTracking().OrderBy(c => c.LastName).ToListAsync());
        clients.Select(c => (c.FirstName, c.LastName, c.Phone, c.Email, c.Notes)).ShouldBe(
        [
            ("Noah", "Adams", "+447700900456", null, null),
            ("Olivia", "Brown", "+447700900123", "olivia@example.test", "Likes tea"),
        ]);
    }

    [Fact]
    public async Task A_name_or_a_note_that_looks_like_a_formula_or_holds_a_comma_cannot_break_the_file()
    {
        await VisitSeed.ClientAsync(factory, _studio.Id, "=HYPERLINK(\"http://evil.example\")", "Brown, Olivia", notes: "@SUM(1)\nsecond line");
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var bytes = await (await owner.GetAsync(new Uri(Path, UriKind.Relative))).Content.ReadAsByteArrayAsync();
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        // An apostrophe in front makes it text; the quotes keep the comma and the line break inside one cell.
        text.ShouldContain("\"'=HYPERLINK(\"\"http://evil.example\"\")\",\"Brown, Olivia\",,,\"'@SUM(1)\nsecond line\",No,");
    }

    [Fact]
    public async Task A_Polish_salon_gets_its_own_headings_and_only_its_own_clients()
    {
        await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123");
        await VisitSeed.ClientAsync(factory, _polish.Id, "Anna", "Nowak", "+48601234567");
        using var owner = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);

        var bytes = await (await owner.GetAsync(new Uri(Path, UriKind.Relative))).Content.ReadAsByteArrayAsync();
        var lines = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        lines[0].ShouldBe("Imię;Nazwisko;Telefon;E-mail;Notatki;Zgoda marketingowa;Data zgody;Klient od;Zakończone wizyty;Ostatnia wizyta;Następna wizyta;Aktywny");
        lines.Length.ShouldBe(2);
        lines[1].ShouldStartWith("Anna;Nowak;'+48601234567;;;Nie;;");
        lines[1].ShouldEndWith(";0;;;Tak");
    }

    [Fact]
    public async Task The_client_list_is_for_the_owner_only()
    {
        await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123");
        using var manager = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Manager);
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        using var anonymous = factory.ClientFor(HostA);
        using var platform = factory.CreateClient();
        platform.DefaultRequestHeaders.Host = "localhost";

        foreach (var refused in new[] { manager, desk })
        {
            var denied = await refused.GetAsync(new Uri(Path, UriKind.Relative));
            denied.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        }

        var login = await anonymous.GetAsync(new Uri(Path, UriKind.Relative));
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        login.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");
        (await platform.GetAsync(new Uri(Path, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>Uploads the file to the client import and confirms the preview (ADR 0019).</summary>
    private static async Task ImportAsync(HttpClient client, byte[] content)
    {
        const string importPath = "/admin/clients/import";
        var formPage = await client.GetAsync(new Uri(importPath, UriKind.Relative));
        formPage.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(IdentityTestClient.AntiForgeryToken(await formPage.Content.ReadAsStringAsync())), "__RequestVerificationToken");
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "File", "clients.csv");
        var upload = await client.PostAsync(new Uri(importPath, UriKind.Relative), form);
        upload.StatusCode.ShouldBe(HttpStatusCode.Redirect, WebUtility.HtmlDecode(await upload.Content.ReadAsStringAsync()));
        var previewPath = upload.Headers.Location!.OriginalString;
        previewPath.ShouldStartWith(importPath + "/", Case.Sensitive, "the file could not be read by the import");

        var preview = await client.GetAsync(new Uri(previewPath, UriKind.Relative));
        var token = IdentityTestClient.AntiForgeryToken(await preview.Content.ReadAsStringAsync());
        var confirm = await client.PostAsync(
            new Uri(previewPath + "/confirm", UriKind.Relative),
            new FormUrlEncodedContent([new KeyValuePair<string, string>("__RequestVerificationToken", token)]));
        confirm.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    private Task<Guid> VisitAsync(Guid clientId, Instant start, AppointmentStatus status)
        => VisitSeed.AppointmentAsync(factory, _studio.Id, clientId, _staffId, _serviceId, start, status);

    private async Task InScopeAsync(Guid tenantId, Func<SalonDbContext, Task> act)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        await act(db);
        await db.SaveChangesAsync();
    }
}