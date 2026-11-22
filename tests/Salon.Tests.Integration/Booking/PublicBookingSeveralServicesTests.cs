using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Appointments;
using Salon.Core.Catalog;
using Salon.Data;
using Salon.Services.Appointments;
using Salon.Services.Availability;
using Salon.Services.Booking;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Booking;

/// <summary>
/// Several services in one online booking (ADR 0095): they follow one another with one person, each an appointment of
/// its own, booked whole or not at all, spoken of in one message and cancelled by one link. Off unless switched on.
/// The salon is the one of <see cref="PublicBookingTests"/> (Anna 09:00–17:00, Tom 10:00–14:00, a 30-minute cut at
/// £20) with a 45-minute colour at £30 beside it.
/// </summary>
public partial class PublicBookingTests
{
    [Fact]
    public async Task Several_services_are_not_offered_unless_the_salon_platform_switches_them_on()
    {
        var colour = await AddTimedServiceAsync("Colour", 45, 30m);
        using var client = factory.ClientFor(HostA);

        var page = await GetAsync(client, $"/book/{_serviceId}?date={Iso(_day)}&staff=any&also={colour}");

        page.ShouldNotContain("data-testid=\"add-service\"");
        page.ShouldNotContain("data-testid=\"visit-lines\"");
        page.ShouldNotContain("Colour", Case.Sensitive, "the further service in the address is ignored");
        SlotHrefs(page).ShouldBe(await ExpectedSlotHrefsAsync(null), "the times are those of the one service");
    }

    [Fact]
    public async Task The_time_page_adds_a_visit_up_and_offers_only_the_times_the_whole_of_it_fits()
    {
        var colour = await AddTimedServiceAsync("Colour", 45, 30m);
        using var host = SeveralServicesOn();
        using var client = ClientOn(host);

        var one = await GetAsync(client, $"/book/{_serviceId}?date={Iso(_day)}&staff={_annaId}");
        one.ShouldContain($"href=\"/book?add={_serviceId}&staff={_annaId}\" class=\"btn btn-compact btn-secondary\" data-testid=\"add-service\"");
        one.ShouldNotContain("data-testid=\"remove-service\"", Case.Sensitive, "one service is changed, not removed");

        var both = await GetAsync(client, $"/book/{_serviceId}?date={Iso(_day)}&staff={_annaId}&also={colour}");

        both.ShouldContain("data-testid=\"service-name\">Cut</p>");
        both.ShouldContain("data-testid=\"extra-service-name\">Colour</p>");
        both.ShouldContain("data-testid=\"summary-when\">75 min</span>");
        both.ShouldContain("data-testid=\"summary-total\">£50.00</p>");
        // Taking one out leaves the other as the booking; the answer to "who with" is kept.
        both.ShouldContain($"href=\"/book/{colour}?staff={_annaId}\"");
        both.ShouldContain($"href=\"/book/{_serviceId}?staff={_annaId}\"");

        var expected = await VisitStartsAsync([_serviceId, colour], _annaId);
        var hrefs = SlotHrefs(both);
        hrefs.Count.ShouldBe(expected.Count);
        hrefs.ShouldAllBe(h => h.EndsWith($"&staff={_annaId}&also={colour}", StringComparison.Ordinal), "a tapped time keeps the whole visit");
        // Anna works to 17:00: a 75-minute visit can start at 15:45 at the latest, the cut alone at 16:30.
        expected.Max().InZone(London).TimeOfDay.ShouldBe(new LocalTime(15, 45));
        (await SlotStartsAsync(_studio.Id, _annaId)).Max().InZone(London).TimeOfDay.ShouldBe(new LocalTime(16, 30));

        // The continue link carries the visit to the details, whose summary names it whole.
        var start = expected[0];
        var picked = await GetAsync(client, $"/book/{_serviceId}?date={Iso(_day)}&staff={_annaId}&also={colour}&start={Uri.EscapeDataString(InstantPattern.ExtendedIso.Format(start))}");
        picked.ShouldContain($"/book/{_serviceId}/details?start={Uri.EscapeDataString(InstantPattern.ExtendedIso.Format(start))}&staff={_annaId}&also={colour}\"");
        var details = await GetAsync(client, $"{DetailsPath(start, _annaId)}&also={colour}");
        details.ShouldContain("Cut + Colour");
        details.ShouldContain("75 min");
        details.ShouldContain("£50.00");
        details.ShouldContain($"<input type=\"hidden\" name=\"Also\" value=\"{colour}\" />");
    }

