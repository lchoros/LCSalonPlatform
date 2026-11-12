using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NodaTime;

using Npgsql;

using Salon.Core.Appointments;
using Salon.Core.Appointments.Events;
using Salon.Core.Catalog;
using Salon.Core.Clients;
using Salon.Core.Events;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Appointments;
using Salon.Services.Availability;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Appointments;

/// <summary>
/// ADR 0012 end to end against Postgres: booking writes the appointment, its history and an outbox event; the exclusion
/// constraint settles races; cancelling frees the slot; transitions go through the state machine; tenants never see each
/// other's appointments. The salon has one stylist (Anna, every day 09:00–17:00), one client and one 30-minute service; bookings are
/// made for tomorrow so the default notice and advance rules never interfere.
/// </summary>
[Collection(SharedDatabase.Name)]
public class BookingServiceTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly Guid UserId = Guid.Parse("0190a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5c");

    private Tenant _tenantA = null!;
    private Tenant _tenantB = null!;
    private Guid _clientId;
    private Guid _annaId;
    private Guid _serviceId;
    private LocalDate _day;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _tenantA = await factory.CreateTenantAsync("appt-a");
        _tenantB = await factory.CreateTenantAsync("appt-b", "pl-PL", "PLN");
        (_clientId, _annaId, _serviceId) = await SeedSalonAsync(_tenantA.Id);
        _day = SystemClock.Instance.GetCurrentInstant().InZone(London).Date.PlusDays(1);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Booking_writes_the_appointment_its_first_status_change_and_an_event_without_personal_data()
    {
        var before = SystemClock.Instance.GetCurrentInstant();

        var appointment = await BookAsync(_tenantA.Id, At(10, 0), notes: "Prefers the window seat");

        await using var scope = factory.CreateTenantScope(_tenantA.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var row = await db.Appointments.SingleAsync();
        row.Id.ShouldBe(appointment.Id);
        row.TenantId.ShouldBe(_tenantA.Id);
        row.ClientId.ShouldBe(_clientId);
        row.StaffMemberId.ShouldBe(_annaId);
        row.ServiceId.ShouldBe(_serviceId);
        row.Start.ShouldBe(At(10, 0));
        row.End.ShouldBe(At(10, 30));
        row.Status.ShouldBe(AppointmentStatus.Booked);
        row.Source.ShouldBe(AppointmentSource.Admin);
        row.Notes.ShouldBe("Prefers the window seat");
        row.PriceAmount.ShouldBe(20m);
        row.Currency.ShouldBe("GBP");
        row.DurationMinutes.ShouldBe(30);
        row.CreatedAt.ShouldBeGreaterThanOrEqualTo(before);
        row.CreatedByUserId.ShouldBe(UserId);
        row.CancelledAt.ShouldBeNull();

        var change = await db.AppointmentStatusChanges.SingleAsync();
        change.AppointmentId.ShouldBe(appointment.Id);
        change.TenantId.ShouldBe(_tenantA.Id);
        change.From.ShouldBeNull();
        change.To.ShouldBe(AppointmentStatus.Booked);
        change.At.ShouldBe(row.CreatedAt);
        change.ByUserId.ShouldBe(UserId);

        var message = (await OutboxRowsAsync()).ShouldHaveSingleItem();
        message.TenantId.ShouldBe(_tenantA.Id);
        message.EventType.ShouldBe(typeof(AppointmentBooked).FullName);
        using var payload = JsonDocument.Parse(message.Payload);
        payload.RootElement.GetProperty("appointmentId").GetGuid().ShouldBe(appointment.Id);
        payload.RootElement.GetProperty("clientId").GetGuid().ShouldBe(_clientId);
        payload.RootElement.GetProperty("start").GetString().ShouldBe(At(10, 0).ToString());
        payload.RootElement.EnumerateObject().Select(p => p.Name)
            .ShouldBe(["appointmentId", "tenantId", "clientId", "staffMemberId", "serviceId", "start", "end", "source"], ignoreOrder: true);
        message.Payload.ShouldNotContain("window seat");
        message.Payload.ShouldNotContain("Anna");
    }

    [Fact]
    public async Task Booking_the_same_person_at_an_overlapping_time_is_refused_and_leaves_one_row()
    {
        await BookAsync(_tenantA.Id, At(10, 0));

        await Should.ThrowAsync<SlotNoLongerAvailableException>(() => BookAsync(_tenantA.Id, At(10, 0)));
        await Should.ThrowAsync<SlotNoLongerAvailableException>(() => BookAsync(_tenantA.Id, At(10, 15)));
        await Should.ThrowAsync<SlotNoLongerAvailableException>(() => BookAsync(_tenantA.Id, At(9, 45)));

        (await CountAppointmentsAsync(_tenantA.Id)).ShouldBe(1);
        (await OutboxRowsAsync()).Count.ShouldBe(1);
        await BookAsync(_tenantA.Id, At(10, 30)); // the slot right after is fine
    }

    [Fact]
    public async Task Two_bookings_racing_for_the_same_slot_let_only_one_through()
    {
        await using var scopeA = factory.CreateTenantScope(_tenantA.Id);
        var dbA = scopeA.ServiceProvider.GetRequiredService<SalonDbContext>();
        await using var transaction = await dbA.Database.BeginTransactionAsync();
        var first = await scopeA.ServiceProvider.GetRequiredService<IBookingService>().BookAsync(Request(At(10, 0)));

        // The second booking cannot see the uncommitted row, so its slot check passes and its insert waits on the constraint.
        var second = BookAsync(_tenantA.Id, At(10, 0));
        await WaitUntilAnInsertWaitsForALockAsync();
        second.IsCompleted.ShouldBeFalse();
        await transaction.CommitAsync();

        var ex = await Should.ThrowAsync<SlotNoLongerAvailableException>(() => second);
        var postgres = ex.InnerException.ShouldBeOfType<DbUpdateException>().InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.ExclusionViolation);
        postgres.ConstraintName.ShouldBe("ex_appointments_staff_time");

        (await CountAppointmentsAsync(_tenantA.Id)).ShouldBe(1);
        (await OutboxRowsAsync()).ShouldHaveSingleItem().Payload.ShouldContain(first.Id.ToString());
    }

    [Fact]
    public async Task Two_raw_inserts_for_one_person_at_the_same_time_hit_the_exclusion_constraint_unless_one_is_cancelled()
    {
        await using var connection = new NpgsqlConnection(SalonWebApplicationFactory.TestConnectionString);
        await connection.OpenAsync();

        await InsertRawAsync(connection, At(10, 0), At(10, 30), AppointmentStatus.Booked);
        var ex = await Should.ThrowAsync<PostgresException>(() => InsertRawAsync(connection, At(10, 15), At(10, 45), AppointmentStatus.Confirmed));
        ex.SqlState.ShouldBe(PostgresErrorCodes.ExclusionViolation);
        ex.ConstraintName.ShouldBe("ex_appointments_staff_time");

        // Touching ranges do not overlap, and cancelled or finished rows do not hold the time.
        await InsertRawAsync(connection, At(10, 30), At(11, 0), AppointmentStatus.Arrived);
        await InsertRawAsync(connection, At(10, 15), At(10, 45), AppointmentStatus.CancelledByClient);
        await InsertRawAsync(connection, At(10, 15), At(10, 45), AppointmentStatus.Completed);
        (await CountAppointmentsAsync(_tenantA.Id)).ShouldBe(4);
    }

    [Fact]
    public async Task Cancelling_frees_the_slot_for_a_new_booking()
    {
        var first = await BookAsync(_tenantA.Id, At(10, 0));
        (await SlotStartsAsync(_tenantA.Id, _annaId)).ShouldNotContain(At(10, 0));

        var cancelled = await InTenantAsync(_tenantA.Id, sp => sp.GetRequiredService<IBookingService>().CancelAsync(first.Id, byClient: true, "Ill"));

        cancelled.Status.ShouldBe(AppointmentStatus.CancelledByClient);
        cancelled.CancelledAt.ShouldNotBeNull();
        cancelled.CancellationReason.ShouldBe("Ill");
        (await SlotStartsAsync(_tenantA.Id, _annaId)).ShouldContain(At(10, 0));
        var second = await BookAsync(_tenantA.Id, At(10, 0));
        second.Id.ShouldNotBe(first.Id);
        (await CountAppointmentsAsync(_tenantA.Id)).ShouldBe(2);
    }

    [Fact]
    public async Task Slots_follow_the_working_hours_the_step_and_existing_bookings()
    {
        await BookAsync(_tenantA.Id, At(10, 0));

        var starts = await SlotStartsAsync(_tenantA.Id, _annaId);

        var local = starts.Select(s => s.InZone(London).TimeOfDay).ToList();
        local.First().ShouldBe(new LocalTime(9, 0));
        local.Last().ShouldBe(new LocalTime(16, 30));
        local.ShouldAllBe(t => t.Minute % 15 == 0);
        local.ShouldContain(new LocalTime(9, 30));
        local.ShouldNotContain(new LocalTime(9, 45));
        local.ShouldNotContain(new LocalTime(10, 0));
        local.ShouldNotContain(new LocalTime(10, 15));
        local.ShouldContain(new LocalTime(10, 30));
        starts.ShouldAllBe(s => s.InZone(London).Date == _day);
        starts.ShouldBeInOrder();
        // With one stylist "any staff" is the same list.
        (await SlotStartsAsync(_tenantA.Id, null)).ShouldBe(starts);
    }

    [Fact]
    public async Task Booking_outside_the_offered_slots_is_refused()
    {
        await Should.ThrowAsync<SlotNoLongerAvailableException>(() => BookAsync(_tenantA.Id, At(8, 0))); // before opening
        await Should.ThrowAsync<SlotNoLongerAvailableException>(() => BookAsync(_tenantA.Id, At(16, 45))); // would end after closing
        await Should.ThrowAsync<SlotNoLongerAvailableException>(() => BookAsync(_tenantA.Id, At(10, 5))); // off the step
        var unknownService = await Should.ThrowAsync<InvalidOperationException>(
            () => InTenantAsync(_tenantA.Id, sp => sp.GetRequiredService<IBookingService>().BookAsync(Request(At(10, 0)) with { ServiceId = Guid.NewGuid() })));
        unknownService.ShouldBeOfType<InvalidOperationException>();

        (await CountAppointmentsAsync(_tenantA.Id)).ShouldBe(0);
        (await OutboxRowsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Transitions_persist_status_changes_and_publish_events()
    {
        var appointment = await BookAsync(_tenantA.Id, At(10, 0));

        await InTenantAsync(_tenantA.Id, sp => sp.GetRequiredService<IBookingService>().ConfirmAsync(appointment.Id, UserId));
        await InTenantAsync(_tenantA.Id, sp => sp.GetRequiredService<IBookingService>().MarkArrivedAsync(appointment.Id, UserId));
        var completed = await InTenantAsync(_tenantA.Id, sp => sp.GetRequiredService<IBookingService>().CompleteAsync(appointment.Id, UserId));

        completed.Status.ShouldBe(AppointmentStatus.Completed);
        await using var scope = factory.CreateTenantScope(_tenantA.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        (await db.Appointments.SingleAsync()).Status.ShouldBe(AppointmentStatus.Completed);
        var changes = await db.AppointmentStatusChanges.OrderBy(c => c.At).ThenBy(c => c.Id).ToListAsync();
        changes.Select(c => (c.From, c.To)).ShouldBe(
        [
            ((AppointmentStatus?)null, AppointmentStatus.Booked),
            (AppointmentStatus.Booked, AppointmentStatus.Confirmed),
            (AppointmentStatus.Confirmed, AppointmentStatus.Arrived),
            (AppointmentStatus.Arrived, AppointmentStatus.Completed),
        ]);
        changes.ShouldAllBe(c => c.ByUserId == UserId && c.AppointmentId == appointment.Id);

        (await OutboxRowsAsync()).Select(m => m.EventType).ShouldBe(
        [
            typeof(AppointmentBooked).FullName,
            typeof(AppointmentConfirmed).FullName,
            typeof(AppointmentArrived).FullName,
            typeof(AppointmentCompleted).FullName,
        ]);
    }

    [Fact]
    public async Task No_show_and_a_salon_cancellation_publish_their_events_and_leave_the_day_list()
    {
        var noShow = await BookAsync(_tenantA.Id, At(10, 0));
        var cancelled = await BookAsync(_tenantA.Id, At(11, 0));

        await InTenantAsync(_tenantA.Id, sp => sp.GetRequiredService<IBookingService>().MarkNoShowAsync(noShow.Id, UserId));
        await InTenantAsync(_tenantA.Id, sp => sp.GetRequiredService<IBookingService>().CancelAsync(cancelled.Id, byClient: false, "Stylist ill", UserId));

        var rows = await OutboxRowsAsync();
        rows.Count.ShouldBe(4);
        rows[2].EventType.ShouldBe(typeof(AppointmentNoShow).FullName);
        rows[3].EventType.ShouldBe(typeof(AppointmentCancelled).FullName);
        using var payload = JsonDocument.Parse(rows[3].Payload);
        payload.RootElement.GetProperty("appointmentId").GetGuid().ShouldBe(cancelled.Id);
        payload.RootElement.GetProperty("status").GetInt32().ShouldBe((int)AppointmentStatus.CancelledBySalon);
        rows[3].Payload.ShouldNotContain("Stylist ill");

        (await InTenantAsync(_tenantA.Id, sp => sp.GetRequiredService<IBookingService>().GetDayAsync(_day))).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_forbidden_transition_throws_and_changes_nothing()
    {
        var appointment = await BookAsync(_tenantA.Id, At(10, 0));

        await Should.ThrowAsync<InvalidAppointmentTransitionException>(
            () => InTenantAsync(_tenantA.Id, sp => sp.GetRequiredService<IBookingService>().CompleteAsync(appointment.Id, UserId)));

        await using var scope = factory.CreateTenantScope(_tenantA.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        (await db.Appointments.SingleAsync()).Status.ShouldBe(AppointmentStatus.Booked);
        (await db.AppointmentStatusChanges.CountAsync()).ShouldBe(1);
        (await OutboxRowsAsync()).ShouldHaveSingleItem().EventType.ShouldBe(typeof(AppointmentBooked).FullName);
    }

    [Fact]
    public async Task An_unknown_appointment_cannot_be_moved()
    {
        await Should.ThrowAsync<AppointmentNotFoundException>(
            () => InTenantAsync(_tenantA.Id, sp => sp.GetRequiredService<IBookingService>().ConfirmAsync(Guid.NewGuid())));
    }

    [Fact]
    public async Task Another_tenant_sees_none_of_the_appointments()
    {
        var appointment = await BookAsync(_tenantA.Id, At(10, 0));

        await using var scope = factory.CreateTenantScope(_tenantB.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        (await db.Appointments.CountAsync()).ShouldBe(0);
        (await db.AppointmentStatusChanges.CountAsync()).ShouldBe(0);
        var booking = scope.ServiceProvider.GetRequiredService<IBookingService>();
        (await booking.GetDayAsync(_day)).ShouldBeEmpty();
        await Should.ThrowAsync<AppointmentNotFoundException>(() => booking.ConfirmAsync(appointment.Id));
        (await CountAppointmentsAsync(_tenantA.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task The_day_list_holds_the_active_appointments_of_that_day_in_start_order()
    {
        var later = await BookAsync(_tenantA.Id, At(14, 0));
        var early = await BookAsync(_tenantA.Id, At(9, 0));
        var cancelled = await BookAsync(_tenantA.Id, At(11, 0));
        await InTenantAsync(_tenantA.Id, sp => sp.GetRequiredService<IBookingService>().CancelAsync(cancelled.Id, byClient: true, null));

        var day = await InTenantAsync(_tenantA.Id, sp => sp.GetRequiredService<IBookingService>().GetDayAsync(_day));

        day.Select(a => a.Id).ShouldBe([early.Id, later.Id]);
        (await InTenantAsync(_tenantA.Id, sp => sp.GetRequiredService<IBookingService>().GetDayAsync(_day.PlusDays(1)))).ShouldBeEmpty();
    }

    private Instant At(int hour, int minute) => London.AtLeniently(_day.At(new LocalTime(hour, minute))).ToInstant();

    private BookAppointmentRequest Request(Instant start, string? notes = null)
        => new(_clientId, _annaId, _serviceId, start, AppointmentSource.Admin, notes, UserId);

    private Task<Appointment> BookAsync(Guid tenantId, Instant start, string? notes = null)
        => InTenantAsync(tenantId, sp => sp.GetRequiredService<IBookingService>().BookAsync(Request(start, notes)));

    private async Task<List<Instant>> SlotStartsAsync(Guid tenantId, Guid? staffMemberId)
    {
        var slots = await InTenantAsync(tenantId, sp => sp.GetRequiredService<IAvailabilityService>().GetSlotsAsync(_serviceId, staffMemberId, _day, _day));
        return slots.Select(s => s.Start).ToList();
    }

    private async Task<T> InTenantAsync<T>(Guid tenantId, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await action(scope.ServiceProvider);
    }

    private Task<int> CountAppointmentsAsync(Guid tenantId)
        => InTenantAsync(tenantId, sp => sp.GetRequiredService<SalonDbContext>().Appointments.CountAsync());

    private async Task<List<OutboxMessage>> OutboxRowsAsync()
    {
        await using var scope = factory.CreateTenantScope(null);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().OutboxMessages.AsNoTracking().OrderBy(m => m.Id).ToListAsync();
    }

    private async Task<(Guid ClientId, Guid StaffId, Guid ServiceId)> SeedSalonAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var anna = new StaffMember { DisplayName = "Anna", CreatedAt = SystemClock.Instance.GetCurrentInstant() };
        db.StaffMembers.Add(anna);
        foreach (var day in WorkingHoursRules.Week)
        {
            db.WorkingHours.Add(new WorkingHours { StaffMemberId = anna.Id, Day = day, Start = new LocalTime(9, 0), End = new LocalTime(17, 0) });
        }

        var cut = new Service { Name = "Cut", Duration = Duration.FromMinutes(30), Price = 20m, Currency = "GBP" };
        var client = new Client { FirstName = "Olivia", LastName = "Brown", CreatedAt = anna.CreatedAt, UpdatedAt = anna.CreatedAt };
        db.Clients.Add(client);
        db.Services.Add(cut);
        await db.SaveChangesAsync();
        return (client.Id, anna.Id, cut.Id);
    }

    private async Task InsertRawAsync(NpgsqlConnection connection, Instant start, Instant end, AppointmentStatus status)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO salon.appointments
                (id, tenant_id, client_id, staff_member_id, service_id, start_at, end_at, status, source, price_amount, currency, duration_minutes, created_at)
            VALUES (@id, @tenant, @client, @staff, @service, @start, @end, @status, 2, 20.00, 'GBP', 30, now())
            """, connection);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("tenant", _tenantA.Id);
        command.Parameters.AddWithValue("client", _clientId);
        command.Parameters.AddWithValue("staff", _annaId);
        command.Parameters.AddWithValue("service", _serviceId);
        command.Parameters.AddWithValue("start", start.ToDateTimeUtc());
        command.Parameters.AddWithValue("end", end.ToDateTimeUtc());
        command.Parameters.AddWithValue("status", (int)status);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Polls pg_stat_activity until a session inserting an appointment waits on a lock (the exclusion check against an uncommitted row).</summary>
    private static async Task WaitUntilAnInsertWaitsForALockAsync()
    {
        await using var connection = new NpgsqlConnection(SalonWebApplicationFactory.TestConnectionString);
        await connection.OpenAsync();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND query ILIKE '%INSERT INTO salon.appointments%'", connection);
            if ((long)(await command.ExecuteScalarAsync())! > 0)
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The second booking never waited on the first transaction.");
    }
}