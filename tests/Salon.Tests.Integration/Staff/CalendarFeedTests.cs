using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Identity;
using Salon.Core.Locations;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Staff;

/// <summary>
/// A staff member's calendar address (ADR 0038): off until somebody switches it on, a secret address that a calendar
/// app reads without signing in, only that person's visits that hold their time or took place, the client by first
/// name and initial only, a new address or "off" that stops the old one at once, and nothing of another salon.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class CalendarFeedTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";

    private static readonly Instant Now = SystemClock.Instance.GetCurrentInstant();

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _annaId;
    private Guid _serviceId;
    private Guid _clientId;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        (_annaId, _serviceId) = await VisitSeed.SalonAsync(factory, _studio.Id);
        _clientId = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123", "olivia@example.com", "Prefers afternoons.");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_address_is_off_until_somebody_switches_it_on_and_then_a_calendar_app_reads_it_without_signing_in()
    {
        var booked = await VisitAsync(_annaId, Now + Duration.FromDays(2), AppointmentStatus.Booked);
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var pagePath = $"/admin/staff/{_annaId}/calendar-feed";

        (await GetAsync(owner, "/admin/staff")).ShouldContain($"href=\"{pagePath}\" data-testid=\"calendar-feed\"");
        var off = await GetAsync(owner, pagePath);
        off.ShouldContain("data-testid=\"feed-off-state\"");
        off.ShouldContain("No phone numbers and no notes leave the salon.");
        off.ShouldNotContain("data-testid=\"feed-address\"");
        (await StaffAsync(_annaId)).CalendarFeedToken.ShouldBeNull();

        var switched = await PostAsync(owner, pagePath + "/new-address", off);
        switched.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        switched.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe(pagePath);
        var on = await GetAsync(owner, pagePath);
        on.ShouldContain("The address is on. Paste it into the calendar.");
        on.ShouldContain("data-testid=\"feed-on\"");
        var token = (await StaffAsync(_annaId)).CalendarFeedToken.ShouldNotBeNull();
        CalendarFeedToken.LooksValid(token).ShouldBeTrue();
        Address(on).ShouldBe($"https://{HostA}/calendar/{token}.ics", "the address is on the salon's own host");

        using var calendarApp = factory.ClientFor(HostA);
        var response = await calendarApp.GetAsync(new Uri($"/calendar/{token}.ics", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType.ShouldNotBeNull().ToString().ShouldBe("text/calendar; charset=utf-8");
        response.Headers.CacheControl.ShouldNotBeNull().Private.ShouldBeTrue("a shared cache must not keep somebody's diary");
        response.Headers.GetValues("X-Robots-Tag").ShouldBe(["noindex, nofollow"]);
        var text = await response.Content.ReadAsStringAsync();
        text.ShouldStartWith("BEGIN:VCALENDAR\r\n");
        text.ShouldContain("X-WR-CALNAME:Anna · Studio 20\r\n");
        text.ShouldContain($"UID:{booked:N}@{HostA}\r\n");
        text.ShouldContain("SUMMARY:Cut · Olivia B.\r\n");
        text.ShouldContain($"DTSTART:{(Now + Duration.FromDays(2)).ToDateTimeUtc():yyyyMMdd'T'HHmmss'Z'}\r\n");
        text.ShouldContain($"DTEND:{(Now + Duration.FromDays(2) + Duration.FromMinutes(30)).ToDateTimeUtc():yyyyMMdd'T'HHmmss'Z'}\r\n");
        text.ShouldNotContain("LOCATION", Case.Sensitive, "the salon has entered no address yet");
        text.ShouldEndWith("END:VCALENDAR\r\n");
    }

    [Fact]
    public async Task The_calendar_holds_this_persons_live_and_past_visits_of_the_window_and_as_little_about_the_client_as_it_can()
    {
        var tomId = await StaffAsync(_studio.Id, "Tom Reed");
        var confirmed = await VisitAsync(_annaId, Now + Duration.FromDays(1), AppointmentStatus.Confirmed);
        var completed = await VisitAsync(_annaId, Now - Duration.FromDays(3), AppointmentStatus.Completed);
        var arrived = await VisitAsync(_annaId, Now - Duration.FromMinutes(10), AppointmentStatus.Arrived);
        var cancelled = await VisitAsync(_annaId, Now + Duration.FromDays(4), AppointmentStatus.CancelledByClient);
        var missed = await VisitAsync(_annaId, Now - Duration.FromDays(5), AppointmentStatus.NoShow);
        var longAgo = await VisitAsync(_annaId, Now - Duration.FromDays(40), AppointmentStatus.Completed);
        var toms = await VisitAsync(tomId, Now + Duration.FromDays(1), AppointmentStatus.Booked);
        await AddressAsync(_studio.Id);
        var token = await SwitchOnAsync(_annaId);
        using var calendarApp = factory.ClientFor(HostA);

        var text = await (await calendarApp.GetAsync(new Uri($"/calendar/{token}.ics", UriKind.Relative))).Content.ReadAsStringAsync();

        Uids(text).ShouldBe([$"{completed:N}", $"{arrived:N}", $"{confirmed:N}"], "oldest first; cancelled, missed, long past and other people's visits are left out");
        text.ShouldNotContain($"{cancelled:N}");
        text.ShouldNotContain($"{missed:N}");
        text.ShouldNotContain($"{longAgo:N}");
        text.ShouldNotContain($"{toms:N}");
        text.ShouldContain("LOCATION:20 Drake Street\\, Rochdale OL16 1NT\r\n");

        // What leaves the salon: a first name and an initial. Never the surname, the phone, the email or a note.
        text.ShouldNotContain("Brown");
        text.ShouldNotContain("7700900123");
        text.ShouldNotContain("olivia@example.com");
        text.ShouldNotContain("afternoons");
    }

    [Fact]
    public async Task A_new_address_or_switching_off_stops_the_old_address_at_once()
    {
        await VisitAsync(_annaId, Now + Duration.FromDays(1), AppointmentStatus.Booked);
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        using var calendarApp = factory.ClientFor(HostA);
        var pagePath = $"/admin/staff/{_annaId}/calendar-feed";
        var first = await SwitchOnAsync(_annaId);
        (await calendarApp.GetAsync(new Uri($"/calendar/{first}.ics", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var page = await GetAsync(owner, pagePath);
        (await PostAsync(owner, pagePath + "/new-address", page)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var second = (await StaffAsync(_annaId)).CalendarFeedToken.ShouldNotBeNull();
        second.ShouldNotBe(first);
        var replaced = await GetAsync(owner, pagePath);
        replaced.ShouldContain("There is a new address. The old one no longer works.");
        replaced.ShouldNotContain(first);
        (await calendarApp.GetAsync(new Uri($"/calendar/{first}.ics", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await calendarApp.GetAsync(new Uri($"/calendar/{second}.ics", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await PostAsync(owner, pagePath + "/off", replaced)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var off = await GetAsync(owner, pagePath);
        off.ShouldContain("The address is switched off. It no longer works.");
        off.ShouldContain("data-testid=\"feed-off-state\"");
        (await StaffAsync(_annaId)).CalendarFeedToken.ShouldBeNull();
        (await calendarApp.GetAsync(new Uri($"/calendar/{second}.ics", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_address_works_only_on_its_own_salon_for_somebody_who_still_works_there_and_only_whole()
    {
        var token = await SwitchOnAsync(_annaId);
        using var here = factory.ClientFor(HostA);
        using var elsewhere = factory.ClientFor(HostB);
        using var platform = factory.CreateClient();
        platform.DefaultRequestHeaders.Host = "localhost";

        (await here.GetAsync(new Uri($"/calendar/{token}.ics", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await elsewhere.GetAsync(new Uri($"/calendar/{token}.ics", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound, "another salon's host does not know this address");
        (await platform.GetAsync(new Uri($"/calendar/{token}.ics", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        foreach (var wrong in new[] { $"/calendar/{token}", $"/calendar/{token}.txt", $"/calendar/{token[..^1]}.ics", "/calendar/.ics", $"/calendar/{CalendarFeedToken.Generate()}.ics" })
        {
            (await here.GetAsync(new Uri(wrong, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound, wrong);
        }

        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            await scope.ServiceProvider.GetRequiredService<SalonDbContext>().StaffMembers.Where(s => s.Id == _annaId).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false));
        }

        (await here.GetAsync(new Uri($"/calendar/{token}.ics", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound, "somebody who no longer works here has no diary to read");
    }

    [Fact]
    public async Task Only_people_who_manage_staff_reach_the_page_and_never_for_another_salons_person()
    {
        var pagePath = $"/admin/staff/{_annaId}/calendar-feed";
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        using var anonymous = factory.ClientFor(HostA);
        using var other = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        var page = await GetAsync(owner, pagePath);

        var denied = await desk.GetAsync(new Uri(pagePath, UriKind.Relative));
        denied.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        var login = await anonymous.GetAsync(new Uri(pagePath, UriKind.Relative));
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        login.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");

        (await other.GetAsync(new Uri(pagePath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var otherPage = await GetAsync(other, "/admin/staff");
        (await PostAsync(other, pagePath + "/new-address", otherPage)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(other, pagePath + "/off", otherPage)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(owner, $"/admin/staff/{Guid.NewGuid()}/calendar-feed/new-address", page)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await owner.PostAsync(new Uri(pagePath + "/new-address", UriKind.Relative), new FormUrlEncodedContent([]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await StaffAsync(_annaId)).CalendarFeedToken.ShouldBeNull();
    }

    private static string Address(string html) => AddressPattern().Match(html).Groups["address"].Value;

    private static List<string> Uids(string calendar) => UidPattern().Matches(calendar).Select(m => m.Groups["id"].Value).ToList();

    private Task<Guid> VisitAsync(Guid staffId, Instant start, AppointmentStatus status)
        => VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, staffId, _serviceId, start, status);

    private async Task<string> SwitchOnAsync(Guid staffId)
    {
        var token = CalendarFeedToken.Generate();
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await scope.ServiceProvider.GetRequiredService<SalonDbContext>().StaffMembers.Where(s => s.Id == staffId).ExecuteUpdateAsync(s => s.SetProperty(p => p.CalendarFeedToken, token));
        return token;
    }

    private async Task<Guid> StaffAsync(Guid tenantId, string name)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var person = new StaffMember { DisplayName = name, CreatedAt = Now };
        db.StaffMembers.Add(person);
        await db.SaveChangesAsync();
        return person.Id;
    }

    private async Task<StaffMember> StaffAsync(Guid staffId)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().StaffMembers.AsNoTracking().SingleAsync(s => s.Id == staffId);
    }

    private async Task AddressAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        db.Locations.Add(new Location { AddressLine1 = "20 Drake Street", City = "Rochdale", Postcode = "OL16 1NT", CreatedAt = Now, UpdatedAt = Now });
        await db.SaveChangesAsync();
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string pageWithToken)
        => await client.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent([new("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(pageWithToken))]));

    [GeneratedRegex("data-testid=\"feed-address\"[^>]*value=\"(?<address>[^\"]+)\"|value=\"(?<address>[^\"]+)\"[^>]*data-testid=\"feed-address\"")]
    private static partial Regex AddressPattern();

    [GeneratedRegex("UID:(?<id>[0-9a-f]{32})@")]
    private static partial Regex UidPattern();
}