    [Fact]
    public async Task Add_another_service_lists_what_is_not_in_the_booking_yet_and_leads_back_with_it_added()
    {
        var colour = await AddTimedServiceAsync("Colour", 45, 30m);
        using var host = SeveralServicesOn();
        using var client = ClientOn(host);

        var list = await GetAsync(client, $"/book?add={_serviceId}&staff={_annaId}");

        list.ShouldContain("data-testid=\"adding-title\">Add another service</h1>");
        list.ShouldContain($"href=\"/book/{_serviceId}?staff={_annaId}\" class=\"back-link\" data-testid=\"back-to-booking\"");
        list.ShouldContain($"href=\"/book/{_serviceId}?also={colour}&staff={_annaId}\" data-testid=\"book-service\"");
        list.ShouldNotContain($"href=\"/book/{_serviceId}\"", Case.Sensitive, "the service already chosen is not offered a second time");
        list.ShouldContain(">Add</span>");

        // Without the switch the same address is the ordinary first step.
        using var plain = factory.ClientFor(HostA);
        var ordinary = await GetAsync(plain, $"/book?add={_serviceId}&staff={_annaId}");
        ordinary.ShouldNotContain("data-testid=\"adding-title\"");
        ordinary.ShouldContain($"href=\"/book/{colour}\" data-testid=\"book-service\"");
    }

    [Fact]
    public async Task When_no_one_person_does_all_of_it_the_page_says_so_and_offers_no_time()
    {
        var colour = await AddTimedServiceAsync("Colour", 45, 30m);
        await SetOffersAsync(_annaId, _serviceId, offers: false);
        await SetOffersAsync(_tomId, colour, offers: false);
        using var host = SeveralServicesOn();
        using var client = ClientOn(host);

        var page = await GetAsync(client, $"/book/{_serviceId}?date={Iso(_day)}&also={colour}");

        page.ShouldContain("data-testid=\"nobody-offers-all\">Nobody here does all of these in one visit. Remove one, or book them separately.</p>");
        page.ShouldNotContain("data-testid=\"slot\"");
        page.ShouldNotContain("data-testid=\"staff-picker\"");
        page.ShouldContain("data-testid=\"remove-service\"");
    }

    [Fact]
    public async Task A_visit_of_two_services_is_two_appointments_in_a_row_one_message_and_one_link_that_cancels_both()
    {
        var colour = await AddTimedServiceAsync("Colour", 45, 30m);
        using var host = SeveralServicesOn();
        using var client = ClientOn(host);
        var start = (await VisitStartsAsync([_serviceId, colour], _annaId))[0];

        // Details with the further service, the code, the confirmation.
        var detailsPage = await GetAsync(client, $"{DetailsPath(start, _annaId)}&also={colour}");
        var fields = Fields(start, _annaId);
        fields["Also"] = colour.ToString();
        var posted = await PostAsync(client, $"/book/{_serviceId}/details", fields, detailsPage);
        posted.StatusCode.ShouldBe(HttpStatusCode.Redirect, await posted.Content.ReadAsStringAsync());
        var verifyPath = posted.Headers.Location.ShouldNotBeNull().OriginalString;
        (await VerificationsAsync(_studio.Id)).ShouldHaveSingleItem().ExtraServiceIds.ShouldBe([colour]);
        // The reconfigured host has a sender of its own; the outbox below runs on the shared one.
        var verified = await PostCodeAsync(client, verifyPath, host.Services.GetRequiredService<RecordingSmsSender>().LastCodeFor(PhoneE164).ShouldNotBeNull());
        verified.StatusCode.ShouldBe(HttpStatusCode.Redirect, await verified.Content.ReadAsStringAsync());

        var booked = await AppointmentsAsync(_studio.Id);
        booked.Count.ShouldBe(2);
        var (cut, dye) = (booked[0], booked[1]);
        cut.ServiceId.ShouldBe(_serviceId);
        dye.ServiceId.ShouldBe(colour);
        cut.Start.ShouldBe(start);
        dye.Start.ShouldBe(cut.End, "the second service follows the first without a gap");
        (cut.DurationMinutes, dye.DurationMinutes).ShouldBe((30, 45));
        (cut.PriceAmount, dye.PriceAmount).ShouldBe((20m, 30m));
        cut.StaffMemberId.ShouldBe(_annaId);
        dye.StaffMemberId.ShouldBe(_annaId);
        cut.VisitGroupId.ShouldNotBeNull();
        dye.VisitGroupId.ShouldBe(cut.VisitGroupId);
        verified.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"/book/done/{cut.Id}");

