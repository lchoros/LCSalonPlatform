using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Appointments;
using Salon.Core.Catalog;
using Salon.Core.Identity;
using Salon.Core.Settings;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Appointments;
using Salon.Services.Events;
using Salon.Services.Settings;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Appointments;

/// <summary>
/// A cancelled visit's time is offered to the first client waiting for it (ADR 0056): one text to the one who asked
/// first for that service, day and person, with a link to book; not at night, not twice in a day to the same person,
/// not for a time that cannot be booked, and only when the owner has switched it on. Anna works 09:00 to 17:00 every
/// day, Tom 10:00 to 14:00; a cut takes 30 minutes; the visits here are three days ahead.
/// </summary>
[Collection(SharedDatabase.Name)]
public class WaitlistOfferTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string NoahPhone = "+447700900111";
    private const string MiaPhone = "+447700900222";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly LocalDate Today = SystemClock.Instance.GetCurrentInstant().InZone(London).Date;
    private static readonly LocalDate Day = Today.PlusDays(3);

    /// <summary>Noon today on the salon's clock: outside the quiet hours whatever time the tests run.</summary>
    private static readonly Instant Noon = London.AtStrictly(Today + new LocalTime(12, 0)).ToInstant();

    private Tenant _studio = null!;
    private Guid _anna;
    private Guid _tom;
    private Guid _cut;
    private Guid _colour;
    private Guid _olivia;
    private Guid _noah;
    private Guid _mia;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        factory.Sms.Clear();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            var now = SystemClock.Instance.GetCurrentInstant();
            var anna = new StaffMember { DisplayName = "Anna", SortOrder = 1, CreatedAt = now };
            var tom = new StaffMember { DisplayName = "Tom", SortOrder = 2, CreatedAt = now };
            db.StaffMembers.AddRange(anna, tom);
            foreach (var day in WorkingHoursRules.Week)
            {
                db.WorkingHours.Add(new WorkingHours { StaffMemberId = anna.Id, Day = day, Start = new LocalTime(9, 0), End = new LocalTime(17, 0) });
                db.WorkingHours.Add(new WorkingHours { StaffMemberId = tom.Id, Day = day, Start = new LocalTime(10, 0), End = new LocalTime(14, 0) });
            }

            var cut = new Service { Name = "Cut", Duration = Duration.FromMinutes(30), Price = 20m, Currency = "GBP", SortOrder = 1 };
            var colour = new Service { Name = "Colour", Duration = Duration.FromMinutes(30), Price = 60m, Currency = "GBP", SortOrder = 2 };
            db.Services.AddRange(cut, colour);
            await db.SaveChangesAsync();
            (_anna, _tom, _cut, _colour) = (anna.Id, tom.Id, cut.Id, colour.Id);
        }

        _olivia = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123");
        _noah = await VisitSeed.ClientAsync(factory, _studio.Id, "Noah", "Adams", NoahPhone);
        _mia = await VisitSeed.ClientAsync(factory, _studio.Id, "Mia", "Clark", MiaPhone);
        await SettingsAsync<BookingSettings>(s => s.TellWaitlistWhenFreed = true);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_one_who_asked_first_gets_one_text_with_a_link_and_is_not_told_again_that_day()
    {
        await WaitAsync(_noah, _cut);
        await WaitAsync(_mia, _cut);
        var first = await CancelledAsync(At(11, 0), _anna);
        var second = await CancelledAsync(At(15, 0), _anna);
        var third = await CancelledAsync(At(16, 0), _anna);

        (await OfferAsync(first, Noon)).ShouldBe(WaitlistOfferOutcome.Told);

        var text = factory.Sms.Messages.ShouldHaveSingleItem();
        text.ToE164.ShouldBe(NoahPhone);
        text.Body.ShouldStartWith("Studio 20: a time for Cut has come up on ");
        text.Body.ShouldContain("11:00");
        text.Body.ShouldContain($"/book/{_cut}?date={LocalDatePattern.Iso.Format(Day)}");
        (await EntryAsync(_noah)).LastToldAt.ShouldBe(Noon);
        (await EntryAsync(_mia)).LastToldAt.ShouldBeNull();

        // An hour later another time is freed: Noah was told today, so it goes to the next in line.
        (await OfferAsync(second, Noon + Duration.FromHours(1))).ShouldBe(WaitlistOfferOutcome.Told);
        factory.Sms.Messages.Select(m => m.ToE164).ShouldBe([NoahPhone, MiaPhone]);
        (await OfferAsync(third, Noon + Duration.FromHours(2))).ShouldBe(WaitlistOfferOutcome.NobodyWaiting);
        factory.Sms.Messages.Count.ShouldBe(2);

        // The next day Noah, still first in line, may be told again.
        (await OfferAsync(third, Noon + Duration.FromHours(24))).ShouldBe(WaitlistOfferOutcome.Told);
        factory.Sms.Messages[^1].ToE164.ShouldBe(NoahPhone);
    }

    [Fact]
    public async Task Only_somebody_who_asked_for_that_service_day_and_person_is_told()
    {
        var freed = await CancelledAsync(At(11, 0), _anna);

        await WaitAsync(_noah, _colour);
        (await OfferAsync(freed, Noon)).ShouldBe(WaitlistOfferOutcome.NobodyWaiting, "waiting for another service");

        await WaitAsync(_noah, _cut, staff: _tom);
        (await OfferAsync(freed, Noon)).ShouldBe(WaitlistOfferOutcome.NobodyWaiting, "wants Tom; the freed time is Anna's");

        await WaitAsync(_mia, _cut, notBefore: Day.PlusDays(1));
        (await OfferAsync(freed, Noon)).ShouldBe(WaitlistOfferOutcome.NobodyWaiting, "cannot come before the day after");

        await WaitAsync(_olivia, _cut);
        (await OfferAsync(freed, Noon)).ShouldBe(WaitlistOfferOutcome.NobodyWaiting, "the one who cancelled is not offered her own time back");
        factory.Sms.Messages.ShouldBeEmpty();

        await WaitAsync(_mia, _cut, notBefore: Day, notAfter: Day);
        (await OfferAsync(freed, Noon)).ShouldBe(WaitlistOfferOutcome.Told);
        factory.Sms.Messages.ShouldHaveSingleItem().ToE164.ShouldBe(MiaPhone);
    }

    [Fact]
    public async Task Somebody_who_cannot_be_texted_is_passed_over_for_the_next_in_line()
    {
        var noPhone = await VisitSeed.ClientAsync(factory, _studio.Id, "Ivy", "Dale");
        await WaitAsync(noPhone, _cut);
        await WaitAsync(_noah, _cut);
        await WaitAsync(_mia, _cut);
        await InScopeAsync(async db =>
        {
            (await db.Clients.SingleAsync(c => c.Id == _noah)).IsActive = false;
            await db.SaveChangesAsync();
        });
        var freed = await CancelledAsync(At(11, 0), _anna);

        (await OfferAsync(freed, Noon)).ShouldBe(WaitlistOfferOutcome.Told);

        factory.Sms.Messages.ShouldHaveSingleItem().ToE164.ShouldBe(MiaPhone);
    }

    [Fact]
    public async Task Nothing_is_sent_when_it_is_switched_off_at_night_or_for_a_time_nobody_can_book()
    {
        await WaitAsync(_noah, _cut);
        var freed = await CancelledAsync(At(11, 0), _anna);
        var standing = await VisitSeed.AppointmentAsync(factory, _studio.Id, _olivia, _anna, _cut, At(13, 0), AppointmentStatus.Booked);
        var tooSoon = await VisitSeed.AppointmentAsync(factory, _studio.Id, _olivia, _anna, _cut, Noon + Duration.FromMinutes(30), AppointmentStatus.CancelledByClient);
        var taken = await CancelledAsync(At(14, 0), _anna);
        await VisitSeed.AppointmentAsync(factory, _studio.Id, _mia, _anna, _cut, At(14, 0), AppointmentStatus.Booked);
        var night = London.AtStrictly(Today + new LocalTime(23, 0)).ToInstant();

        (await OfferAsync(freed, night)).ShouldBe(WaitlistOfferOutcome.QuietHours);
        (await OfferAsync(standing, Noon)).ShouldBe(WaitlistOfferOutcome.NothingToOffer, "the visit still stands");
        (await OfferAsync(tooSoon, Noon)).ShouldBe(WaitlistOfferOutcome.NothingToOffer, "too close to be booked online");
        (await OfferAsync(Guid.NewGuid(), Noon)).ShouldBe(WaitlistOfferOutcome.NothingToOffer);
        (await OfferAsync(taken, Noon)).ShouldBe(WaitlistOfferOutcome.NotBookable, "somebody else has the time already");
        await SettingsAsync<BookingSettings>(s => s.TellWaitlistWhenFreed = false);
        (await OfferAsync(freed, Noon)).ShouldBe(WaitlistOfferOutcome.SwitchedOff);

        factory.Sms.Messages.ShouldBeEmpty();
        (await EntryAsync(_noah)).LastToldAt.ShouldBeNull();
    }

    [Fact]
    public async Task A_real_cancellation_reaches_the_waiting_client_through_the_outbox()
    {
        // No quiet hours, so the test does not depend on the hour it runs at.
        await SettingsAsync<NotificationSettings>(s =>
        {
            s.QuietHoursStartMinute = 0;
            s.QuietHoursEndMinute = 0;
        });
        await WaitAsync(_noah, _cut);
        var visit = await VisitSeed.AppointmentAsync(factory, _studio.Id, _olivia, _anna, _cut, At(11, 0), AppointmentStatus.Booked);

        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            await scope.ServiceProvider.GetRequiredService<IBookingService>().CancelAsync(visit, byClient: true, reason: null);
        }

        await using (var scope = factory.CreateTenantScope(null))
        {
            await scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync();
        }

        factory.Sms.Messages.Where(m => m.ToE164 == NoahPhone).ShouldHaveSingleItem().Body.ShouldContain("has come up");
        (await EntryAsync(_noah)).LastToldAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_owner_switches_the_text_on_and_the_desk_sees_who_was_told()
    {
        await SettingsAsync<BookingSettings>(s => s.TellWaitlistWhenFreed = false);
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        const string path = "/admin/settings/waitlist";

        var page = await GetAsync(owner, path);
        page.ShouldContain("Text the first person waiting when a visit is cancelled");
        var saved = await owner.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent(
        [
            new("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(page)),
            new("TellWhenFreed", "true"),
        ]));
        saved.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var rules = await LoadAsync();
        (rules.TellWaitlistWhenFreed, rules.PublicWaitlist).ShouldBe((true, false), "the two switches are saved apart");

        await WaitAsync(_noah, _cut);
        (await OfferAsync(await CancelledAsync(At(11, 0), _anna), Noon)).ShouldBe(WaitlistOfferOutcome.Told);
        var list = await GetAsync(owner, "/admin/appointments/waitlist");
        list.ShouldContain("data-testid=\"told-on\">Texted about a freed time on ");
        (await GetAsync(owner, "/admin/settings/messages/Waitlist.TimeFreed/sms")).ShouldContain("a time for {{ServiceName}} has come up on {{AppointmentStart}}");
    }

    /// <summary>A time of the day three days ahead on the salon's clock.</summary>
    private static Instant At(int hour, int minute) => London.AtStrictly(Day + new LocalTime(hour, minute)).ToInstant();

    /// <summary>Olivia's visit at that time, already cancelled by her: its time is free again.</summary>
    private Task<Guid> CancelledAsync(Instant start, Guid staff)
        => VisitSeed.AppointmentAsync(factory, _studio.Id, _olivia, staff, _cut, start, AppointmentStatus.CancelledByClient);

    private async Task WaitAsync(Guid client, Guid service, Guid? staff = null, LocalDate? notBefore = null, LocalDate? notAfter = null)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var outcome = await scope.ServiceProvider.GetRequiredService<IWaitlistService>().AddAsync(new WaitlistRequest(client, service, staff, notBefore, notAfter, null), null);
        (outcome is WaitlistAddOutcome.Added or WaitlistAddOutcome.Updated).ShouldBeTrue(outcome.ToString());
    }

    private async Task<WaitlistOfferOutcome> OfferAsync(Guid appointmentId, Instant now)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await scope.ServiceProvider.GetRequiredService<IWaitlistOfferService>().OfferFreedTimeAsync(appointmentId, now);
    }

    private async Task<WaitlistEntry> EntryAsync(Guid client)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().WaitlistEntries.AsNoTracking().SingleAsync(e => e.ClientId == client && e.ClosedAt == null);
    }

    private async Task SettingsAsync<T>(Action<T> change)
        where T : class, ISettings, new()
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var settings = scope.ServiceProvider.GetRequiredService<ISettingService>();
        var current = await settings.LoadAsync<T>();
        change(current);
        await settings.SaveAsync(current);
    }

    private async Task<BookingSettings> LoadAsync()
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await scope.ServiceProvider.GetRequiredService<ISettingService>().LoadAsync<BookingSettings>();
    }

    private async Task InScopeAsync(Func<SalonDbContext, Task> act)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await act(scope.ServiceProvider.GetRequiredService<SalonDbContext>());
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}