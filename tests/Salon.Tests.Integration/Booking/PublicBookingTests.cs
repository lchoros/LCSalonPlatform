using System.Net;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using NodaTime;
using NodaTime.Text;

using Salon.Core.Appointments;
using Salon.Core.Appointments.Events;
using Salon.Core.Booking;
using Salon.Core.Catalog;
using Salon.Core.Settings;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Availability;
using Salon.Services.Events;
using Salon.Services.Settings;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Booking;

/// <summary>
/// The public booking flow over HTTP (ADR 0014): services → slots → details (SMS code) → verify → confirmation, the
/// confirmation SMS through the outbox, cancel by token, the no-verification path, tenant isolation of tokens and the
/// per-address rate limit. The salon has two stylists (Anna every day 09:00–17:00, Tom every day 10:00–14:00) and one
/// 30-minute service; bookings target tomorrow so the default notice rule never interferes.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class PublicBookingTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string Phone = "07700 900123";
    private const string PhoneE164 = "+447700900123";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _annaId;
    private Guid _tomId;
    private Guid _serviceId;
    private LocalDate _day;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        factory.Sms.Clear();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        (_annaId, _tomId, _serviceId) = await SeedSalonAsync(_studio.Id);
        _day = SystemClock.Instance.GetCurrentInstant().InZone(London).Date.PlusDays(1);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Step_1_lists_the_active_services_as_links_to_the_time_picker()
    {
        await AddServiceAsync(_studio.Id, "Hidden", active: false);
        using var client = factory.ClientFor(HostA);

        var html = await GetAsync(client, "/book");

        html.ShouldContain("Choose a service");
        html.ShouldContain("Studio 20");
        html.ShouldContain($"href=\"/book/{_serviceId}\"");
        html.ShouldContain("Cut");
        html.ShouldNotContain("Hidden");
        html.ShouldContain("£20.00");
        html.ShouldContain("30 min");

        using var platform = factory.ClientFor("localhost");
        (await platform.GetAsync(new Uri("/book", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Step_2_lists_the_engine_slots_for_anyone_and_for_one_person()
    {
        using var client = factory.ClientFor(HostA);

        var anyone = await GetAsync(client, $"/book/{_serviceId}?date={Iso(_day)}");
        var tomOnly = await GetAsync(client, $"/book/{_serviceId}?date={Iso(_day)}&staff={_tomId}");

        anyone.ShouldContain("Choose a time");
        anyone.ShouldContain("data-testid=\"staff-any\"");
        anyone.ShouldContain("Anyone available");
        anyone.ShouldContain("Anna");
        anyone.ShouldContain("Tom");
        SlotHrefs(anyone).ShouldBe(await ExpectedSlotHrefsAsync(null));
        SlotHrefs(tomOnly).ShouldBe(await ExpectedSlotHrefsAsync(_tomId));
        tomOnly.ShouldContain($"staff={_tomId}");
        DayCount(anyone).ShouldBe(14);

        (await client.GetAsync(new Uri($"/book/{Guid.NewGuid()}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_date_strip_is_bounded_by_MaxAdvanceDays()
    {
        await SaveSettingsAsync(_studio.Id, s => s.MaxAdvanceDays = 3);
        using var client = factory.ClientFor(HostA);

        var html = await GetAsync(client, $"/book/{_serviceId}");

        DayCount(html).ShouldBe(4);
    }

    [Fact]
    public async Task The_details_post_creates_a_verification_and_sends_one_six_digit_code_without_logging_it()
    {
        using var client = factory.ClientFor(HostA);

        var response = await PostDetailsAsync(client, Fields(At(10, 0)));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location.ShouldNotBeNull().OriginalString;
        location.ShouldStartWith("/book/verify/");
        var verificationId = Guid.Parse(location["/book/verify/".Length..]);

        var sms = factory.Sms.Messages.ShouldHaveSingleItem();
        sms.ToE164.ShouldBe(PhoneE164);
        sms.Body.ShouldContain("Studio 20");
        sms.Body.ShouldContain("10 minutes");
        var code = factory.Sms.LastCodeFor(PhoneE164).ShouldNotBeNull();
        code.Length.ShouldBe(6);

        var row = (await VerificationsAsync(_studio.Id)).ShouldHaveSingleItem();
        row.Id.ShouldBe(verificationId);
        row.PhoneE164.ShouldBe(PhoneE164);
        row.CodeHash.ShouldNotContain(code);
        row.CodeSalt.ShouldNotContain(code);
        row.CodeHash.ShouldBe(PhoneVerificationRules.Hash(row.CodeSalt, code));
        row.ExpiresAt.ShouldBe(row.CreatedAt.Plus(Duration.FromMinutes(10)));
        row.Attempts.ShouldBe(0);
        row.VerifiedAt.ShouldBeNull();
        row.ServiceId.ShouldBe(_serviceId);
        row.StaffMemberId.ShouldBeNull();
        row.Start.ShouldBe(At(10, 0));
        row.FirstName.ShouldBe("Olivia");
        row.LastName.ShouldBe("Brown");
        row.Email.ShouldBe("olivia@example.com");
        row.MarketingConsent.ShouldBeTrue();
        row.ConsentWording.ShouldNotBeNull().ShouldContain("Studio 20");
        (await AppointmentsAsync(_studio.Id)).ShouldBeEmpty();

        var verifyPage = await GetAsync(client, location);
        verifyPage.ShouldContain("data-testid=\"verify-form\"");
        verifyPage.ShouldContain("+44•••••••123");
        verifyPage.ShouldNotContain(PhoneE164);
    }

    [Fact]
    public async Task Five_wrong_codes_kill_the_verification_so_the_right_code_no_longer_books()
    {
        using var client = factory.ClientFor(HostA);
        var verifyPath = await StartAsync(client, Fields(At(10, 0)));
        var code = factory.Sms.LastCodeFor(PhoneE164)!;
        var wrong = code == "000000" ? "000001" : "000000";
        // The anti-forgery token is per session, not per form: once the row is dead the verify page has no form, so later
        // POSTs borrow a token from another page of this salon.
        var tokenPage = await GetAsync(client, DetailsPath(At(10, 0), null));

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var response = await PostCodeAsync(client, verifyPath, wrong);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).ShouldContain($"{5 - attempt} attempts left");
        }

        var fifth = await PostCodeAsync(client, verifyPath, wrong);
        fifth.StatusCode.ShouldBe(HttpStatusCode.OK);
        var fifthHtml = WebUtility.HtmlDecode(await fifth.Content.ReadAsStringAsync());
        fifthHtml.ShouldContain("Too many wrong codes");
        fifthHtml.ShouldContain("data-testid=\"start-again\"");
        (await VerificationsAsync(_studio.Id)).ShouldHaveSingleItem().Attempts.ShouldBe(5);

        var deadPage = await GetAsync(client, verifyPath);
        deadPage.ShouldNotContain("data-testid=\"verify-form\"");
        deadPage.ShouldContain("data-testid=\"start-again\"");

        var right = await PostAsync(client, verifyPath, new() { ["Code"] = code }, tokenPage);
        right.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await right.Content.ReadAsStringAsync()).ShouldContain("Too many wrong codes");
        (await AppointmentsAsync(_studio.Id)).ShouldBeEmpty();
        (await VerificationsAsync(_studio.Id)).ShouldHaveSingleItem().Attempts.ShouldBe(5, "a dead row counts nothing more");
    }

    [Fact]
    public async Task A_malformed_code_is_a_validation_message_and_costs_no_attempt()
    {
        using var client = factory.ClientFor(HostA);
        var verifyPath = await StartAsync(client, Fields(At(10, 0)));

        var malformed = await PostCodeAsync(client, verifyPath, "12");

        malformed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await malformed.Content.ReadAsStringAsync()).ShouldContain("Enter the 6-digit code.");
        (await VerificationsAsync(_studio.Id)).ShouldHaveSingleItem().Attempts.ShouldBe(0);
    }

    [Fact]
    public async Task The_right_code_books_online_creates_the_client_queues_the_event_and_the_confirmation_sms_follows()
    {
        using var client = factory.ClientFor(HostA);
        var verifyPath = await StartAsync(client, Fields(At(10, 0)));
        var code = factory.Sms.LastCodeFor(PhoneE164)!;

        var response = await PostCodeAsync(client, verifyPath, code);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location.ShouldNotBeNull().OriginalString;
        location.ShouldStartWith("/book/done/");
        var appointmentId = Guid.Parse(location["/book/done/".Length..]);

        var appointment = (await AppointmentsAsync(_studio.Id)).ShouldHaveSingleItem();
        appointment.Id.ShouldBe(appointmentId);
        appointment.Source.ShouldBe(AppointmentSource.Online);
        appointment.Status.ShouldBe(AppointmentStatus.Booked);
        appointment.Start.ShouldBe(At(10, 0));
        appointment.End.ShouldBe(At(10, 30));
        appointment.ServiceId.ShouldBe(_serviceId);
        new[] { _annaId, _tomId }.ShouldContain(appointment.StaffMemberId);
        appointment.CreatedByUserId.ShouldBeNull();
        var token = appointment.PublicToken.ShouldNotBeNull();
        PublicBookingToken.LooksValid(token).ShouldBeTrue();

        var clientRow = (await ClientsAsync(_studio.Id)).ShouldHaveSingleItem();
        appointment.ClientId.ShouldBe(clientRow.Id);
        clientRow.FirstName.ShouldBe("Olivia");
        clientRow.LastName.ShouldBe("Brown");
        clientRow.Phone.ShouldBe(PhoneE164);
        clientRow.Email.ShouldBe("olivia@example.com");
        clientRow.MarketingConsent.ShouldBeTrue();
        clientRow.MarketingConsentAt.ShouldNotBeNull();
        clientRow.MarketingConsentWording.ShouldNotBeNull().ShouldContain("I agree to Studio 20");
        (await ClientsAsync(_polish.Id)).ShouldBeEmpty();

        var verification = (await VerificationsAsync(_studio.Id)).ShouldHaveSingleItem();
        verification.VerifiedAt.ShouldNotBeNull();
        verification.AppointmentId.ShouldBe(appointmentId);

        var booked = (await OutboxRowsAsync()).Where(m => m.EventType == typeof(AppointmentBooked).FullName).ShouldHaveSingleItem();
        booked.TenantId.ShouldBe(_studio.Id);
        booked.Payload.ShouldContain(appointmentId.ToString());
        booked.Payload.ShouldNotContain("Olivia");
        booked.Payload.ShouldNotContain("7700");

        var done = await GetAsync(client, location);
        done.ShouldContain("Your appointment is booked");
        done.ShouldContain("Cut");
        done.ShouldContain("Studio 20");
        done.ShouldContain("10:00");
        done.ShouldContain($"href=\"/book/cancel/{token}\"");

        // A repeated POST (back button) lands on the same confirmation without a second booking; the verify GET does too.
        var again = await PostAsync(client, verifyPath, new() { ["Code"] = code }, await GetAsync(client, DetailsPath(At(10, 0), null)));
        again.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        again.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe(location);
        var verifyAgain = await client.GetAsync(new Uri(verifyPath, UriKind.Relative));
        verifyAgain.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        verifyAgain.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe(location);
        (await AppointmentsAsync(_studio.Id)).Count.ShouldBe(1);

        // The outbox dispatcher runs the confirmation consumer in the tenant's scope.
        factory.Sms.Clear();
        await DispatchAsync();
        var confirmation = factory.Sms.Messages.ShouldHaveSingleItem();
        confirmation.ToE164.ShouldBe(PhoneE164);
        confirmation.Body.ShouldContain("Olivia");
        confirmation.Body.ShouldContain("Cut");
        confirmation.Body.ShouldContain("Studio 20");
        confirmation.Body.ShouldContain(_day.ToString("ddd d MMM", System.Globalization.CultureInfo.GetCultureInfo("en-GB")) + ", 10:00");
        confirmation.Body.ShouldContain($"https://studio-20.localhost/book/cancel/{token}");
    }

    [Fact]
    public async Task A_returning_client_is_recognised_by_phone_and_not_duplicated()
    {
        var existingId = await CreateClientAsync(_studio.Id, "Liv", "Brown-Smith", "+44 7700 900123");
        using var client = factory.ClientFor(HostA);
        var verifyPath = await StartAsync(client, Fields(At(10, 0)));

        var response = await PostCodeAsync(client, verifyPath, factory.Sms.LastCodeFor(PhoneE164)!);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var clientRow = (await ClientsAsync(_studio.Id)).ShouldHaveSingleItem();
        clientRow.Id.ShouldBe(existingId);
        clientRow.FirstName.ShouldBe("Liv", "an existing card keeps its names");
        clientRow.MarketingConsent.ShouldBeTrue("a tick on an existing card grants consent");
        (await AppointmentsAsync(_studio.Id)).ShouldHaveSingleItem().ClientId.ShouldBe(existingId);
    }

    [Fact]
    public async Task Cancelling_by_token_frees_the_slot_and_the_link_cannot_be_reused()
    {
        using var client = factory.ClientFor(HostA);
        var (appointment, _) = await BookThroughTheFlowAsync(client, At(10, 0));
        var cancelPath = $"/book/cancel/{appointment.PublicToken}";
        (await SlotStartsAsync(_studio.Id, appointment.StaffMemberId)).ShouldNotContain(At(10, 0));

        var page = await GetAsync(client, cancelPath);
        page.ShouldContain("data-testid=\"cancel-form\"");
        page.ShouldContain("Cut");

        var cancelled = await PostAsync(client, cancelPath, [], page);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = WebUtility.HtmlDecode(await cancelled.Content.ReadAsStringAsync());
        result.ShouldContain("Your appointment has been cancelled.");

        var row = (await AppointmentsAsync(_studio.Id)).ShouldHaveSingleItem();
        row.Status.ShouldBe(AppointmentStatus.CancelledByClient);
        row.CancelledAt.ShouldNotBeNull();
        (await SlotStartsAsync(_studio.Id, appointment.StaffMemberId)).ShouldContain(At(10, 0));
        (await OutboxRowsAsync()).Select(m => m.EventType).ShouldContain(typeof(AppointmentCancelled).FullName);

        var reused = await GetAsync(client, cancelPath);
        reused.ShouldContain("already been cancelled");
        reused.ShouldNotContain("data-testid=\"cancel-form\"");
        var repost = await PostAsync(client, cancelPath, [], page);
        (await repost.Content.ReadAsStringAsync()).ShouldContain("already been cancelled");
        (await AppointmentsAsync(_studio.Id)).Count.ShouldBe(1);

        var bogus = await GetAsync(client, "/book/cancel/" + new string('A', 43));
        bogus.ShouldContain("This link is not valid or has already been used.");
        (await client.GetAsync(new Uri("/book/done/" + Guid.NewGuid(), UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_salon_cannot_see_or_cancel_the_appointment_behind_a_token()
    {
        using var clientA = factory.ClientFor(HostA);
        var (appointment, _) = await BookThroughTheFlowAsync(clientA, At(10, 0));
        var cancelPath = $"/book/cancel/{appointment.PublicToken}";
        using var clientB = factory.ClientFor(HostB);

        var page = await GetAsync(clientB, cancelPath);
        page.ShouldContain("Ten link jest nieprawidłowy");
        page.ShouldNotContain("Cut");
        page.ShouldNotContain("data-testid=\"cancel-form\"");

        // Any page of salon B with a form supplies the anti-forgery token for B's session.
        var login = await GetAsync(clientB, "/account/login");
        var post = await PostAsync(clientB, cancelPath, [], login);
        post.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await post.Content.ReadAsStringAsync()).ShouldContain("Ten link jest nieprawid");
        (await clientB.GetAsync(new Uri($"/book/done/{appointment.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await AppointmentsAsync(_studio.Id)).ShouldHaveSingleItem().Status.ShouldBe(AppointmentStatus.Booked);
    }

    [Fact]
    public async Task With_verification_switched_off_the_details_post_books_at_once()
    {
        await SaveSettingsAsync(_studio.Id, s => s.RequireSmsVerification = false);
        using var client = factory.ClientFor(HostA);

        var details = await GetAsync(client, DetailsPath(At(10, 0), _annaId));
        details.ShouldContain("Anna");
        details.ShouldNotContain("6-digit code");
        var response = await PostDetailsAsync(client, Fields(At(10, 0), _annaId));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldStartWith("/book/done/");
        factory.Sms.Messages.ShouldBeEmpty();
        (await VerificationsAsync(_studio.Id)).ShouldBeEmpty();
        var appointment = (await AppointmentsAsync(_studio.Id)).ShouldHaveSingleItem();
        appointment.StaffMemberId.ShouldBe(_annaId);
        appointment.Source.ShouldBe(AppointmentSource.Online);
        appointment.PublicToken.ShouldNotBeNull();
        (await ClientsAsync(_studio.Id)).ShouldHaveSingleItem().Phone.ShouldBe(PhoneE164);
    }

    [Fact]
    public async Task Invalid_details_re_render_with_messages_in_the_salon_language_and_send_nothing()
    {
        var (_, _, polishService) = await SeedSalonAsync(_polish.Id);
        using var client = factory.ClientFor(HostB);
        var start = DateTimeZoneProviders.Tzdb["Europe/Warsaw"].AtLeniently(_day.At(new LocalTime(10, 0))).ToInstant();

        var response = await PostAsync(client, $"/book/{polishService}/details", new()
        {
            ["Start"] = InstantPattern.ExtendedIso.Format(start),
            ["FirstName"] = "",
            ["LastName"] = "",
            ["Phone"] = "07700 900123",
            ["Email"] = "nie-email",
        }, await GetAsync(client, $"/book/{polishService}/details?start={Uri.EscapeDataString(InstantPattern.ExtendedIso.Format(start))}"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        html.ShouldContain("Podaj imię.");
        html.ShouldContain("Podaj nazwisko.");
        html.ShouldContain("Podaj prawidłowy numer telefonu");
        html.ShouldContain("Podaj prawidłowy adres e-mail.");
        factory.Sms.Messages.ShouldBeEmpty();
        (await VerificationsAsync(_polish.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_fourth_code_for_one_phone_within_an_hour_is_refused()
    {
        using var client = factory.ClientFor(HostA);

        for (var i = 0; i < 3; i++)
        {
            (await PostDetailsAsync(client, Fields(At(10, 0)))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        var fourth = await PostDetailsAsync(client, Fields(At(10, 0)));

        fourth.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await fourth.Content.ReadAsStringAsync()).ShouldContain("already sent several codes");
        factory.Sms.Messages.Count.ShouldBe(3);
        (await VerificationsAsync(_studio.Id)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_post_without_the_anti_forgery_token_is_refused()
    {
        using var client = factory.ClientFor(HostA);

        var response = await client.PostAsync(new Uri($"/book/{_serviceId}/details", UriKind.Relative), new FormUrlEncodedContent(Fields(At(10, 0))));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        factory.Sms.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Rapid_details_posts_from_one_address_are_rate_limited_on_a_host_with_the_default_policy()
    {
        using var limited = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(c =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["PublicBooking:SmsCodeRateLimit:PermitLimit"] = "10" })));
        using var client = limited.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri($"https://{HostA}") });
        var form = await client.GetAsync(new Uri(DetailsPath(At(10, 0), null), UriKind.Relative));
        var token = IdentityTestClient.AntiForgeryToken(await form.Content.ReadAsStringAsync());

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 11; attempt++)
        {
            // Empty names: a validation re-render, so no SMS and no per-phone limit; the address limit counts every POST.
            var response = await client.PostAsync(
                new Uri($"/book/{_serviceId}/details", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string> { ["Start"] = InstantPattern.ExtendedIso.Format(At(10, 0)), ["__RequestVerificationToken"] = token }));
            statuses.Add(response.StatusCode);
        }

        statuses.Take(10).ShouldAllBe(s => s == HttpStatusCode.OK);
        statuses[10].ShouldBe(HttpStatusCode.TooManyRequests);
    }

    private Instant At(int hour, int minute) => London.AtLeniently(_day.At(new LocalTime(hour, minute))).ToInstant();

    private static string Iso(LocalDate date) => LocalDatePattern.Iso.Format(date);

    private string DetailsPath(Instant start, Guid? staffId)
        => $"/book/{_serviceId}/details?start={Uri.EscapeDataString(InstantPattern.ExtendedIso.Format(start))}" + (staffId is { } s ? $"&staff={s}" : string.Empty);

    private static Dictionary<string, string> Fields(Instant start, Guid? staffId = null)
    {
        var fields = new Dictionary<string, string>
        {
            ["Start"] = InstantPattern.ExtendedIso.Format(start),
            ["FirstName"] = " Olivia ",
            ["LastName"] = "Brown",
            ["Phone"] = Phone,
            ["Email"] = "Olivia@Example.com",
            ["MarketingConsent"] = "true",
        };
        if (staffId is { } id)
        {
            fields["StaffMemberId"] = id.ToString();
        }

        return fields;
    }

    /// <summary>The page as text: entities decoded, because the encoder writes "+" as &amp;#x2B; and phones start with one.</summary>
    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, Dictionary<string, string> fields, string pageWithToken)
    {
        fields["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(pageWithToken);
        return await client.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    private async Task<HttpResponseMessage> PostDetailsAsync(HttpClient client, Dictionary<string, string> fields)
    {
        var staffId = fields.TryGetValue("StaffMemberId", out var s) ? Guid.Parse(s) : (Guid?)null;
        var page = await GetAsync(client, DetailsPath(InstantPattern.ExtendedIso.Parse(fields["Start"]).Value, staffId));
        return await PostAsync(client, $"/book/{_serviceId}/details", fields, page);
    }

    /// <summary>Posts the details and returns the verify path the client is sent to.</summary>
    private async Task<string> StartAsync(HttpClient client, Dictionary<string, string> fields)
    {
        var response = await PostDetailsAsync(client, fields);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
        return response.Headers.Location.ShouldNotBeNull().OriginalString;
    }

    /// <summary>GETs the verify page (which must still offer the form) for its anti-forgery token and POSTs the code.</summary>
    private static async Task<HttpResponseMessage> PostCodeAsync(HttpClient client, string verifyPath, string code)
    {
        var page = await GetAsync(client, verifyPath);
        page.ShouldContain("data-testid=\"verify-form\"");
        return await PostAsync(client, verifyPath, new() { ["Code"] = code }, page);
    }

    private async Task<(Appointment Appointment, string DonePath)> BookThroughTheFlowAsync(HttpClient client, Instant start)
    {
        var verifyPath = await StartAsync(client, Fields(start));
        var response = await PostCodeAsync(client, verifyPath, factory.Sms.LastCodeFor(PhoneE164)!);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
        var appointment = (await AppointmentsAsync(_studio.Id)).Single(a => a.Start == start);
        return (appointment, response.Headers.Location!.OriginalString);
    }

    private static List<string> SlotHrefs(string html)
        => SlotLinkPattern().Matches(html).Select(m => m.Groups["href"].Value).ToList();

    private static int DayCount(string html) => DayLinkPattern().Count(html);

    private async Task<List<string>> ExpectedSlotHrefsAsync(Guid? staffId)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var slots = await scope.ServiceProvider.GetRequiredService<IAvailabilityService>().GetSlotsAsync(_serviceId, staffId, _day, _day);
        slots.ShouldNotBeEmpty();
        var suffix = staffId is { } s ? $"&staff={s}" : string.Empty;
        return slots.Select(slot => $"/book/{_serviceId}/details?start={Uri.EscapeDataString(InstantPattern.ExtendedIso.Format(slot.Start))}{suffix}").ToList();
    }

    private async Task<List<Instant>> SlotStartsAsync(Guid tenantId, Guid staffId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var slots = await scope.ServiceProvider.GetRequiredService<IAvailabilityService>().GetSlotsAsync(_serviceId, staffId, _day, _day);
        return slots.Select(s => s.Start).ToList();
    }

    private async Task SaveSettingsAsync(Guid tenantId, Action<BookingSettings> change)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var settings = scope.ServiceProvider.GetRequiredService<ISettingService>();
        var current = await settings.LoadAsync<BookingSettings>();
        change(current);
        await settings.SaveAsync(current);
    }

    private async Task DispatchAsync()
    {
        await using var scope = factory.CreateTenantScope(null);
        await scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync();
    }

    private async Task<Guid> CreateClientAsync(Guid tenantId, string firstName, string lastName, string phone)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<Salon.Services.Clients.IClientService>()
            .CreateAsync(new Salon.Services.Clients.CreateClientRequest(firstName, lastName, phone));
    }

    private async Task<List<PhoneVerification>> VerificationsAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().PhoneVerifications.AsNoTracking().OrderBy(v => v.Id).ToListAsync();
    }

    private async Task<List<Appointment>> AppointmentsAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Appointments.AsNoTracking().OrderBy(a => a.Start).ToListAsync();
    }

    private async Task<List<Core.Clients.Client>> ClientsAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Clients.AsNoTracking().ToListAsync();
    }

    private async Task<List<Core.Events.OutboxMessage>> OutboxRowsAsync()
    {
        await using var scope = factory.CreateTenantScope(null);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().OutboxMessages.AsNoTracking().OrderBy(m => m.Id).ToListAsync();
    }

    private async Task<Guid> AddServiceAsync(Guid tenantId, string name, bool active)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var service = new Service { Name = name, Duration = Duration.FromMinutes(30), Price = 20m, Currency = "GBP", IsActive = active, SortOrder = 9 };
        db.Services.Add(service);
        await db.SaveChangesAsync();
        return service.Id;
    }

    private async Task<(Guid AnnaId, Guid TomId, Guid ServiceId)> SeedSalonAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
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

        var currency = tenantId == _polish.Id ? "PLN" : "GBP";
        var cut = new Service { Name = "Cut", Duration = Duration.FromMinutes(30), Price = 20m, Currency = currency, SortOrder = 1 };
        db.Services.Add(cut);
        await db.SaveChangesAsync();
        return (anna.Id, tom.Id, cut.Id);
    }

    [GeneratedRegex("href=\"(?<href>/book/[^\"]+/details\\?[^\"]+)\" data-testid=\"slot\"")]
    private static partial Regex SlotLinkPattern();

    [GeneratedRegex("data-testid=\"day\"")]
    private static partial Regex DayLinkPattern();
}
