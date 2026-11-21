using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Appointments;
using Salon.Core.Appointments.Events;
using Salon.Core.Catalog;
using Salon.Core.Events;
using Salon.Core.Identity;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Appointments;
using Salon.Services.Events;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Appointments;

/// <summary>
/// The waitlist (ADR 0041): the desk puts a client on it from their card or from a full day, the list shows who has
/// waited longest first with what they want, a booking for that service takes them off by itself, the desk can take
/// them off by hand, a wish whose last day has passed stops waiting, and nothing of another salon is seen or changed.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class WaitlistTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string ListPath = "/admin/appointments/waitlist";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly Instant Now = SystemClock.Instance.GetCurrentInstant();
    private static readonly LocalDate Today = Now.InZone(London).Date;

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _annaId;
    private Guid _cutId;
    private Guid _colourId;
    private Guid _oliviaId;
    private Guid _noahId;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        (_annaId, _cutId) = await VisitSeed.SalonAsync(factory, _studio.Id);
        _colourId = await ServiceAsync(_studio.Id, "Colour");
        _oliviaId = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123");
        _noahId = await VisitSeed.ClientAsync(factory, _studio.Id, "Noah", "Reed", "+447700900555");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_desk_puts_a_client_on_the_list_from_their_card_and_finds_them_there_with_what_they_want()
    {
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        var empty = await GetAsync(desk, ListPath);
        empty.ShouldContain("data-testid=\"empty-state\"");
        empty.ShouldContain("Nobody is waiting.");
        (await GetAsync(desk, "/admin/appointments")).ShouldNotContain("data-testid=\"waitlist-link\"", Case.Sensitive, "the waiting list is a tab; its link shows only when somebody waits (ADR 0092)");

        var card = await GetAsync(desk, $"/admin/clients/{_oliviaId}");
        card.ShouldContain($"href=\"/admin/appointments/waitlist/new?clientId={_oliviaId}\" data-testid=\"add-to-waitlist\"");
        var form = await GetAsync(desk, $"{ListPath}/new?clientId={_oliviaId}");
        form.ShouldContain("data-testid=\"waitlist-client\">Olivia Brown<");
        form.ShouldContain(">Cut</option>");
        form.ShouldContain(">Anna</option>");

        var notBefore = LocalDatePattern.Iso.Format(Today.PlusDays(3));
        var notAfter = LocalDatePattern.Iso.Format(Today.PlusDays(20));
        var saved = await PostAsync(desk, $"{ListPath}/new", form, new()
        {
            ["clientId"] = _oliviaId.ToString(),
            ["serviceId"] = _cutId.ToString(),
            ["staffId"] = _annaId.ToString(),
            ["notBefore"] = notBefore,
            ["notAfter"] = notAfter,
            ["note"] = "  mornings only ",
        });

        saved.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        saved.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe(ListPath);
        var entry = (await EntriesAsync(_studio.Id)).ShouldHaveSingleItem();
        entry.ClientId.ShouldBe(_oliviaId);
        entry.ServiceId.ShouldBe(_cutId);
        entry.StaffMemberId.ShouldBe(_annaId);
        entry.NotBefore.ShouldBe(Today.PlusDays(3));
        entry.NotAfter.ShouldBe(Today.PlusDays(20));
        entry.Note.ShouldBe("mornings only");
        entry.CreatedByUserId.ShouldNotBeNull();
        entry.ClosedAt.ShouldBeNull();

        var list = await GetAsync(desk, ListPath);
        list.ShouldContain("Olivia Brown is on the waitlist.");
        list.ShouldContain("data-testid=\"waitlist\" data-total=\"1\"");
        list.ShouldContain("data-testid=\"waiting-client\">Olivia Brown<");
        list.ShouldContain("data-testid=\"waiting-phone\">+447700900123<");
        list.ShouldContain("data-testid=\"waiting-service\">Cut<");
        list.ShouldContain("data-testid=\"waiting-staff\">Anna<");
        list.ShouldContain("data-testid=\"waiting-note\">mornings only<");
        // The booking form opens for this client on the first day they can come.
        list.ShouldContain($"href=\"/admin/appointments/new?clientId={_oliviaId}&serviceId={_cutId}&staffId={_annaId}&date={notBefore}\"");
        (await GetAsync(desk, "/admin/appointments")).ShouldContain("data-testid=\"waitlist-link\" data-count=\"1\"");
        (await GetAsync(desk, "/admin/appointments/calendar")).ShouldContain("data-testid=\"waitlist-count\">1<");
    }

    [Fact]
    public async Task The_one_who_asked_first_is_on_top_and_asking_again_keeps_the_place()
    {
        (await AddAsync(new WaitlistRequest(_oliviaId, _cutId, null, null, null, null))).ShouldBe(WaitlistAddOutcome.Added);
        (await AddAsync(new WaitlistRequest(_noahId, _cutId, _annaId, null, null, null))).ShouldBe(WaitlistAddOutcome.Added);
        (await AddAsync(new WaitlistRequest(_oliviaId, _colourId, null, null, null, null))).ShouldBe(WaitlistAddOutcome.Added, "another service is another wish");

        // Olivia changes her mind about the days: same entry, same place in the queue.
        (await AddAsync(new WaitlistRequest(_oliviaId, _cutId, _annaId, Today.PlusDays(2), null, "after work"))).ShouldBe(WaitlistAddOutcome.Updated);

        var waiting = await WaitlistAsync(s => s.ListWaitingAsync());
        waiting.Select(w => (w.ClientName, w.ServiceName)).ShouldBe([("Olivia Brown", "Cut"), ("Noah Reed", "Cut"), ("Olivia Brown", "Colour")]);
        waiting[0].StaffName.ShouldBe("Anna");
        waiting[0].Note.ShouldBe("after work");
        waiting[0].BookingDay.ShouldBe(Today.PlusDays(2));
        waiting[1].BookingDay.ShouldBe(Today, "as soon as possible means today");
        waiting[2].StaffName.ShouldBeNull();
        (await EntriesAsync(_studio.Id)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_booking_for_the_service_takes_the_client_off_the_list_by_itself()
    {
        await AddAsync(new WaitlistRequest(_oliviaId, _cutId, null, null, null, null));
        await AddAsync(new WaitlistRequest(_oliviaId, _colourId, null, null, null, null));
        await AddAsync(new WaitlistRequest(_noahId, _cutId, null, null, null, null));
        var start = Now + Duration.FromDays(2);
        var visit = await VisitSeed.AppointmentAsync(factory, _studio.Id, _oliviaId, _annaId, _cutId, start, AppointmentStatus.Booked);

        // The booking raises its event; the outbox hands it to the waitlist like any other consumer.
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            await scope.ServiceProvider.GetRequiredService<IEventPublisher>().PublishAsync(
                new AppointmentBooked(visit, _studio.Id, _oliviaId, _annaId, _cutId, start, start + Duration.FromMinutes(30), AppointmentSource.Admin));
            await scope.ServiceProvider.GetRequiredService<SalonDbContext>().SaveChangesAsync();
        }

        await using (var scope = factory.CreateTenantScope(null))
        {
            await scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync();
        }

        var entries = await EntriesAsync(_studio.Id);
        var closed = entries.Single(e => e.ClientId == _oliviaId && e.ServiceId == _cutId);
        closed.Outcome.ShouldBe(WaitlistOutcome.Booked);
        closed.ClosedAt.ShouldNotBeNull();
        closed.AppointmentId.ShouldBe(visit);
        closed.ClosedByUserId.ShouldBeNull("a booking closed it, not a person");
        entries.Where(e => e.ClosedAt is null).Select(e => (e.ClientId, e.ServiceId)).ShouldBe([(_oliviaId, _colourId), (_noahId, _cutId)], ignoreOrder: true);
        (await WaitlistAsync(s => s.CountWaitingAsync())).ShouldBe(2);

        // Once booked, the client may wait for the same service again: the closed entry does not stand in the way.
        (await AddAsync(new WaitlistRequest(_oliviaId, _cutId, null, null, null, null))).ShouldBe(WaitlistAddOutcome.Added);
    }

    [Fact]
    public async Task The_desk_takes_a_client_off_the_list_by_hand_once()
    {
        await AddAsync(new WaitlistRequest(_oliviaId, _cutId, null, null, null, null));
        var entry = (await EntriesAsync(_studio.Id)).ShouldHaveSingleItem().Id;
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        var list = await GetAsync(desk, ListPath);

        var removed = await PostAsync(desk, $"{ListPath}/{entry}/remove?page=1", list, []);

        removed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        removed.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe(ListPath);
        var after = await GetAsync(desk, ListPath);
        after.ShouldContain("The client is off the waitlist.");
        after.ShouldContain("data-testid=\"empty-state\"");
        var closed = (await EntriesAsync(_studio.Id)).ShouldHaveSingleItem();
        closed.Outcome.ShouldBe(WaitlistOutcome.Removed);
        closed.ClosedByUserId.ShouldNotBeNull();
        (await PostAsync(desk, $"{ListPath}/{entry}/remove", after, [])).StatusCode.ShouldBe(HttpStatusCode.NotFound, "an entry is closed once");
    }

    [Fact]
    public async Task A_wish_whose_last_day_has_passed_stops_waiting_without_anybody_touching_it()
    {
        await AddAsync(new WaitlistRequest(_oliviaId, _cutId, null, null, Today.PlusDays(1), null));
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            await scope.ServiceProvider.GetRequiredService<SalonDbContext>().WaitlistEntries
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.NotAfter, Today.PlusDays(-1)));
        }

        (await WaitlistAsync(s => s.CountWaitingAsync())).ShouldBe(0);
        (await WaitlistAsync(s => s.ListWaitingAsync())).ShouldBeEmpty();
        (await EntriesAsync(_studio.Id)).ShouldHaveSingleItem().ClosedAt.ShouldBeNull("it is not closed, only no longer waiting");
    }

    [Fact]
    public async Task What_cannot_be_a_wish_is_refused_with_the_reason_and_saves_nothing()
    {
        var inactiveService = await ServiceAsync(_studio.Id, "Old perm", active: false);
        var polishService = await ServiceAsync(_polish.Id, "Strzyżenie");

        (await AddAsync(new WaitlistRequest(_oliviaId, _cutId, null, Today.PlusDays(5), Today.PlusDays(2), null))).ShouldBe(WaitlistAddOutcome.DaysInvalid);
        (await AddAsync(new WaitlistRequest(_oliviaId, _cutId, null, null, Today.PlusDays(-1), null))).ShouldBe(WaitlistAddOutcome.DaysInvalid, "a last day already past");
        (await AddAsync(new WaitlistRequest(_oliviaId, _cutId, null, null, null, new string('a', 201)))).ShouldBe(WaitlistAddOutcome.NoteTooLong);
        (await AddAsync(new WaitlistRequest(Guid.NewGuid(), _cutId, null, null, null, null))).ShouldBe(WaitlistAddOutcome.ClientUnknown);
        (await AddAsync(new WaitlistRequest(_oliviaId, inactiveService, null, null, null, null))).ShouldBe(WaitlistAddOutcome.ServiceUnknown);
        (await AddAsync(new WaitlistRequest(_oliviaId, polishService, null, null, null, null))).ShouldBe(WaitlistAddOutcome.ServiceUnknown, "another salon's service");
        (await AddAsync(new WaitlistRequest(_oliviaId, _cutId, Guid.NewGuid(), null, null, null))).ShouldBe(WaitlistAddOutcome.StaffUnknown);
        (await EntriesAsync(_studio.Id)).ShouldBeEmpty();

        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        var form = await GetAsync(desk, $"{ListPath}/new?clientId={_oliviaId}&serviceId={_cutId}");
        form.ShouldContain($"<option value=\"{_cutId}\" selected=\"selected\">Cut</option>");
        var refused = await PostAsync(desk, $"{ListPath}/new", form, new()
        {
            ["clientId"] = _oliviaId.ToString(),
            ["serviceId"] = _cutId.ToString(),
            ["notBefore"] = LocalDatePattern.Iso.Format(Today.PlusDays(5)),
            ["notAfter"] = LocalDatePattern.Iso.Format(Today.PlusDays(2)),
            ["note"] = "after work",
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.OK);
        var again = WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync());
        again.ShouldContain("data-testid=\"error\" class=\"notice notice-error\">Check the days: the last day cannot be before the first, or already past.<");
        again.ShouldContain("value=\"after work\"", Case.Sensitive, "what was typed is kept");
        (await PostAsync(desk, $"{ListPath}/new", form, new() { ["clientId"] = _oliviaId.ToString() })).StatusCode.ShouldBe(HttpStatusCode.OK, "no service chosen is a message, not an error page");
        (await EntriesAsync(_studio.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_full_day_on_the_booking_form_offers_the_waitlist_for_that_client_and_service()
    {
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        // Anna has no working hours in this salon, so every day is full.
        var form = await GetAsync(desk, $"/admin/appointments/new?clientId={_oliviaId}&serviceId={_cutId}&staffId={_annaId}");

        form.ShouldContain("data-testid=\"no-slots\"");
        form.ShouldContain($"href=\"/admin/appointments/waitlist/new?clientId={_oliviaId}&serviceId={_cutId}&staffId={_annaId}\"");
        form.ShouldContain("Put on the waitlist");
        (await GetAsync(desk, $"/admin/appointments/new?serviceId={_cutId}")).ShouldNotContain("data-testid=\"no-slots-waitlist\"", Case.Sensitive, "nobody to put on the list yet");
    }

    [Fact]
    public async Task The_list_needs_the_diary_permissions_a_sign_in_the_salon_and_a_token_and_another_salon_sees_nothing()
    {
        await AddAsync(new WaitlistRequest(_oliviaId, _cutId, null, null, null, null));
        var entry = (await EntriesAsync(_studio.Id)).ShouldHaveSingleItem().Id;
        using var staff = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Staff);
        using var anonymous = factory.ClientFor(HostA);
        using var other = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        foreach (var path in new[] { ListPath, $"{ListPath}/new?clientId={_oliviaId}" })
        {
            // Staff work the diary but do not see client names and phone numbers (Clients.View).
            var denied = await staff.GetAsync(new Uri(path, UriKind.Relative));
            denied.StatusCode.ShouldBe(HttpStatusCode.Redirect, path);
            denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied", path);
            var login = await anonymous.GetAsync(new Uri(path, UriKind.Relative));
            login.StatusCode.ShouldBe(HttpStatusCode.Redirect, path);
            login.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login", path);
        }

        var otherList = await GetAsync(other, ListPath);
        otherList.ShouldContain("data-testid=\"empty-state\"");
        otherList.ShouldNotContain("Olivia");
        (await other.GetAsync(new Uri($"{ListPath}/new?clientId={_oliviaId}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(other, $"{ListPath}/{entry}/remove", otherList, [])).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(other, $"{ListPath}/new", otherList, new() { ["clientId"] = _oliviaId.ToString(), ["serviceId"] = _cutId.ToString() })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await desk.GetAsync(new Uri($"{ListPath}/new", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound, "the form is for a client");

        (await desk.PostAsync(new Uri($"{ListPath}/{entry}/remove", UriKind.Relative), new FormUrlEncodedContent([]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await EntriesAsync(_studio.Id)).ShouldHaveSingleItem().ClosedAt.ShouldBeNull();
    }

    [Fact]
    public async Task The_database_itself_keeps_one_wait_per_client_and_service_and_an_outcome_with_every_close()
    {
        await AddAsync(new WaitlistRequest(_oliviaId, _cutId, null, null, null, null));
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();

        db.WaitlistEntries.Add(new WaitlistEntry { ClientId = _oliviaId, ServiceId = _cutId, CreatedAt = Now });
        (await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync())).InnerException.ShouldNotBeNull().Message.ShouldContain("ux_waitlist_entries_one_wait_per_client_and_service");
        db.ChangeTracker.Clear();

        var entry = await db.WaitlistEntries.SingleAsync();
        entry.ClosedAt = Now;
        (await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync())).InnerException.ShouldNotBeNull().Message.ShouldContain("ck_waitlist_entries_closed_with_outcome");
    }

    private Task<WaitlistAddOutcome> AddAsync(WaitlistRequest request) => WaitlistAsync(s => s.AddAsync(request, null));

    private async Task<T> WaitlistAsync<T>(Func<IWaitlistService, Task<T>> act)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await act(scope.ServiceProvider.GetRequiredService<IWaitlistService>());
    }

    private async Task<Guid> ServiceAsync(Guid tenantId, string name, bool active = true)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var service = new Service { Name = name, Duration = Duration.FromMinutes(60), Price = 60m, Currency = "GBP", IsActive = active, SortOrder = 5 };
        db.Services.Add(service);
        await db.SaveChangesAsync();
        return service.Id;
    }

    private async Task<List<WaitlistEntry>> EntriesAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().WaitlistEntries.AsNoTracking().OrderBy(w => w.CreatedAt).ThenBy(w => w.Id).ToListAsync();
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string pageWithToken, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(pageWithToken);
        return await client.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent(fields));
    }
}