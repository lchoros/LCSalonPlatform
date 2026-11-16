using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Appointments;
using Salon.Core.Booking;
using Salon.Core.Identity;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Booking;
using Salon.Services.Settings;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Booking;

/// <summary>
/// "Tell me when a time comes up" on the public booking pages (ADR 0055): off until the owner switches it on; the
/// number is proven with a text message code before anybody reaches the desk's waitlist; a code sent for the waitlist
/// cannot make a booking and the other way round; a client who is already on the list keeps one entry; nothing of
/// another salon.
/// </summary>
[Collection(SharedDatabase.Name)]
public class PublicWaitlistTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string Phone = "07700 900123";
    private const string PhoneE164 = "+447700900123";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly LocalDate Today = SystemClock.Instance.GetCurrentInstant().InZone(London).Date;

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _staffId;
    private Guid _serviceId;

    private string FormPath => $"/book/{_serviceId}/waitlist";

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        factory.Sms.Clear();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        (_staffId, _serviceId) = await VisitSeed.SalonAsync(factory, _studio.Id);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Nothing_is_offered_until_the_owner_switches_it_on()
    {
        using var visitor = factory.ClientFor(HostA);

        (await GetAsync(visitor, $"/book/{_serviceId}")).ShouldNotContain("data-testid=\"join-waitlist\"");
        (await visitor.GetAsync(new Uri(FormPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await visitor.GetAsync(new Uri($"/book/waitlist/done?service={_serviceId}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await SwitchAsync(_studio.Id, on: true);

        var picker = await GetAsync(visitor, $"/book/{_serviceId}?staff={_staffId}");
        picker.ShouldContain("No time that suits you?");
        picker.ShouldContain($"href=\"/book/{_serviceId}/waitlist?staff={_staffId}\" class=\"font-medium underline underline-offset-4\" data-testid=\"join-waitlist\"");
        var form = await GetAsync(visitor, $"{FormPath}?staff={_staffId}");
        form.ShouldContain("Tell me when a time comes up");
        form.ShouldContain("data-testid=\"summary-staff\">Anna<");
        form.ShouldContain("Ask the salon at any time to take you off the list.");
    }

    [Fact]
    public async Task A_visitor_proves_the_number_with_the_code_and_is_on_the_desks_list()
    {
        await SwitchAsync(_studio.Id, on: true);
        using var visitor = factory.ClientFor(HostA);
        var first = LocalDatePattern.Iso.Format(Today.PlusDays(2));
        var last = LocalDatePattern.Iso.Format(Today.PlusDays(9));

        var start = await PostFormAsync(visitor, Fields(first, last, "  Mornings only "), $"{FormPath}?staff={_staffId}", _staffId);

        start.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var verifyPath = start.Headers.Location!.OriginalString;
        verifyPath.ShouldStartWith("/book/waitlist/verify/");
        (await CountAsync(_studio.Id)).ShouldBe((0, 0), "nothing reaches the salon before the number is proven");
        var code = factory.Sms.LastCodeFor(PhoneE164).ShouldNotBeNull();
        var verifyPage = await GetAsync(visitor, verifyPath);
        verifyPage.ShouldContain("data-testid=\"code-sent-to\"");
        verifyPage.ShouldNotContain("7700900123", Case.Sensitive, "the number is masked");

        var done = await PostCodeAsync(visitor, verifyPath, code, verifyPage);

        done.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        done.Headers.Location!.OriginalString.ShouldBe($"/book/waitlist/done?service={_serviceId}");
        (await GetAsync(visitor, done.Headers.Location.OriginalString)).ShouldContain("data-testid=\"waitlist-done\">You are on the list<");
        await InScopeAsync(_studio.Id, async db =>
        {
            var client = await db.Clients.AsNoTracking().SingleAsync();
            (client.FirstName, client.LastName, client.Phone, client.Email, client.MarketingConsent).ShouldBe(("Olivia", "Brown", PhoneE164, "olivia@example.test", false));
            var entry = await db.WaitlistEntries.AsNoTracking().SingleAsync();
            (entry.ClientId, entry.ServiceId, entry.StaffMemberId, entry.NotBefore, entry.NotAfter, entry.Note, entry.CreatedByUserId, entry.ClosedAt)
                .ShouldBe((client.Id, _serviceId, _staffId, Today.PlusDays(2), Today.PlusDays(9), "Mornings only", null, null));
        });

        // The same code sent again (a second tap) is still "on the list", with one entry.
        (await PostCodeAsync(visitor, verifyPath, code, verifyPage)).Headers.Location!.OriginalString.ShouldStartWith("/book/waitlist/done");
        (await CountAsync(_studio.Id)).ShouldBe((1, 1));

        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var list = await GetAsync(owner, "/admin/appointments/waitlist");
        list.ShouldContain("Olivia Brown");
        list.ShouldContain("data-testid=\"asked-online\">Asked online<");
    }

    [Fact]
    public async Task A_known_client_gets_no_second_card_and_asking_again_keeps_one_entry()
    {
        await SwitchAsync(_studio.Id, on: true);
        var known = await VisitSeed.ClientAsync(factory, _studio.Id, "Liv", "Brown", PhoneE164);
        using var visitor = factory.ClientFor(HostA);

        await JoinAsync(visitor, Fields(note: "Any day"));
        await JoinAsync(visitor, Fields(note: "After five"));

        (await CountAsync(_studio.Id)).ShouldBe((1, 1));
        await InScopeAsync(_studio.Id, async db =>
        {
            var entry = await db.WaitlistEntries.AsNoTracking().SingleAsync();
            (entry.ClientId, entry.Note).ShouldBe((known, "After five"));
            (await db.Clients.AsNoTracking().SingleAsync()).FirstName.ShouldBe("Liv", "the card the salon has is not rewritten from a public form");
        });
    }

    [Fact]
    public async Task A_code_sent_for_the_waitlist_cannot_make_a_booking_and_a_booking_code_cannot_join_the_list()
    {
        await SwitchAsync(_studio.Id, on: true);
        using var visitor = factory.ClientFor(HostA);
        var verifyPath = (await PostFormAsync(visitor, Fields(), FormPath)).Headers.Location!.OriginalString;
        var waitlistId = Guid.Parse(verifyPath[(verifyPath.LastIndexOf('/') + 1)..]);
        var code = factory.Sms.LastCodeFor(PhoneE164).ShouldNotBeNull();

        (await visitor.GetAsync(new Uri($"/book/verify/{waitlistId}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var asBooking = await ScopedAsync<IPublicBookingService, PublicBookingVerification>(_studio.Id, s => s.VerifyAndBookAsync(waitlistId, code));
        asBooking.Outcome.ShouldBe(PublicBookingVerificationOutcome.NotFound);

        var bookingRow = Guid.Empty;
        await InScopeAsync(_studio.Id, async db =>
        {
            (await db.Appointments.AsNoTracking().AnyAsync()).ShouldBeFalse();
            var booking = new PhoneVerification
            {
                PhoneE164 = "+447700900456",
                CodeSalt = PhoneVerificationRules.GenerateSalt(),
                CodeHash = "x",
                ServiceId = _serviceId,
                Start = SystemClock.Instance.GetCurrentInstant() + Duration.FromDays(1),
                FirstName = "Noah",
                LastName = "Adams",
                CreatedAt = SystemClock.Instance.GetCurrentInstant(),
                ExpiresAt = SystemClock.Instance.GetCurrentInstant() + Duration.FromMinutes(10),
            };
            booking.CodeHash = PhoneVerificationRules.Hash(booking.CodeSalt, "123456");
            db.PhoneVerifications.Add(booking);
            await db.SaveChangesAsync();
            bookingRow = booking.Id;
        });

        (await visitor.GetAsync(new Uri($"/book/waitlist/verify/{bookingRow}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var asWaitlist = await ScopedAsync<IPublicWaitlistService, PublicWaitlistVerification>(_studio.Id, s => s.VerifyAndJoinAsync(bookingRow, "123456"));
        asWaitlist.Outcome.ShouldBe(PublicWaitlistOutcome.NotFound);
        (await CountAsync(_studio.Id)).ShouldBe((0, 0));
    }

    [Fact]
    public async Task A_wrong_code_counts_and_too_many_end_the_request()
    {
        await SwitchAsync(_studio.Id, on: true);
        using var visitor = factory.ClientFor(HostA);
        var verifyPath = (await PostFormAsync(visitor, Fields(), FormPath)).Headers.Location!.OriginalString;
        var page = await GetAsync(visitor, verifyPath);

        var wrong = await PostCodeAsync(visitor, verifyPath, "000000", page);
        wrong.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await wrong.Content.ReadAsStringAsync()).ShouldContain("data-testid=\"code-error\"");

        HttpResponseMessage last = wrong;
        for (var i = 0; i < PhoneVerificationRules.MaxAttempts; i++)
        {
            last = await PostCodeAsync(visitor, verifyPath, "000000", page);
        }

        var dead = WebUtility.HtmlDecode(await last.Content.ReadAsStringAsync());
        dead.ShouldContain("data-testid=\"verify-message\"");
        dead.ShouldContain($"href=\"{FormPath}\" data-testid=\"start-again\"");
        var code = factory.Sms.LastCodeFor(PhoneE164).ShouldNotBeNull();
        (await PostCodeAsync(visitor, verifyPath, code, page)).StatusCode.ShouldBe(HttpStatusCode.OK, "the right code no longer helps");
        (await CountAsync(_studio.Id)).ShouldBe((0, 0));
    }

    [Fact]
    public async Task What_is_typed_wrong_is_said_next_to_its_field_and_no_code_is_sent()
    {
        await SwitchAsync(_studio.Id, on: true);
        using var visitor = factory.ClientFor(HostA);
        var yesterday = LocalDatePattern.Iso.Format(Today.PlusDays(-1));

        var missing = await HtmlAsync(await PostFormAsync(visitor, new() { ["FirstName"] = " ", ["LastName"] = "Brown", ["Phone"] = "12" }, FormPath));
        missing.ShouldContain("Enter your first name.");
        missing.ShouldContain("Enter a valid mobile number");

        var days = await HtmlAsync(await PostFormAsync(visitor, Fields(LocalDatePattern.Iso.Format(Today.PlusDays(5)), LocalDatePattern.Iso.Format(Today.PlusDays(3))), FormPath));
        days.ShouldContain("The last day cannot be before the first.");
        (await HtmlAsync(await PostFormAsync(visitor, Fields(null, yesterday), FormPath))).ShouldContain("That day has already passed.");
        (await HtmlAsync(await PostFormAsync(visitor, Fields(note: new string('x', WaitlistEntry.NoteMaxLength + 1)), FormPath))).ShouldContain($"Use at most {WaitlistEntry.NoteMaxLength} characters.");
        (await HtmlAsync(await PostFormAsync(visitor, Fields("soon"), FormPath))).ShouldContain("Choose a day from the calendar, or leave it empty.");

        factory.Sms.Messages.ShouldBeEmpty();
        await InScopeAsync(_studio.Id, async db => (await db.PhoneVerifications.AsNoTracking().AnyAsync()).ShouldBeFalse());

        var noToken = await visitor.PostAsync(new Uri(FormPath, UriKind.Relative), new FormUrlEncodedContent(Fields()));
        noToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task One_number_gets_only_so_many_codes_in_an_hour()
    {
        await SwitchAsync(_studio.Id, on: true);
        using var visitor = factory.ClientFor(HostA);
        for (var i = 0; i < PhoneVerificationRules.MaxCodesPerPhone; i++)
        {
            (await PostFormAsync(visitor, Fields(), FormPath)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        var refused = await PostFormAsync(visitor, Fields(), FormPath);

        refused.StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Sms.Messages.Count.ShouldBe(PhoneVerificationRules.MaxCodesPerPhone);
    }

    [Fact]
    public async Task With_the_code_switched_off_the_visitor_is_on_the_list_at_once()
    {
        await SettingsAsync(_studio.Id, s =>
        {
            s.PublicWaitlist = true;
            s.RequireSmsVerification = false;
        });
        using var visitor = factory.ClientFor(HostA);

        var response = await PostFormAsync(visitor, Fields(), FormPath);

        response.Headers.Location!.OriginalString.ShouldBe($"/book/waitlist/done?service={_serviceId}");
        factory.Sms.Messages.ShouldBeEmpty();
        (await CountAsync(_studio.Id)).ShouldBe((1, 1));
    }

    [Fact]
    public async Task Another_salon_does_not_see_the_request_and_reads_its_own_form_in_its_own_language()
    {
        await SwitchAsync(_studio.Id, on: true);
        await SwitchAsync(_polish.Id, on: true);
        var (_, polishService) = await VisitSeed.SalonAsync(factory, _polish.Id);
        using var visitor = factory.ClientFor(HostA);
        using var elsewhere = factory.ClientFor(HostB);
        var verifyPath = (await PostFormAsync(visitor, Fields(), FormPath)).Headers.Location!.OriginalString;

        (await elsewhere.GetAsync(new Uri(verifyPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await elsewhere.GetAsync(new Uri(FormPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound, "this salon's service is not theirs");
        (await GetAsync(elsewhere, $"/book/{polishService}/waitlist")).ShouldContain("Dajcie znać, gdy zwolni się termin");
        (await CountAsync(_polish.Id)).ShouldBe((0, 0));
    }

    [Fact]
    public async Task The_owner_switches_it_on_in_settings_and_the_other_booking_rules_stay()
    {
        await SettingsAsync(_studio.Id, s => s.MinNoticeMinutes = 120);
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        using var manager = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Manager);
        const string path = "/admin/settings/waitlist";

        var page = await GetAsync(owner, path);
        page.ShouldContain("Waitlist on the booking pages");
        page.ShouldNotContain("checked=\"checked\"", Case.Sensitive, "off until the owner switches it on");
        page.ShouldContain("href=\"/admin/settings/waitlist\" data-testid=\"settings-nav-waitlist\" aria-current=\"page\"");

        var saved = await owner.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent(
        [
            new("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(page)),
            new("Enabled", "true"),
        ]));

        saved.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var after = await GetAsync(owner, path);
        after.ShouldContain("The booking pages now offer the waitlist.");
        after.ShouldContain("checked=\"checked\"");
        var rules = await LoadAsync(_studio.Id);
        (rules.PublicWaitlist, rules.MinNoticeMinutes).ShouldBe((true, 120));
        (await LoadAsync(_polish.Id)).PublicWaitlist.ShouldBeFalse();

        var denied = await manager.GetAsync(new Uri(path, UriKind.Relative));
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        var off = await owner.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent([new("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(after))]));
        off.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await LoadAsync(_studio.Id)).PublicWaitlist.ShouldBeFalse();
    }

    private static Dictionary<string, string> Fields(string? notBefore = null, string? notAfter = null, string? note = null) => new()
    {
        ["FirstName"] = "Olivia",
        ["LastName"] = "Brown",
        ["Phone"] = Phone,
        ["Email"] = "olivia@example.test",
        ["NotBefore"] = notBefore ?? string.Empty,
        ["NotAfter"] = notAfter ?? string.Empty,
        ["Note"] = note ?? string.Empty,
    };

    private async Task JoinAsync(HttpClient visitor, Dictionary<string, string> fields)
    {
        var verifyPath = (await PostFormAsync(visitor, fields, FormPath)).Headers.Location!.OriginalString;
        var code = factory.Sms.LastCodeFor(PhoneE164).ShouldNotBeNull();
        (await PostCodeAsync(visitor, verifyPath, code, await GetAsync(visitor, verifyPath))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient visitor, Dictionary<string, string> fields, string formPath, Guid? staff = null)
    {
        var posted = new Dictionary<string, string>(fields)
        {
            ["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(await GetAsync(visitor, formPath)),
        };
        if (staff is { } staffId)
        {
            posted["StaffMemberId"] = staffId.ToString();
        }

        return await visitor.PostAsync(new Uri(formPath.Split('?')[0], UriKind.Relative), new FormUrlEncodedContent(posted));
    }

    private static Task<HttpResponseMessage> PostCodeAsync(HttpClient visitor, string verifyPath, string code, string pageWithToken)
        => visitor.PostAsync(new Uri(verifyPath, UriKind.Relative), new FormUrlEncodedContent(
        [
            new("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(pageWithToken)),
            new("Code", code),
        ]));

    private static async Task<string> HtmlAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private Task SwitchAsync(Guid tenantId, bool on) => SettingsAsync(tenantId, s => s.PublicWaitlist = on);

    private async Task SettingsAsync(Guid tenantId, Action<BookingSettings> change)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var settings = scope.ServiceProvider.GetRequiredService<ISettingService>();
        var current = await settings.LoadAsync<BookingSettings>();
        change(current);
        await settings.SaveAsync(current);
    }

    private async Task<BookingSettings> LoadAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<ISettingService>().LoadAsync<BookingSettings>();
    }

    /// <summary>How many client cards and waiting entries the salon has.</summary>
    private async Task<(int Clients, int Waiting)> CountAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        return (await db.Clients.CountAsync(), await db.WaitlistEntries.CountAsync(e => e.ClosedAt == null));
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
}