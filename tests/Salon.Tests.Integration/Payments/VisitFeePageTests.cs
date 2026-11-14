using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Settings;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Payments;

/// <summary>
/// The fee page of a visit (ADR 0022) as the desk uses it: marking a no-show leads to the preview, a person adds the fee,
/// records the payment or waives it, and the client card and the next visit's card show what is owed.
/// </summary>
[Collection(SharedDatabase.Name)]
public class VisitFeePageTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";

    private static readonly Instant Now = SystemClock.Instance.GetCurrentInstant();

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _staffId;
    private Guid _serviceId;
    private Guid _clientId;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        (_staffId, _serviceId) = await VisitSeed.SalonAsync(factory, _studio.Id);
        _clientId = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123");
        await SavePolicyAsync(_studio.Id, hours: 24, latePercent: 50, noShowPercent: 100);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Marking_a_no_show_leads_to_the_fee_preview_and_adding_the_fee_shows_it_as_owed()
    {
        var visit = await VisitAsync(Now - Duration.FromMinutes(40), AppointmentStatus.Booked);
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        var marked = await PostAsync(client, $"/admin/appointments/{visit}/no-show", new(), await GetAsync(client, CardPath(visit)));

        marked.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        marked.Headers.Location!.OriginalString.ShouldBe(FeePath(visit));
        var preview = await GetAsync(client, FeePath(visit));
        preview.ShouldContain("Olivia Brown");
        preview.ShouldContain("data-testid=\"fee-proposal\" data-kind=\"NoShowFee\"");
        preview.ShouldContain("The client did not turn up. Your policy sets a no-show fee of 100% of the price: £20.00.");
        preview.ShouldContain("name=\"amount\" type=\"text\" inputmode=\"decimal\" value=\"20.00\"");
        preview.ShouldContain("Nothing is charged to a card.");
        (await EntriesAsync(_studio.Id)).ShouldBeEmpty("a preview is not a fee");

        var charged = await PostAsync(client, FeePath(visit), new() { ["amount"] = "12.50" }, preview);

        charged.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        charged.Headers.Location!.OriginalString.ShouldBe(FeePath(visit));
        var owed = await GetAsync(client, FeePath(visit));
        owed.ShouldContain("The fee has been added to the client's account.");
        owed.ShouldContain("data-testid=\"fee\" data-status=\"Owed\" data-kind=\"NoShowFee\"");
        owed.ShouldContain("£12.50");
        owed.ShouldContain("Fee added by receptionist@studio-20.localhost");
        owed.ShouldContain("data-testid=\"pay-form\"");
        owed.ShouldContain("data-testid=\"waive-form\"");
        var row = (await EntriesAsync(_studio.Id)).ShouldHaveSingleItem();
        row.Kind.ShouldBe(ClientAccountEntryKind.NoShowFee);
        row.Amount.ShouldBe(12.50m);
        row.ClientId.ShouldBe(_clientId);
        row.ByUserId.ShouldNotBeNull();
    }

    [Fact]
    public async Task Recording_the_payment_settles_the_fee_and_keeps_who_took_it_and_the_reference()
    {
        var visit = await VisitAsync(Now - Duration.FromHours(2), AppointmentStatus.NoShow);
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Manager);
        await PostAsync(client, FeePath(visit), new() { ["amount"] = "20.00" }, await GetAsync(client, FeePath(visit)));

        var paid = await PostAsync(client, FeePath(visit) + "/paid", new() { ["method"] = "CardAtSalonTerminal" }, await GetAsync(client, FeePath(visit)));

        paid.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var page = await GetAsync(client, FeePath(visit));
        page.ShouldContain("The payment has been recorded.");
        page.ShouldContain("data-testid=\"fee\" data-status=\"Paid\"");
        page.ShouldContain("Paid: Card at the salon's terminal. Recorded by manager@studio-20.localhost");
        page.ShouldContain("Reference: manual:no-show-fee:");
        page.ShouldNotContain("data-testid=\"pay-form\"");
        page.ShouldNotContain("data-testid=\"waive-form\"");

        // A second click on a stale page changes nothing and says why.
        var again = await PostAsync(client, FeePath(visit) + "/paid", new() { ["method"] = "Cash" }, page);
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await again.Content.ReadAsStringAsync()).ShouldContain("This fee has already been paid or waived.");
        (await EntriesAsync(_studio.Id)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Waiving_needs_a_reason_and_shows_it_in_the_history()
    {
        var visit = await VisitAsync(Now - Duration.FromHours(2), AppointmentStatus.NoShow);
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        await PostAsync(client, FeePath(visit), new(), await GetAsync(client, FeePath(visit)));
        var owed = await GetAsync(client, FeePath(visit));

        var blank = await PostAsync(client, FeePath(visit) + "/waive", new() { ["reason"] = "  " }, owed);

        blank.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await blank.Content.ReadAsStringAsync()).ShouldContain("Say why the fee is waived.");
        (await EntriesAsync(_studio.Id)).ShouldHaveSingleItem();

        var waived = await PostAsync(client, FeePath(visit) + "/waive", new() { ["reason"] = "Her train was cancelled" }, owed);

        waived.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var page = await GetAsync(client, FeePath(visit));
        page.ShouldContain("The fee has been waived.");
        page.ShouldContain("data-testid=\"fee\" data-status=\"Waived\"");
        page.ShouldContain("Waived by owner@studio-20.localhost");
        page.ShouldContain("Her train was cancelled");
    }

    [Theory]
    [InlineData("0", "Enter an amount above zero with at most two decimals.")]
    [InlineData("abc", "Enter an amount above zero with at most two decimals.")]
    [InlineData("20.01", "The amount must be above zero and no more than the limit shown under the field.")]
    public async Task A_wrong_amount_shows_the_preview_again_with_the_reason_and_adds_nothing(string amount, string message)
    {
        var visit = await VisitAsync(Now - Duration.FromHours(2), AppointmentStatus.NoShow);
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        var response = await PostAsync(client, FeePath(visit), new() { ["amount"] = amount }, await GetAsync(client, FeePath(visit)));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        html.ShouldContain(message);
        html.ShouldContain("data-testid=\"charge-form\"");
        (await EntriesAsync(_studio.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_policy_a_no_show_goes_back_to_the_day_and_the_page_offers_no_fee()
    {
        await SavePolicyAsync(_studio.Id, hours: 0, latePercent: 0, noShowPercent: 0);
        var visit = await VisitAsync(Now - Duration.FromMinutes(40), AppointmentStatus.Booked);
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var marked = await PostAsync(client, $"/admin/appointments/{visit}/no-show", new(), await GetAsync(client, CardPath(visit)));

        marked.Headers.Location!.OriginalString.ShouldStartWith("/admin/appointments?date=");
        var page = await GetAsync(client, FeePath(visit));
        page.ShouldContain("Your cancellation policy sets no fee for this visit.");
        page.ShouldContain("href=\"/admin/settings/cancellation-policy\"");
        page.ShouldNotContain("data-testid=\"charge-form\"");
        var refused = await PostAsync(client, FeePath(visit), new() { ["amount"] = "5" }, page);
        refused.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await EntriesAsync(_studio.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_late_cancellation_at_the_desk_leads_to_its_fee_and_one_in_time_does_not()
    {
        var soon = await VisitAsync(Now + Duration.FromHours(3), AppointmentStatus.Booked);
        var far = await VisitAsync(Now + Duration.FromDays(5), AppointmentStatus.Booked);
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        var board = await GetAsync(client, CardPath(soon));

        var late = await PostAsync(client, $"/admin/appointments/{soon}/cancel", new(), board);
        var inTime = await PostAsync(client, $"/admin/appointments/{far}/cancel", new(), board);

        late.Headers.Location!.OriginalString.ShouldBe(FeePath(soon));
        inTime.Headers.Location!.OriginalString.ShouldStartWith("/admin/appointments?date=");
        var preview = await GetAsync(client, FeePath(soon));
        preview.ShouldContain("data-testid=\"fee-proposal\" data-kind=\"LateCancellationFee\"");
        preview.ShouldContain("Your policy sets a late cancellation fee of 50% of the price: £10.00.");
    }

    [Fact]
    public async Task The_client_card_and_the_next_visit_show_what_is_owed()
    {
        var missed = await VisitAsync(Now - Duration.FromDays(3), AppointmentStatus.NoShow);
        var next = await VisitAsync(Now + Duration.FromDays(2), AppointmentStatus.Booked);
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        var before = await GetAsync(client, $"/admin/clients/{_clientId}");
        before.ShouldNotContain("data-testid=\"client-fees\"");
        before.ShouldContain($"href=\"/admin/payments/fees/{missed}\" data-testid=\"visit-fee-link\"");
        (await GetAsync(client, $"/admin/appointments/calendar/{next}")).ShouldNotContain("data-testid=\"card-client-owes\"");

        await PostAsync(client, FeePath(missed), new(), await GetAsync(client, FeePath(missed)));

        var card = await GetAsync(client, $"/admin/clients/{_clientId}");
        card.ShouldContain("data-testid=\"client-fees\"");
        card.ShouldContain("Owes £20.00");
        card.ShouldContain($"href=\"/admin/payments/fees/{missed}\" data-testid=\"fee-link\"");
        card.ShouldContain("No-show fee");
        var visitCard = await GetAsync(client, $"/admin/appointments/calendar/{next}");
        visitCard.ShouldContain("This client owes £20.00 in fees from earlier visits.");
        visitCard.ShouldContain($"href=\"/admin/clients/{_clientId}\"");

        await PostAsync(client, FeePath(missed) + "/paid", new() { ["method"] = "Cash" }, await GetAsync(client, FeePath(missed)));

        var settled = await GetAsync(client, $"/admin/clients/{_clientId}");
        settled.ShouldContain("Nothing owed");
        settled.ShouldContain("data-testid=\"fee-row\" data-status=\"Paid\"");
        (await GetAsync(client, $"/admin/appointments/calendar/{next}")).ShouldNotContain("data-testid=\"card-client-owes\"");
    }

    [Fact]
    public async Task The_page_speaks_the_salon_language_and_its_money()
    {
        var (staff, service) = await VisitSeed.SalonAsync(factory, _polish.Id);
        var jan = await VisitSeed.ClientAsync(factory, _polish.Id, "Jan", "Kowalski");
        var visit = await VisitSeed.AppointmentAsync(factory, _polish.Id, jan, staff, service, Now - Duration.FromHours(2), AppointmentStatus.NoShow);
        await SavePolicyAsync(_polish.Id, hours: 24, latePercent: 50, noShowPercent: 50);
        using var client = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);

        var page = await GetAsync(client, FeePath(visit));

        page.ShouldContain("Opłata za nieobecność");
        page.ShouldContain("Klient nie przyszedł.");
        page.ShouldContain("value=\"10,00\"");
        var charged = await PostAsync(client, FeePath(visit), new() { ["amount"] = "7,50" }, page);
        charged.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await EntriesAsync(_polish.Id)).ShouldHaveSingleItem().Amount.ShouldBe(7.50m);
        (await GetAsync(client, FeePath(visit))).ShouldContain("Do zapłaty");
    }

    [Fact]
    public async Task Staff_without_the_permission_another_salon_and_a_post_without_a_token_get_nowhere()
    {
        var visit = await VisitAsync(Now - Duration.FromHours(2), AppointmentStatus.NoShow);
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var preview = await GetAsync(owner, FeePath(visit));

        // The Staff role neither sees clients nor records payments (ADR 0009): sent to the access-denied page.
        using var staff = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Staff);
        var get = await staff.GetAsync(new Uri(FeePath(visit), UriKind.Relative));
        get.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        get.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        var post = await PostAsync(staff, FeePath(visit), new(), preview);
        post.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");

        // Another salon's visit does not exist here, for reads and writes alike.
        using var other = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        (await other.GetAsync(new Uri(FeePath(visit), UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var otherPage = await GetAsync(other, "/admin/settings/cancellation-policy");
        (await PostAsync(other, FeePath(visit), new(), otherPage)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(other, FeePath(visit) + "/paid", new() { ["method"] = "Cash" }, otherPage)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // No anti-forgery token, no fee.
        var bare = await owner.PostAsync(new Uri(FeePath(visit), UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string> { ["amount"] = "5" }));
        bare.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Not signed in: to the login page.
        using var anonymous = factory.ClientFor(HostA);
        (await anonymous.GetAsync(new Uri(FeePath(visit), UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await EntriesAsync(_studio.Id)).ShouldBeEmpty();
    }

    private static string FeePath(Guid appointmentId) => $"/admin/payments/fees/{appointmentId}";

    /// <summary>The visit's card in the calendar: a page that always carries an anti-forgery token for an active visit.</summary>
    private static string CardPath(Guid appointmentId) => $"/admin/appointments/calendar/{appointmentId}";

    private Task<Guid> VisitAsync(Instant start, AppointmentStatus status)
        => VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, start, status);

    private async Task SavePolicyAsync(Guid tenantId, int hours, int latePercent, int noShowPercent)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        await scope.ServiceProvider.GetRequiredService<ISettingService>().SaveAsync(new CancellationPolicySettings
        {
            FreeChangeHours = hours,
            LateCancellationFeePercent = latePercent,
            NoShowFeePercent = noShowPercent,
        });
    }

    private async Task<List<ClientAccountEntry>> EntriesAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().ClientAccountEntries.AsNoTracking().OrderBy(e => e.At).ToListAsync();
    }

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
}