        var done = await GetAsync(client, $"/book/done/{cut.Id}");
        done.ShouldContain("data-testid=\"done-service\">Cut + Colour</dd>");
        done.ShouldContain("£50.00");

        // One confirmation for the visit, naming both; nothing for the second part.
        factory.Sms.Clear();
        await DispatchAsync();
        var confirmation = factory.Sms.Messages.ShouldHaveSingleItem();
        confirmation.Body.ShouldContain("your Cut + Colour at Studio 20 is booked");

        // The visit cannot be moved online yet: the page says so and offers to cancel.
        var change = await GetAsync(client, $"/book/change/{cut.PublicToken}");
        change.ShouldContain("This visit has several services and cannot be moved online.");
        change.ShouldNotContain("data-testid=\"slot\"");

        // The one link cancels the whole visit.
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var outcome = await scope.ServiceProvider.GetRequiredService<IPublicBookingService>().CancelByTokenAsync(cut.PublicToken);
            outcome.Outcome.ShouldBe(PublicCancellationOutcome.Cancelled);
        }

        (await AppointmentsAsync(_studio.Id)).ShouldAllBe(a => a.Status == AppointmentStatus.CancelledByClient);
    }

    [Fact]
    public async Task A_visit_is_booked_whole_or_not_at_all()
    {
        var colour = await AddTimedServiceAsync("Colour", 45, 30m);
        var clientId = await CreateClientAsync(_studio.Id, "Olivia", "Brown", Phone);
        var ten = _day.At(new LocalTime(10, 0)).InZoneStrictly(London).ToInstant();
        var halfPast = _day.At(new LocalTime(10, 30)).InZoneStrictly(London).ToInstant();

        await using var scope = factory.CreateTenantScope(_studio.Id);
        var booking = scope.ServiceProvider.GetRequiredService<IBookingService>();
        // Somebody already has Anna at 10:30, where the colour of a 10:00 visit would have to go.
        await booking.BookAsync(new BookAppointmentRequest(clientId, _annaId, _serviceId, halfPast, AppointmentSource.Admin));

        (await VisitStartsAsync([_serviceId, colour], _annaId)).ShouldNotContain(ten, "the cut would fit at 10:00, the visit does not");
        await Should.ThrowAsync<SlotNoLongerAvailableException>(() =>
            booking.BookVisitAsync(new BookVisitRequest(clientId, _annaId, [_serviceId, colour], ten, AppointmentSource.Online)));
        (await AppointmentsAsync(_studio.Id)).ShouldHaveSingleItem().Start.ShouldBe(halfPast);

        // "Anyone" still finds the visit at 10:00: with Tom, who is free and offers both.
        await using var other = factory.CreateTenantScope(_studio.Id);
        var anyone = await other.ServiceProvider.GetRequiredService<IAvailabilityService>().GetVisitSlotsAsync([_serviceId, colour], null, _day, _day);
        anyone.Single(s => s.Start == ten).StaffMemberId.ShouldBe(_tomId);
        anyone.Single(s => s.Start == ten).End.ShouldBe(ten.Plus(Duration.FromMinutes(75)));
    }

    private WebApplicationFactory<Program> SeveralServicesOn()
        => factory.WithWebHostBuilder(b => b.UseSetting("Features:SeveralServices", "true"));

    private static HttpClient ClientOn(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri($"https://{HostA}") });
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
        return client;
    }

    private async Task<Guid> AddTimedServiceAsync(string name, int minutes, decimal price)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var service = new Service { Name = name, Duration = Duration.FromMinutes(minutes), Price = price, Currency = "GBP", SortOrder = 2 };
        db.Services.Add(service);
        await db.SaveChangesAsync();
        return service.Id;
    }

    private async Task SetOffersAsync(Guid staffId, Guid serviceId, bool offers)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        db.StaffServiceOverrides.Add(new StaffServiceOverride { StaffMemberId = staffId, ServiceId = serviceId, Offers = offers });
        await db.SaveChangesAsync();
    }

    private async Task<List<Instant>> VisitStartsAsync(IReadOnlyList<Guid> serviceIds, Guid? staffId)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var slots = await scope.ServiceProvider.GetRequiredService<IAvailabilityService>().GetVisitSlotsAsync(serviceIds, staffId, _day, _day);
        return slots.Select(s => s.Start).ToList();
    }
}