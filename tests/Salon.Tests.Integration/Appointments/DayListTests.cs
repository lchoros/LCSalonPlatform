using System.Net;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NodaTime;
using NodaTime.Text;

using Salon.Core.Appointments;
using Salon.Core.Catalog;
using Salon.Core.Clients;
using Salon.Core.Identity;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Appointments;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Appointments;

/// <summary>
/// The admin day list over HTTP (ADR 0012, admin booking slice): one column per bookable staff member with that day's
/// active appointments in the salon zone, prev/next navigation, status actions that go through the state machine and
/// refuse a forbidden move with a localized message, and the gates (Bookings.Manage + Clients.View, tenant, anti-forgery).
/// The salon has Anna (every day 09:00–17:00) and Tom (no appointments), one 30-minute service and one client.
/// </summary>
[Collection(SharedDatabase.Name)]
public class DayListTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string ListPath = "/admin/appointments";
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _annaId;
    private Guid _serviceId;
    private Guid _clientId;
    private LocalDate _day;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        (_clientId, _annaId, _serviceId) = await SeedSalonAsync(_studio.Id);
        _day = SystemClock.Instance.GetCurrentInstant().InZone(London).Date.PlusDays(1);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_day_shows_each_staff_members_appointments_in_the_salon_zone_with_navigation()
    {
        await BookAsync(At(14, 0));
        await BookAsync(At(10, 0));
        var cancelled = await BookAsync(At(11, 0));
        await InTenantAsync(_studio.Id, sp => sp.GetRequiredService<IBookingService>().CancelAsync(cancelled.Id, byClient: true, null));
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var html = await GetAsync(client, $"{ListPath}?date={Iso(_day)}");

        html.ShouldContain("<html lang=\"en-GB\"");
        Count(html, "data-testid=\"staff-column\"").ShouldBe(2);
        Count(html, "data-testid=\"appointment\"").ShouldBe(2, "the cancelled one leaves the day");
        html.IndexOf("Anna Kowalska", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("Tom Reed", StringComparison.Ordinal), "staff in sort order");
        html.IndexOf("10:00", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("14:00", StringComparison.Ordinal), "appointments in start order");
        html.ShouldContain("10:30");
        html.ShouldContain("Olivia Brown");
        html.ShouldContain("Cut");
        Count(html, "data-testid=\"status-badge\">Booked<").ShouldBe(2);
        html.ShouldContain("data-testid=\"column-empty\"");
        html.ShouldContain($"href=\"{ListPath}?date={Iso(_day.PlusDays(-1))}\"");
        html.ShouldContain($"href=\"{ListPath}?date={Iso(_day.PlusDays(1))}\"");
        html.ShouldContain($"href=\"{ListPath}/new?date={Iso(_day)}\"");
        html.ShouldContain("data-testid=\"action-confirm\"");
        html.ShouldContain("data-testid=\"action-arrived\"");
        html.ShouldContain("data-testid=\"action-no-show\"");
        html.ShouldContain("data-testid=\"action-cancel\"");
        html.ShouldNotContain("data-testid=\"action-complete\"", Case.Sensitive, "Booked cannot go straight to Completed");
        html.ShouldContain("__RequestVerificationToken");

        var today = SystemClock.Instance.GetCurrentInstant().InZone(London).Date;
        var defaultDay = await GetAsync(client, ListPath);
        defaultDay.ShouldContain($"href=\"{ListPath}?date={Iso(today.PlusDays(1))}\"", Case.Sensitive, "without a date the list shows today in the salon zone");
        defaultDay.ShouldNotContain("Olivia Brown");
    }

    [Fact]
    public async Task Status_actions_move_the_appointment_and_a_forbidden_move_is_refused_with_a_message()
    {
        var appointment = await BookAsync(At(10, 0));
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var confirm = await PostActionAsync(client, appointment.Id, "confirm");
        confirm.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        confirm.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"{ListPath}?date={Iso(_day)}");
        var afterConfirm = await GetAsync(client, confirm.Headers.Location.OriginalString);
        afterConfirm.ShouldContain("data-testid=\"flash\"");
        afterConfirm.ShouldContain("Confirmed");
        (await StatusAsync(appointment.Id)).ShouldBe(AppointmentStatus.Confirmed);

        var complete = await PostActionAsync(client, appointment.Id, "complete");
        complete.StatusCode.ShouldBe(HttpStatusCode.OK, "a forbidden transition re-renders the day with a message");
        var refused = WebUtility.HtmlDecode(await complete.Content.ReadAsStringAsync());
        refused.ShouldContain("data-testid=\"error\"");
        refused.ShouldContain("An appointment with status Confirmed cannot be marked Completed.");
        refused.ShouldContain("Olivia Brown");
        (await StatusAsync(appointment.Id)).ShouldBe(AppointmentStatus.Confirmed);

        (await PostActionAsync(client, appointment.Id, "arrived")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await PostActionAsync(client, appointment.Id, "complete")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await StatusAsync(appointment.Id)).ShouldBe(AppointmentStatus.Completed);
        (await GetAsync(client, $"{ListPath}?date={Iso(_day)}")).ShouldNotContain("data-testid=\"appointment\"");

        var second = await BookAsync(At(12, 0));
        (await PostActionAsync(client, second.Id, "no-show")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await StatusAsync(second.Id)).ShouldBe(AppointmentStatus.NoShow);
        var third = await BookAsync(At(13, 0));
        (await PostActionAsync(client, third.Id, "cancel")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await StatusAsync(third.Id)).ShouldBe(AppointmentStatus.CancelledBySalon);

        await using var scope = factory.CreateTenantScope(_studio.Id);
        (await scope.ServiceProvider.GetRequiredService<SalonDbContext>().AppointmentStatusChanges.CountAsync(c => c.AppointmentId == appointment.Id)).ShouldBe(4);

        (await PostActionAsync(client, Guid.NewGuid(), "confirm")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostActionAsync(client, second.Id, "teleport")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_polish_salon_sees_polish_labels_and_the_new_nav_links()
    {
        using var client = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);

        var html = await GetAsync(client, ListPath);

        html.ShouldContain("<html lang=\"pl-PL\"");
        html.ShouldContain("Wizyty");
        html.ShouldContain("Zarezerwuj");
        html.ShouldContain("data-testid=\"nav-appointments\"");
        html.ShouldContain("data-testid=\"nav-clients\"");
        html.ShouldContain("href=\"/admin/clients\"");
    }

    [Fact]
    public async Task A_receptionist_is_let_in_a_staff_member_is_denied_and_another_salon_sees_nothing()
    {
        var appointment = await BookAsync(At(10, 0));

        using var receptionist = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        (await GetAsync(receptionist, $"{ListPath}?date={Iso(_day)}")).ShouldContain("Olivia Brown");
        (await PostActionAsync(receptionist, appointment.Id, "confirm")).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        using var staff = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Staff);
        var denied = await staff.GetAsync(new Uri(ListPath, UriKind.Relative));
        denied.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");

        using var anonymous = factory.ClientFor(HostA);
        var challenge = await anonymous.GetAsync(new Uri(ListPath, UriKind.Relative));
        challenge.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        challenge.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");

        using var other = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        (await GetAsync(other, $"{ListPath}?date={Iso(_day)}")).ShouldNotContain("Olivia Brown");
        (await PostActionAsync(other, appointment.Id, "arrived")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await StatusAsync(appointment.Id)).ShouldBe(AppointmentStatus.Confirmed);

        using var platform = factory.ClientFor("localhost");
        (await platform.GetAsync(new Uri(ListPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_status_post_without_the_anti_forgery_token_is_refused()
    {
        var appointment = await BookAsync(At(10, 0));
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var response = await client.PostAsync(new Uri($"{ListPath}/{appointment.Id}/confirm", UriKind.Relative), new FormUrlEncodedContent([]));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await StatusAsync(appointment.Id)).ShouldBe(AppointmentStatus.Booked);
    }

    private static string Iso(LocalDate day) => LocalDatePattern.Iso.Format(day);

    private static int Count(string html, string needle) => html.Split(needle).Length - 1;

    /// <summary>The page as text: entities decoded, so names with an ampersand and the en dash read like the screen.</summary>
    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>GETs the day for its anti-forgery token (the cookie half rides on the client) and POSTs the status action with it.</summary>
    private async Task<HttpResponseMessage> PostActionAsync(HttpClient client, Guid appointmentId, string transition)
    {
        var token = IdentityTestClient.AntiForgeryToken(await GetAsync(client, $"{ListPath}?date={Iso(_day)}"));
        return await client.PostAsync(
            new Uri($"{ListPath}/{appointmentId}/{transition}", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
    }

    private Instant At(int hour, int minute) => London.AtLeniently(_day.At(new LocalTime(hour, minute))).ToInstant();

    private Task<Appointment> BookAsync(Instant start)
        => InTenantAsync(_studio.Id, sp => sp.GetRequiredService<IBookingService>().BookAsync(
            new BookAppointmentRequest(_clientId, _annaId, _serviceId, start, AppointmentSource.Online)));

    private Task<AppointmentStatus> StatusAsync(Guid appointmentId)
        => InTenantAsync(_studio.Id, sp => sp.GetRequiredService<SalonDbContext>().Appointments.AsNoTracking().Where(a => a.Id == appointmentId).Select(a => a.Status).SingleAsync());

    private async Task<T> InTenantAsync<T>(Guid tenantId, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await action(scope.ServiceProvider);
    }

    private async Task<(Guid ClientId, Guid StaffId, Guid ServiceId)> SeedSalonAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var now = SystemClock.Instance.GetCurrentInstant();
        var anna = new StaffMember { DisplayName = "Anna Kowalska", Colour = "#BE185D", SortOrder = 1, CreatedAt = now };
        db.StaffMembers.Add(anna);
        foreach (var day in WorkingHoursRules.Week)
        {
            db.WorkingHours.Add(new WorkingHours { StaffMemberId = anna.Id, Day = day, Start = new LocalTime(9, 0), End = new LocalTime(17, 0) });
        }

        db.StaffMembers.Add(new StaffMember { DisplayName = "Tom Reed", Colour = "#1D4ED8", SortOrder = 2, CreatedAt = now });
        var cut = new Service { Name = "Cut", Duration = Duration.FromMinutes(30), Price = 20m, Currency = "GBP" };
        db.Services.Add(cut);
        var client = new Client { FirstName = "Olivia", LastName = "Brown", Phone = "+447700900123", CreatedAt = now, UpdatedAt = now };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return (client.Id, anna.Id, cut.Id);
    }
}