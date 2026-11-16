using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Appointments;
using Salon.Core.Identity;
using Salon.Core.Notifications;
using Salon.Core.Payments;
using Salon.Core.Products;
using Salon.Core.Settings;
using Salon.Core.Tasks;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Notifications;
using Salon.Services.Payments;
using Salon.Services.Settings;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Notifications;

/// <summary>
/// The day's numbers by email to the salon's owners (ADR 0050): off until switched on, sent once a day from its time
/// on the salon's clock, to every active owner and nobody else, in the salon's language, with numbers only. A mail
/// server that is down costs the day, never a second email. A cut is £20.
/// </summary>
[Collection(SharedDatabase.Name)]
public class DailySummaryTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly LocalDate Today = SystemClock.Instance.GetCurrentInstant().InZone(London).Date;
    private static readonly string Iso = LocalDatePattern.Iso.Format(Today);

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _staffId;
    private Guid _serviceId;
    private Guid _clientId;
    private RecordingEmailSender _mail = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _mail = factory.Services.GetRequiredService<RecordingEmailSender>();
        _mail.Clear();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        (_staffId, _serviceId) = await VisitSeed.SalonAsync(factory, _studio.Id);
        _clientId = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123");
        await factory.CreateUserAsync(_studio.Id, "owner@studio-20.test", Role.Owner);
    }

    public Task DisposeAsync()
    {
        _mail.Clear();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task The_owners_get_the_days_numbers_once_from_the_chosen_time()
    {
        await factory.CreateUserAsync(_studio.Id, "second@studio-20.test", Role.Owner);
        await factory.CreateUserAsync(_studio.Id, "manager@studio-20.test", Role.Manager);
        var gone = await factory.CreateUserAsync(_studio.Id, "gone@studio-20.test", Role.Owner);
        await InScopeAsync(_studio.Id, async services =>
        {
            var db = services.GetRequiredService<SalonDbContext>();
            (await db.Users.SingleAsync(u => u.Id == gone.Id)).IsActive = false;
            db.Products.Add(new Product { Name = "Shampoo 250 ml", Currency = "GBP", RetailPrice = 12.5m, StockOnHand = 1, LowStockAt = 2 });
            db.Products.Add(new Product { Name = "Hair oil 50 ml", Currency = "GBP", RetailPrice = 18m, StockOnHand = 9, LowStockAt = 2 });
            await db.SaveChangesAsync();
        });
        var paid = await VisitAsync(At(8, 0), AppointmentStatus.Arrived);
        await InScopeAsync(_studio.Id, s => s.GetRequiredService<IClientAccountService>().PayBillAsync(paid, PaymentMethod.Cash, [], null));
        await SetAsync(paid, AppointmentStatus.Completed);
        await VisitAsync(At(9, 0), AppointmentStatus.Completed);
        await VisitAsync(At(10, 0), AppointmentStatus.NoShow);
        await VisitAsync(At(11, 0), AppointmentStatus.CancelledByClient);
        await VisitAsync(At(9, 30) + Duration.FromDays(1), AppointmentStatus.Booked);
        await VisitAsync(At(14, 0) + Duration.FromDays(1), AppointmentStatus.Confirmed);
        await SwitchOnAsync(_studio.Id, 20 * 60);

        (await SendAsync(_studio.Id, At(19, 55))).ShouldBeNull("its time has not come");
        _mail.Messages.ShouldBeEmpty();

        (await SendAsync(_studio.Id, At(20, 5))).ShouldBe(2);

        _mail.Messages.Select(m => m.To).ShouldBe(["owner@studio-20.test", "second@studio-20.test"], ignoreOrder: true);
        var message = _mail.Messages[0];
        message.Subject.ShouldStartWith("Studio 20: £20.00 taken on ");
        var text = message.TextBody.ShouldNotBeNull();
        text.ShouldContain("Taken: £20.00\n- Cash: £20.00\n");
        text.ShouldContain("Completed visits: 2\nNo-shows: 1\nCancelled: 1\nCompleted visits not paid: 1 (£20.00)\n");
        text.ShouldContain("Booked for tomorrow: 2, the first at 09:30\nProducts running low: 1\n");
        text.ShouldContain($"/admin/reports?from={Iso}&to={Iso}");
        text.ShouldContain("://studio-20.");
        text.ShouldNotContain("Olivia", Case.Insensitive, "numbers only: no client is named");
        text.ShouldNotContain("Brown", Case.Insensitive);
        message.HtmlBody.ShouldContain($"/admin/reports?from={Iso}&amp;to={Iso}\">");

        (await SendAsync(_studio.Id, At(20, 10))).ShouldBeNull("a day is sent once");
        (await SendAsync(_studio.Id, At(23, 55))).ShouldBeNull();
        _mail.Messages.Count.ShouldBe(2);
        var delivery = (await DeliveriesAsync(_studio.Id)).ShouldHaveSingleItem();
        (delivery.Day, delivery.Recipients).ShouldBe((Today, 2));

        (await SendAsync(_studio.Id, At(20, 5) + Duration.FromDays(1))).ShouldBe(2, "the next day is a new day");
        _mail.Messages.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Nothing_is_sent_until_the_owner_switches_it_on()
    {
        (await SendAsync(_studio.Id, At(21, 0))).ShouldBeNull();

        _mail.Messages.ShouldBeEmpty();
        (await DeliveriesAsync(_studio.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_quiet_day_says_so_and_leaves_out_what_there_is_nothing_of()
    {
        await SwitchOnAsync(_studio.Id, 18 * 60);

        (await SendAsync(_studio.Id, At(18, 0))).ShouldBe(1);

        var message = _mail.Messages.ShouldHaveSingleItem();
        message.Subject.ShouldStartWith("Studio 20: £0.00 taken on ");
        var text = message.TextBody.ShouldNotBeNull();
        text.ShouldContain("No money was recorded.\n");
        text.ShouldContain("Completed visits: 0\nNo-shows: 0\nCancelled: 0\n\nNothing is booked for tomorrow.\n");
        text.ShouldNotContain("not paid");
        text.ShouldNotContain("running low");
        text.ShouldContain("You get this email because the daily summary is switched on in Settings.");
    }

    [Fact]
    public async Task A_Polish_salon_gets_its_own_numbers_in_Polish_on_its_own_clock()
    {
        var paid = await VisitAsync(At(8, 0), AppointmentStatus.Arrived);
        await InScopeAsync(_studio.Id, s => s.GetRequiredService<IClientAccountService>().PayBillAsync(paid, PaymentMethod.Cash, [], null));
        await factory.CreateUserAsync(_polish.Id, "wlasciciel@salon-pl.test", Role.Owner);
        await SwitchOnAsync(_studio.Id, 20 * 60);
        await SwitchOnAsync(_polish.Id, 20 * 60);

        // Five past seven in London is five past eight in Warsaw.
        (await SendAsync(_studio.Id, At(19, 5))).ShouldBeNull();
        (await SendAsync(_polish.Id, At(19, 5))).ShouldBe(1);

        var message = _mail.Messages.ShouldHaveSingleItem();
        message.To.ShouldBe("wlasciciel@salon-pl.test");
        message.Subject.ShouldStartWith("Salon PL: utarg 0,00");
        var text = message.TextBody.ShouldNotBeNull();
        text.ShouldContain("Nie zapisano żadnych wpłat.");
        text.ShouldContain("Zakończone wizyty: 0");
        text.ShouldContain("Na jutro nie ma rezerwacji.");
        text.ShouldContain("://salon-pl.");
        text.ShouldNotContain("20.00", Case.Sensitive, "the other salon's money is not this salon's");
    }

    [Fact]
    public async Task A_mail_server_that_is_down_costs_the_day_and_never_sends_it_twice()
    {
        await SwitchOnAsync(_studio.Id, 20 * 60);
        _mail.Fail = true;

        (await SendAsync(_studio.Id, At(20, 5))).ShouldBe(0);

        _mail.Fail = false;
        (await SendAsync(_studio.Id, At(20, 10))).ShouldBeNull("the day is claimed; the report has the numbers");
        _mail.Messages.ShouldBeEmpty();
        (await DeliveriesAsync(_studio.Id)).ShouldHaveSingleItem().Recipients.ShouldBe(0);
    }

    [Fact]
    public async Task The_job_that_sends_it_runs_every_five_minutes()
    {
        await using var scope = factory.Services.CreateAsyncScope();

        var task = scope.ServiceProvider.GetServices<IScheduleTask>().Single(t => t.SystemName == "Notifications.SendDailySummaries");

        task.CronExpression.ShouldBe("*/5 * * * *");
    }

    /// <summary>A time of today on the London clock.</summary>
    private static Instant At(int hour, int minute) => London.AtStrictly(Today + new LocalTime(hour, minute)).ToInstant();

    private async Task<int?> SendAsync(Guid tenantId, Instant now)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<IDailySummaryService>().SendIfDueAsync(now);
    }

    private Task SwitchOnAsync(Guid tenantId, int minute)
        => InScopeAsync(tenantId, s => s.GetRequiredService<ISettingService>().SaveAsync(new DailySummarySettings { Enabled = true, SendAtMinute = minute }));

    private Task<Guid> VisitAsync(Instant start, AppointmentStatus status)
        => VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, start, status);

    private Task SetAsync(Guid appointmentId, AppointmentStatus status)
        => InScopeAsync(_studio.Id, s => s.GetRequiredService<SalonDbContext>().Appointments
            .Where(a => a.Id == appointmentId)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.Status, status)));

    private async Task<List<DailySummaryDelivery>> DeliveriesAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().DailySummaryDeliveries.AsNoTracking().OrderBy(d => d.Day).ToListAsync();
    }

    private async Task InScopeAsync(Guid tenantId, Func<IServiceProvider, Task> act)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        await act(scope.ServiceProvider);
    }
}