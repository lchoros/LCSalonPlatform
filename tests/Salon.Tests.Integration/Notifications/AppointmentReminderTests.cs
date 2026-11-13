using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NodaTime;

using Salon.Core.Appointments;
using Salon.Core.Notifications;
using Salon.Core.Settings;
using Salon.Core.Tasks;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Notifications;
using Salon.Services.Settings;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Notifications;

/// <summary>Appointment reminders (ADR 0020): due moments, quiet hours, one SMS per moment, per salon.</summary>
[Collection(SharedDatabase.Name)]
public class AppointmentReminderTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string Phone = "+447700900123";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];

    /// <summary>Friday 20 November 2026, 14:00 in London: outside the default quiet hours.</summary>
    private static readonly Instant Now = London.AtStrictly(new LocalDateTime(2026, 11, 20, 14, 0)).ToInstant();

    private Tenant _studio = null!;
    private Tenant _other = null!;
    private Guid _staffId;
    private Guid _serviceId;
    private Guid _clientId;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        factory.Sms.Clear();
        factory.Emails.Clear();
        _studio = await factory.CreateTenantAsync("reminders-a", name: "Studio 20");
        _other = await factory.CreateTenantAsync("reminders-b", name: "Other Salon");
        (_staffId, _serviceId) = await VisitSeed.SalonAsync(factory, _studio.Id);
        _clientId = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", Phone);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_visit_tomorrow_gets_one_reminder_a_day_before_and_never_a_second_copy()
    {
        var start = Now + Duration.FromHours(24);
        var appointmentId = await VisitAsync(start);

        (await SendDueAsync(_studio.Id, Now - Duration.FromMinutes(1))).ShouldBe(0, "a minute early");
        (await SendDueAsync(_studio.Id, Now)).ShouldBe(1);
        (await SendDueAsync(_studio.Id, Now + Duration.FromMinutes(5))).ShouldBe(0, "the next pass finds the claim");

        var sms = factory.Sms.Messages.ShouldHaveSingleItem();
        sms.ToE164.ShouldBe(Phone);
        sms.Body.ShouldBe("Reminder from Studio 20: Cut on Sat 21 Nov, 14:00. See you then.");
        var row = (await RemindersAsync(_studio.Id)).ShouldHaveSingleItem();
        row.AppointmentId.ShouldBe(appointmentId);
        row.Kind.ShouldBe(ReminderKind.First);
        row.AppointmentStart.ShouldBe(start);
        row.Outcome.ShouldBe(ReminderOutcome.Sent);
    }

    [Fact]
    public async Task The_second_reminder_follows_two_hours_before_the_visit()
    {
        var start = Now + Duration.FromHours(24);
        await VisitAsync(start);
        await SendDueAsync(_studio.Id, Now);

        (await SendDueAsync(_studio.Id, start - Duration.FromHours(2) - Duration.FromMinutes(1))).ShouldBe(0);
        (await SendDueAsync(_studio.Id, start - Duration.FromHours(2))).ShouldBe(1);
        (await SendDueAsync(_studio.Id, start - Duration.FromHours(1))).ShouldBe(0);

        factory.Sms.Messages.Count.ShouldBe(2);
        (await RemindersAsync(_studio.Id)).Select(r => (r.Kind, r.Outcome)).ShouldBe(
            [(ReminderKind.First, ReminderOutcome.Sent), (ReminderKind.Second, ReminderOutcome.Sent)], ignoreOrder: true);
    }

    [Fact]
    public async Task Nothing_is_sent_in_quiet_hours_and_the_reminder_goes_out_when_they_end()
    {
        // 07:00 tomorrow: a day before is 07:00 today, inside the default 21:00 to 08:00 quiet hours.
        var start = London.AtStrictly(new LocalDateTime(2026, 11, 21, 7, 0)).ToInstant();
        await VisitAsync(start);

        (await SendDueAsync(_studio.Id, start - Duration.FromHours(24))).ShouldBe(0, "07:00 is quiet");
        (await SendDueAsync(_studio.Id, start - Duration.FromHours(23) - Duration.FromMinutes(1))).ShouldBe(0, "07:59 is quiet");
        (await SendDueAsync(_studio.Id, start - Duration.FromHours(23))).ShouldBe(1, "08:00 ends the quiet hours");
    }

    [Fact]
    public async Task Cancelled_visits_get_nothing_and_a_client_without_a_phone_is_recorded_not_texted()
    {
        var start = Now + Duration.FromHours(24);
        await VisitAsync(start, AppointmentStatus.CancelledByClient);
        var phoneless = await VisitSeed.ClientAsync(factory, _studio.Id, "No", "Phone");
        await VisitAsync(start + Duration.FromHours(1), clientId: phoneless);

        (await SendDueAsync(_studio.Id, Now + Duration.FromHours(1))).ShouldBe(0);

        factory.Sms.Messages.ShouldBeEmpty();
        (await RemindersAsync(_studio.Id)).ShouldHaveSingleItem().Outcome.ShouldBe(ReminderOutcome.NoPhone);
    }

    [Fact]
    public async Task A_client_with_an_email_address_is_reminded_by_text_and_by_email_as_one_reminder()
    {
        var olivia = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Green", "+447700900555", "olivia.green@example.com");
        var start = Now + Duration.FromHours(24);
        await VisitAsync(start, clientId: olivia);

        (await SendDueAsync(_studio.Id, Now)).ShouldBe(1, "one visit reminded, on two channels");

        factory.Sms.Messages.ShouldHaveSingleItem().ToE164.ShouldBe("+447700900555");
        var email = factory.Emails.Messages.ShouldHaveSingleItem();
        email.To.ShouldBe("olivia.green@example.com");
        email.Subject.ShouldBe("Reminder: your Cut at Studio 20");
        email.TextBody.ShouldNotBeNull().ShouldStartWith("Hi Olivia,\na quick reminder that your Cut with Anna at Studio 20 is on Sat 21 Nov, 14:00.");
        (await RemindersAsync(_studio.Id)).ShouldHaveSingleItem().Outcome.ShouldBe(ReminderOutcome.Sent);
        (await SendDueAsync(_studio.Id, Now + Duration.FromMinutes(5))).ShouldBe(0);
        factory.Emails.Messages.ShouldHaveSingleItem("never a second copy by email either");
    }

    [Fact]
    public async Task A_mail_server_that_is_down_does_not_cost_the_client_their_text()
    {
        var olivia = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Green", "+447700900555", "olivia.green@example.com");
        await VisitAsync(Now + Duration.FromHours(24), clientId: olivia);
        factory.Emails.Fail = true;

        (await SendDueAsync(_studio.Id, Now)).ShouldBe(1);

        factory.Sms.Messages.ShouldHaveSingleItem();
        factory.Emails.Messages.ShouldBeEmpty();
        (await RemindersAsync(_studio.Id)).ShouldHaveSingleItem().Outcome.ShouldBe(ReminderOutcome.Sent, "the text went out");
    }

    [Fact]
    public async Task A_client_with_only_an_email_address_is_reminded_by_email_unless_emails_are_switched_off()
    {
        var emailOnly = await VisitSeed.ClientAsync(factory, _studio.Id, "Ed", "Mail", email: "ed.mail@example.com");
        await VisitAsync(Now + Duration.FromHours(24), clientId: emailOnly);
        var other = await VisitSeed.ClientAsync(factory, _studio.Id, "Em", "Mail", email: "em.mail@example.com");
        var later = await VisitAsync(Now + Duration.FromHours(25), clientId: other);

        (await SendDueAsync(_studio.Id, Now)).ShouldBe(1);
        factory.Sms.Messages.ShouldBeEmpty();
        factory.Emails.Messages.ShouldHaveSingleItem().To.ShouldBe("ed.mail@example.com");

        await SaveSettingsAsync(_studio.Id, s => s.EmailEnabled = false);
        (await SendDueAsync(_studio.Id, Now + Duration.FromHours(1))).ShouldBe(0, "nobody the salon may tell");
        factory.Emails.Messages.ShouldHaveSingleItem();
        (await RemindersAsync(_studio.Id)).Single(r => r.AppointmentId == later).Outcome.ShouldBe(ReminderOutcome.NoPhone);
    }

    [Fact]
    public async Task A_visit_booked_inside_the_lead_time_skips_the_late_first_reminder_but_still_gets_the_second()
    {
        var start = Now + Duration.FromHours(5);
        await VisitAsync(start, createdAt: Now - Duration.FromHours(1));

        (await SendDueAsync(_studio.Id, Now)).ShouldBe(0, "the day-before moment passed before the booking existed");
        (await SendDueAsync(_studio.Id, start - Duration.FromHours(2))).ShouldBe(1);

        (await RemindersAsync(_studio.Id)).ShouldHaveSingleItem().Kind.ShouldBe(ReminderKind.Second);
    }

    [Fact]
    public async Task A_moved_visit_is_reminded_again_for_its_new_time()
    {
        var start = Now + Duration.FromHours(24);
        var appointmentId = await VisitAsync(start);
        await SendDueAsync(_studio.Id, Now);
        var newStart = start + Duration.FromHours(24);
        await MoveAsync(appointmentId, newStart, movedAt: Now + Duration.FromHours(1));

        (await SendDueAsync(_studio.Id, Now + Duration.FromHours(2))).ShouldBe(0, "the new time is more than a day away");
        (await SendDueAsync(_studio.Id, newStart - Duration.FromHours(24))).ShouldBe(1);

        factory.Sms.Messages.Count.ShouldBe(2);
        factory.Sms.Messages[1].Body.ShouldContain("Sun 22 Nov, 14:00");
        (await RemindersAsync(_studio.Id)).Select(r => r.AppointmentStart).ShouldBe([start, newStart], ignoreOrder: true);
    }

    [Fact]
    public async Task Two_reminders_due_in_one_pass_are_one_message()
    {
        // The task was down: both the day-before and the two-hours-before moments have passed.
        var start = Now + Duration.FromMinutes(90);
        await VisitAsync(start);

        (await SendDueAsync(_studio.Id, Now)).ShouldBe(1);
        (await SendDueAsync(_studio.Id, Now + Duration.FromMinutes(5))).ShouldBe(0);

        factory.Sms.Messages.ShouldHaveSingleItem();
        (await RemindersAsync(_studio.Id)).Select(r => (r.Kind, r.Outcome)).ShouldBe(
            [(ReminderKind.Second, ReminderOutcome.Sent), (ReminderKind.First, ReminderOutcome.Superseded)], ignoreOrder: true);
    }

    [Fact]
    public async Task A_visit_less_than_half_an_hour_away_is_left_alone()
    {
        await VisitAsync(Now + Duration.FromMinutes(20));

        (await SendDueAsync(_studio.Id, Now)).ShouldBe(0);

        (await RemindersAsync(_studio.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Switched_off_reminders_send_nothing_and_another_salon_is_never_touched()
    {
        await VisitAsync(Now + Duration.FromHours(24));
        var (otherStaff, otherService) = await VisitSeed.SalonAsync(factory, _other.Id);
        var otherClient = await VisitSeed.ClientAsync(factory, _other.Id, "Jan", "Kowalski", "+447700900999");
        await VisitAsync(Now + Duration.FromHours(24), tenantId: _other.Id, clientId: otherClient, staffId: otherStaff, serviceId: otherService);
        await SaveSettingsAsync(_studio.Id, s => s.RemindersEnabled = false);

        (await SendDueAsync(_studio.Id, Now)).ShouldBe(0, "switched off");
        factory.Sms.Messages.ShouldBeEmpty();

        (await SendDueAsync(_other.Id, Now)).ShouldBe(1);
        factory.Sms.Messages.ShouldHaveSingleItem().ToE164.ShouldBe("+447700900999");
        (await RemindersAsync(_studio.Id)).ShouldBeEmpty("the other salon's claim is not visible here");
        (await RemindersAsync(_other.Id)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_task_runs_every_five_minutes_and_serves_every_salon()
    {
        // The task reads the real clock, so the visits are a day from now and quiet hours are off for both salons.
        var realNow = SystemClock.Instance.GetCurrentInstant();
        var start = realNow + Duration.FromHours(24) - Duration.FromMinutes(1);
        await VisitAsync(start, createdAt: realNow - Duration.FromDays(3));
        var (otherStaff, otherService) = await VisitSeed.SalonAsync(factory, _other.Id);
        var otherClient = await VisitSeed.ClientAsync(factory, _other.Id, "Jan", "Kowalski", "+447700900999");
        await VisitAsync(start, tenantId: _other.Id, clientId: otherClient, staffId: otherStaff, serviceId: otherService, createdAt: realNow - Duration.FromDays(3));
        await SaveSettingsAsync(_studio.Id, s => s.QuietHoursEndMinute = s.QuietHoursStartMinute);
        await SaveSettingsAsync(_other.Id, s => s.QuietHoursEndMinute = s.QuietHoursStartMinute);

        using (var scope = factory.Services.CreateScope())
        {
            var task = scope.ServiceProvider.GetServices<IScheduleTask>().Single(t => t.SystemName == "Notifications.SendReminders");
            task.CronExpression.ShouldBe("*/5 * * * *");
            await task.ExecuteAsync(CancellationToken.None);
            await task.ExecuteAsync(CancellationToken.None);
        }

        factory.Sms.Messages.Select(m => m.ToE164).ShouldBe([Phone, "+447700900999"], ignoreOrder: true);
    }

    private async Task<int> SendDueAsync(Guid tenantId, Instant now)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<IAppointmentReminderService>().SendDueAsync(now);
    }

    /// <summary>A booked visit created well before any reminder moment unless <paramref name="createdAt"/> says otherwise.</summary>
    private async Task<Guid> VisitAsync(
        Instant start,
        AppointmentStatus status = AppointmentStatus.Booked,
        Guid? tenantId = null,
        Guid? clientId = null,
        Guid? staffId = null,
        Guid? serviceId = null,
        Instant? createdAt = null)
    {
        var tenant = tenantId ?? _studio.Id;
        var id = await VisitSeed.AppointmentAsync(factory, tenant, clientId ?? _clientId, staffId ?? _staffId, serviceId ?? _serviceId, start, status);
        var created = createdAt ?? start - Duration.FromDays(7);
        await using var scope = factory.CreateTenantScope(tenant);
        await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Appointments
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.CreatedAt, created));
        return id;
    }

    /// <summary>What <c>MoveAsync</c> leaves behind: a new start and a history row whose status did not change (ADR 0016).</summary>
    private async Task MoveAsync(Guid appointmentId, Instant newStart, Instant movedAt)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var appointment = await db.Appointments.SingleAsync(a => a.Id == appointmentId);
        appointment.Start = newStart;
        appointment.End = newStart + Duration.FromMinutes(appointment.DurationMinutes);
        db.AppointmentStatusChanges.Add(new AppointmentStatusChange
        {
            TenantId = _studio.Id,
            AppointmentId = appointmentId,
            From = appointment.Status,
            To = appointment.Status,
            At = movedAt,
            Reason = "Moved",
        });
        await db.SaveChangesAsync();
    }

    private async Task SaveSettingsAsync(Guid tenantId, Action<NotificationSettings> change)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var settings = scope.ServiceProvider.GetRequiredService<ISettingService>();
        var current = await settings.LoadAsync<NotificationSettings>();
        change(current);
        await settings.SaveAsync(current);
    }

    private async Task<List<AppointmentReminder>> RemindersAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().AppointmentReminders.AsNoTracking().OrderBy(r => r.CreatedAt).ToListAsync();
    }
}