using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Npgsql;
using Salon.Core.Appointments;
using Salon.Core.Catalog;
using Salon.Core.Identity;
using Salon.Core.Import;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Appointments;
using Salon.Services.Appointments.Import;
using Salon.Services.Events;
using Salon.Services.Notifications;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Appointments;

/// <summary>
/// The coming weeks of the old system's diary from a file (ADR 0067), against the real database: a preview that
/// changes nothing and says why a row is left out, a confirm that writes the visits silently (no confirmation, the
/// waitlist closed, reminders from now on), one write that is all or nothing, a second run that adds nothing, and the
/// pages, the file of rows not brought over, the other salon and the roles. Anna works every day; a colour takes 90
/// minutes at £80; Sarah Smith and Olivia Brown are clients with phones; the visits are a few days ahead.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class ImportAppointmentsTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string ImportPath = "/admin/appointments/import";
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string SarahPhone = "+447700900111";
    private const string OliviaPhone = "+447700900222";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly LocalDate Today = SystemClock.Instance.GetCurrentInstant().InZone(London).Date;
    private static readonly LocalDate Day = Today.PlusDays(3);

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _anna;
    private Guid _tom;
    private Guid _colour;
    private Guid _sarah;
    private Guid _olivia;

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
            var anna = new StaffMember { DisplayName = "Anna Kowalska", SortOrder = 1, CreatedAt = now };
            var tom = new StaffMember { DisplayName = "Tom", SortOrder = 2, CreatedAt = now };
            db.StaffMembers.AddRange(anna, tom);
            foreach (var day in WorkingHoursRules.Week)
            {
                db.WorkingHours.Add(new WorkingHours { StaffMemberId = anna.Id, Day = day, Start = new LocalTime(9, 0), End = new LocalTime(17, 0) });
            }

            var colour = new Service { Name = "Colour", Duration = Duration.FromMinutes(90), Price = 80m, Currency = "GBP", SortOrder = 1 };
            db.Services.Add(colour);
            await db.SaveChangesAsync();
            (_anna, _tom, _colour) = (anna.Id, tom.Id, colour.Id);
        }

        _sarah = await VisitSeed.ClientAsync(factory, _studio.Id, "Sarah", "Smith", SarahPhone);
        _olivia = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", OliviaPhone);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Confirming_writes_the_visits_silently_closes_the_waitlist_and_leaves_reminders_to_come()
    {
        var owner = await factory.CreateUserAsync(_studio.Id, "owner@studio-20.test", Role.Owner);
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            (await scope.ServiceProvider.GetRequiredService<IWaitlistService>().AddAsync(new WaitlistRequest(_sarah, _colour, null, null, null, null), null)).ShouldBe(WaitlistAddOutcome.Added);
        }

        var preview = await PreviewAsync(Csv(
            $"{D(Day)},10:00,11:30,Sarah Smith,07700 900111,Colour,Anna,95.00,Confirmed",
            $"{D(Day)},12:00,,Olivia Brown,,Colour,Anna,,"));
        preview.NewCount.ShouldBe(2);

        var result = await CommitAsync(preview, owner.Id);

        (result.Added, result.AlreadyThere, result.Cancelled, result.NotBrought.Count).ShouldBe((2, 0, 0, 0));
        var visits = await VisitsAsync();
        visits.Select(v => (v.ClientId, v.StaffMemberId, v.ServiceId, v.Status, v.Source, v.PriceAmount, v.DepositAmount, v.DurationMinutes, v.Currency, v.CreatedByUserId))
            .ShouldBe(
            [
                (_sarah, _anna, _colour, AppointmentStatus.Booked, AppointmentSource.Import, 95m, 0m, 90, "GBP", (Guid?)owner.Id),
                (_olivia, _anna, _colour, AppointmentStatus.Booked, AppointmentSource.Import, 80m, 0m, 90, "GBP", (Guid?)owner.Id),
            ]);
        visits[0].Start.ShouldBe(At(Day, 10, 0));
        visits[0].End.ShouldBe(At(Day, 11, 30));
        visits.ShouldAllBe(v => v.PublicToken != null && v.PublicToken.Length == 22);
        var history = await InScopeAsync(s => s.GetRequiredService<SalonDbContext>().AppointmentStatusChanges.AsNoTracking().ToListAsync());
        history.Select(h => (h.From, h.To, h.Reason, h.ByUserId)).ShouldBe([(null, AppointmentStatus.Booked, "Imported", owner.Id), (null, AppointmentStatus.Booked, "Imported", owner.Id)]);

        // The outbox delivers the booked events: nobody is texted or emailed, and Sarah's wait for a colour is over.
        await DispatchAsync();
        factory.Sms.Messages.ShouldBeEmpty();
        factory.Emails.Messages.ShouldBeEmpty();
        var wish = await InScopeAsync(s => s.GetRequiredService<SalonDbContext>().WaitlistEntries.AsNoTracking().SingleAsync(e => e.ClientId == _sarah));
        (wish.Outcome, wish.AppointmentId).ShouldBe((WaitlistOutcome.Booked, visits[0].Id));

        // From now on they are reminded as any visit: the day before, Sarah gets her reminder.
        var dayBefore = At(Day, 10, 0) - Duration.FromHours(24);
        (await InScopeAsync(s => s.GetRequiredService<IAppointmentReminderService>().SendDueAsync(dayBefore))).ShouldBeGreaterThanOrEqualTo(1);
        factory.Sms.Messages.ShouldContain(m => m.ToE164 == SarahPhone);
    }

    [Fact]
    public async Task The_preview_says_why_rows_are_left_out_and_changes_nothing()
    {
        var preview = await PreviewAsync(Csv(
            $"{D(Today.PlusDays(-30))},10:00,,Sarah Smith,,Colour,Anna,,",
            $"{D(Day)},10:00,,Sarah Smith,07700 900999,Colour,Anna,,",
            $"{D(Day)},12:00,,Olivia Brown,,Highlights,Anna,,",
            $"{D(Day)},14:00,,Olivia Brown,,Colour,Becky,,",
            $"{D(Day)},16:00,,Olivia Brown,,Colour,Anna,,Pending",
            $"{D(Day.PlusDays(1))},10:00,,Olivia Brown,,Colour,Anna,,Cancelled",
            $"{D(Day.PlusDays(1))},12:00,,Olivia Brown,,Colour,Anna,,"));

        (preview.PastCount, preview.NewCount, preview.CancelledCount, preview.InvalidCount).ShouldBe((1, 1, 1, 4));
        preview.PastDays.ShouldBe([Today.PlusDays(-30)]);
        preview.UnknownServices.ShouldHaveSingleItem().Value.ShouldBe("Highlights");
        preview.UnknownStaff.ShouldHaveSingleItem().Value.ShouldBe("Becky");
        preview.Statuses.Select(s => (s.Word, s.Reading)).ShouldBe([("Cancelled", OldStatus.Gone), ("Pending", OldStatus.Unknown)], ignoreOrder: true);
        preview.RemindersFrom.ShouldNotBeNull();
        (await VisitsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_clash_the_database_refuses_at_the_last_moment_leaves_nothing_of_the_file()
    {
        // Two confirms of two files at once. The first holds its transaction open with Sarah's visit at 10:00. The
        // second file has four free times and that same 10:00 with Anna: its check cannot see the first's visit (not
        // committed yet), so it reaches the database, which makes it wait and then refuses it.
        var first = await PreviewAsync(Csv($"{D(Day)},10:00,,Sarah Smith,,Colour,Anna,,"));
        var second = await PreviewAsync(Csv(
            $"{D(Day)},12:00,,Olivia Brown,,Colour,Anna,,",
            $"{D(Day)},14:00,,Olivia Brown,,Colour,Anna,,",
            $"{D(Day.PlusDays(1))},10:00,,Olivia Brown,,Colour,Anna,,",
            $"{D(Day.PlusDays(1))},12:00,,Olivia Brown,,Colour,Anna,,",
            $"{D(Day)},10:30,,Olivia Brown,,Colour,Anna,,"));
        second.NewCount.ShouldBe(5);
        var outboxBefore = await OutboxCountAsync();

        await using var firstScope = factory.CreateTenantScope(_studio.Id);
        var firstDb = firstScope.ServiceProvider.GetRequiredService<SalonDbContext>();
        await using var firstTransaction = await firstDb.Database.BeginTransactionAsync();
        (await firstScope.ServiceProvider.GetRequiredService<IAppointmentImportService>().CommitAsync(first, null)).Added.ShouldBe(1);

        var secondRun = Task.Run(async () =>
        {
            await using var scope = factory.CreateTenantScope(_studio.Id);
            await using var transaction = await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<IAppointmentImportService>().CommitAsync(second, null);
            await transaction.CommitAsync();
        });
        await WaitForALockAsync();
        await firstTransaction.CommitAsync();

        await Should.ThrowAsync<AppointmentImportClashException>(() => secondRun);
        var visits = await VisitsAsync();
        visits.ShouldHaveSingleItem().ClientId.ShouldBe(_sarah, "nothing of the second file, not even its four free times");
        (await InScopeAsync(s => s.GetRequiredService<SalonDbContext>().AppointmentStatusChanges.CountAsync())).ShouldBe(1);
        (await OutboxCountAsync()).ShouldBe(outboxBefore + 1, "one event, the first file's");
    }

    [Fact]
    public async Task A_second_run_adds_nothing_and_names_a_visit_cancelled_in_the_old_system_since()
    {
        var file = Csv(
            $"{D(Day)},10:00,,Sarah Smith,,Colour,Anna,,",
            $"{D(Day)},12:00,,Olivia Brown,,Colour,Anna,,");
        (await CommitAsync(await PreviewAsync(file), null)).Added.ShouldBe(2);

        // Sarah cancels here through her link; the old system still says booked. Olivia cancels in the old system.
        var sarahs = (await VisitsAsync()).Single(v => v.ClientId == _sarah);
        await InScopeAsync(s => s.GetRequiredService<IBookingService>().CancelAsync(sarahs.Id, byClient: true, reason: null));
        var again = await PreviewAsync(Csv(
            $"{D(Day)},10:00,,Sarah Smith,,Colour,Anna,,",
            $"{D(Day)},12:00,,Olivia Brown,,Colour,Anna,,Cancelled"));

        again.Rows.Select(r => r.Status).ShouldBe([AppointmentImportStatus.AlreadyThere, AppointmentImportStatus.CancelledSince]);
        var result = await CommitAsync(again, null);
        (result.Added, result.AlreadyThere).ShouldBe((0, 1));
        var olivias = (await VisitsAsync()).Single(v => v.ClientId == _olivia);
        result.NotBrought.ShouldHaveSingleItem().Match.ExistingAppointmentId.ShouldBe(olivias.Id);
        olivias.Status.ShouldBe(AppointmentStatus.Booked, "the owner decides; the file does not cancel");
        (await VisitsAsync()).Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_owner_reads_a_file_in_through_the_pages_and_downloads_what_was_not_brought_over()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var form = await GetOkAsync(owner, ImportPath);
        form.ShouldContain("Your diary from a file");
        form.ShouldContain("Date,Start time,End time,Client,Phone,Service,Staff,Price,Status");
        form.ShouldContain("Notes are not brought over: they can hold health details.");
        form.ShouldContain("href=\"/admin/clients/import\"");

        var upload = await UploadAsync(owner, Encoding.UTF8.GetBytes(Csv(
            $"{D(Day)},10:00,11:30,Sarah Smith,07700 900111,Colour,Anna,,",
            $"{D(Day)},12:00,,Olivia Brown,,Highlights,Anna,,")), "diary.csv");
        upload.StatusCode.ShouldBe(HttpStatusCode.Redirect, await upload.Content.ReadAsStringAsync());
        var previewPath = upload.Headers.Location!.OriginalString;
        TokenPattern().IsMatch(previewPath[(ImportPath.Length + 1)..]).ShouldBeTrue(previewPath);

        var html = await GetOkAsync(owner, previewPath);
        (Count(html, "count-new"), Count(html, "count-not-brought")).ShouldBe(("1", "1"));
        html.ShouldContain("Nothing has changed yet.");
        html.ShouldContain("No service of this name here.");
        html.ShouldContain("Highlights: 1 row");
        html.ShouldContain("Colour · Anna Kowalska (by first name) · 90 min · £80.00");
        html.ShouldContain("Our reminders go out from now on. No visit to add starts before ");
        html.ShouldContain("1 future visit will not be in your diary.");
        html.ShouldContain("Add 1 visit");

        var file = await owner.GetAsync(new Uri(previewPath + "/not-brought.csv", UriKind.Relative));
        file.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bytes = await file.Content.ReadAsByteArrayAsync();
        var lines = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].ShouldBe("Row,Date,Time,Client,Phone,Email,Service,Staff,Why not brought over");
        lines.Length.ShouldBe(2);
        lines[1].ShouldStartWith("3,");
        lines[1].ShouldContain("Olivia Brown");
        lines[1].ShouldContain("No service of this name here.");

        var confirm = await PostAsync(owner, previewPath + "/confirm", html);
        confirm.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var result = await GetOkAsync(owner, confirm.Headers.Location!.OriginalString);
        result.ShouldContain("The diary is read in");
        Count(result, "result-added").ShouldBe("1");
        result.ShouldContain("1 future visit could not be read in.");
        (await VisitsAsync()).ShouldHaveSingleItem().ClientId.ShouldBe(_sarah);

        // A second press shows the result again and adds nothing.
        (await PostAsync(owner, previewPath + "/confirm", html)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await VisitsAsync()).Count.ShouldBe(1);

        // The visits file says where the visit came from.
        var visits = await owner.GetAsync(new Uri($"/admin/reports/visits.csv?from={Day:yyyy-MM-dd}&to={Day:yyyy-MM-dd}", UriKind.Relative));
        Encoding.UTF8.GetString(await visits.Content.ReadAsByteArrayAsync()).ShouldContain("From the old system");
    }

    [Theory]
    [InlineData("Booking ref,Who\nA1,Sarah\n", "We could not find a column for: the date, the start time, the service, the client.")]
    [InlineData("Date,Time,Client,Service\n11/20/2030,10:00,Sarah Smith,Colour\n", "The dates in this file are written month first")]
    [InlineData("Date,Time,Client,Service\n", "The file has no visits.")]
    public async Task A_file_that_cannot_be_used_is_refused_with_the_reason(string csv, string message)
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var response = await UploadAsync(owner, Encoding.UTF8.GetBytes(csv), "diary.csv");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()).ShouldContain(message);
    }

    [Fact]
    public async Task Another_salon_cannot_see_confirm_or_download_and_only_owners_and_managers_get_in()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var previewPath = (await UploadAsync(owner, Encoding.UTF8.GetBytes(Csv($"{D(Day)},10:00,,Sarah Smith,,Colour,Anna,,")), "diary.csv")).Headers.Location!.OriginalString;

        using var polish = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        (await polish.GetAsync(new Uri(previewPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await polish.GetAsync(new Uri(previewPath + "/not-brought.csv", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(polish, previewPath + "/confirm", await GetOkAsync(polish, ImportPath))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var receptionist = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist, "desk@studio-20.test");
        foreach (var path in new[] { ImportPath, previewPath })
        {
            var denied = await receptionist.GetAsync(new Uri(path, UriKind.Relative));
            denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied", path);
        }

        using var manager = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Manager, "manager@studio-20.test");
        (await GetOkAsync(manager, ImportPath)).ShouldContain("Your diary from a file");

        var noToken = await owner.PostAsync(new Uri(previewPath + "/confirm", UriKind.Relative), new FormUrlEncodedContent([]));
        noToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await VisitsAsync()).ShouldBeEmpty();

        var unknown = await owner.GetAsync(new Uri($"{ImportPath}/{new string('a', 64)}", UriKind.Relative));
        unknown.Headers.Location!.OriginalString.ShouldBe(ImportPath);
    }

    private static string D(LocalDate day) => day.ToString("dd/MM/yyyy", null);

    private static Instant At(LocalDate day, int hour, int minute) => London.AtStrictly(day + new LocalTime(hour, minute)).ToInstant();

    private static string Csv(params string[] rows) => "Date,Start time,End time,Client,Phone,Service,Staff,Price,Status\n" + string.Join('\n', rows) + "\n";

    private async Task<AppointmentImportPreview> PreviewAsync(string csv)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var imports = scope.ServiceProvider.GetRequiredService<IAppointmentImportService>();
        var parsed = scope.ServiceProvider.GetRequiredService<IAppointmentImportProvider>().Parse(Encoding.UTF8.GetBytes(csv), await imports.FirstDayAsync());
        parsed.IsSuccess.ShouldBeTrue(parsed.FileError?.ToString() ?? parsed.Error?.ToString());
        return await imports.PreviewAsync(parsed, "diary.csv");
    }

    /// <summary>In a transaction, as the request pipeline runs every command.</summary>
    private async Task<AppointmentImportResult> CommitAsync(AppointmentImportPreview preview, Guid? byUserId)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await using var transaction = await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Database.BeginTransactionAsync();
        var result = await scope.ServiceProvider.GetRequiredService<IAppointmentImportService>().CommitAsync(preview, byUserId);
        await transaction.CommitAsync();
        return result;
    }

    private Task<List<Appointment>> VisitsAsync()
        => InScopeAsync(s => s.GetRequiredService<SalonDbContext>().Appointments.AsNoTracking().OrderBy(a => a.Start).ToListAsync());

    private static async Task<long> OutboxCountAsync()
    {
        await using var connection = new NpgsqlConnection(SalonWebApplicationFactory.TestConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select count(*) from salon.outbox_messages", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Until a session of this database waits on a lock: the second confirm has reached the clash rule.</summary>
    private static async Task WaitForALockAsync()
    {
        await using var connection = new NpgsqlConnection(SalonWebApplicationFactory.TestConnectionString);
        await connection.OpenAsync();
        for (var attempt = 0; attempt < 200; attempt++)
        {
            await using var command = new NpgsqlCommand("select count(*) from pg_stat_activity where datname = current_database() and wait_event_type = 'Lock'", connection);
            if ((long)(await command.ExecuteScalarAsync())! > 0)
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("The second confirm never reached the database's clash rule.");
    }

    private async Task DispatchAsync()
    {
        await using var scope = factory.CreateTenantScope(null);
        await scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync();
    }

    private async Task InScopeAsync(Func<IServiceProvider, Task> act)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await act(scope.ServiceProvider);
    }

    private async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> act)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await act(scope.ServiceProvider);
    }

    private static async Task<string> GetOkAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] content, string fileName)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(IdentityTestClient.AntiForgeryToken(await GetOkAsync(client, ImportPath))), "__RequestVerificationToken");
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "File", fileName);
        return await client.PostAsync(new Uri(ImportPath, UriKind.Relative), form);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string pageWithToken)
        => client.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent([new KeyValuePair<string, string>("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(pageWithToken))]));

    private static string Count(string html, string testId)
    {
        var match = Regex.Match(html, $"data-testid=\"{testId}\"[^>]*>\\s*(?<value>\\d+)\\s*<");
        match.Success.ShouldBeTrue($"no {testId} in the page");
        return match.Groups["value"].Value;
    }

    [GeneratedRegex("^[a-f0-9]{64}$")]
    private static partial Regex TokenPattern();
}