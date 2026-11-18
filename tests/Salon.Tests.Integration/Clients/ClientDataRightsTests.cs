using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Npgsql;
using Salon.Core.Appointments;
using Salon.Core.Booking;
using Salon.Core.Clients;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Clients;
using Salon.Services.Payments;
using Salon.Services.Settings;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Clients;

/// <summary>
/// A client's rights over their data (ADR 0062). A copy has everything the salon holds: details, health notes,
/// visits, money, consultation answers, what they waited for. Erasing takes every trace of who the person was out of
/// every table and leaves the visits and the money, so the till and the reports are the same before and after. It
/// waits until an upcoming visit, a deposit held and a fee owed are settled. Only the owner erases; another salon has
/// no such client. Zenobia Quillfeather has a name nobody else in the database shares, so a search of every text
/// column can prove she is gone.
/// </summary>
[Collection(SharedDatabase.Name)]
public class ClientDataRightsTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string Phone = "+447700900777";
    private const string Email = "zenobia.q@example.test";
    private const string Notes = "prefers oat milk zq40";
    private const string Health = "allergic to PPD zq41";
    private const string VisitNote = "running late zq42";
    private const string WaitNote = "mornings only zq43";
    private const string Said = "itchy scalp zq44";
    private const string Reason = "moved house zq45";
    private const string CancelLink = "link-zq46";

    /// <summary>Everything that would say who she was or anything about her, as it could sit in a text column.</summary>
    private static readonly string[] Needles = ["Zenobia", "Quillfeather", "Quilfeather", "7700900777", "zenobia.q", "zq40", "zq41", "zq42", "zq43", "zq44", "zq45", "zq46"];

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly Instant Now = SystemClock.Instance.GetCurrentInstant();
    private static readonly LocalDate Today = Now.InZone(London).Date;

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _staffId;
    private Guid _serviceId;
    private Guid _clientId;
    private int _visits;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        (_staffId, _serviceId) = await VisitSeed.SalonAsync(factory, _studio.Id);
        _clientId = await ScopedAsync<IClientService, Guid>(s => s.CreateAsync(new CreateClientRequest("Zenobia", "Quillfeather", Phone, Email, Notes, Health, Today.PlusDays(-10), true, "I agree to offers by text.")));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_copy_has_everything_the_salon_holds_about_the_client()
    {
        var user = await factory.CreateUserAsync(_studio.Id, "manager@studio-20.test", Role.Manager);
        await FillHistoryAsync();

        var copy = (await DataAsync(s => s.GetCopyAsync(_clientId, user.Id))).ShouldNotBeNull();

        (copy.Card.FullName, copy.Card.Phone, copy.Card.Email, copy.Card.Notes, copy.Card.HealthNotes, copy.Card.MarketingConsent, copy.Card.MarketingConsentWording)
            .ShouldBe(("Zenobia Quillfeather", Phone, Email, Notes, Health, true, "I agree to offers by text."));
        copy.MadeAt.Date.ShouldBe(Today);
        copy.Visits.Select(v => (v.Status, v.Notes, v.CancellationReason, v.Price.Amount)).ShouldBe(
        [
            (AppointmentStatus.CancelledByClient, null, Reason, 20m),
            (AppointmentStatus.Completed, VisitNote, null, 20m),
        ], ignoreOrder: true);
        copy.Money.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            line => line.Kind.ShouldBe(ClientAccountEntryKind.VisitPayment),
            line => line.Amount.Amount.ShouldBe(20m),
            line => line.Method.ShouldBe(PaymentMethod.Cash));
        var form = copy.Consultations.ShouldHaveSingleItem();
        form.Answers.ShouldNotBeNull().Details.ShouldBe(Said);
        form.Answers.Concerns.ShouldBe([ConsultationQuestions.Allergies]);
        copy.Waitlist.ShouldHaveSingleItem().Note.ShouldBe(WaitNote);
        copy.MergedCards.ShouldBe(1);
        copy.ToString().ShouldNotContain("Zenobia");

        // Another salon has no such client; nobody's copy is somebody else's.
        (await DataAsync(s => s.GetCopyAsync(_clientId, null), _polish.Id)).ShouldBeNull();
        (await DataAsync(s => s.GetCopyAsync(Guid.NewGuid(), null))).ShouldBeNull();
    }

    [Fact]
    public async Task Erasing_takes_the_person_out_of_every_table_and_leaves_the_visits_and_the_money()
    {
        var owner = await factory.CreateUserAsync(_studio.Id, "owner@studio-20.test", Role.Owner);
        await FillHistoryAsync();
        var tillBefore = await ScopedAsync<ICashUpService, CashDay>(s => s.GetDayAsync(null));
        (await FoundAsync()).ShouldNotBeEmpty("the search of every text column finds her while she is a client");
        var preview = (await DataAsync(s => s.PreviewErasureAsync(_clientId))).ShouldNotBeNull();
        (preview.CanErase, preview.ClientName, preview.VisitsKept, preview.MoneyLinesKept, preview.ConsultationsRemoved, preview.WaitlistEntriesRemoved)
            .ShouldBe((true, "Zenobia Quillfeather", 2, 1, 1, 1));

        var result = await DataAsync(s => s.EraseAsync(_clientId, owner.Id));

        result.Outcome.ShouldBe(ErasureOutcome.Erased);
        (await FoundAsync()).ShouldBeEmpty("nothing in any text column of the database says who she was");
        await InScopeAsync(async db =>
        {
            var row = await db.Clients.AsNoTracking().SingleAsync(c => c.Id == _clientId);
            (row.FirstName, row.LastName, row.Phone, row.Email, row.Notes, row.HealthNotesProtected, row.LastPatchTestOn).ShouldBe(("Erased", "client", null, null, null, null, null));
            (row.MarketingConsent, row.MarketingConsentAt, row.MarketingConsentWording, row.IsActive, row.ErasedByUserId).ShouldBe((false, null, null, false, owner.Id));
            row.ErasedAt.ShouldNotBeNull();
            var duplicate = await db.Clients.AsNoTracking().SingleAsync(c => c.MergedIntoClientId == _clientId);
            (duplicate.FirstName, duplicate.LastName, duplicate.ErasedAt.HasValue).ShouldBe(("Erased", "client", true));
            (await db.ConsultationRecords.CountAsync()).ShouldBe(0);
            (await db.WaitlistEntries.CountAsync()).ShouldBe(0);
            (await db.PhoneVerifications.CountAsync()).ShouldBe(0);

            // The visits and the money are the salon's books: they stay, pointing at the row without a name.
            var visits = await db.Appointments.AsNoTracking().Where(a => a.ClientId == _clientId).ToListAsync();
            visits.Count.ShouldBe(2);
            visits.ShouldAllBe(v => v.Notes == null && v.CancellationReason == null && v.PublicToken == null);
            var line = await db.ClientAccountEntries.AsNoTracking().SingleAsync();
            (line.ClientId, line.Kind, line.Amount, line.Method).ShouldBe((_clientId, ClientAccountEntryKind.VisitPayment, 20m, PaymentMethod.Cash));
        });

        var tillAfter = await ScopedAsync<ICashUpService, CashDay>(s => s.GetDayAsync(null));
        (tillAfter.Takings.Amount, tillAfter.ExpectedCash.Amount).ShouldBe((tillBefore.Takings.Amount, tillBefore.ExpectedCash.Amount));
        tillAfter.Movements.ShouldHaveSingleItem().ClientName.ShouldBe("Erased client");

        // She is not found, cannot be edited, merged or given a form, and can become a client again.
        (await ScopedAsync<IClientService, IReadOnlyList<ClientSummary>>(s => s.SearchAsync("Erased"))).ShouldBeEmpty();
        (await ScopedAsync<IClientService, IReadOnlyList<ClientSummary>>(s => s.SearchAsync(null))).ShouldBeEmpty();
        (await ScopedAsync<IClientService, ClientSummary?>(s => s.FindByPhoneAsync(Phone))).ShouldBeNull();
        await Should.ThrowAsync<InvalidOperationException>(() => ScopedAsync<IClientService, bool>(async s =>
        {
            await s.UpdateDetailsAsync(_clientId, "Zenobia", "Quillfeather", Phone, null, null);
            return true;
        }));
        var twice = await DataAsync(s => s.EraseAsync(_clientId, owner.Id));
        twice.Outcome.ShouldBe(ErasureOutcome.Blocked, "erasing twice changes nothing");
        twice.Blockers.ShouldBe([ErasureBlocker.AlreadyErased]);
        var again = await ScopedAsync<IClientService, Guid>(s => s.CreateAsync(new CreateClientRequest("Zenobia", "Quillfeather", Phone, Email)));
        again.ShouldNotBe(_clientId);
    }

    [Fact]
    public async Task An_upcoming_visit_a_deposit_held_and_a_fee_owed_are_settled_first()
    {
        await SavePolicyAsync(noShowPercent: 50m);
        var upcoming = await VisitAsync(AppointmentStatus.Booked, Duration.FromDays(3));
        await AccountAsync(s => s.RecordDepositAsync(upcoming, 5m, PaymentMethod.Cash, null));
        var missed = await VisitAsync(AppointmentStatus.NoShow);
        await AccountAsync(s => s.ChargeFeeAsync(missed, null, null));

        var blocked = await DataAsync(s => s.EraseAsync(_clientId, null));
        blocked.Outcome.ShouldBe(ErasureOutcome.Blocked);
        blocked.Blockers.ShouldBe([ErasureBlocker.UpcomingVisit, ErasureBlocker.DepositHeld, ErasureBlocker.FeeOwed]);
        (await DataAsync(s => s.PreviewErasureAsync(_clientId))).ShouldNotBeNull().CanErase.ShouldBeFalse();
        (await FoundAsync()).ShouldNotBeEmpty("a refused erasure changes nothing");

        // One thing at a time, as the desk would.
        await SetStatusAsync(upcoming, AppointmentStatus.CancelledBySalon);
        (await DataAsync(s => s.EraseAsync(_clientId, null))).Blockers.ShouldBe([ErasureBlocker.DepositHeld, ErasureBlocker.FeeOwed]);
        await AccountAsync(s => s.ReturnDepositAsync(upcoming, PaymentMethod.Cash, null));
        (await DataAsync(s => s.EraseAsync(_clientId, null))).Blockers.ShouldBe([ErasureBlocker.FeeOwed]);
        await AccountAsync(s => s.WaiveFeeAsync(missed, "The client asked to be forgotten.", null));

        // A visit long past that nobody gave an outcome does not stand in the way.
        await VisitAsync(AppointmentStatus.Booked, -Duration.FromDays(40));
        (await DataAsync(s => s.EraseAsync(_clientId, null))).Outcome.ShouldBe(ErasureOutcome.Erased);
        await InScopeAsync(async db => (await db.ClientAccountEntries.CountAsync()).ShouldBe(4, "the fee, its waiver, the deposit and its return all stay"));
    }

    [Fact]
    public async Task A_merged_card_is_erased_through_the_card_that_kept_it_and_another_salon_has_no_such_client()
    {
        var duplicate = await ScopedAsync<IClientService, Guid>(s => s.CreateAsync(new CreateClientRequest("Zenobia", "Quilfeather")));
        await ScopedAsync<IClientService, bool>(async s =>
        {
            await s.MergeAsync(_clientId, duplicate, null);
            return true;
        });

        var merged = await DataAsync(s => s.EraseAsync(duplicate, null));
        merged.Outcome.ShouldBe(ErasureOutcome.Blocked);
        merged.Blockers.ShouldBe([ErasureBlocker.MergedAway]);
        (await DataAsync(s => s.EraseAsync(_clientId, null), _polish.Id)).Outcome.ShouldBe(ErasureOutcome.NotFound);
        (await DataAsync(s => s.PreviewErasureAsync(_clientId), _polish.Id)).ShouldBeNull();
        (await DataAsync(s => s.EraseAsync(Guid.NewGuid(), null))).Outcome.ShouldBe(ErasureOutcome.NotFound);
        (await FoundAsync()).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task The_owner_makes_a_copy_and_erases_from_the_card_and_nobody_else_erases()
    {
        await FillHistoryAsync();
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var card = await GetAsync(owner, CardPath);
        card.ShouldContain($"href=\"{CardPath}/data\"");
        card.ShouldContain($"href=\"{CardPath}/erase\"");

        using var copied = await owner.GetAsync(new Uri(CardPath + "/data", UriKind.Relative));
        copied.StatusCode.ShouldBe(HttpStatusCode.OK);
        copied.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        var copy = WebUtility.HtmlDecode(await copied.Content.ReadAsStringAsync());
        foreach (var held in new[] { "Zenobia Quillfeather", Phone, Email, Notes, Health, VisitNote, Reason, Said, WaitNote, "I agree to offers by text." })
        {
            copy.ShouldContain(held);
        }

        var confirm = await GetAsync(owner, CardPath + "/erase");
        confirm.ShouldContain("Zenobia Quillfeather");
        confirm.ShouldContain("Consultation forms: 1.");
        confirm.ShouldContain("Visits: 2.");
        confirm.ShouldContain("Lines of money: 1.");

        // Without the tick nothing happens.
        var unticked = await PostAsync(owner, CardPath + "/erase", confirm);
        unticked.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await unticked.Content.ReadAsStringAsync()).ShouldContain("Tick the box to confirm that this cannot be undone.");
        (await FoundAsync()).ShouldNotBeEmpty();

        // A manager makes copies but does not erase; the desk and staff do neither.
        using var manager = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Manager);
        (await manager.GetAsync(new Uri(CardPath + "/data", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(manager, CardPath)).ShouldNotContain($"href=\"{CardPath}/erase\"");
        foreach (var path in new[] { CardPath + "/erase" })
        {
            var denied = await manager.GetAsync(new Uri(path, UriKind.Relative));
            denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
            (await PostAsync(manager, path, confirm, ("understood", "true"))).Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        }

        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        (await GetAsync(desk, CardPath)).ShouldNotContain("data-testid=\"data-rights\"");
        foreach (var path in new[] { CardPath + "/data", CardPath + "/erase" })
        {
            (await desk.GetAsync(new Uri(path, UriKind.Relative))).Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        }

        using var theirOwner = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        (await theirOwner.GetAsync(new Uri(CardPath + "/data", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await theirOwner.GetAsync(new Uri(CardPath + "/erase", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var noToken = await owner.PostAsync(new Uri(CardPath + "/erase", UriKind.Relative), new FormUrlEncodedContent([new("understood", "true")]));
        noToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await FoundAsync()).ShouldNotBeEmpty("nobody but the owner, with the tick and the token, erased anything");

        // The owner confirms.
        var erased = await PostAsync(owner, CardPath + "/erase", confirm, ("understood", "true"));
        erased.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        erased.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe(CardPath);
        (await FoundAsync()).ShouldBeEmpty();

        var after = await GetAsync(owner, CardPath);
        after.ShouldContain("The client was erased.");
        after.ShouldContain("data-testid=\"erased-banner\"");
        after.ShouldContain("Erased client");
        after.ShouldNotContain("Zenobia");
        after.ShouldNotContain($"href=\"{CardPath}/edit\"");
        after.ShouldNotContain("data-testid=\"data-rights\"");
        (await owner.GetAsync(new Uri(CardPath + "/edit", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync(owner, "/admin/clients?q=Quillfeather")).ShouldNotContain("data-testid=\"client-row\"");
        (await GetAsync(owner, "/admin/clients/export.csv")).ShouldNotContain("Erased");
        (await GetAsync(owner, CardPath + "/erase")).ShouldContain("This client was already erased.");
    }

    [Fact]
    public async Task The_erased_row_speaks_the_salon_language()
    {
        var theirs = await ScopedAsync<IClientService, Guid>(s => s.CreateAsync(new CreateClientRequest("Zofia", "Kowalska")), _polish.Id);

        (await DataAsync(s => s.EraseAsync(theirs, null), _polish.Id)).Outcome.ShouldBe(ErasureOutcome.Erased);

        await using var scope = factory.CreateTenantScope(_polish.Id);
        var row = await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Clients.AsNoTracking().SingleAsync(c => c.Id == theirs);
        row.FullName.ShouldBe("Usunięty klient");
    }

    private string CardPath => $"/admin/clients/{_clientId}";

    /// <summary>
    /// A history in every table that holds something about a client: a completed, paid visit with a note and a cancel
    /// link, a cancelled visit with a reason, a consultation form, a waitlist request, what she typed on the booking
    /// pages, and a duplicate card merged into hers.
    /// </summary>
    private async Task FillHistoryAsync()
    {
        var done = await VisitAsync(AppointmentStatus.Arrived);
        await AccountAsync(s => s.PayBillAsync(done, PaymentMethod.Cash, [], null));
        var cancelled = await VisitAsync(AppointmentStatus.CancelledByClient, -Duration.FromDays(5));
        await InScopeAsync(async db =>
        {
            await db.Appointments.Where(a => a.Id == done)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AppointmentStatus.Completed).SetProperty(a => a.Notes, VisitNote).SetProperty(a => a.PublicToken, CancelLink));
            await db.Appointments.Where(a => a.Id == cancelled).ExecuteUpdateAsync(s => s.SetProperty(a => a.CancellationReason, Reason));
            db.WaitlistEntries.Add(new WaitlistEntry { ClientId = _clientId, ServiceId = _serviceId, Note = WaitNote, CreatedAt = Now });
            var typed = new PhoneVerification
            {
                PhoneE164 = Phone,
                CodeSalt = PhoneVerificationRules.GenerateSalt(),
                CodeHash = "x",
                ServiceId = _serviceId,
                Start = Now + Duration.FromDays(1),
                FirstName = "Zenobia",
                LastName = "Quillfeather",
                Email = Email,
                CreatedAt = Now,
                ExpiresAt = Now + Duration.FromMinutes(10),
            };
            db.PhoneVerifications.Add(typed);
            await db.SaveChangesAsync();
        });

        var answers = ConsultationQuestions.All.ToDictionary(key => key, key => key == ConsultationQuestions.Allergies);
        (await ScopedAsync<IConsultationService, ConsultationResult>(s => s.RecordAsync(new ConsultationRequest(_clientId, "Colour", answers, Said, null, "Zenobia Quillfeather", true, "I confirm."), null)))
            .Outcome.ShouldBe(ConsultationOutcome.Recorded);

        var duplicate = await ScopedAsync<IClientService, Guid>(s => s.CreateAsync(new CreateClientRequest("Zenobia", "Quilfeather")));
        await ScopedAsync<IClientService, bool>(async s =>
        {
            await s.MergeAsync(_clientId, duplicate, null);
            return true;
        });
    }

    /// <summary>
    /// Every text column of every table of the salon's schema, searched for anything that would say who she was.
    /// Returns "table.column" for each one that still holds something. Cipher text cannot be searched; the tests check
    /// those columns are empty by name.
    /// </summary>
    private async Task<IReadOnlyList<string>> FoundAsync()
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        await db.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var columns = new List<(string Table, string Column)>();
        await using (var list = new NpgsqlCommand(
            "SELECT table_name, column_name FROM information_schema.columns WHERE table_schema = 'salon' AND data_type IN ('text', 'character varying', 'jsonb', 'json') ORDER BY table_name, column_name",
            connection))
        await using (var reader = await list.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                columns.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        columns.Count.ShouldBeGreaterThan(40, "the search must really cover the schema");
        var patterns = Needles.Select(n => "%" + n + "%").ToArray();
        var found = new List<string>();
        foreach (var (table, column) in columns)
        {
            await using var search = new NpgsqlCommand($"SELECT count(*) FROM salon.\"{table}\" WHERE \"{column}\"::text ILIKE ANY (@patterns)", connection);
            search.Parameters.AddWithValue("patterns", patterns);
            if ((long)(await search.ExecuteScalarAsync())! > 0)
            {
                found.Add($"{table}.{column}");
            }
        }

        return found;
    }

    /// <summary>A visit today, each a little earlier than the last so no two overlap; or so far from now.</summary>
    private Task<Guid> VisitAsync(AppointmentStatus status, Duration? fromNow = null)
        => VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, Now + (fromNow ?? Duration.Zero) - Duration.FromMinutes(35 * ++_visits), status);

    private Task SetStatusAsync(Guid appointmentId, AppointmentStatus status)
        => InScopeAsync(db => db.Appointments.Where(a => a.Id == appointmentId).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, status)));

    private async Task SavePolicyAsync(decimal noShowPercent)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await scope.ServiceProvider.GetRequiredService<ISettingService>().SaveAsync(new CancellationPolicySettings { NoShowFeePercent = noShowPercent });
    }

    private Task<T> DataAsync<T>(Func<IClientDataService, Task<T>> act, Guid? tenantId = null) => ScopedAsync(act, tenantId);

    private Task<T> AccountAsync<T>(Func<IClientAccountService, Task<T>> act) => ScopedAsync(act);

    private async Task<T> ScopedAsync<TService, T>(Func<TService, Task<T>> act, Guid? tenantId = null)
        where TService : notnull
    {
        await using var scope = factory.CreateTenantScope(tenantId ?? _studio.Id);
        return await act(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task InScopeAsync(Func<SalonDbContext, Task> act)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await act(scope.ServiceProvider.GetRequiredService<SalonDbContext>());
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