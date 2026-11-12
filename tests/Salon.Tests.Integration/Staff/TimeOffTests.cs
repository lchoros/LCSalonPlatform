using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NodaTime;
using NodaTime.Text;

using Salon.Core.Appointments;
using Salon.Core.Catalog;
using Salon.Core.Clients;
using Salon.Core.Events;
using Salon.Core.Identity;
using Salon.Core.Staff;
using Salon.Core.Staff.Events;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Appointments;
using Salon.Services.Availability;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Staff;

/// <summary>
/// The TimeOff slice end to end over HTTP as a signed-in Owner (ADR 0015): the page, adding whole-day and partial entries
/// (rows carry the tenant id, an event goes to the outbox), an overlap refused with a localized message, field errors in
/// Polish, deleting, past entries collapsed, and the gates (Staff.Manage, another salon). Then the engine: a day off
/// yields no slots, a partial day only the free part, and booking into time off is refused. Tom works every day 09:00–17:00
/// and the salon sells one 30-minute service; entries are placed from tomorrow on, so notice and advance rules never interfere.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class TimeOffTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly LocalDatePattern IsoDate = LocalDatePattern.Iso;

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _tomId;
    private Guid _ewaId;
    private Guid _serviceId;
    private Guid _clientId;
    private LocalDate _tomorrow;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        (_tomId, _serviceId, _clientId) = await SeedSalonAsync(_studio.Id, "Tom Reed");
        (_ewaId, _, _) = await SeedSalonAsync(_polish.Id, "Ewa Nowak");
        _tomorrow = SystemClock.Instance.GetCurrentInstant().InZone(London).Date.PlusDays(1);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_page_shows_the_person_an_empty_form_and_the_empty_state()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var html = await GetPageAsync(client, _tomId);

        html.ShouldContain("data-testid=\"time-off-page\"");
        html.ShouldContain("data-testid=\"staff-name\">Tom Reed<");
        html.ShouldContain("data-testid=\"time-off-form\"");
        html.ShouldContain("__RequestVerificationToken");
        html.ShouldContain("data-testid=\"empty-state\"");
        html.ShouldNotContain("data-testid=\"past\"");
        InputTag(html, "FromDate").ShouldContain("type=\"date\"");
        InputTag(html, "Start").ShouldContain("type=\"time\"");
        InputTag(html, "Start").ShouldContain("step=\"300\"");
        html.ShouldContain("Reason (visible to the salon only)");
        html.ShouldContain($"href=\"/admin/staff/{_tomId}/hours\"");
    }

    [Fact]
    public async Task The_staff_list_and_the_hours_page_link_to_time_off()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var list = await (await client.GetAsync(new Uri("/admin/staff", UriKind.Relative))).Content.ReadAsStringAsync();
        var hours = await (await client.GetAsync(new Uri($"/admin/staff/{_tomId}/hours", UriKind.Relative))).Content.ReadAsStringAsync();

        list.ShouldContain($"href=\"/admin/staff/{_tomId}/time-off\" data-testid=\"time-off\"");
        list.ShouldContain(">Time off<");
        hours.ShouldContain($"href=\"/admin/staff/{_tomId}/time-off\" data-testid=\"time-off\"");
    }

    [Fact]
    public async Task Adding_whole_days_stores_a_row_for_the_tenant_publishes_an_event_and_lists_the_range()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);
        var from = _tomorrow;
        var to = _tomorrow.PlusDays(4);

        var response = await PostAddAsync(client, _tomId, Fields(from, to, reason: "  Holiday  "));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"/admin/staff/{_tomId}/time-off");

        var row = (await RowsAsync(_studio.Id)).ShouldHaveSingleItem();
        row.TenantId.ShouldBe(_studio.Id);
        row.StaffMemberId.ShouldBe(_tomId);
        row.FromDate.ShouldBe(from);
        row.ToDate.ShouldBe(to);
        row.Start.ShouldBeNull();
        row.End.ShouldBeNull();
        row.IsWholeDay.ShouldBeTrue();
        row.Reason.ShouldBe("Holiday");
        row.CreatedAt.ShouldBeGreaterThan(Instant.MinValue);

        var message = (await OutboxRowsAsync()).ShouldHaveSingleItem();
        message.TenantId.ShouldBe(_studio.Id);
        message.EventType.ShouldBe(typeof(StaffTimeOffChanged).FullName);
        message.Payload.ShouldContain(_tomId.ToString());
        message.Payload.ShouldNotContain("Holiday");

        var html = await (await client.GetAsync(response.Headers.Location!)).Content.ReadAsStringAsync();
        html.ShouldContain("Time off has been added.");
        html.ShouldContain("data-testid=\"time-off-entry\" data-whole-day=\"true\"");
        html.ShouldContain($"data-testid=\"entry-dates\">{Short(from, "en-GB")}–{Short(to, "en-GB")}<");
        html.ShouldContain("data-testid=\"entry-time\">Whole day<");
        html.ShouldContain("data-testid=\"entry-reason\">Holiday<");
        html.ShouldNotContain("data-testid=\"empty-state\"");
    }

    [Fact]
    public async Task Adding_a_partial_day_stores_the_times_and_shows_the_time_range()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var response = await PostAddAsync(client, _tomId, Fields(_tomorrow, _tomorrow, "14:00", "15:30"));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var row = (await RowsAsync(_studio.Id)).ShouldHaveSingleItem();
        row.Start.ShouldBe(new LocalTime(14, 0));
        row.End.ShouldBe(new LocalTime(15, 30));
        row.IsWholeDay.ShouldBeFalse();
        row.Reason.ShouldBeNull();

        var html = await (await client.GetAsync(response.Headers.Location!)).Content.ReadAsStringAsync();
        html.ShouldContain("data-testid=\"time-off-entry\" data-whole-day=\"false\"");
        html.ShouldContain($"data-testid=\"entry-dates\">{Short(_tomorrow, "en-GB")}<");
        html.ShouldContain("data-testid=\"entry-time\">14:00–15:30<");
        html.ShouldNotContain("data-testid=\"entry-reason\"");
    }

    [Fact]
    public async Task An_overlapping_entry_is_refused_with_a_message_and_nothing_is_stored()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);
        (await PostAddAsync(client, _tomId, Fields(_tomorrow, _tomorrow.PlusDays(2)))).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var wholeDay = await PostAddAsync(client, _tomId, Fields(_tomorrow.PlusDays(2), _tomorrow.PlusDays(5)));
        var partial = await PostAddAsync(client, _tomId, Fields(_tomorrow.PlusDays(1), _tomorrow.PlusDays(1), "10:00", "11:00", "Dentist"));

        foreach (var response in new[] { wholeDay, partial })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var html = await response.Content.ReadAsStringAsync();
            html.ShouldContain("data-testid=\"form-error\"");
            html.ShouldContain("This person already has time off in that period.");
            html.ShouldContain("data-testid=\"staff-name\">Tom Reed<");
        }

        InputTag(await partial.Content.ReadAsStringAsync(), "Reason").ShouldContain("value=\"Dentist\"");
        (await RowsAsync(_studio.Id)).Count.ShouldBe(1);
        (await OutboxRowsAsync()).Count.ShouldBe(1);

        // Adjacent days and a non-overlapping range on a busy day are fine.
        (await PostAddAsync(client, _tomId, Fields(_tomorrow.PlusDays(3), _tomorrow.PlusDays(3)))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await PostAddAsync(client, _tomId, Fields(_tomorrow.PlusDays(4), _tomorrow.PlusDays(4), "09:00", "10:00"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await PostAddAsync(client, _tomId, Fields(_tomorrow.PlusDays(4), _tomorrow.PlusDays(4), "10:00", "11:00"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await RowsAsync(_studio.Id)).Count.ShouldBe(4);
    }

    [Fact]
    public async Task Field_errors_come_in_the_salon_language_next_to_their_field()
    {
        using var client = await OwnerClientAsync("salon-pl.localhost", _polish.Id);

        var inverted = await PostAddAsync(client, _ewaId, Fields(_tomorrow.PlusDays(1), _tomorrow, "09:03", null, new string('x', 201)));
        var missing = await PostAddAsync(client, _ewaId, new Dictionary<string, string> { ["FromDate"] = "", ["ToDate"] = "not-a-date", ["Start"] = "", ["End"] = "", ["Reason"] = "" });
        var backwards = await PostAddAsync(client, _ewaId, Fields(_tomorrow, _tomorrow, "12:00", "09:00"));

        inverted.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await inverted.Content.ReadAsStringAsync();
        html.ShouldContain("<html lang=\"pl-PL\"");
        html.ShouldContain("Urlop i nieobecności");
        html.ShouldContain("Ostatni dzień nie może być wcześniejszy niż pierwszy.");
        html.ShouldContain("Godziny muszą być wielokrotnością 5 minut.");
        html.ShouldContain("Podaj godzinę początku i końca albo zostaw oba pola puste, aby zablokować całe dni.");
        html.ShouldContain("Powód może mieć najwyżej 200 znaków.");
        InputTag(html, "ToDate").ShouldContain("input-validation-error");
        InputTag(html, "Start").ShouldContain("input-validation-error");
        InputTag(html, "End").ShouldContain("input-validation-error");
        InputTag(html, "FromDate").ShouldNotContain("input-validation-error");
        html.ShouldNotContain("data-testid=\"form-error\"");

        var missingHtml = await missing.Content.ReadAsStringAsync();
        missingHtml.ShouldContain("Podaj pierwszy dzień.");
        missingHtml.ShouldContain("Podaj datę w formacie RRRR-MM-DD.");

        (await backwards.Content.ReadAsStringAsync()).ShouldContain("Godzina zakończenia musi być późniejsza niż godzina rozpoczęcia.");
        (await RowsAsync(_polish.Id)).ShouldBeEmpty();
        (await OutboxRowsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_removes_the_row_publishes_an_event_and_an_unknown_entry_is_not_found()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);
        (await PostAddAsync(client, _tomId, Fields(_tomorrow, _tomorrow))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var entryId = (await RowsAsync(_studio.Id)).Single().Id;
        var token = IdentityTestClient.AntiForgeryToken(await GetPageAsync(client, _tomId));

        var response = await client.PostAsync(
            new Uri($"/admin/staff/{_tomId}/time-off/{entryId}/delete", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"/admin/staff/{_tomId}/time-off");
        (await RowsAsync(_studio.Id)).ShouldBeEmpty();
        (await OutboxRowsAsync()).Count.ShouldBe(2);
        var html = await (await client.GetAsync(response.Headers.Location!)).Content.ReadAsStringAsync();
        html.ShouldContain("The time off has been removed.");
        html.ShouldContain("data-testid=\"empty-state\"");

        var again = await client.PostAsync(
            new Uri($"/admin/staff/{_tomId}/time-off/{entryId}/delete", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var noToken = await client.PostAsync(new Uri($"/admin/staff/{_tomId}/time-off/{Guid.NewGuid()}/delete", UriKind.Relative), new FormUrlEncodedContent([]));
        noToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Past_entries_are_collapsed_to_the_last_ten_and_upcoming_ones_come_soonest_first()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);
        var today = _tomorrow.PlusDays(-1);
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            for (var i = 1; i <= 12; i++)
            {
                db.StaffTimeOff.Add(new StaffTimeOff { StaffMemberId = _tomId, FromDate = today.PlusDays(-7 * i), ToDate = today.PlusDays((-7 * i) + 1), Reason = $"Past {i}", CreatedAt = SystemClock.Instance.GetCurrentInstant() });
            }

            db.StaffTimeOff.Add(new StaffTimeOff { StaffMemberId = _tomId, FromDate = today.PlusDays(-1), ToDate = today, Reason = "Ends today", CreatedAt = SystemClock.Instance.GetCurrentInstant() });
            db.StaffTimeOff.Add(new StaffTimeOff { StaffMemberId = _tomId, FromDate = today.PlusDays(10), ToDate = today.PlusDays(10), Reason = "Later", CreatedAt = SystemClock.Instance.GetCurrentInstant() });
            db.StaffTimeOff.Add(new StaffTimeOff { StaffMemberId = _tomId, FromDate = today.PlusDays(3), ToDate = today.PlusDays(3), Reason = "Sooner", CreatedAt = SystemClock.Instance.GetCurrentInstant() });
            await db.SaveChangesAsync();
        }

        var html = await GetPageAsync(client, _tomId);

        html.ShouldContain("Past (12)");
        PastEntryPattern().Count(html).ShouldBe(10);
        html.ShouldContain("Past 1<");
        html.ShouldContain("Past 10<");
        html.ShouldNotContain("Past 11<");
        html.ShouldContain("2 older entries are not shown.");
        html.IndexOf("Past 1<", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("Past 2<", StringComparison.Ordinal), "newest past entry first");
        var upcoming = html[..html.IndexOf("data-testid=\"past\"", StringComparison.Ordinal)];
        EntryPattern().Count(upcoming).ShouldBe(3);
        upcoming.IndexOf("Ends today<", StringComparison.Ordinal).ShouldBeLessThan(upcoming.IndexOf("Sooner<", StringComparison.Ordinal));
        upcoming.IndexOf("Sooner<", StringComparison.Ordinal).ShouldBeLessThan(upcoming.IndexOf("Later<", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_day_off_has_no_slots_a_partial_day_keeps_the_free_part_and_booking_into_time_off_is_refused()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);
        var dayOff = _tomorrow;
        var dentistDay = _tomorrow.PlusDays(1);
        (await SlotTimesAsync(dayOff)).Count.ShouldBe(31);

        (await PostAddAsync(client, _tomId, Fields(dayOff, dayOff))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await PostAddAsync(client, _tomId, Fields(dentistDay, dentistDay, "14:00", "15:00"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        (await SlotTimesAsync(dayOff)).ShouldBeEmpty();
        var dentist = await SlotTimesAsync(dentistDay);
        dentist.ShouldContain(new LocalTime(13, 30));
        dentist.ShouldNotContain(new LocalTime(13, 45));
        dentist.ShouldNotContain(new LocalTime(14, 0));
        dentist.ShouldNotContain(new LocalTime(14, 45));
        dentist.ShouldContain(new LocalTime(15, 0));
        dentist.Count.ShouldBe(26);
        (await SlotTimesAsync(_tomorrow.PlusDays(2))).Count.ShouldBe(31, "the next day is untouched");

        await Should.ThrowAsync<SlotNoLongerAvailableException>(() => BookAsync(At(dayOff, 10, 0)));
        await Should.ThrowAsync<SlotNoLongerAvailableException>(() => BookAsync(At(dentistDay, 14, 0)));
        await Should.ThrowAsync<SlotNoLongerAvailableException>(() => BookAsync(At(dentistDay, 13, 45)));
        var booked = await BookAsync(At(dentistDay, 15, 0));
        booked.Status.ShouldBe(AppointmentStatus.Booked);

        await using var scope = factory.CreateTenantScope(_studio.Id);
        (await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Appointments.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Another_salon_sees_nothing_and_cannot_add_or_delete()
    {
        using var studio = await OwnerClientAsync("studio-20.localhost", _studio.Id);
        using var polish = await OwnerClientAsync("salon-pl.localhost", _polish.Id);
        (await PostAddAsync(studio, _tomId, Fields(_tomorrow, _tomorrow))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var entryId = (await RowsAsync(_studio.Id)).Single().Id;

        (await polish.GetAsync(new Uri($"/admin/staff/{_tomId}/time-off", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await polish.GetAsync(new Uri($"/admin/staff/{Guid.NewGuid()}/time-off", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var token = IdentityTestClient.AntiForgeryToken(await GetPageAsync(polish, _ewaId));
        var add = Fields(_tomorrow.PlusDays(3), _tomorrow.PlusDays(3));
        add["__RequestVerificationToken"] = token;
        (await polish.PostAsync(new Uri($"/admin/staff/{_tomId}/time-off", UriKind.Relative), new FormUrlEncodedContent(add))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var delete = await polish.PostAsync(
            new Uri($"/admin/staff/{_ewaId}/time-off/{entryId}/delete", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        delete.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await RowsAsync(_studio.Id)).Count.ShouldBe(1);
        (await RowsAsync(_polish.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_receptionist_is_denied_and_an_anonymous_visitor_is_asked_to_sign_in()
    {
        using var receptionist = await factory.SignedInClientAsync("studio-20.localhost", _studio.Id, Role.Receptionist);
        using var anonymous = factory.ClientFor("studio-20.localhost");

        var denied = await receptionist.GetAsync(new Uri($"/admin/staff/{_tomId}/time-off", UriKind.Relative));
        denied.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");

        var login = await anonymous.GetAsync(new Uri($"/admin/staff/{_tomId}/time-off", UriKind.Relative));
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        login.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");
    }

    private static Dictionary<string, string> Fields(LocalDate from, LocalDate to, string? start = null, string? end = null, string? reason = null)
        => new()
        {
            ["FromDate"] = IsoDate.Format(from),
            ["ToDate"] = IsoDate.Format(to),
            ["Start"] = start ?? string.Empty,
            ["End"] = end ?? string.Empty,
            ["Reason"] = reason ?? string.Empty,
        };

    /// <summary>The date as the page shows it: NodaTime's short pattern of the request culture.</summary>
    private static string Short(LocalDate date, string culture) => LocalDatePattern.Create("d", CultureInfo.GetCultureInfo(culture)).Format(date);

    private static string InputTag(string html, string name)
    {
        var match = Regex.Match(html, $"<input[^>]*name=\"{Regex.Escape(name)}\"[^>]*>");
        match.Success.ShouldBeTrue($"no input named {name}");
        return match.Value;
    }

    private static async Task<string> GetPageAsync(HttpClient client, Guid staffId)
    {
        var response = await client.GetAsync(new Uri($"/admin/staff/{staffId}/time-off", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<HttpResponseMessage> PostAddAsync(HttpClient client, Guid staffId, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(await GetPageAsync(client, staffId));
        return await client.PostAsync(new Uri($"/admin/staff/{staffId}/time-off", UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    private Task<HttpClient> OwnerClientAsync(string host, Guid tenantId) => factory.SignedInClientAsync(host, tenantId, Role.Owner);

    private static Instant At(LocalDate day, int hour, int minute) => London.AtLeniently(day.At(new LocalTime(hour, minute))).ToInstant();

    private async Task<List<LocalTime>> SlotTimesAsync(LocalDate day)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var slots = await scope.ServiceProvider.GetRequiredService<IAvailabilityService>().GetSlotsAsync(_serviceId, _tomId, day, day);
        return slots.Select(s => s.Start.InZone(London).TimeOfDay).ToList();
    }

    private async Task<Appointment> BookAsync(Instant start)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await scope.ServiceProvider.GetRequiredService<IBookingService>()
            .BookAsync(new BookAppointmentRequest(_clientId, _tomId, _serviceId, start, AppointmentSource.Admin, null, null));
    }

    private async Task<(Guid StaffId, Guid ServiceId, Guid ClientId)> SeedSalonAsync(Guid tenantId, string name)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var staff = new StaffMember { DisplayName = name, CreatedAt = SystemClock.Instance.GetCurrentInstant() };
        db.StaffMembers.Add(staff);
        foreach (var day in WorkingHoursRules.Week)
        {
            db.WorkingHours.Add(new WorkingHours { StaffMemberId = staff.Id, Day = day, Start = new LocalTime(9, 0), End = new LocalTime(17, 0) });
        }

        var cut = new Service { Name = "Cut", Duration = Duration.FromMinutes(30), Price = 20m, Currency = "GBP" };
        db.Services.Add(cut);
        var client = new Client { FirstName = "Olivia", LastName = "Brown", CreatedAt = staff.CreatedAt, UpdatedAt = staff.CreatedAt };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return (staff.Id, cut.Id, client.Id);
    }

    private async Task<List<StaffTimeOff>> RowsAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().StaffTimeOff.AsNoTracking().OrderBy(t => t.FromDate).ToListAsync();
    }

    private async Task<List<OutboxMessage>> OutboxRowsAsync()
    {
        await using var scope = factory.CreateTenantScope(null);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().OutboxMessages.AsNoTracking().OrderBy(m => m.Id).ToListAsync();
    }

    [GeneratedRegex("data-testid=\"time-off-entry\"")]
    private static partial Regex EntryPattern();

    [GeneratedRegex("data-testid=\"past-entry\"")]
    private static partial Regex PastEntryPattern();
}