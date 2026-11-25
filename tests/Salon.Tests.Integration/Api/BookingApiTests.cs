using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Text;
using Salon.Api.Contracts.Booking;
using Salon.Core.Appointments;
using Salon.Core.Catalog;
using Salon.Core.Settings;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Settings;
using Salon.Tests.Integration.Infrastructure;
using IAvailabilityService = Salon.Services.Availability.IAvailabilityService;
using VisitPart = Salon.Services.Availability.VisitPart;

namespace Salon.Tests.Integration.Api;

/// <summary>
/// The public booking API for the client app over HTTP (ADR 0097, docs/mobile/BOOKING-API.md): the same salon as
/// <see cref="Booking.PublicBookingTests"/> (Anna every day 09:00–17:00, Tom 10:00–14:00, a 30-minute cut at £20, a
/// 45-minute colour at £30), asked as JSON. The answers must say what the booking pages say, because they come from the
/// same queries; the refusals are <see cref="ApiProblem"/>s.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class BookingApiTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string Phone = "07700 900123";
    private const string PhoneE164 = "+447700900123";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _annaId;
    private Guid _tomId;
    private Guid _cutId;
    private Guid _colourId;
    private LocalDate _day;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        factory.Sms.Clear();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        await SeedAsync();
        _day = SystemClock.Instance.GetCurrentInstant().InZone(London).Date.PlusDays(1);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Services_are_the_salons_active_ones_with_their_prices_options_and_the_owners_pick()
    {
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            var nails = new ServiceCategory { Name = "Nails", SortOrder = 1 };
            db.ServiceCategories.Add(nails);
            db.Services.Add(new Service { Name = "Hidden", Duration = Duration.FromMinutes(30), Price = 5m, Currency = "GBP", IsActive = false });
            db.Services.Add(new Service { Name = "Gel short", Duration = Duration.FromMinutes(40), Price = 25m, Currency = "GBP", CategoryId = nails.Id, VariantGroup = "Gel", VariantLabel = "Short", SortOrder = 1, IsPopular = true });
            db.Services.Add(new Service { Name = "Gel long", Duration = Duration.FromMinutes(60), Price = 35m, Currency = "GBP", CategoryId = nails.Id, VariantGroup = "Gel", VariantLabel = "Long", SortOrder = 2 });
            // Tom charges more for a cut: the cut is "from" its cheapest price.
            db.StaffServiceOverrides.Add(new StaffServiceOverride { StaffMemberId = _tomId, ServiceId = _cutId, Price = 26m });
            await db.SaveChangesAsync();
        }

        await using (var scope = factory.CreateTenantScope(_polish.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            db.Services.Add(new Service { Name = "Strzyżenie", Duration = Duration.FromMinutes(30), Price = 80m, Currency = "PLN" });
            await db.SaveChangesAsync();
        }

        using var client = Api();
        var response = await client.GetAsync(new Uri(BookingApi.Services, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        var json = await response.Content.ReadAsStringAsync();
        json.ShouldContain("\"isPopular\":true", Case.Sensitive, "the JSON is camelCase");
        var list = (await response.Content.ReadFromJsonAsync<ServiceList>())!;

        list.Salon.ShouldBe(new SalonInfo("Studio 20", "en-GB", "GBP", "Europe/London", false, 1));
        // The catalogue's own order: the category first, then the services without one.
        list.Services.Select(s => s.Name).ShouldBe(["Gel short", "Gel long", "Cut", "Colour"]);
        var gel = list.Services[0];
        gel.ShouldBe(new ServiceItem(gel.Id, "Gel short", null, 40, 25m, false, "Nails", "Gel", "Short", true));
        list.Services[1].VariantGroup.ShouldBe("Gel");
        list.Services[1].IsPopular.ShouldBeFalse();
        var cut = list.Services.Single(s => s.Id == _cutId);
        cut.ShouldBe(new ServiceItem(_cutId, "Cut", null, 30, 20m, true, null, null, null, false));
        list.Services.ShouldNotContain(s => s.Name == "Hidden" || s.Name == "Strzyżenie");

        // A salon that takes several services in one booking says so.
        using var several = SeveralServicesOn();
        var open = (await Api(several).GetFromJsonAsync<ServiceList>(new Uri(BookingApi.Services, UriKind.Relative)))!;
        (open.Salon.SeveralServices, open.Salon.MaxServices).ShouldBe((true, VisitRules.MaxServices));

        // The bare platform domain has no booking.
        using var platform = Api(host: "localhost");
        (await platform.GetAsync(new Uri(BookingApi.Services, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Availability_of_one_service_shows_the_times_of_anyone_and_asks_who_with_before_going_on()
    {
        using var client = Api();
        var today = SystemClock.Instance.GetCurrentInstant().InZone(London).Date;

        var asked = await AvailabilityAsync(client, new AvailabilityRequest([new VisitPartRequest(_cutId, null)], Date: Iso(_day)));

        asked.SelectedDate.ShouldBe(Iso(_day));
        asked.Days.Count.ShouldBe(14);
        asked.Days[0].Date.ShouldBe(Iso(today));
        asked.EarlierFrom.ShouldBeNull();
        asked.LaterFrom.ShouldBe(Iso(today.PlusDays(14)));
        asked.Times.Select(t => t.Start).ShouldBe((await StartsAsync([_cutId], null)).Select(Iso));
        asked.Times.ShouldContain(t => t.LocalTime == "09:00" && t.PartOfDay == "morning");
        asked.Times.ShouldContain(t => t.LocalTime == "12:00" && t.PartOfDay == "afternoon");
        asked.Times.ShouldContain(t => t.LocalTime == "16:00" && t.PartOfDay == "evening");
        var part = asked.Parts.ShouldHaveSingleItem();
        part.ShouldBe(part with { ServiceId = _cutId, Name = "Cut", DurationMinutes = 30, Price = 20m, PriceIsFrom = false, StaffId = null, Anyone = false, NeedsStaffChoice = true, LocalStart = null, LocalEnd = null });
        part.Staff.Select(s => s.Name).ShouldBe(["Anna", "Tom"]);
        (asked.Total, asked.TotalMinutes, asked.Currency).ShouldBe((20m, 30, "GBP"));
        asked.ChosenStart.ShouldBeNull();
        asked.CanContinue.ShouldBeFalse();
        asked.CanAddService.ShouldBeFalse();

        // A tapped time with the question still open: kept, but not yet enough to go on.
        var first = asked.Times[0];
        var tapped = await AvailabilityAsync(client, new AvailabilityRequest([new VisitPartRequest(_cutId, null)], Date: Iso(_day), Start: first.Start));
        tapped.ChosenStart.ShouldBe(first.Start);
        tapped.Parts[0].NeedsStaffChoice.ShouldBeTrue();
        tapped.CanContinue.ShouldBeFalse();

        // "Anyone available" is an answer: the service has its own time, and the client may go on.
        var anyone = await AvailabilityAsync(client, new AvailabilityRequest([new VisitPartRequest(_cutId, BookingApi.AnyStaff)], Date: Iso(_day), Start: first.Start));
        anyone.Parts[0].Anyone.ShouldBeTrue();
        anyone.Parts[0].NeedsStaffChoice.ShouldBeFalse();
        first.LocalTime.ShouldBe("09:00", "Anna starts at nine");
        (anyone.Parts[0].LocalStart, anyone.Parts[0].LocalEnd).ShouldBe(("09:00", "09:30"));
        anyone.CanContinue.ShouldBeTrue();
        anyone.CanAddService.ShouldBeFalse("the salon takes one service per booking");

        // A time that is not free (nobody works at 07:00) comes back as not chosen.
        var early = Instant(7, 0);
        var stale = await AvailabilityAsync(client, new AvailabilityRequest([new VisitPartRequest(_cutId, BookingApi.AnyStaff)], Date: Iso(_day), Start: Iso(early)));
        stale.ChosenStart.ShouldBeNull();
        stale.CanContinue.ShouldBeFalse();
    }

    [Fact]
    public async Task Availability_with_a_person_chosen_is_that_persons_times_and_price()
    {
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            db.StaffServiceOverrides.Add(new StaffServiceOverride { StaffMemberId = _tomId, ServiceId = _cutId, Price = 26m });
            await db.SaveChangesAsync();
        }

        using var client = Api();

        var anyone = await AvailabilityAsync(client, new AvailabilityRequest([new VisitPartRequest(_cutId, BookingApi.AnyStaff)], Date: Iso(_day)));
        (anyone.Parts[0].Price, anyone.Parts[0].PriceIsFrom, anyone.TotalIsFrom).ShouldBe((20m, true, true));

        var tom = await AvailabilityAsync(client, new AvailabilityRequest([new VisitPartRequest(_cutId, _tomId.ToString())], Date: Iso(_day)));
        tom.Parts[0].StaffId.ShouldBe(_tomId);
        tom.Parts[0].NeedsStaffChoice.ShouldBeFalse();
        (tom.Parts[0].Price, tom.Parts[0].PriceIsFrom, tom.Total).ShouldBe((26m, false, 26m));
        tom.Times.Select(t => t.Start).ShouldBe((await StartsAsync([_cutId], _tomId)).Select(Iso));
        tom.Times[0].LocalTime.ShouldBe("10:00", "Tom starts at ten");

        // A person who is not here is no answer: the question is open again.
        var stranger = await AvailabilityAsync(client, new AvailabilityRequest([new VisitPartRequest(_cutId, Guid.NewGuid().ToString())], Date: Iso(_day)));
        stranger.Parts[0].NeedsStaffChoice.ShouldBeTrue();

        // A service this salon does not have is not found.
        var unknown = await client.PostAsJsonAsync(new Uri(BookingApi.Availability, UriKind.Relative), new AvailabilityRequest([new VisitPartRequest(Guid.NewGuid(), null)]));
        await ShouldBeProblemAsync(unknown, HttpStatusCode.NotFound, "notFound");
    }

    [Fact]
    public async Task Availability_of_two_services_gives_each_its_own_person_and_its_own_part_of_the_time()
    {
        using var host = SeveralServicesOn();
        using var client = Api(host);
        var ten = Instant(10, 0);

        // The colour has nobody yet: its card says the answer is owed, and nothing goes on.
        var owed = await AvailabilityAsync(client, new AvailabilityRequest([new VisitPartRequest(_cutId, _annaId.ToString()), new VisitPartRequest(_colourId, null)], Date: Iso(_day), Start: Iso(ten)));
        owed.Parts.Select(p => p.NeedsStaffChoice).ShouldBe([false, true]);
        owed.CanContinue.ShouldBeFalse();
        owed.CanAddService.ShouldBeFalse();

        // Anna cuts, Tom colours: one after the other, the total of both, and room for another service.
        var split = await AvailabilityAsync(client, new AvailabilityRequest([new VisitPartRequest(_cutId, _annaId.ToString()), new VisitPartRequest(_colourId, _tomId.ToString())], Date: Iso(_day), Start: Iso(ten)));
        split.ChosenStart.ShouldBe(Iso(ten));
        split.Parts.Select(p => (p.Name, p.StaffId, p.LocalStart, p.LocalEnd)).ShouldBe([("Cut", _annaId, "10:00", "10:30"), ("Colour", _tomId, "10:30", "11:15")]);
        (split.Total, split.TotalMinutes).ShouldBe((50m, 75));
        split.CanContinue.ShouldBeTrue();
        split.CanAddService.ShouldBeTrue();
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var plan = await scope.ServiceProvider.GetRequiredService<IAvailabilityService>().GetVisitPlanAsync([new VisitPart(_cutId, _annaId), new VisitPart(_colourId, _tomId)], _day, _day);
            split.Times.Select(t => t.Start).ShouldBe(plan.Select(s => Iso(s.Start)));
        }

        // Booked so, Tom has the colour until 11:15: for a cut at 10:30 the colour's sheet offers only Anna.
        await BookThroughApiAsync(client, [new VisitPartRequest(_cutId, _annaId.ToString()), new VisitPartRequest(_colourId, _tomId.ToString())], ten, host);
        var filtered = await AvailabilityAsync(client, new AvailabilityRequest([new VisitPartRequest(_cutId, _annaId.ToString()), new VisitPartRequest(_colourId, null)], Date: Iso(_day), Start: Iso(Instant(10, 30))));
        filtered.ChosenStart.ShouldNotBeNull();
        filtered.Parts[1].Staff.Select(s => s.Id).ShouldBe([_annaId]);
        filtered.Parts[0].Staff.Select(s => s.Id).ShouldContain(_annaId, "the person chosen stays in sight on their own sheet");
    }

    [Fact]
    public async Task Availability_refuses_a_visit_it_cannot_read_with_the_fields_named()
    {
        using var client = Api();

        var several = await client.PostAsJsonAsync(new Uri(BookingApi.Availability, UriKind.Relative), new AvailabilityRequest([new VisitPartRequest(_cutId, null), new VisitPartRequest(_colourId, null)]));
        var closed = await ShouldBeProblemAsync(several, HttpStatusCode.BadRequest, "validation");
        closed.Fields!["Parts"].ShouldBe(["This salon takes one service per online booking."]);

        var none = await client.PostAsJsonAsync(new Uri(BookingApi.Availability, UriKind.Relative), new AvailabilityRequest([]));
        (await ShouldBeProblemAsync(none, HttpStatusCode.BadRequest, "validation")).Fields!.Keys.ShouldBe(["Parts"]);

        var bad = await client.PostAsJsonAsync(new Uri(BookingApi.Availability, UriKind.Relative), new AvailabilityRequest([new VisitPartRequest(_cutId, "bob")], From: "tomorrow", Date: "26/11/2026", Start: "ten"));
        var fields = (await ShouldBeProblemAsync(bad, HttpStatusCode.BadRequest, "validation")).Fields!;
        fields.Keys.Order(StringComparer.Ordinal).ShouldBe(["Date", "From", "Parts[0].Staff", "Start"]);

        using var open = SeveralServicesOn();
        var twice = await Api(open).PostAsJsonAsync(new Uri(BookingApi.Availability, UriKind.Relative), new AvailabilityRequest([new VisitPartRequest(_cutId, null), new VisitPartRequest(_cutId, null)]));
        (await ShouldBeProblemAsync(twice, HttpStatusCode.BadRequest, "validation")).Fields!["Parts"].ShouldBe(["Each service can be in a booking once."]);

        var unreadable = await client.PostAsync(new Uri(BookingApi.Availability, UriKind.Relative), new StringContent("{\"parts\":", System.Text.Encoding.UTF8, "application/json"));
        await ShouldBeProblemAsync(unreadable, HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task Terms_are_the_notices_of_the_details_page()
    {
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var settings = scope.ServiceProvider.GetRequiredService<ISettingService>();
            await settings.SaveAsync(new CancellationPolicySettings { FreeChangeHours = 24, LateCancellationFeePercent = 50m, NoShowFeePercent = 100m });
        }

        using var client = Api();
        var terms = (await client.GetFromJsonAsync<BookingTerms>(new Uri(BookingApi.Terms, UriKind.Relative)))!;

        terms.RequiresSmsCode.ShouldBeTrue();
        terms.ConsentWording.ShouldContain("Studio 20");
        terms.PrivacyUrl.ShouldEndWith("/privacy");
        new Uri(terms.PrivacyUrl).Host.ShouldBe(HostA);
        (terms.FreeChangeHours, terms.LateCancellationFeePercent, terms.NoShowFeePercent).ShouldBe((24, 50m, 100m));

        // The Polish salon says it in its own language.
        using var polish = Api(host: HostB);
        var polishTerms = (await polish.GetFromJsonAsync<BookingTerms>(new Uri(BookingApi.Terms, UriKind.Relative)))!;
        polishTerms.ConsentWording.ShouldContain("Salon PL");
        polishTerms.ConsentWording.ShouldStartWith("Zgadzam się");
    }

    [Fact]
    public async Task One_service_is_booked_through_start_and_the_code_and_its_confirmation_has_the_manage_link()
    {
        using var client = Api();
        var start = (await StartsAsync([_cutId], _annaId))[0];

        var response = await client.PostAsJsonAsync(new Uri(BookingApi.Start, UriKind.Relative), Details([new VisitPartRequest(_cutId, _annaId.ToString())], start));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var started = (await response.Content.ReadFromJsonAsync<StartBookingResponse>())!;
        var verificationId = started.VerificationId.ShouldNotBeNull();
        started.AppointmentId.ShouldBeNull();
        started.MaskedPhone.ShouldNotBeNull().ShouldNotContain("900123");
        (await AppointmentsAsync()).ShouldBeEmpty("nothing is booked before the code");

        // A wrong code spends an attempt and says how many are left; the right one books.
        var code = factory.Sms.LastCodeFor(PhoneE164).ShouldNotBeNull();
        var wrong = await VerifyAsync(client, verificationId, code == "000000" ? "111111" : "000000");
        wrong.ShouldBe(new VerifyResponse(VerifyOutcomes.WrongCode, null, 4));
        var booked = await VerifyAsync(client, verificationId, code);
        booked.Outcome.ShouldBe(VerifyOutcomes.Booked);
        var appointment = (await AppointmentsAsync()).ShouldHaveSingleItem();
        booked.AppointmentId.ShouldBe(appointment.Id);
        (appointment.ServiceId, appointment.StaffMemberId, appointment.Start, appointment.Source).ShouldBe((_cutId, _annaId, start, AppointmentSource.Online));

        // The same code again lands on the same booking instead of a second one.
        (await VerifyAsync(client, verificationId, code)).AppointmentId.ShouldBe(appointment.Id);
        (await AppointmentsAsync()).Count.ShouldBe(1);

        var visit = (await client.GetFromJsonAsync<BookedVisit>(new Uri(BookingApi.Visit(appointment.Id), UriKind.Relative)))!;
        var local = start.InZone(London);
        visit.ShouldBe(new BookedVisit(
            appointment.Id,
            "Cut",
            "Anna",
            Iso(start),
            Iso(start.Plus(Duration.FromMinutes(30))),
            Iso(local.Date),
            local.TimeOfDay.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            20m,
            "GBP",
            "booked",
            visit.ManageUrl));
        var manage = new Uri(visit.ManageUrl);
        manage.Host.ShouldBe(HostA);
        manage.AbsolutePath.ShouldBe($"/v/{appointment.PublicToken}");
    }

    [Fact]
    public async Task Two_services_with_two_people_are_two_appointments_in_a_row()
    {
        using var host = SeveralServicesOn();
        using var client = Api(host);
        var ten = Instant(10, 0);

        var visitId = await BookThroughApiAsync(client, [new VisitPartRequest(_cutId, _annaId.ToString()), new VisitPartRequest(_colourId, _tomId.ToString())], ten, host);

        var booked = await AppointmentsAsync();
        booked.Select(a => (a.ServiceId, a.StaffMemberId)).ShouldBe([(_cutId, _annaId), (_colourId, _tomId)]);
        booked[0].Start.ShouldBe(ten);
        booked[1].Start.ShouldBe(booked[0].End);
        booked[1].VisitGroupId.ShouldBe(booked[0].VisitGroupId.ShouldNotBeNull());
        visitId.ShouldBe(booked[0].Id);

        var visit = (await client.GetFromJsonAsync<BookedVisit>(new Uri(BookingApi.Visit(visitId), UriKind.Relative)))!;
        (visit.ServiceName, visit.Price, visit.Start, visit.End).ShouldBe(("Cut + Colour", 50m, Iso(ten), Iso(ten.Plus(Duration.FromMinutes(75)))));
    }

    [Fact]
    public async Task Start_refuses_details_with_the_fields_named_in_the_words_of_the_form()
    {
        using var client = Api();
        var start = (await StartsAsync([_cutId], _annaId))[0];

        var blank = await client.PostAsJsonAsync(
            new Uri(BookingApi.Start, UriKind.Relative),
            new StartBookingRequest([new VisitPartRequest(_cutId, _annaId.ToString())], Iso(start), " ", "Brown", "12", "not-an-email", false));
        var fields = (await ShouldBeProblemAsync(blank, HttpStatusCode.BadRequest, "validation")).Fields!;
        fields["FirstName"].ShouldBe(["Enter your first name."]);
        fields["Phone"].ShouldBe(["Enter a valid mobile number; use the country code for numbers from abroad."]);
        fields["Email"].ShouldBe(["Enter a valid email address."]);
        fields.Keys.ShouldNotContain("LastName");

        // Every service needs its answer to "who with" by now, and a time that reads.
        var owed = await client.PostAsJsonAsync(new Uri(BookingApi.Start, UriKind.Relative), Details([new VisitPartRequest(_cutId, null)], start) with { Start = "soon" });
        var shape = (await ShouldBeProblemAsync(owed, HttpStatusCode.BadRequest, "validation")).Fields!;
        shape["Parts[0].Staff"].ShouldBe(["Choose who you would like for each service."]);
        shape["Start"].ShouldBe(["Choose a time first."]);

        // A person who does not do the service, or a service this salon does not have, is not found.
        await SetOffersAsync(_tomId, _cutId, offers: false);
        var notTom = await client.PostAsJsonAsync(new Uri(BookingApi.Start, UriKind.Relative), Details([new VisitPartRequest(_cutId, _tomId.ToString())], start));
        await ShouldBeProblemAsync(notTom, HttpStatusCode.NotFound, "notFound");
        var unknown = await client.PostAsJsonAsync(new Uri(BookingApi.Start, UriKind.Relative), Details([new VisitPartRequest(Guid.NewGuid(), BookingApi.AnyStaff)], start));
        await ShouldBeProblemAsync(unknown, HttpStatusCode.NotFound, "notFound");

        factory.Sms.Messages.ShouldBeEmpty("no code is sent for a refused request");
    }

    [Fact]
    public async Task With_codes_off_start_books_at_once_and_a_time_taken_meanwhile_is_a_conflict()
    {
        await SaveSettingsAsync(s => s.RequireSmsVerification = false);
        using var client = Api();
        var nine = Instant(9, 0);

        var terms = (await client.GetFromJsonAsync<BookingTerms>(new Uri(BookingApi.Terms, UriKind.Relative)))!;
        terms.RequiresSmsCode.ShouldBeFalse();

        var response = await client.PostAsJsonAsync(new Uri(BookingApi.Start, UriKind.Relative), Details([new VisitPartRequest(_cutId, _annaId.ToString())], nine));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var started = (await response.Content.ReadFromJsonAsync<StartBookingResponse>())!;
        started.VerificationId.ShouldBeNull();
        started.MaskedPhone.ShouldBeNull();
        started.AppointmentId.ShouldBe((await AppointmentsAsync()).ShouldHaveSingleItem().Id);

        // Anna is no longer free at nine.
        var again = await client.PostAsJsonAsync(new Uri(BookingApi.Start, UriKind.Relative), Details([new VisitPartRequest(_cutId, _annaId.ToString())], nine) with { Phone = "07700 900456" });
        var taken = await ShouldBeProblemAsync(again, HttpStatusCode.Conflict, "slotTaken");
        taken.Message.ShouldBe("Sorry, that time has just been taken. Please pick another.");
        (await AppointmentsAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_number_that_has_had_its_codes_is_refused_more()
    {
        using var client = Api();
        var start = (await StartsAsync([_cutId], _annaId))[0];

        for (var i = 0; i < 3; i++)
        {
            var sent = await client.PostAsJsonAsync(new Uri(BookingApi.Start, UriKind.Relative), Details([new VisitPartRequest(_cutId, _annaId.ToString())], start));
            sent.StatusCode.ShouldBe(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());
        }

        var fourth = await client.PostAsJsonAsync(new Uri(BookingApi.Start, UriKind.Relative), Details([new VisitPartRequest(_cutId, _annaId.ToString())], start));
        var problem = await ShouldBeProblemAsync(fourth, (HttpStatusCode)429, "tooManyCodes");
        problem.Message.ShouldBe("We have already sent several codes to this number. Please wait an hour and try again.");
    }

    [Fact]
    public async Task Rapid_posts_from_one_address_meet_the_pages_limit_and_get_a_problem_back()
    {
        using var limited = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(c =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["PublicBooking:CodeVerifyRateLimit:PermitLimit"] = "2" })));
        using var client = Api(limited);

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            last = await client.PostAsJsonAsync(new Uri(BookingApi.Verify(Guid.NewGuid()), UriKind.Relative), new VerifyRequest("123456"));
            statuses.Add(last.StatusCode);
        }

        statuses.ShouldBe([HttpStatusCode.NotFound, HttpStatusCode.NotFound, (HttpStatusCode)429]);
        last!.Headers.RetryAfter.ShouldNotBeNull();
        await ShouldBeProblemAsync(last, (HttpStatusCode)429, "tooManyCodes");
    }

    [Fact]
    public async Task A_code_that_is_not_six_digits_is_refused_before_an_attempt_is_spent()
    {
        using var client = Api();
        var start = (await StartsAsync([_cutId], _annaId))[0];
        var started = await StartAsync(client, [new VisitPartRequest(_cutId, _annaId.ToString())], start);

        var response = await client.PostAsJsonAsync(new Uri(BookingApi.Verify(started), UriKind.Relative), new VerifyRequest("12"));

        (await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, "validation")).Fields!["Code"].ShouldBe(["Enter the 6-digit code."]);
        (await VerifyAsync(client, started, "000000" == factory.Sms.LastCodeFor(PhoneE164) ? "111111" : "000000")).AttemptsLeft.ShouldBe(4, "the short code spent nothing");
    }

    [Fact]
    public async Task A_verification_or_a_visit_of_one_salon_is_not_found_at_another()
    {
        using var client = Api();
        var start = (await StartsAsync([_cutId], _annaId))[0];
        var verificationId = await StartAsync(client, [new VisitPartRequest(_cutId, _annaId.ToString())], start);
        var code = factory.Sms.LastCodeFor(PhoneE164).ShouldNotBeNull();

        using var other = Api(host: HostB);
        var foreign = await other.PostAsJsonAsync(new Uri(BookingApi.Verify(verificationId), UriKind.Relative), new VerifyRequest(code));
        await ShouldBeProblemAsync(foreign, HttpStatusCode.NotFound, "notFound");
        (await AppointmentsAsync()).ShouldBeEmpty("the code did not book at the other salon");

        var booked = await VerifyAsync(client, verificationId, code);
        var appointmentId = booked.AppointmentId.ShouldNotBeNull();
        (await other.GetAsync(new Uri(BookingApi.Visit(appointmentId), UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync(new Uri(BookingApi.Visit(Guid.NewGuid()), UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync(new Uri(BookingApi.Visit(appointmentId), UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static string Iso(LocalDate date) => LocalDatePattern.Iso.Format(date);

    private static string Iso(Instant instant) => InstantPattern.ExtendedIso.Format(instant);

    private Instant Instant(int hour, int minute) => _day.At(new LocalTime(hour, minute)).InZoneStrictly(London).ToInstant();

    private HttpClient Api(WebApplicationFactory<Program>? app = null, string host = HostA)
    {
        var client = (app ?? factory).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri($"https://{host}"), HandleCookies = false });
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    private WebApplicationFactory<Program> SeveralServicesOn()
        => factory.WithWebHostBuilder(b => b.UseSetting("Features:SeveralServices", "true"));

    private static StartBookingRequest Details(IReadOnlyList<VisitPartRequest> parts, Instant start)
        => new(parts, Iso(start), " Olivia ", "Brown", Phone, "Olivia@Example.com", true);

    private static async Task<Availability> AvailabilityAsync(HttpClient client, AvailabilityRequest request)
    {
        var response = await client.PostAsJsonAsync(new Uri(BookingApi.Availability, UriKind.Relative), request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Availability>())!;
    }

    private static async Task<Guid> StartAsync(HttpClient client, IReadOnlyList<VisitPartRequest> parts, Instant start)
    {
        var response = await client.PostAsJsonAsync(new Uri(BookingApi.Start, UriKind.Relative), Details(parts, start));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<StartBookingResponse>())!.VerificationId.ShouldNotBeNull();
    }

    private static async Task<VerifyResponse> VerifyAsync(HttpClient client, Guid verificationId, string code)
    {
        var response = await client.PostAsJsonAsync(new Uri(BookingApi.Verify(verificationId), UriKind.Relative), new VerifyRequest(code));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<VerifyResponse>())!;
    }

    /// <summary>Start and code through the API; the reconfigured host has an SMS sender of its own.</summary>
    private static async Task<Guid> BookThroughApiAsync(HttpClient client, IReadOnlyList<VisitPartRequest> parts, Instant start, WebApplicationFactory<Program> host)
    {
        var verificationId = await StartAsync(client, parts, start);
        var code = host.Services.GetRequiredService<RecordingSmsSender>().LastCodeFor(PhoneE164).ShouldNotBeNull();
        var booked = await VerifyAsync(client, verificationId, code);
        booked.Outcome.ShouldBe(VerifyOutcomes.Booked);
        return booked.AppointmentId.ShouldNotBeNull();
    }

    private static async Task<ApiProblem> ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, body);
        response.Content.Headers.ContentType?.MediaType.ShouldNotBeNull().ShouldContain("json");
        var problem = System.Text.Json.JsonSerializer.Deserialize<ApiProblem>(body, Json).ShouldNotBeNull();
        problem.Code.ShouldBe(code);
        problem.Message.ShouldNotBeNullOrWhiteSpace();
        return problem;
    }

    private async Task<List<Instant>> StartsAsync(IReadOnlyList<Guid> serviceIds, Guid? staffId)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var slots = await scope.ServiceProvider.GetRequiredService<IAvailabilityService>().GetVisitSlotsAsync(serviceIds, staffId, _day, _day);
        slots.ShouldNotBeEmpty();
        return slots.Select(s => s.Start).ToList();
    }

    private async Task<List<Appointment>> AppointmentsAsync()
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Appointments.AsNoTracking().OrderBy(a => a.Start).ToListAsync();
    }

    private async Task SaveSettingsAsync(Action<BookingSettings> change)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var settings = scope.ServiceProvider.GetRequiredService<ISettingService>();
        var current = await settings.LoadAsync<BookingSettings>();
        change(current);
        await settings.SaveAsync(current);
    }

    private async Task SetOffersAsync(Guid staffId, Guid serviceId, bool offers)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        db.StaffServiceOverrides.Add(new StaffServiceOverride { StaffMemberId = staffId, ServiceId = serviceId, Offers = offers });
        await db.SaveChangesAsync();
    }

    private async Task SeedAsync()
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
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
        var colour = new Service { Name = "Colour", Duration = Duration.FromMinutes(45), Price = 30m, Currency = "GBP", SortOrder = 2 };
        db.Services.AddRange(cut, colour);
        await db.SaveChangesAsync();
        (_annaId, _tomId, _cutId, _colourId) = (anna.Id, tom.Id, cut.Id, colour.Id);
    }
}