using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Appointments;
using Salon.Core.Appointments.Events;
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
/// The admin calendar over HTTP (ADR 0016): the day grid places each person's active appointments in their column with
/// their colour and hides cancelled ones, the week grid shows one person Monday to Sunday, the card offers free times
/// without the one the visit holds, a move rewrites the row with a "Moved" history line and an outbox event, a taken slot
/// and a cancelled visit are refused with a message, and the gates hold (Bookings.Manage + Clients.View, tenant,
/// anti-forgery). The salon has Anna (every day 09:00–17:00) and Tom (every day 10:00–14:00), one 30-minute service and one client.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class CalendarTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string CalendarPath = "/admin/appointments/calendar";
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _annaId;
    private Guid _tomId;
    private Guid _serviceId;
    private Guid _clientId;
    private LocalDate _day;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        (_clientId, _annaId, _tomId, _serviceId) = await SeedSalonAsync(_studio.Id);
        await SeedSalonAsync(_polish.Id);
        _day = SystemClock.Instance.GetCurrentInstant().InZone(London).Date.PlusDays(1);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_day_view_places_appointments_in_their_staff_columns_with_colours_and_hides_cancelled_ones()
    {
        await BookAsync(_annaId, At(14, 0));
        await BookAsync(_annaId, At(10, 0));
        var cancelled = await BookAsync(_annaId, At(11, 0));
        await InTenantAsync(_studio.Id, sp => sp.GetRequiredService<IBookingService>().CancelAsync(cancelled.Id, byClient: true, null));
        var tomsVisit = await BookAsync(_tomId, At(11, 0));
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var html = await GetAsync(client, DayPath(_day, null));

        html.ShouldContain("<html lang=\"en-GB\"");
        html.ShouldContain("data-testid=\"nav-calendar\"");
        Count(html, "data-testid=\"calendar-column\"").ShouldBe(2);
        html.IndexOf("Anna Kowalska", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("Tom Reed", StringComparison.Ordinal), "staff in sort order");
        var blocks = Blocks(html);
        blocks.Count.ShouldBe(3, "the cancelled one is not drawn");
        blocks.Count(b => b.StaffId == _annaId).ShouldBe(2);
        blocks.Single(b => b.StaffId == _tomId).Id.ShouldBe(tomsVisit.Id);
        blocks.ShouldAllBe(b => b.Status == "Booked");
        html.ShouldContain("border-color: #BE185D", Case.Sensitive, "Anna's colour edges her blocks");
        html.ShouldContain("background-color: #BE185D26");
        html.ShouldContain("border-color: #1D4ED8");
        html.ShouldContain("Olivia");
        html.ShouldContain("Cut");
        html.ShouldContain("10:00");
        html.ShouldContain("14:00");
        html.ShouldContain("09:00", Case.Sensitive, "the grid starts at the earliest working start");
        html.ShouldNotContain(">08:00<", Case.Sensitive, "no 08:00 fallback when the rota is known");
        html.ShouldContain("data-working=\"false\"", Case.Sensitive, "Tom's 09:00 row is outside his hours");
        html.ShouldContain("data-working=\"true\"");
        Count(html, "data-testid=\"calendar-list-item\"").ShouldBe(3, "the phone list mirrors the grid");
        html.ShouldContain("data-testid=\"staff-chip-all\" aria-current=\"page\"");
        Count(html, "data-testid=\"staff-chip\"").ShouldBe(2);
        html.ShouldContain($"href=\"{DayPath(_day.PlusDays(-1), null)}\"");
        html.ShouldContain($"href=\"{DayPath(_day.PlusDays(1), null)}\"");
        html.ShouldContain($"href=\"/admin/appointments/new?date={Iso(_day)}\"");
        html.ShouldContain($"href=\"/admin/appointments?date={Iso(_day)}\"");
        html.ShouldContain($"href=\"{CalendarPath}/{tomsVisit.Id}\"");
        html.ShouldNotContain("<script", Case.Sensitive, "the calendar ships no inline script");

        var filtered = await GetAsync(client, DayPath(_day, _annaId));
        Count(filtered, "data-testid=\"calendar-column\"").ShouldBe(1);
        Blocks(filtered).Count.ShouldBe(2);
        filtered.ShouldNotContain("data-testid=\"calendar-list\"", Case.Sensitive, "one person's day is a grid on every screen");
        filtered.ShouldContain($"href=\"/admin/appointments/new?date={Iso(_day)}&staffId={_annaId}\"");

        var today = SystemClock.Instance.GetCurrentInstant().InZone(London).Date;
        var defaultDay = await GetAsync(client, CalendarPath);
        defaultDay.ShouldContain($"href=\"{DayPath(today.PlusDays(1), null)}\"", Case.Sensitive, "without a date the calendar shows today in the salon zone");
        defaultDay.ShouldNotContain("Olivia");
    }

    [Fact]
    public async Task The_week_view_shows_one_person_from_monday_to_sunday_and_defaults_to_the_first_bookable()
    {
        var annas = await BookAsync(_annaId, At(10, 0));
        await BookAsync(_tomId, At(11, 0));
        var monday = _day.PlusDays(-((int)_day.DayOfWeek - 1));
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var html = await GetAsync(client, $"{CalendarPath}?view=week&date={Iso(_day)}");

        Count(html, "data-testid=\"calendar-column\"").ShouldBe(7);
        for (var i = 0; i < 7; i++)
        {
            html.ShouldContain($"data-date=\"{Iso(monday.PlusDays(i))}\"");
        }

        var blocks = Blocks(html);
        blocks.ShouldHaveSingleItem().Id.ShouldBe(annas.Id, "the first bookable person's week by default");
        html.ShouldContain($"data-testid=\"staff-chip\" data-staff-id=\"{_annaId}\" aria-current=\"page\"");
        html.ShouldNotContain("data-testid=\"staff-chip-all\"", Case.Sensitive, "a week always belongs to one person");
        html.ShouldContain($"href=\"{CalendarPath}?view=week&date={Iso(_day.PlusDays(-7))}&staff={_annaId}\"");
        html.ShouldContain($"href=\"{CalendarPath}?view=week&date={Iso(_day.PlusDays(7))}&staff={_annaId}\"");
        html.ShouldContain("data-testid=\"view-week\" aria-current=\"page\"");
        html.ShouldContain("min-w-[42rem]", Case.Sensitive, "the week scrolls inside its own box");

        var toms = await GetAsync(client, $"{CalendarPath}?view=week&date={Iso(_day)}&staff={_tomId}");
        Blocks(toms).ShouldHaveSingleItem().StaffId.ShouldBe(_tomId);
    }

    [Fact]
    public async Task The_card_offers_free_times_without_the_current_one_and_a_move_rewrites_the_row_with_history_and_an_event()
    {
        var appointment = await BookAsync(_annaId, At(10, 0));
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var card = await GetAsync(client, $"{CalendarPath}/{appointment.Id}");
        card.ShouldContain("Olivia Brown");
        card.ShouldContain("Cut");
        card.ShouldContain("Anna Kowalska");
        card.ShouldContain("10:00–10:30");
        card.ShouldContain("£20.00");
        card.ShouldContain("data-testid=\"action-confirm\"");
        card.ShouldContain($"action=\"/admin/appointments/{appointment.Id}/cancel\"");
        card.ShouldContain("data-testid=\"move-step\"");
        card.ShouldContain("data-testid=\"move-slot\"");
        card.ShouldNotContain($"value=\"{InstantPattern.ExtendedIso.Format(At(10, 0))}\"", Case.Sensitive, "the time the visit holds now is not offered");
        card.ShouldContain($"value=\"{InstantPattern.ExtendedIso.Format(At(10, 30))}\"", Case.Sensitive, "the slot right after is free: the visit does not block itself");
        card.ShouldContain($"value=\"{InstantPattern.ExtendedIso.Format(At(16, 30))}\"");

        var tomsDay = await GetAsync(client, $"{CalendarPath}/{appointment.Id}?staffId={_tomId}&date={Iso(_day)}");
        tomsDay.ShouldContain($"<option value=\"{_tomId}\" selected");
        tomsDay.ShouldContain($"value=\"{InstantPattern.ExtendedIso.Format(At(10, 0))}\"", Case.Sensitive, "Tom is free at 10:00");
        tomsDay.ShouldNotContain($"value=\"{InstantPattern.ExtendedIso.Format(At(9, 0))}\"", Case.Sensitive, "Tom starts at 10:00");

        var response = await PostMoveAsync(client, appointment.Id, _tomId, At(11, 0));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe(DayPath(_day, _tomId));
        var after = await GetAsync(client, response.Headers.Location.OriginalString);
        after.ShouldContain("data-testid=\"flash\"");
        after.ShouldContain("Olivia Brown");
        after.ShouldContain("Tom Reed");
        after.ShouldContain("11:00");
        Blocks(after).ShouldHaveSingleItem().StaffId.ShouldBe(_tomId);

        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var row = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == appointment.Id);
        row.StaffMemberId.ShouldBe(_tomId);
        row.Start.ShouldBe(At(11, 0));
        row.End.ShouldBe(At(11, 30));
        row.Status.ShouldBe(AppointmentStatus.Booked);
        row.DurationMinutes.ShouldBe(30);
        row.PriceAmount.ShouldBe(20m);
        var changes = await db.AppointmentStatusChanges.AsNoTracking().Where(c => c.AppointmentId == appointment.Id).OrderBy(c => c.At).ToListAsync();
        changes.Count.ShouldBe(2);
        changes[1].From.ShouldBe(AppointmentStatus.Booked);
        changes[1].To.ShouldBe(AppointmentStatus.Booked);
        changes[1].Reason.ShouldBe("Moved");
        changes[1].ByUserId.ShouldNotBeNull("the signed-in owner is recorded");
        var moved = (await db.OutboxMessages.AsNoTracking().Where(m => m.EventType == typeof(AppointmentMoved).FullName).ToListAsync()).ShouldHaveSingleItem();
        moved.TenantId.ShouldBe(_studio.Id);
        moved.Payload.ShouldContain(appointment.Id.ToString());
        moved.Payload.ShouldContain(_tomId.ToString());
    }

    [Fact]
    public async Task Moving_onto_a_taken_slot_re_renders_the_card_with_a_message_and_fresh_times()
    {
        var first = await BookAsync(_annaId, At(10, 0));
        await BookAsync(_annaId, At(11, 0));
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var response = await PostMoveAsync(client, first.Id, _annaId, At(11, 0));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        html.ShouldContain("data-testid=\"error\"");
        html.ShouldContain("That time is already taken. Pick another one below.");
        html.ShouldContain("data-testid=\"move-slot\"");
        html.ShouldNotContain($"value=\"{InstantPattern.ExtendedIso.Format(At(11, 0))}\"");
        (await StartAsync(first.Id)).ShouldBe(At(10, 0));

        var blank = await PostMoveAsync(client, first.Id, null, At(12, 0));
        blank.StatusCode.ShouldBe(HttpStatusCode.OK);
        var refused = WebUtility.HtmlDecode(await blank.Content.ReadAsStringAsync());
        refused.ShouldContain("data-testid=\"validation-summary\"");
        refused.ShouldContain("Choose a staff member.");
        (await StartAsync(first.Id)).ShouldBe(At(10, 0));
    }

    [Fact]
    public async Task A_cancelled_appointment_has_no_move_form_and_a_move_post_is_refused()
    {
        var appointment = await BookAsync(_annaId, At(10, 0));
        await InTenantAsync(_studio.Id, sp => sp.GetRequiredService<IBookingService>().CancelAsync(appointment.Id, byClient: false, "Stylist ill"));
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var card = await GetAsync(client, $"{CalendarPath}/{appointment.Id}");
        card.ShouldContain("Cancelled by salon");
        card.ShouldNotContain("data-testid=\"move-step\"");
        card.ShouldNotContain("data-testid=\"card-actions\"");

        var response = await PostMoveAsync(client, appointment.Id, _annaId, At(12, 0));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        html.ShouldContain("Only booked, confirmed or arrived appointments can be moved.");
        (await StartAsync(appointment.Id)).ShouldBe(At(10, 0));

        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        (await db.OutboxMessages.AsNoTracking().CountAsync(m => m.EventType == typeof(AppointmentMoved).FullName)).ShouldBe(0);
    }

    [Fact]
    public async Task A_polish_salon_sees_polish_labels()
    {
        using var client = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);

        var html = await GetAsync(client, CalendarPath);

        html.ShouldContain("<html lang=\"pl-PL\"");
        html.ShouldContain("Kalendarz");
        html.ShouldContain("Dzień");
        html.ShouldContain("Tydzień");
        html.ShouldContain("Wszyscy");
        html.ShouldContain("Zarezerwuj");
    }

    [Fact]
    public async Task A_staff_member_is_denied_and_another_salon_sees_nothing()
    {
        var appointment = await BookAsync(_annaId, At(10, 0));

        using var staff = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Staff);
        var denied = await staff.GetAsync(new Uri(CalendarPath, UriKind.Relative));
        denied.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");

        using var anonymous = factory.ClientFor(HostA);
        var challenge = await anonymous.GetAsync(new Uri(CalendarPath, UriKind.Relative));
        challenge.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        challenge.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");

        using var other = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        (await GetAsync(other, DayPath(_day, null))).ShouldNotContain("Olivia");
        (await other.GetAsync(new Uri($"{CalendarPath}/{appointment.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var token = IdentityTestClient.AntiForgeryToken(await GetAsync(other, "/admin/appointments"));
        var move = await other.PostAsync(
            new Uri($"{CalendarPath}/{appointment.Id}/move", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["staffMemberId"] = _annaId.ToString(),
                ["start"] = InstantPattern.ExtendedIso.Format(At(12, 0)),
                ["date"] = Iso(_day),
            }));
        move.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await StartAsync(appointment.Id)).ShouldBe(At(10, 0));

        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var untokened = await owner.PostAsync(
            new Uri($"{CalendarPath}/{appointment.Id}/move", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string> { ["staffMemberId"] = _annaId.ToString(), ["start"] = InstantPattern.ExtendedIso.Format(At(12, 0)) }));
        untokened.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await StartAsync(appointment.Id)).ShouldBe(At(10, 0));
    }

    private static string Iso(LocalDate day) => LocalDatePattern.Iso.Format(day);

    private static string DayPath(LocalDate day, Guid? staff) => $"{CalendarPath}?view=day&date={Iso(day)}{(staff is { } id ? $"&staff={id}" : string.Empty)}";

    private static int Count(string html, string needle) => html.Split(needle).Length - 1;

    /// <summary>The grid blocks of a page: appointment id, column staff id and status, as the view's data attributes say.</summary>
    private static List<(Guid Id, Guid StaffId, string Status)> Blocks(string html)
        => BlockPattern().Matches(html)
            .Select(m => (Guid.Parse(m.Groups["id"].Value), Guid.Parse(m.Groups["staff"].Value), m.Groups["status"].Value))
            .ToList();

    /// <summary>The page as text: entities decoded, so names with an ampersand and the en dash read like the screen.</summary>
    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>GETs the card for its anti-forgery token (the cookie half rides on the client) and POSTs the move with it.</summary>
    private async Task<HttpResponseMessage> PostMoveAsync(HttpClient client, Guid appointmentId, Guid? staffMemberId, Instant start)
    {
        var token = IdentityTestClient.AntiForgeryToken(await GetAsync(client, $"{CalendarPath}/{appointmentId}"));
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["start"] = InstantPattern.ExtendedIso.Format(start),
            ["date"] = Iso(_day),
        };
        if (staffMemberId is { } staff)
        {
            fields["staffMemberId"] = staff.ToString();
        }

        return await client.PostAsync(new Uri($"{CalendarPath}/{appointmentId}/move", UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    private Instant At(int hour, int minute) => London.AtLeniently(_day.At(new LocalTime(hour, minute))).ToInstant();

    private Task<Appointment> BookAsync(Guid staffId, Instant start)
        => InTenantAsync(_studio.Id, sp => sp.GetRequiredService<IBookingService>().BookAsync(
            new BookAppointmentRequest(_clientId, staffId, _serviceId, start, AppointmentSource.Online)));

    private Task<Instant> StartAsync(Guid appointmentId)
        => InTenantAsync(_studio.Id, sp => sp.GetRequiredService<SalonDbContext>().Appointments.AsNoTracking().Where(a => a.Id == appointmentId).Select(a => a.Start).SingleAsync());

    private async Task<T> InTenantAsync<T>(Guid tenantId, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await action(scope.ServiceProvider);
    }

    private async Task<(Guid ClientId, Guid AnnaId, Guid TomId, Guid ServiceId)> SeedSalonAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var now = SystemClock.Instance.GetCurrentInstant();
        var anna = new StaffMember { DisplayName = "Anna Kowalska", Colour = "#BE185D", SortOrder = 1, CreatedAt = now };
        var tom = new StaffMember { DisplayName = "Tom Reed", Colour = "#1D4ED8", SortOrder = 2, CreatedAt = now };
        db.StaffMembers.AddRange(anna, tom);
        foreach (var day in WorkingHoursRules.Week)
        {
            db.WorkingHours.Add(new WorkingHours { StaffMemberId = anna.Id, Day = day, Start = new LocalTime(9, 0), End = new LocalTime(17, 0) });
            db.WorkingHours.Add(new WorkingHours { StaffMemberId = tom.Id, Day = day, Start = new LocalTime(10, 0), End = new LocalTime(14, 0) });
        }

        var cut = new Service { Name = "Cut", Duration = Duration.FromMinutes(30), Price = 20m, Currency = "GBP" };
        db.Services.Add(cut);
        var client = new Client { FirstName = "Olivia", LastName = "Brown", Phone = "+447700900123", CreatedAt = now, UpdatedAt = now };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return (client.Id, anna.Id, tom.Id, cut.Id);
    }

    [GeneratedRegex("data-testid=\"calendar-block\"\\s+data-appointment-id=\"(?<id>[^\"]+)\"\\s+data-staff-id=\"(?<staff>[^\"]+)\"\\s+data-status=\"(?<status>[^\"]+)\"")]
    private static partial Regex BlockPattern();
}