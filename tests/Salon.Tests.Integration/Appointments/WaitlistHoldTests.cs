using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Catalog;
using Salon.Core.Identity;
using Salon.Core.Settings;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Appointments;
using Salon.Services.Availability;
using Salon.Services.Clients;
using Salon.Services.Events;
using Salon.Services.Settings;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Appointments;

/// <summary>
/// A freed time is kept for the waiting client who was told about it (ADR 0065): it is off the booking pages while
/// it is kept, the link in their text books it with one tap, and when they do not take it the next in line is told.
/// One time is kept for one client and one client keeps one time, also when two things happen at the same moment.
/// Anna works 09:00 to 17:00 every day, Tom 10:00 to 14:00; a cut takes 30 minutes and costs £20; the visits are
/// three days ahead; freed times are kept for 30 minutes.
/// </summary>
[Collection(SharedDatabase.Name)]
public class WaitlistHoldTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string NoahPhone = "+447700900111";
    private const string MiaPhone = "+447700900222";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly LocalDate Today = SystemClock.Instance.GetCurrentInstant().InZone(London).Date;
    private static readonly LocalDate Day = Today.PlusDays(3);

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _anna;
    private Guid _cut;
    private Guid _olivia;
    private Guid _noah;
    private Guid _mia;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        factory.Sms.Clear();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
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
            db.Services.Add(cut);
            await db.SaveChangesAsync();
            (_anna, _cut) = (anna.Id, cut.Id);
        }

        _olivia = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123");
        _noah = await VisitSeed.ClientAsync(factory, _studio.Id, "Noah", "Adams", NoahPhone);
        _mia = await VisitSeed.ClientAsync(factory, _studio.Id, "Mia", "Clark", MiaPhone);
        await SettingsAsync<BookingSettings>(s => s.TellWaitlistWhenFreed = true);

        // No quiet hours, so the tests do not depend on the hour they run at.
        await SettingsAsync<NotificationSettings>(s =>
        {
            s.QuietHoursStartMinute = 0;
            s.QuietHoursEndMinute = 0;
        });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_freed_time_is_kept_for_the_first_client_waiting_and_is_off_the_booking_pages()
    {
        (await LoadAsync()).HoldFreedTimeMinutes.ShouldBe(30, "a salon that texts its waiting clients keeps the time for them unless it says otherwise");
        await WaitAsync(_noah);
        await WaitAsync(_mia);
        var freed = await CancelledAsync(At(11, 0));
        (await SlotsAsync(_anna)).ShouldContain(At(11, 0));
        var before = Now();

        (await OfferAsync(freed)).ShouldBe(WaitlistOfferOutcome.Told);

        var text = factory.Sms.Messages.ShouldHaveSingleItem();
        text.ToE164.ShouldBe(NoahPhone);
        text.Body.ShouldStartWith("Studio 20: Cut on ");
        text.Body.ShouldContain("11:00 has come up and is kept for you until ");
        var offer = (await OffersAsync()).ShouldHaveSingleItem();
        text.Body.ShouldEndWith($"/w/{offer.Token}");
        (offer.WaitlistEntryId, offer.ServiceId, offer.StaffMemberId, offer.Start, offer.End, offer.EndedAt, offer.Outcome, offer.FreedByAppointmentId)
            .ShouldBe(((await EntryAsync(_noah)).Id, _cut, _anna, At(11, 0), At(11, 30), null, null, freed));
        offer.HeldUntil.ShouldBe(offer.OfferedAt + Duration.FromMinutes(30));
        offer.OfferedAt.ShouldBeInRange(before, Now());
        offer.Token.Length.ShouldBe(22);

        // Kept means nobody else can book it: not on Anna's page, and not for "anybody" unless Tom is free then.
        var annas = await SlotsAsync(_anna);
        annas.ShouldNotContain(At(11, 0));
        annas.ShouldContain(At(10, 30));
        annas.ShouldContain(At(11, 30));
        (await SlotsAsync(null)).ShouldContain(At(11, 0), "Tom works at eleven and nothing of his is kept");
        await Should.ThrowAsync<SlotNoLongerAvailableException>(() => InScopeAsync(s => s.GetRequiredService<IBookingService>()
            .BookAsync(new BookAppointmentRequest(_mia, _anna, _cut, At(11, 0), AppointmentSource.Admin))));

        // The desk sees whose it is; the same cancellation heard twice keeps nothing twice and texts nobody again.
        var waiting = await InScopeAsync(s => s.GetRequiredService<IWaitlistService>().ListWaitingAsync());
        var held = waiting.Single(w => w.ClientId == _noah).Held.ShouldNotBeNull();
        (held.OfferId, held.StaffName, held.Start.ToInstant()).ShouldBe((offer.Id, "Anna", At(11, 0)));
        waiting.Single(w => w.ClientId == _mia).Held.ShouldBeNull();
        (await OfferAsync(freed)).ShouldBe(WaitlistOfferOutcome.NotBookable);
        factory.Sms.Messages.Count.ShouldBe(1);
        (await OffersAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_client_takes_the_time_with_the_link_and_the_visit_is_theirs()
    {
        await WaitAsync(_noah);
        await OfferAsync(await CancelledAsync(At(11, 0)));
        var token = (await OffersAsync()).ShouldHaveSingleItem().Token;

        var kept = (await FindAsync(token)).ShouldNotBeNull();
        (kept.State, kept.ServiceName, kept.StaffName, kept.Start.ToInstant(), kept.Price.Amount, kept.Price.Currency, kept.ClientFirstName, kept.VisitToken)
            .ShouldBe((WaitlistOfferState.Held, "Cut", "Anna", At(11, 0), 20m, "GBP", "Noah", null));

        var taken = await TakeAsync(token);

        taken.Outcome.ShouldBe(TakeHeldTimeOutcome.Booked);
        var visit = await InScopeAsync(s => s.GetRequiredService<SalonDbContext>().Appointments.AsNoTracking().SingleAsync(a => a.ClientId == _noah));
        (visit.StaffMemberId, visit.ServiceId, visit.Start, visit.Status, visit.Source, visit.PriceAmount).ShouldBe((_anna, _cut, At(11, 0), AppointmentStatus.Booked, AppointmentSource.Online, 20m));
        taken.VisitToken.ShouldBe(visit.PublicToken);
        var offer = (await OffersAsync()).ShouldHaveSingleItem();
        (offer.Outcome, offer.AppointmentId).ShouldBe((WaitlistOfferEnd.Taken, visit.Id));
        offer.EndedAt.ShouldNotBeNull();

        // The same link again, or a second tap: the visit they already have.
        var again = await TakeAsync(token);
        (again.Outcome, again.VisitToken).ShouldBe((TakeHeldTimeOutcome.AlreadyBooked, visit.PublicToken));
        var after = (await FindAsync(token)).ShouldNotBeNull();
        (after.State, after.VisitToken).ShouldBe((WaitlistOfferState.Taken, visit.PublicToken));
        (await InScopeAsync(s => s.GetRequiredService<SalonDbContext>().Appointments.CountAsync(a => a.ClientId == _noah))).ShouldBe(1);

        // The booking is a booking like any other: it closes the wish and the client gets their confirmation.
        await DispatchAsync();
        var wish = await InScopeAsync(s => s.GetRequiredService<SalonDbContext>().WaitlistEntries.AsNoTracking().SingleAsync(e => e.ClientId == _noah));
        (wish.Outcome, wish.AppointmentId).ShouldBe((WaitlistOutcome.Booked, visit.Id));
        factory.Sms.Messages.Count(m => m.ToE164 == NoahPhone).ShouldBe(2, "the offer and the confirmation");
        (await PassOnAsync()).ShouldBe(0, "a time that was taken is not passed on");
        (await SlotsAsync(_anna)).ShouldNotContain(At(11, 0));
    }

    [Fact]
    public async Task A_time_not_taken_goes_to_the_next_in_line_and_never_back_to_whoever_gave_it_up()
    {
        // Olivia cancels and is herself waiting for a cut, first in line.
        await WaitAsync(_olivia);
        await WaitAsync(_noah);
        await WaitAsync(_mia);
        await OfferAsync(await CancelledAsync(At(11, 0)));
        factory.Sms.Messages.ShouldHaveSingleItem().ToE164.ShouldBe(NoahPhone);
        var noahs = (await OffersAsync()).ShouldHaveSingleItem();
        (await PassOnAsync()).ShouldBe(0, "the time is still kept");

        // Half an hour passes. From that moment anybody can book it, whether or not the clock has run.
        await RunOutAsync(noahs.Id);
        (await SlotsAsync(_anna)).ShouldContain(At(11, 0));
        (await FindAsync(noahs.Token)).ShouldNotBeNull().State.ShouldBe(WaitlistOfferState.Over);

        (await PassOnAsync()).ShouldBe(1);

        var offers = await OffersAsync();
        offers.Count.ShouldBe(2);
        (offers[0].Id, offers[0].Outcome).ShouldBe((noahs.Id, WaitlistOfferEnd.NotTaken));
        var mias = offers[1];
        (mias.WaitlistEntryId, mias.Start, mias.EndedAt, mias.FreedByAppointmentId).ShouldBe(((await EntryAsync(_mia)).Id, At(11, 0), null, noahs.FreedByAppointmentId));
        factory.Sms.Messages.Select(m => m.ToE164).ShouldBe([NoahPhone, MiaPhone]);
        factory.Sms.Messages[1].Body.ShouldEndWith($"/w/{mias.Token}");
        (await SlotsAsync(_anna)).ShouldNotContain(At(11, 0), "it is kept again, for Mia");

        // Noah comes too late; Mia does not take it either; Olivia gave the time up and is not offered it back.
        (await TakeAsync(noahs.Token)).Outcome.ShouldBe(TakeHeldTimeOutcome.Over);
        await RunOutAsync(mias.Id);
        (await PassOnAsync()).ShouldBe(0);
        factory.Sms.Messages.Count.ShouldBe(2);
        (await OffersAsync()).ShouldAllBe(o => o.Outcome == WaitlistOfferEnd.NotTaken);
        (await SlotsAsync(_anna)).ShouldContain(At(11, 0), "nobody is left to tell: the time is on the booking pages");
        (await InScopeAsync(s => s.GetRequiredService<SalonDbContext>().Appointments.CountAsync(a => AppointmentStateMachine.Active.Contains(a.Status)))).ShouldBe(0);
    }

    [Fact]
    public async Task A_client_who_stops_waiting_gives_the_time_up_and_the_desk_can_let_one_go()
    {
        var user = await factory.CreateUserAsync(_studio.Id, "desk@studio-20.test", Role.Receptionist);
        await WaitAsync(_noah);
        await WaitAsync(_mia);
        await OfferAsync(await CancelledAsync(At(11, 0)));
        var noahs = (await OffersAsync()).ShouldHaveSingleItem();

        // The desk takes Noah off the list: his time goes to Mia on the next tick of the clock.
        (await InScopeAsync(s => s.GetRequiredService<IWaitlistService>().RemoveAsync(noahs.WaitlistEntryId, user.Id))).ShouldBeTrue();
        (await PassOnAsync()).ShouldBe(1);
        var mias = (await OffersAsync()).Single(o => o.EndedAt == null);
        mias.WaitlistEntryId.ShouldBe((await EntryAsync(_mia)).Id);
        (await OffersAsync()).Single(o => o.Id == noahs.Id).Outcome.ShouldBe(WaitlistOfferEnd.NotTaken);

        // The desk lets Mia's time go: it is free at once and nobody else is told.
        (await ReleaseAsync(mias.Id, user.Id, _polish.Id)).ShouldBeFalse("another salon has no such offer");
        (await ReleaseAsync(mias.Id, user.Id)).ShouldBeTrue();
        var released = (await OffersAsync()).Single(o => o.Id == mias.Id);
        (released.Outcome, released.EndedByUserId).ShouldBe((WaitlistOfferEnd.Released, user.Id));
        (await SlotsAsync(_anna)).ShouldContain(At(11, 0));
        (await ReleaseAsync(mias.Id, user.Id)).ShouldBeFalse("it was let go already");
        (await ReleaseAsync(Guid.NewGuid(), user.Id)).ShouldBeFalse();
        (await PassOnAsync()).ShouldBe(0);
        (await TakeAsync(mias.Token)).Outcome.ShouldBe(TakeHeldTimeOutcome.Over);
        factory.Sms.Messages.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_kept_time_that_cannot_be_booked_any_more_is_said_to_be_gone()
    {
        await WaitAsync(_noah);
        await OfferAsync(await CancelledAsync(At(11, 0)));
        var offer = (await OffersAsync()).ShouldHaveSingleItem();

        // Somebody at the desk wrote a visit straight into the diary at that time.
        await VisitSeed.AppointmentAsync(factory, _studio.Id, _mia, _anna, _cut, At(11, 0), AppointmentStatus.Booked);

        (await TakeAsync(offer.Token)).ShouldBe(new TakeHeldTimeResult(TakeHeldTimeOutcome.Gone, null));
        (await OffersAsync()).ShouldHaveSingleItem().Outcome.ShouldBe(WaitlistOfferEnd.Gone);
        (await FindAsync(offer.Token)).ShouldNotBeNull().State.ShouldBe(WaitlistOfferState.Over);
        (await InScopeAsync(s => s.GetRequiredService<SalonDbContext>().Appointments.CountAsync(a => a.ClientId == _noah))).ShouldBe(0);
        (await EntryAsync(_noah)).ClosedAt.ShouldBeNull("Noah is still waiting");
        (await TakeAsync(offer.Token)).Outcome.ShouldBe(TakeHeldTimeOutcome.Over);
    }

    [Fact]
    public async Task A_link_is_the_salons_own_and_a_salon_that_keeps_nothing_sends_the_old_text()
    {
        await WaitAsync(_noah);
        await OfferAsync(await CancelledAsync(At(11, 0)));
        var token = (await OffersAsync()).ShouldHaveSingleItem().Token;

        (await FindAsync(token, _polish.Id)).ShouldBeNull();
        (await TakeAsync(token, _polish.Id)).ShouldBe(new TakeHeldTimeResult(TakeHeldTimeOutcome.NotFound, null));
        foreach (var wrong in new[] { null, string.Empty, "short", token + "x", new string('a', 22), "<script>alert(1)</script>" })
        {
            (await FindAsync(wrong)).ShouldBeNull();
            (await TakeAsync(wrong)).Outcome.ShouldBe(TakeHeldTimeOutcome.NotFound);
        }

        (await OffersAsync()).ShouldHaveSingleItem().EndedAt.ShouldBeNull("none of that touched the offer");

        // Keeping switched off (0 minutes): the text of ADR 0056 with the booking page's address, and nothing is kept.
        await SettingsAsync<BookingSettings>(s => s.HoldFreedTimeMinutes = 0);
        await WaitAsync(_mia);
        (await OfferAsync(await CancelledAsync(At(15, 0)))).ShouldBe(WaitlistOfferOutcome.Told);
        factory.Sms.Messages[^1].Body.ShouldContain("Book it before it goes: ");
        factory.Sms.Messages[^1].Body.ShouldContain($"/book/{_cut}?date=");
        (await OffersAsync()).Count.ShouldBe(1);
        (await SlotsAsync(_anna)).ShouldContain(At(15, 0));
    }

    [Fact]
    public async Task Several_taps_at_the_same_moment_book_one_visit()
    {
        // One time is taken first, so nothing below waits for a first run of anything.
        await WaitAsync(_mia);
        await OfferAsync(await CancelledAsync(At(9, 0)));
        (await TakeAsync((await OffersAsync()).ShouldHaveSingleItem().Token)).Outcome.ShouldBe(TakeHeldTimeOutcome.Booked);
        await WaitAsync(_noah);
        await OfferAsync(await CancelledAsync(At(11, 0)));
        var token = (await OffersAsync()).Single(o => o.EndedAt == null).Token;

        // Five requests with the same link, each ready and waiting, let go at the same moment.
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new List<Task>();
        var taps = Enumerable.Range(0, 5).Select(_ =>
        {
            var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ready.Add(waiting.Task);
            return Task.Run(async () =>
            {
                await using var scope = factory.CreateTenantScope(_studio.Id);
                var offers = scope.ServiceProvider.GetRequiredService<IWaitlistOfferService>();
                (await offers.FindAsync(token)).ShouldNotBeNull();
                waiting.SetResult();
                await go.Task;
                return await offers.TakeAsync(token);
            });
        }).ToArray();
        await Task.WhenAll(ready);
        go.SetResult();
        var results = await Task.WhenAll(taps);

        results.Select(r => r.Outcome).Order().ToArray().ShouldBe(
            [TakeHeldTimeOutcome.Booked, TakeHeldTimeOutcome.AlreadyBooked, TakeHeldTimeOutcome.AlreadyBooked, TakeHeldTimeOutcome.AlreadyBooked, TakeHeldTimeOutcome.AlreadyBooked],
            string.Join(", ", results.Select(r => r.Outcome)));
        var visit = await InScopeAsync(s => s.GetRequiredService<SalonDbContext>().Appointments.AsNoTracking().SingleAsync(a => a.ClientId == _noah));
        results.ShouldAllBe(r => r.VisitToken == visit.PublicToken);
        (await OffersAsync()).Single(o => o.Token == token).Outcome.ShouldBe(WaitlistOfferEnd.Taken);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(0)]
    public async Task Two_times_freed_at_the_same_moment_tell_one_waiting_client_once(int holdMinutes)
    {
        await SettingsAsync<BookingSettings>(s => s.HoldFreedTimeMinutes = holdMinutes);

        // One offer is made first, so nothing below waits for a first run of anything.
        await WaitAsync(_mia);
        (await OfferAsync(await CancelledAsync(At(9, 0)))).ShouldBe(WaitlistOfferOutcome.Told);
        factory.Sms.Clear();

        // Noah alone can be told now (Mia was, a moment ago). Six of Anna's times are freed at once.
        await WaitAsync(_noah);
        var freed = new List<Guid>();
        for (var hour = 10; hour < 16; hour++)
        {
            freed.Add(await CancelledAsync(At(hour, 0)));
        }

        var now = Now();
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new List<Task>();
        var offers = freed.Select(visit =>
        {
            var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ready.Add(waiting.Task);
            return Task.Run(async () =>
            {
                await using var scope = factory.CreateTenantScope(_studio.Id);
                var service = scope.ServiceProvider.GetRequiredService<IWaitlistOfferService>();
                (await service.FindAsync(new string('a', 22))).ShouldBeNull();
                waiting.SetResult();
                await go.Task;
                return await service.OfferFreedTimeAsync(visit, now);
            });
        }).ToArray();
        await Task.WhenAll(ready);
        go.SetResult();
        var outcomes = await Task.WhenAll(offers);

        outcomes.Count(o => o == WaitlistOfferOutcome.Told).ShouldBe(1, string.Join(", ", outcomes));
        outcomes.Count(o => o == WaitlistOfferOutcome.NobodyWaiting).ShouldBe(5, string.Join(", ", outcomes));
        factory.Sms.Messages.ShouldHaveSingleItem().ToE164.ShouldBe(NoahPhone);
        var noahsWish = (await EntryAsync(_noah)).Id;
        (await OffersAsync()).Count(o => o.EndedAt == null && o.WaitlistEntryId == noahsWish).ShouldBe(holdMinutes > 0 ? 1 : 0);
    }

    [Fact]
    public async Task The_copy_of_a_clients_data_lists_what_was_kept_for_them_and_a_forgotten_client_leaves_no_offer_behind()
    {
        var owner = await factory.CreateUserAsync(_studio.Id, "owner@studio-20.test", Role.Owner);
        await WaitAsync(_noah);
        await OfferAsync(await CancelledAsync(At(11, 0)));
        var offer = (await OffersAsync()).ShouldHaveSingleItem();

        var copy = (await InScopeAsync(s => s.GetRequiredService<IClientDataService>().GetCopyAsync(_noah, owner.Id))).ShouldNotBeNull();
        var kept = copy.Waitlist.ShouldHaveSingleItem().Offered.ShouldHaveSingleItem();
        (kept.Start.ToInstant(), kept.Outcome, kept.Ending).ShouldBe((At(11, 0), null, "Standing"));
        await RunOutAsync(offer.Id);
        await PassOnAsync();
        copy = (await InScopeAsync(s => s.GetRequiredService<IClientDataService>().GetCopyAsync(_noah, owner.Id))).ShouldNotBeNull();
        copy.Waitlist.ShouldHaveSingleItem().Offered.ShouldHaveSingleItem().Ending.ShouldBe("NotTaken");
        using var ownerClient = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner, "boss@studio-20.test");
        (await GetAsync(ownerClient, $"/admin/clients/{_noah}/data")).ShouldContain("A time was kept for them: ");

        var erased = await InScopeAsync(s => s.GetRequiredService<IClientDataService>().EraseAsync(_noah, owner.Id));

        erased.Outcome.ShouldBe(ErasureOutcome.Erased);
        (await OffersAsync()).ShouldBeEmpty();
        (await FindAsync(offer.Token)).ShouldBeNull();
    }

    [Fact]
    public async Task The_client_opens_the_link_books_with_one_button_and_the_desk_sees_what_is_kept()
    {
        await WaitAsync(_noah);
        await WaitAsync(_mia);
        await OfferAsync(await CancelledAsync(At(11, 0)));
        var token = (await OffersAsync()).ShouldHaveSingleItem().Token;
        using var anyone = factory.ClientFor(HostA);

        // The desk's list says whose the time is.
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        var list = await GetAsync(desk, "/admin/appointments/waitlist");
        list.ShouldContain("Kept for this client: ");
        list.ShouldContain("11:00 with Anna, until ");
        list.ShouldContain("Let this time go");

        // Nobody signs in to open the link.
        var page = await GetAsync(anyone, $"/w/{token}");
        page.ShouldContain("A time is kept for you");
        page.ShouldContain("Noah, this time is kept for you until ");
        page.ShouldContain(">Cut<");
        page.ShouldContain(">Anna<");
        page.ShouldContain("£20.00");
        page.ShouldContain("Book this time");
        page.ShouldContain($"href=\"/book/{_cut}\"");

        var noToken = await anyone.PostAsync(new Uri($"/w/{token}", UriKind.Relative), new FormUrlEncodedContent([]));
        noToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await OffersAsync()).ShouldHaveSingleItem().EndedAt.ShouldBeNull();

        var booked = await PostAsync(anyone, $"/w/{token}", page);
        booked.StatusCode.ShouldBe(HttpStatusCode.OK);
        var done = WebUtility.HtmlDecode(await booked.Content.ReadAsStringAsync());
        done.ShouldContain("Your visit is booked");
        done.ShouldContain("It is booked. We look forward to seeing you.");
        var visit = await InScopeAsync(s => s.GetRequiredService<SalonDbContext>().Appointments.AsNoTracking().SingleAsync(a => a.ClientId == _noah));
        done.ShouldContain($"href=\"/book/cancel/{visit.PublicToken}\"");
        done.ShouldNotContain("Book this time");

        // The link again shows the visit; a link nobody gave out is explained, not a 404.
        var again = await GetAsync(anyone, $"/w/{token}");
        again.ShouldContain("Your visit is booked");
        (await PostAsync(anyone, $"/w/{token}", page)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await InScopeAsync(s => s.GetRequiredService<SalonDbContext>().Appointments.CountAsync(a => a.ClientId == _noah))).ShouldBe(1);
        (await GetAsync(anyone, $"/w/{new string('b', 22)}")).ShouldContain("We do not know this link.");
        using var theirs = factory.ClientFor(HostB);
        (await GetAsync(theirs, $"/w/{token}")).ShouldNotContain("Noah");
    }

    [Fact]
    public async Task A_link_that_ran_out_says_so_and_the_desk_lets_a_time_go_from_the_list()
    {
        await WaitAsync(_noah);
        await OfferAsync(await CancelledAsync(At(11, 0)));
        var offer = (await OffersAsync()).ShouldHaveSingleItem();
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        var list = await GetAsync(desk, "/admin/appointments/waitlist");
        var releasePath = $"/admin/appointments/waitlist/held/{offer.Id}/release";
        list.ShouldContain($"action=\"{releasePath}\"");

        using var staff = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Staff);
        (await PostAsync(staff, releasePath, list)).Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        using var theirOwner = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        (await PostAsync(theirOwner, releasePath, await GetAsync(theirOwner, "/admin/settings/waitlist"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await OffersAsync()).ShouldHaveSingleItem().EndedAt.ShouldBeNull();

        // The client has the page open, with its button, while the desk lets the time go.
        using var anyone = factory.ClientFor(HostA);
        var open = await GetAsync(anyone, $"/w/{offer.Token}");
        open.ShouldContain("Book this time");

        var released = await PostAsync(desk, releasePath, list);
        released.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        released.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe("/admin/appointments/waitlist");
        list = await GetAsync(desk, "/admin/appointments/waitlist");
        list.ShouldContain("The time is no longer kept. Anybody can book it now.");
        list.ShouldNotContain("Kept for this client: ");
        (await PostAsync(desk, releasePath, list)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var page = await GetAsync(anyone, $"/w/{offer.Token}");
        page.ShouldContain("This time is no longer kept for you.");
        page.ShouldNotContain("Book this time");
        var late = await PostAsync(anyone, $"/w/{offer.Token}", open);
        WebUtility.HtmlDecode(await late.Content.ReadAsStringAsync()).ShouldContain("This time is no longer kept for you.");
        (await InScopeAsync(s => s.GetRequiredService<SalonDbContext>().Appointments.CountAsync(a => a.ClientId == _noah))).ShouldBe(0);
    }

    [Fact]
    public async Task The_owner_says_for_how_long_a_time_is_kept()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        const string path = "/admin/settings/waitlist";
        var page = await GetAsync(owner, path);
        page.ShouldContain("Keep the time for them for this many minutes");
        page.ShouldContain("name=\"HoldMinutes\" type=\"text\" inputmode=\"numeric\" value=\"30\"");

        (await PostAsync(owner, path, page, ("TellWhenFreed", "true"), ("HoldMinutes", "45"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var rules = await LoadAsync();
        (rules.TellWaitlistWhenFreed, rules.HoldFreedTimeMinutes).ShouldBe((true, 45));

        // More than four hours, less than nothing, and letters are refused in words; nothing is saved.
        foreach (var wrong in new[] { "241", "-1", "a while", "12.5" })
        {
            var refused = await PostAsync(owner, path, page, ("HoldMinutes", wrong));
            refused.StatusCode.ShouldBe(HttpStatusCode.OK, wrong);
            WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()).ShouldContain("Enter a whole number of minutes from 0 to 240.");
        }

        rules = await LoadAsync();
        (rules.TellWaitlistWhenFreed, rules.HoldFreedTimeMinutes).ShouldBe((true, 45));
        (await PostAsync(owner, path, page, ("TellWhenFreed", "true"), ("HoldMinutes", "0"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await LoadAsync()).HoldFreedTimeMinutes.ShouldBe(0);
        (await PostAsync(owner, path, page, ("TellWhenFreed", "true"), ("HoldMinutes", "240"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await LoadAsync()).HoldFreedTimeMinutes.ShouldBe(240);
    }

    private static Instant Now() => SystemClock.Instance.GetCurrentInstant();

    private static Instant At(int hour, int minute) => London.AtStrictly(Day + new LocalTime(hour, minute)).ToInstant();

    /// <summary>Olivia's visit with Anna at that time, already cancelled by her: its time is free again.</summary>
    private Task<Guid> CancelledAsync(Instant start)
        => VisitSeed.AppointmentAsync(factory, _studio.Id, _olivia, _anna, _cut, start, AppointmentStatus.CancelledByClient);

    private async Task WaitAsync(Guid client)
    {
        var outcome = await InScopeAsync(s => s.GetRequiredService<IWaitlistService>().AddAsync(new WaitlistRequest(client, _cut, null, null, null, null), null));
        (outcome is WaitlistAddOutcome.Added or WaitlistAddOutcome.Updated).ShouldBeTrue(outcome.ToString());
    }

    private Task<WaitlistOfferOutcome> OfferAsync(Guid appointmentId)
        => InScopeAsync(s => s.GetRequiredService<IWaitlistOfferService>().OfferFreedTimeAsync(appointmentId, Now()));

    private Task<HeldTime?> FindAsync(string? token, Guid? tenantId = null)
        => InScopeAsync(s => s.GetRequiredService<IWaitlistOfferService>().FindAsync(token), tenantId);

    private Task<TakeHeldTimeResult> TakeAsync(string? token, Guid? tenantId = null)
        => InScopeAsync(s => s.GetRequiredService<IWaitlistOfferService>().TakeAsync(token), tenantId);

    private Task<bool> ReleaseAsync(Guid offerId, Guid? byUserId, Guid? tenantId = null)
        => InScopeAsync(s => s.GetRequiredService<IWaitlistOfferService>().ReleaseAsync(offerId, byUserId), tenantId);

    private Task<int> PassOnAsync()
        => InScopeAsync(s => s.GetRequiredService<IWaitlistOfferService>().PassOnDueAsync(Now()));

    /// <summary>The starts that can be booked on the day for a cut, with that person or with anybody.</summary>
    private async Task<IReadOnlyList<Instant>> SlotsAsync(Guid? staff)
        => [.. (await InScopeAsync(s => s.GetRequiredService<IAvailabilityService>().GetSlotsAsync(_cut, staff, Day, Day))).Select(s => s.Start)];

    /// <summary>As if the time the offer is kept for had passed a minute ago.</summary>
    private Task<int> RunOutAsync(Guid offerId)
        => InScopeAsync(s => s.GetRequiredService<SalonDbContext>().WaitlistOffers.Where(o => o.Id == offerId)
            .ExecuteUpdateAsync(u => u.SetProperty(o => o.HeldUntil, Now() - Duration.FromMinutes(1))));

    private Task<List<WaitlistOffer>> OffersAsync()
        => InScopeAsync(s => s.GetRequiredService<SalonDbContext>().WaitlistOffers.AsNoTracking().OrderBy(o => o.OfferedAt).ThenBy(o => o.Id).ToListAsync());

    private Task<WaitlistEntry> EntryAsync(Guid client)
        => InScopeAsync(s => s.GetRequiredService<SalonDbContext>().WaitlistEntries.AsNoTracking().SingleAsync(e => e.ClientId == client && e.ClosedAt == null));

    private Task<BookingSettings> LoadAsync() => InScopeAsync(s => s.GetRequiredService<ISettingService>().LoadAsync<BookingSettings>());

    private async Task DispatchAsync()
    {
        await using var scope = factory.CreateTenantScope(null);
        await scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync();
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

    private async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> act, Guid? tenantId = null)
    {
        await using var scope = factory.CreateTenantScope(tenantId ?? _studio.Id);
        return await act(scope.ServiceProvider);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string pageWithToken, params (string Key, string Value)[] fields)
        => client.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent(
            [new KeyValuePair<string, string>("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(pageWithToken)), .. fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))]));

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}