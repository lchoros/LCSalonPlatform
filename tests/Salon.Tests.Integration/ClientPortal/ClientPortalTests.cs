using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Booking;
using Salon.Core.Clients;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Booking;
using Salon.Services.ClientPortal;
using Salon.Services.Clients;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.ClientPortal;

/// <summary>
/// "My bookings" over HTTP (ADR 0083): a client proves a mobile number with the booking flow's text message code and
/// sees the visits still ahead of them at this salon, with the change and cancel links that already exist. The page
/// tells nobody which numbers the salon knows; the session is a cookie of its own that another salon, the staff
/// pages and time all refuse; with the flag off none of it exists.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class ClientPortalTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string VisitsPath = "/my-bookings";
    private const string SignInPath = "/my-bookings/sign-in";
    private const string SignOutPath = "/my-bookings/sign-out";
    private const string CookieName = "salon.client";
    private const string Phone = "07700 900123";
    private const string PhoneE164 = "+447700900123";

    // Nobody's number. Same country code and last three digits as Olivia's, so the two are masked alike and the
    // pages can be compared letter for letter.
    private const string UnknownPhone = "07711 900123";
    private const string UnknownE164 = "+447711900123";
    private const string SophiePhoneE164 = "+447700900456";
    private const string Refused = "That code isn't right or is no longer valid. Check the text message, or ask for a new code.";

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _staffId;
    private Guid _serviceId;
    private Guid _olivia;
    private Instant _now;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        factory.Sms.Clear();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        (_staffId, _serviceId) = await VisitSeed.SalonAsync(factory, _studio.Id);
        _olivia = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", PhoneE164, "olivia@example.test", "Prefers oat milk");
        _now = SystemClock.Instance.GetCurrentInstant();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_known_number_gets_a_code_and_sees_only_its_own_upcoming_visits_with_links_that_work()
    {
        var upcoming = await VisitAsync(_studio.Id, _olivia, _staffId, _serviceId, _now + Duration.FromDays(3), notes: "Allergic to latex");
        var later = await VisitAsync(_studio.Id, _olivia, _staffId, _serviceId, _now + Duration.FromDays(10));
        var legacy = await VisitAsync(_studio.Id, _olivia, _staffId, _serviceId, _now + Duration.FromDays(12), withLink: false);
        await VisitAsync(_studio.Id, _olivia, _staffId, _serviceId, _now - Duration.FromDays(7), AppointmentStatus.Completed, "Paid late last time");
        var cancelled = await VisitAsync(_studio.Id, _olivia, _staffId, _serviceId, _now + Duration.FromDays(5), AppointmentStatus.CancelledBySalon);
        // Somebody else at this salon, and the same number on a card at another salon.
        var sophie = await VisitSeed.ClientAsync(factory, _studio.Id, "Sophie", "Smith", SophiePhoneE164);
        var sophies = await VisitAsync(_studio.Id, sophie, _staffId, _serviceId, _now + Duration.FromDays(4));
        var (polishStaff, polishService) = await SalonAsync(_polish.Id, "Kasia", "Strzyżenie");
        var polishCard = await VisitSeed.ClientAsync(factory, _polish.Id, "Olivia", "Brown", PhoneE164);
        var polishVisit = await VisitAsync(_polish.Id, polishCard, polishStaff, polishService, _now + Duration.FromDays(2));
        using var client = factory.ClientFor(HostA);

        // Without a session the page sends the visitor to the sign-in.
        var before = await client.GetAsync(new Uri(VisitsPath, UriKind.Relative));
        before.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        before.Headers.Location!.OriginalString.ShouldBe(SignInPath);

        var asked = await RequestCodeAsync(client, Phone);
        asked.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var codePath = asked.Headers.Location!.OriginalString;
        codePath.ShouldStartWith("/my-bookings/code/");
        codePath.ShouldNotContain("900123", Case.Sensitive, "nothing personal in an address");
        var text = factory.Sms.Messages.ShouldHaveSingleItem();
        text.ToE164.ShouldBe(PhoneE164);
        text.Body.ShouldStartWith("Studio 20: your code is ");
        var code = factory.Sms.LastCodeFor(PhoneE164).ShouldNotBeNull();
        var codePage = await GetAsync(client, codePath);
        codePage.ShouldContain("If +44•••••••123 is on a booking with us");
        codePage.ShouldNotContain("7700900123", Case.Sensitive, "the number is masked");

        var entered = await PostCodeAsync(client, codePath, code, codePage);

        entered.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        entered.Headers.Location!.OriginalString.ShouldBe(VisitsPath);
        var cookie = SetCookie(entered, CookieName).ShouldNotBeNull();
        cookie.ShouldContain("path=/my-bookings", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("samesite=lax", Case.Insensitive);
        cookie.ShouldNotContain("domain=", Case.Insensitive);
        cookie.ShouldNotContain("expires=", Case.Insensitive);
        SetCookie(entered, IdentityTestClient.CookieName).ShouldBeNull("the staff cookie is not touched");

        var visitsResponse = await client.GetAsync(new Uri(VisitsPath, UriKind.Relative));
        visitsResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        visitsResponse.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        var page = WebUtility.HtmlDecode(await visitsResponse.Content.ReadAsStringAsync());
        page.ShouldContain("Your upcoming visits at Studio 20.");
        Count(page, "data-testid=\"portal-visit\"").ShouldBe(3);
        page.ShouldContain("data-testid=\"portal-visit-service\">Cut<");
        page.ShouldContain("with Anna");
        page.ShouldContain($"href=\"/book/change/{upcoming.Token}\" data-testid=\"portal-visit-change\"");
        page.ShouldContain($"href=\"/book/cancel/{upcoming.Token}\" data-testid=\"portal-visit-cancel\"");
        page.ShouldContain($"href=\"/book/cancel/{later.Token}\" data-testid=\"portal-visit-cancel\"");
        page.IndexOf(upcoming.Token!, StringComparison.Ordinal).ShouldBeLessThan(page.IndexOf(later.Token!, StringComparison.Ordinal), "soonest first");
        // A visit booked at the desk long ago has no link of its own: it is listed, and the client is told to ask.
        legacy.Token.ShouldBeNull();
        Count(page, "data-testid=\"portal-visit-ask\"").ShouldBe(1);
        page.ShouldContain("href=\"/book\" data-testid=\"portal-book-another\"");
        page.ShouldContain("data-testid=\"portal-sign-out\"");

        // Nothing of anybody else, of another salon, of the past, and nothing the desk wrote or charged.
        page.ShouldNotContain(sophies.Token!, Case.Sensitive);
        page.ShouldNotContain(polishVisit.Token!, Case.Sensitive);
        page.ShouldNotContain(cancelled.Token!, Case.Sensitive);
        page.ShouldNotContain("Strzyżenie", Case.Sensitive);
        page.ShouldNotContain("Sophie", Case.Sensitive);
        page.ShouldNotContain("Olivia", Case.Sensitive);
        page.ShouldNotContain("Brown", Case.Sensitive);
        page.ShouldNotContain("900123", Case.Sensitive);
        page.ShouldNotContain("olivia@example.test", Case.Sensitive);
        page.ShouldNotContain("oat milk", Case.Sensitive);
        page.ShouldNotContain("latex", Case.Sensitive);
        page.ShouldNotContain("Paid late", Case.Sensitive);
        page.ShouldNotContain("£", Case.Sensitive);
        page.ShouldNotContain("20.00", Case.Sensitive);

        // The links are the pages that already exist, and they work.
        (await GetAsync(client, $"/book/change/{upcoming.Token}")).ShouldContain("Change the time");
        var cancelPage = await GetAsync(client, $"/book/cancel/{upcoming.Token}");
        cancelPage.ShouldContain("data-testid=\"cancel-form\"");
        var cancelledNow = await client.PostAsync(new Uri($"/book/cancel/{upcoming.Token}", UriKind.Relative), new FormUrlEncodedContent(
            [new("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(cancelPage))]));
        WebUtility.HtmlDecode(await cancelledNow.Content.ReadAsStringAsync()).ShouldContain("Your appointment has been cancelled.");
        var after = await GetAsync(client, VisitsPath);
        Count(after, "data-testid=\"portal-visit\"").ShouldBe(2);
        after.ShouldNotContain(upcoming.Token!, Case.Sensitive);

        // Signed in already: the sign-in page sends the client on.
        (await client.GetAsync(new Uri(SignInPath, UriKind.Relative))).Headers.Location!.OriginalString.ShouldBe(VisitsPath);
    }

    [Fact]
    public async Task A_client_with_nothing_ahead_is_told_so_and_offered_to_book()
    {
        await VisitAsync(_studio.Id, _olivia, _staffId, _serviceId, _now - Duration.FromDays(7), AppointmentStatus.Completed);
        await VisitAsync(_studio.Id, _olivia, _staffId, _serviceId, _now - Duration.FromMinutes(5));
        using var client = factory.ClientFor(HostA);
        await SignInAsync(client, Phone, PhoneE164);

        var page = await GetAsync(client, VisitsPath);

        page.ShouldContain("data-testid=\"portal-no-visits\">You have no upcoming visits.<");
        page.ShouldNotContain("data-testid=\"portal-visit\"", Case.Sensitive, "a visit that has started is no longer ahead");
        page.ShouldContain("href=\"/book\" data-testid=\"portal-book-another\"");
    }

    [Fact]
    public async Task A_number_the_salon_does_not_know_gets_the_same_pages_and_no_text_message()
    {
        using var knownVisitor = factory.ClientFor(HostA);
        using var unknownVisitor = factory.ClientFor(HostA);

        var known = await RequestCodeAsync(knownVisitor, Phone);
        var unknown = await RequestCodeAsync(unknownVisitor, UnknownPhone);

        // The same answer: a redirect to a code page.
        (known.StatusCode, unknown.StatusCode).ShouldBe((HttpStatusCode.Redirect, HttpStatusCode.Redirect));
        var knownPath = known.Headers.Location!.OriginalString;
        var unknownPath = unknown.Headers.Location!.OriginalString;
        Shape(unknownPath).ShouldBe(Shape(knownPath));
        HeaderNames(unknown).ShouldBe(HeaderNames(known));

        // One text message, to the number the salon knows.
        factory.Sms.Messages.ShouldHaveSingleItem().ToE164.ShouldBe(PhoneE164);

        // The same code page, letter for letter (the masks are alike by construction).
        var knownPage = await GetAsync(knownVisitor, knownPath);
        var unknownPage = await GetAsync(unknownVisitor, unknownPath);
        Shape(unknownPage).ShouldBe(Shape(knownPage));

        // The same refusal for a wrong code, and for every code after too many of them.
        var code = factory.Sms.LastCodeFor(PhoneE164).ShouldNotBeNull();
        var wrong = code == "000000" ? "000001" : "000000";
        for (var attempt = 0; attempt <= PhoneVerificationRules.MaxAttempts; attempt++)
        {
            var knownAnswer = await PostCodeAsync(knownVisitor, knownPath, wrong, knownPage);
            var unknownAnswer = await PostCodeAsync(unknownVisitor, unknownPath, wrong, unknownPage);

            (knownAnswer.StatusCode, unknownAnswer.StatusCode).ShouldBe((HttpStatusCode.OK, HttpStatusCode.OK));
            var knownHtml = WebUtility.HtmlDecode(await knownAnswer.Content.ReadAsStringAsync());
            var unknownHtml = WebUtility.HtmlDecode(await unknownAnswer.Content.ReadAsStringAsync());
            knownHtml.ShouldContain($"data-testid=\"portal-code-error\">{Refused}<");
            Shape(unknownHtml).ShouldBe(Shape(knownHtml), $"attempt {attempt + 1}");
            SetCookie(unknownAnswer, CookieName).ShouldBeNull();
        }

        // Both rows are stored alike; the unknown number's can never be proven, whatever is typed.
        var rows = await RowsAsync(_studio.Id);
        rows.Count.ShouldBe(2);
        rows.ShouldAllBe(r => r.Purpose == PhoneVerificationPurpose.ClientPortal && r.ServiceId == null && r.VerifiedAt == null
            && r.Attempts == PhoneVerificationRules.MaxAttempts && r.FirstName.Length == 0 && r.LastName.Length == 0);
        rows.Select(r => r.PhoneE164).Order().ShouldBe([PhoneE164, UnknownE164]);
        (await ClientCountAsync(_studio.Id)).ShouldBe(1, "asking for a code never makes a client card");
    }

    [Fact]
    public async Task A_wrong_an_expired_a_used_and_a_spent_code_are_refused_in_one_wording()
    {
        await VisitAsync(_studio.Id, _olivia, _staffId, _serviceId, _now + Duration.FromDays(3));
        using var client = factory.ClientFor(HostA);

        // Wrong.
        var (path, code, page) = await AskAsync(client, Phone, PhoneE164);
        (await RefusalAsync(await PostCodeAsync(client, path, code == "111111" ? "222222" : "111111", page))).ShouldBe(Refused);

        // Not six digits: asked for again in the booking flow's words, and no attempt is counted.
        var malformed = await PostCodeAsync(client, path, "12", page);
        (await RefusalAsync(malformed)).ShouldBe("Enter the 6-digit code.");
        (await RowsAsync(_studio.Id)).ShouldHaveSingleItem().Attempts.ShouldBe(1);

        // Expired: the right code, too late.
        await InScopeAsync(_studio.Id, db => db.PhoneVerifications.ExecuteUpdateAsync(s => s.SetProperty(v => v.ExpiresAt, _now - Duration.FromMinutes(1))));
        var late = await PostCodeAsync(client, path, code, page);
        late.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RefusalAsync(late)).ShouldBe(Refused);
        SetCookie(late, CookieName).ShouldBeNull();

        // Spent: a code opens the door once.
        factory.Sms.Clear();
        (path, code, page) = await AskAsync(client, Phone, PhoneE164);
        (await PostCodeAsync(client, path, code, page)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        using var thief = factory.ClientFor(HostA);
        var replayPage = await GetAsync(thief, path);
        var replay = await PostCodeAsync(thief, path, code, replayPage);
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RefusalAsync(replay)).ShouldBe(Refused);
        SetCookie(replay, CookieName).ShouldBeNull();

        // An id that is nobody's, and a post without the anti-forgery token.
        (await client.GetAsync(new Uri($"/my-bookings/code/{Guid.NewGuid()}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var noToken = await thief.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent([new("Code", code)]));
        noToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_code_sent_for_a_booking_does_not_sign_in_and_a_sign_in_code_does_not_book()
    {
        using var client = factory.ClientFor(HostA);
        var (path, code, _) = await AskAsync(client, Phone, PhoneE164);
        var signInId = Guid.Parse(path[(path.LastIndexOf('/') + 1)..]);

        (await client.GetAsync(new Uri($"/book/verify/{signInId}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync(new Uri($"/book/waitlist/verify/{signInId}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var asBooking = await ScopedAsync<IPublicBookingService, PublicBookingVerification>(_studio.Id, s => s.VerifyAndBookAsync(signInId, code));
        asBooking.Outcome.ShouldBe(PublicBookingVerificationOutcome.NotFound);
        (await ScopedAsync<IPublicBookingService, PendingVerification?>(_studio.Id, s => s.GetVerificationAsync(signInId))).ShouldBeNull();

        var bookingRow = Guid.Empty;
        await InScopeAsync(_studio.Id, async db =>
        {
            var salt = PhoneVerificationRules.GenerateSalt();
            var booking = new PhoneVerification
            {
                PhoneE164 = PhoneE164,
                CodeSalt = salt,
                CodeHash = PhoneVerificationRules.Hash(salt, "123456"),
                ServiceId = _serviceId,
                Start = _now + Duration.FromDays(1),
                FirstName = "Olivia",
                LastName = "Brown",
                CreatedAt = _now,
                ExpiresAt = _now + Duration.FromMinutes(10),
            };
            db.PhoneVerifications.Add(booking);
            await db.SaveChangesAsync();
            bookingRow = booking.Id;
        });

        (await client.GetAsync(new Uri($"/my-bookings/code/{bookingRow}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var asSignIn = await ScopedAsync<IClientPortalService, ClientPortalSignIn>(_studio.Id, s => s.VerifyAsync(bookingRow, "123456"));
        asSignIn.Outcome.ShouldBe(ClientPortalSignInOutcome.NotFound);

        // The database itself refuses a booking row without a service; only a sign-in row may have none.
        await Should.ThrowAsync<DbUpdateException>(() => InScopeAsync(_studio.Id, async db =>
        {
            db.PhoneVerifications.Add(new PhoneVerification
            {
                PhoneE164 = PhoneE164,
                CodeSalt = "s",
                CodeHash = "h",
                Purpose = PhoneVerificationPurpose.Booking,
                FirstName = "Olivia",
                LastName = "Brown",
                CreatedAt = _now,
                ExpiresAt = _now + Duration.FromMinutes(10),
            });
            await db.SaveChangesAsync();
        }));
    }

    [Fact]
    public async Task A_number_is_on_one_card_per_salon_today_and_the_same_number_at_another_salon_stays_there()
    {
        // ADR 0011: a number is unique per salon, so "every card with the number" is one card. The day a family may
        // share a number this refusal goes, and the portal's query already reads every card that carries it.
        await Should.ThrowAsync<DbUpdateException>(() => VisitSeed.ClientAsync(factory, _studio.Id, "Mia", "Brown", PhoneE164));

        var mine = await VisitAsync(_studio.Id, _olivia, _staffId, _serviceId, _now + Duration.FromDays(3));
        var (polishStaff, polishService) = await SalonAsync(_polish.Id, "Kasia", "Strzyżenie");
        var polishCard = await VisitSeed.ClientAsync(factory, _polish.Id, "Mia", "Brown", PhoneE164);
        var theirs = await VisitAsync(_polish.Id, polishCard, polishStaff, polishService, _now + Duration.FromDays(3));

        // Asked through the service with the other salon's card id, this salon answers "no session".
        (await ScopedAsync<IClientPortalService, IReadOnlyList<ClientPortalVisit>?>(_studio.Id, s => s.GetUpcomingVisitsAsync(polishCard, _now + Duration.FromHours(1)))).ShouldBeNull();
        var own = await ScopedAsync<IClientPortalService, IReadOnlyList<ClientPortalVisit>?>(_studio.Id, s => s.GetUpcomingVisitsAsync(_olivia, _now + Duration.FromHours(1)));
        own.ShouldNotBeNull().ShouldHaveSingleItem().PublicToken.ShouldBe(mine.Token);
        var polish = await ScopedAsync<IClientPortalService, IReadOnlyList<ClientPortalVisit>?>(_polish.Id, s => s.GetUpcomingVisitsAsync(polishCard, _now + Duration.FromHours(1)));
        polish.ShouldNotBeNull().ShouldHaveSingleItem().PublicToken.ShouldBe(theirs.Token);
    }

    [Fact]
    public async Task Another_salons_cookie_is_refused_and_the_other_salon_reads_its_own_pages_in_its_own_language()
    {
        await VisitAsync(_studio.Id, _olivia, _staffId, _serviceId, _now + Duration.FromDays(3));
        var (polishStaff, polishService) = await SalonAsync(_polish.Id, "Kasia", "Strzyżenie");
        var polishCard = await VisitSeed.ClientAsync(factory, _polish.Id, "Olivia", "Brown", PhoneE164);
        var polishVisit = await VisitAsync(_polish.Id, polishCard, polishStaff, polishService, _now + Duration.FromDays(2));
        using var client = factory.ClientFor(HostA);
        var cookie = await SignInAsync(client, Phone, PhoneE164);
        (await GetAsync(client, VisitsPath)).ShouldContain("data-testid=\"portal-visit\"");

        // The browser would never send a host-only cookie to another salon; sent by hand, it is worthless there.
        using var elsewhere = factory.ClientFor(HostB, handleCookies: false);
        var refused = await SendWithCookieAsync(elsewhere, HttpMethod.Get, VisitsPath, cookie);
        refused.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        refused.Headers.Location!.OriginalString.ShouldBe(SignInPath);
        SetCookie(refused, CookieName).ShouldNotBeNull().ShouldStartWith(CookieName + "=;", Case.Sensitive, "the foreign cookie is dropped");
        // Refused as a session before anything is looked up: the sign-in shows its form instead of sending "them" on.
        (await SendWithCookieAsync(elsewhere, HttpMethod.Get, SignInPath, cookie)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var home = factory.ClientFor(HostA, handleCookies: false);
        (await SendWithCookieAsync(home, HttpMethod.Get, SignInPath, cookie)).StatusCode.ShouldBe(HttpStatusCode.Redirect, "at its own salon it is a session");

        // The verification row of one salon does not exist at the other.
        using var anonymous = factory.ClientFor(HostA);
        var path = (await RequestCodeAsync(anonymous, Phone)).Headers.Location!.OriginalString;
        (await elsewhere.GetAsync(new Uri(path, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The other salon's own client signs in there, in Polish, and sees only that salon's visit.
        using var polish = factory.ClientFor(HostB);
        (await GetAsync(polish, SignInPath)).ShouldContain("Moje wizyty");
        factory.Sms.Clear();
        await SignInAsync(polish, "+44 7700 900123", PhoneE164);
        var page = await GetAsync(polish, VisitsPath);
        page.ShouldContain("Twoje nadchodzące wizyty w salonie Salon PL.");
        page.ShouldContain("Strzyżenie");
        page.ShouldContain($"href=\"/book/cancel/{polishVisit.Token}\"");
        Count(page, "data-testid=\"portal-visit\"").ShouldBe(1);
    }

    [Fact]
    public async Task Staff_pages_do_not_take_the_clients_cookie_and_the_clients_pages_do_not_take_a_staff_login()
    {
        await VisitAsync(_studio.Id, _olivia, _staffId, _serviceId, _now + Duration.FromDays(3));
        using var client = factory.ClientFor(HostA);
        var clientCookie = await SignInAsync(client, Phone, PhoneE164);
        var clientValue = clientCookie[(CookieName.Length + 1)..];
        using var raw = factory.ClientFor(HostA, handleCookies: false);

        // The client's cookie, on its own name and dressed up as the staff cookie: the admin asks for a login.
        foreach (var attempt in new[] { clientCookie, $"{IdentityTestClient.CookieName}={clientValue}", $"{clientCookie}; {IdentityTestClient.CookieName}={clientValue}" })
        {
            var admin = await SendWithCookieAsync(raw, HttpMethod.Get, "/admin/appointments", attempt);
            admin.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            admin.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");
        }

        // An owner signed in to the admin is not a client: "My bookings" asks them to sign in like anybody else.
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var asOwner = await owner.GetAsync(new Uri(VisitsPath, UriKind.Relative));
        asOwner.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        asOwner.Headers.Location!.OriginalString.ShouldBe(SignInPath);

        // The staff cookie's value under the client's name is not a session either.
        await factory.CreateUserAsync(_studio.Id, $"manager@{HostA}", Role.Manager);
        using var manager = factory.ClientFor(HostA);
        var staffCookie = IdentityTestClient.AuthCookie(await IdentityTestClient.LoginAsync(manager, $"manager@{HostA}", IdentityTestClient.Password)).ShouldNotBeNull();
        var staffValue = staffCookie[(IdentityTestClient.CookieName.Length + 1)..];
        var disguised = await SendWithCookieAsync(raw, HttpMethod.Get, VisitsPath, $"{CookieName}={staffValue}");
        disguised.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        disguised.Headers.Location!.OriginalString.ShouldBe(SignInPath);

        // Signing the client out leaves the staff login alone.
        var visits = await GetAsync(client, VisitsPath);
        var left = await client.PostAsync(new Uri(SignOutPath, UriKind.Relative), new FormUrlEncodedContent(
            [new("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(visits))]));
        left.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        left.Headers.Location!.OriginalString.ShouldBe(SignInPath);
        SetCookie(left, CookieName).ShouldNotBeNull().ShouldStartWith(CookieName + "=;");
        SetCookie(left, IdentityTestClient.CookieName).ShouldBeNull();
        (await GetAsync(client, SignInPath)).ShouldContain("data-testid=\"portal-flash\" class=\"notice notice-ok\">You have signed out.<");
        (await client.GetAsync(new Uri(VisitsPath, UriKind.Relative))).Headers.Location!.OriginalString.ShouldBe(SignInPath);
        (await owner.GetAsync(new Uri("/admin/appointments", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Thirty_idle_minutes_or_twelve_hours_end_the_session()
    {
        await VisitAsync(_studio.Id, _olivia, _staffId, _serviceId, _now + Duration.FromDays(3));
        var clock = new MovableClock(SystemClock.Instance.GetCurrentInstant());
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(clock);
        }));
        var sms = host.Services.GetRequiredService<RecordingSmsSender>();
        using var client = ClientOf(host, HostA);

        // Idle: 29 minutes later the client is still in, and that visit counts; 30 minutes after it they are not.
        await SignInAsync(client, Phone, PhoneE164, sms);
        clock.Advance(Duration.FromMinutes(29));
        (await client.GetAsync(new Uri(VisitsPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        clock.Advance(Duration.FromMinutes(29));
        (await client.GetAsync(new Uri(VisitsPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        clock.Advance(Duration.FromMinutes(30));
        var idle = await client.GetAsync(new Uri(VisitsPath, UriKind.Relative));
        idle.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        idle.Headers.Location!.OriginalString.ShouldBe(SignInPath);
        SetCookie(idle, CookieName).ShouldNotBeNull().ShouldStartWith(CookieName + "=;");

        // Absolute: a visit every 25 minutes keeps it alive for twelve hours and not a minute longer.
        clock.Advance(Duration.FromHours(2));
        await SignInAsync(client, Phone, PhoneE164, sms);
        for (var minutes = 25; minutes < 12 * 60; minutes += 25)
        {
            clock.Advance(Duration.FromMinutes(25));
            (await client.GetAsync(new Uri(VisitsPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK, $"{minutes} minutes in");
        }

        clock.Advance(Duration.FromMinutes(20));
        var old = await client.GetAsync(new Uri(VisitsPath, UriKind.Relative));
        old.StatusCode.ShouldBe(HttpStatusCode.Redirect, "twelve hours after the code, twenty minutes after the last visit");
        old.Headers.Location!.OriginalString.ShouldBe(SignInPath);
    }

    [Fact]
    public async Task A_card_changed_or_erased_after_the_code_ends_the_session()
    {
        await VisitAsync(_studio.Id, _olivia, _staffId, _serviceId, _now + Duration.FromDays(3));
        using var client = factory.ClientFor(HostA);
        await SignInAsync(client, Phone, PhoneE164);
        (await client.GetAsync(new Uri(VisitsPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The desk gives the card another number: the number that was proven is no longer the card's.
        await ScopedAsync<IClientService, bool>(_studio.Id, async s =>
        {
            await s.UpdateDetailsAsync(_olivia, "Olivia", "Brown", "07700 900789", null, null);
            return true;
        });

        var changed = await client.GetAsync(new Uri(VisitsPath, UriKind.Relative));
        changed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        changed.Headers.Location!.OriginalString.ShouldBe(SignInPath);
        SetCookie(changed, CookieName).ShouldNotBeNull().ShouldStartWith(CookieName + "=;");

        // The old number opens nothing any more; the new one does.
        factory.Sms.Clear();
        await RequestCodeAsync(client, Phone);
        factory.Sms.Messages.ShouldBeEmpty();
        await SignInAsync(client, "07700 900789", "+447700900789");
        (await client.GetAsync(new Uri(VisitsPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A card without a number, as after an erasure, opens nothing either.
        await InScopeAsync(_studio.Id, db => db.Clients.Where(c => c.Id == _olivia).ExecuteUpdateAsync(s => s.SetProperty(c => c.Phone, (string?)null)));
        (await client.GetAsync(new Uri(VisitsPath, UriKind.Relative))).Headers.Location!.OriginalString.ShouldBe(SignInPath);
    }

    [Fact]
    public async Task A_fourth_code_in_an_hour_is_refused_in_the_booking_flows_words_whether_or_not_the_number_is_known()
    {
        using var client = factory.ClientFor(HostA);
        foreach (var number in new[] { Phone, UnknownPhone })
        {
            for (var i = 0; i < PhoneVerificationRules.MaxCodesPerPhone; i++)
            {
                (await RequestCodeAsync(client, number)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
            }

            var fourth = await RequestCodeAsync(client, number);

            fourth.StatusCode.ShouldBe(HttpStatusCode.OK);
            var html = WebUtility.HtmlDecode(await fourth.Content.ReadAsStringAsync());
            html.ShouldContain("data-testid=\"portal-phone-error\">We have already sent several codes to this number. Please wait an hour and try again.<");
        }

        factory.Sms.Messages.Count.ShouldBe(PhoneVerificationRules.MaxCodesPerPhone, "only the known number was texted");
        (await RowsAsync(_studio.Id)).Count.ShouldBe(2 * PhoneVerificationRules.MaxCodesPerPhone);

        // The limit is the booking flow's own: these codes count against a booking with the same number too.
        var start = await ScopedAsync<IPublicBookingService, Exception?>(_studio.Id, async s =>
            await Record.ExceptionAsync(() => s.StartAsync(new PublicBookingRequest(_serviceId, _staffId, _now + Duration.FromDays(1), "Olivia", "Brown", Phone, null, false, null))));
        start.ShouldBeOfType<PhoneVerificationRateLimitedException>();
    }

    [Fact]
    public async Task What_is_typed_wrong_is_said_in_the_booking_flows_words_and_sends_nothing()
    {
        using var client = factory.ClientFor(HostA);

        var blank = await RequestCodeAsync(client, " ");
        blank.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await blank.Content.ReadAsStringAsync()).ShouldContain("Enter your mobile number.");
        var unreadable = await RequestCodeAsync(client, "12");
        WebUtility.HtmlDecode(await unreadable.Content.ReadAsStringAsync()).ShouldContain("Enter a valid mobile number");

        factory.Sms.Messages.ShouldBeEmpty();
        (await RowsAsync(_studio.Id)).ShouldBeEmpty();
        var noToken = await client.PostAsync(new Uri(SignInPath, UriKind.Relative), new FormUrlEncodedContent([new("Phone", Phone)]));
        noToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        factory.Sms.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Rapid_posts_from_one_address_are_limited_by_the_booking_flows_own_policies()
    {
        using var limited = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(c =>
            c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PublicBooking:SmsCodeRateLimit:PermitLimit"] = "2",
                ["PublicBooking:CodeVerifyRateLimit:PermitLimit"] = "3",
            })));
        using var client = ClientOf(limited, HostA);
        var form = await GetAsync(client, SignInPath);

        // Asking for codes: two posts an hour here (ten by default), then 429 with Retry-After, as on a booking.
        var path = string.Empty;
        for (var i = 0; i < 2; i++)
        {
            var asked = await client.PostAsync(new Uri(SignInPath, UriKind.Relative), new FormUrlEncodedContent(
                [new("Phone", UnknownPhone), new("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(form))]));
            asked.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            path = asked.Headers.Location!.OriginalString;
        }

        var third = await client.PostAsync(new Uri(SignInPath, UriKind.Relative), new FormUrlEncodedContent(
            [new("Phone", UnknownPhone), new("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(form))]));
        third.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        third.Headers.RetryAfter.ShouldNotBeNull();

        // Trying codes: three an hour here (twenty by default).
        var page = await GetAsync(client, path);
        for (var i = 0; i < 3; i++)
        {
            (await PostCodeAsync(client, path, "000000", page)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await PostCodeAsync(client, path, "000000", page)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task With_the_flag_off_no_address_exists_and_no_link_shows()
    {
        using var client = factory.ClientFor(HostA);
        var path = (await RequestCodeAsync(client, Phone)).Headers.Location!.OriginalString;

        // On: a salon's public pages carry the link; the bare platform domain has neither the link nor the pages.
        (await GetAsync(client, "/book")).ShouldContain("href=\"/my-bookings\" data-testid=\"my-bookings-link\"");
        using var platform = factory.ClientFor("localhost");
        (await GetAsync(platform, "/")).ShouldNotContain("my-bookings");
        foreach (var address in new[] { VisitsPath, SignInPath, path })
        {
            (await platform.GetAsync(new Uri(address, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound, address);
        }

        (await platform.PostAsync(new Uri(SignInPath, UriKind.Relative), new FormUrlEncodedContent([new("Phone", Phone)]))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Off: every address is a 404, with or without a form, and the link is gone.
        using var off = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(c =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Features:ClientPortal"] = "false" })));
        using var visitor = ClientOf(off, HostA);
        (await GetAsync(visitor, "/book")).ShouldNotContain("my-bookings");
        (await GetAsync(visitor, "/")).ShouldNotContain("my-bookings");
        foreach (var address in new[] { VisitsPath, SignInPath, path, $"/my-bookings/code/{Guid.NewGuid()}" })
        {
            (await visitor.GetAsync(new Uri(address, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound, address);
        }

        // Anti-forgery tokens are not bound to one form: the staff login's is as good as any for the second post.
        var token = IdentityTestClient.AntiForgeryToken(await GetAsync(visitor, IdentityTestClient.LoginPath));
        foreach (var address in new[] { SignInPath, path, SignOutPath })
        {
            var bare = await visitor.PostAsync(new Uri(address, UriKind.Relative), new FormUrlEncodedContent([new("Phone", Phone), new("Code", "123456")]));
            bare.StatusCode.ShouldBe(HttpStatusCode.NotFound, $"POST {address} without a token");
            var withToken = await visitor.PostAsync(new Uri(address, UriKind.Relative), new FormUrlEncodedContent(
                [new("Phone", Phone), new("Code", "123456"), new("__RequestVerificationToken", token)]));
            withToken.StatusCode.ShouldBe(HttpStatusCode.NotFound, $"POST {address} with a token");
        }

        off.Services.GetRequiredService<RecordingSmsSender>().Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_log_names_ids_and_outcomes_and_never_the_number_the_code_or_a_name()
    {
        await VisitAsync(_studio.Id, _olivia, _staffId, _serviceId, _now + Duration.FromDays(3));
        var log = new CapturingLoggerProvider();
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services => services.AddSingleton<ILoggerProvider>(log)));
        var sms = host.Services.GetRequiredService<RecordingSmsSender>();
        using var client = ClientOf(host, HostA);

        // An unknown number, a wrong code, the right code, the visits, signing out.
        var unknownPath = (await RequestCodeAsync(client, UnknownPhone)).Headers.Location!.OriginalString;
        await PostCodeAsync(client, unknownPath, "000000", await GetAsync(client, unknownPath));
        var (path, code, page) = await AskAsync(client, Phone, PhoneE164, sms);
        await PostCodeAsync(client, path, code == "111111" ? "222222" : "111111", page);
        (await PostCodeAsync(client, path, code, page)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var visits = await GetAsync(client, VisitsPath);
        await client.PostAsync(new Uri(SignOutPath, UriKind.Relative), new FormUrlEncodedContent(
            [new("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(visits))]));

        var lines = log.Lines.ToList();
        lines.ShouldContain(l => l.Contains("signed in to the client portal", StringComparison.Ordinal), "the capture sees the portal's own lines");
        lines.ShouldContain(l => l.Contains("refused: WrongCode", StringComparison.Ordinal));
        foreach (var secret in new[] { "7700900123", "7711900123", "07700 900123", code, "Olivia", "Brown", "olivia@example.test" })
        {
            lines.Where(l => l.Contains(secret, StringComparison.OrdinalIgnoreCase)).ShouldBeEmpty($"'{secret}' is in the log");
        }
    }

    private static HttpClient ClientOf(WebApplicationFactory<Program> host, string hostName)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri($"https://{hostName}") });
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
        return client;
    }

    /// <summary>Posts the sign-in form with the number, after reading the form for its anti-forgery token.</summary>
    private static async Task<HttpResponseMessage> RequestCodeAsync(HttpClient client, string phone)
    {
        var form = await GetAsync(client, SignInPath);
        return await client.PostAsync(new Uri(SignInPath, UriKind.Relative), new FormUrlEncodedContent(
            [new("Phone", phone), new("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(form))]));
    }

    /// <summary>Asks for a code and reads it from the "phone": the code page's address, the code and the page.</summary>
    private async Task<(string Path, string Code, string Page)> AskAsync(HttpClient client, string phone, string phoneE164, RecordingSmsSender? sms = null)
    {
        var asked = await RequestCodeAsync(client, phone);
        asked.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var path = asked.Headers.Location!.OriginalString;
        var code = (sms ?? factory.Sms).LastCodeFor(phoneE164).ShouldNotBeNull();
        return (path, code, await GetAsync(client, path));
    }

    /// <summary>Signs the client in and returns the "salon.client=value" pair the server set.</summary>
    private async Task<string> SignInAsync(HttpClient client, string phone, string phoneE164, RecordingSmsSender? sms = null)
    {
        var (path, code, page) = await AskAsync(client, phone, phoneE164, sms);
        var entered = await PostCodeAsync(client, path, code, page);
        entered.StatusCode.ShouldBe(HttpStatusCode.Redirect, "the right code did not sign in");
        entered.Headers.Location!.OriginalString.ShouldBe(VisitsPath);
        return SetCookie(entered, CookieName).ShouldNotBeNull().Split(';')[0];
    }

    private static Task<HttpResponseMessage> PostCodeAsync(HttpClient client, string codePath, string code, string pageWithToken)
        => client.PostAsync(new Uri(codePath, UriKind.Relative), new FormUrlEncodedContent(
        [
            new("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(pageWithToken)),
            new("Code", code),
        ]));

    private static async Task<HttpResponseMessage> SendWithCookieAsync(HttpClient client, HttpMethod method, string path, string cookie)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }

    /// <summary>The message next to the code field, or next to the phone field.</summary>
    private static async Task<string> RefusalAsync(HttpResponseMessage response)
    {
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var match = FieldErrorPattern().Match(html);
        match.Success.ShouldBeTrue("the page shows no message next to the field");
        return match.Groups["message"].Value;
    }

    /// <summary>The whole Set-Cookie line for <paramref name="name"/>, or null when the response sets no such cookie.</summary>
    private static string? SetCookie(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(name + "=", StringComparison.Ordinal))
            : null;

    private static string[] HeaderNames(HttpResponseMessage response)
        => [.. response.Headers.Select(h => h.Key).Concat(response.Content.Headers.Select(h => h.Key)).Order(StringComparer.Ordinal)];

    /// <summary>A page or an address with what differs by design taken out: ids and anti-forgery tokens.</summary>
    private static string Shape(string text) => GuidPattern().Replace(TokenPattern().Replace(text, "TOKEN"), "ID");

    private static int Count(string text, string fragment) => text.Split(fragment).Length - 1;

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private async Task<(Guid StaffId, Guid ServiceId)> SalonAsync(Guid tenantId, string staffName, string serviceName)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var staff = new Salon.Core.Staff.StaffMember { DisplayName = staffName, CreatedAt = _now };
        var service = new Salon.Core.Catalog.Service { Name = serviceName, Duration = Duration.FromMinutes(30), Price = 80m, Currency = "PLN" };
        db.StaffMembers.Add(staff);
        db.Services.Add(service);
        await db.SaveChangesAsync();
        return (staff.Id, service.Id);
    }

    /// <summary>One visit written straight to the tables, with the client's own link unless told otherwise.</summary>
    private async Task<(Guid Id, string? Token)> VisitAsync(
        Guid tenantId,
        Guid clientId,
        Guid staffId,
        Guid serviceId,
        Instant start,
        AppointmentStatus status = AppointmentStatus.Booked,
        string? notes = null,
        bool withLink = true)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var appointment = new Appointment
        {
            TenantId = tenantId,
            ClientId = clientId,
            StaffMemberId = staffId,
            ServiceId = serviceId,
            Start = start,
            End = start + Duration.FromMinutes(30),
            Status = status,
            Source = withLink ? AppointmentSource.Online : AppointmentSource.Admin,
            Notes = notes,
            PriceAmount = 20m,
            Currency = "GBP",
            DurationMinutes = 30,
            CreatedAt = _now - Duration.FromDays(1),
            PublicToken = withLink ? PublicBookingToken.Generate() : null,
        };
        db.Appointments.Add(appointment);
        db.AppointmentStatusChanges.Add(new AppointmentStatusChange { TenantId = tenantId, AppointmentId = appointment.Id, To = status, At = appointment.CreatedAt });
        await db.SaveChangesAsync();
        return (appointment.Id, appointment.PublicToken);
    }

    private async Task<List<PhoneVerification>> RowsAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().PhoneVerifications.AsNoTracking().OrderBy(v => v.CreatedAt).ToListAsync();
    }

    private async Task<int> ClientCountAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Set<Client>().CountAsync();
    }

    private async Task InScopeAsync(Guid tenantId, Func<SalonDbContext, Task> act)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        await act(scope.ServiceProvider.GetRequiredService<SalonDbContext>());
    }

    private async Task<T> ScopedAsync<TService, T>(Guid tenantId, Func<TService, Task<T>> act)
        where TService : notnull
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await act(scope.ServiceProvider.GetRequiredService<TService>());
    }

    [GeneratedRegex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase)]
    private static partial Regex GuidPattern();

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"[^\"]+\"")]
    private static partial Regex TokenPattern();

    [GeneratedRegex("data-testid=\"portal-(?:code|phone)-error\">(?<message>[^<]+)<")]
    private static partial Regex FieldErrorPattern();

    /// <summary>The clock abstraction the host already has (NodaTime's <see cref="IClock"/>), moved by hand.</summary>
    private sealed class MovableClock(Instant start) : IClock
    {
        private long _ticks = start.ToUnixTimeTicks();

        public Instant GetCurrentInstant() => Instant.FromUnixTimeTicks(Interlocked.Read(ref _ticks));

        public void Advance(Duration by) => Interlocked.Add(ref _ticks, by.BclCompatibleTicks);
    }

    /// <summary>Keeps every log line the host writes, with its values, so a test can read what an operator would.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Capture(categoryName, Lines);

        public void Dispose()
        {
        }

        private sealed class Capture(string category, ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? string.Join(" | ", pairs.Select(p => $"{p.Key}={p.Value}")) : string.Empty;
                lines.Enqueue($"{logLevel} {category}: {formatter(state, exception)} [{values}] {exception}");
            }
        }
    }
}