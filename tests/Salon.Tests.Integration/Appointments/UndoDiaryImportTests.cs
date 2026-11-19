using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Catalog;
using Salon.Core.Identity;
using Salon.Core.Import;
using Salon.Core.Payments;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Appointments.Import;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Appointments;

/// <summary>
/// Undoing a diary import (ADR 0074): a file read in by mistake is taken out whole, a visit the desk has touched
/// since - moved, marked or taken money for - stays with its history, and reading the same file in again brings the
/// removed visits back. Another salon's read-ins are its own; the staff role cannot open the page.
/// </summary>
[Collection(SharedDatabase.Name)]
public class UndoDiaryImportTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string UndoPath = "/admin/appointments/import/undo";
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly LocalDate Day = SystemClock.Instance.GetCurrentInstant().InZone(London).Date.PlusDays(3);

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _sarah;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            var now = SystemClock.Instance.GetCurrentInstant();
            var anna = new StaffMember { DisplayName = "Anna Kowalska", SortOrder = 1, CreatedAt = now };
            db.StaffMembers.Add(anna);
            foreach (var day in WorkingHoursRules.Week)
            {
                db.WorkingHours.Add(new WorkingHours { StaffMemberId = anna.Id, Day = day, Start = new LocalTime(9, 0), End = new LocalTime(17, 0) });
            }

            db.Services.Add(new Service { Name = "Colour", Duration = Duration.FromMinutes(90), Price = 80m, Currency = "GBP", SortOrder = 1 });
            await db.SaveChangesAsync();
        }

        _sarah = await VisitSeed.ClientAsync(factory, _studio.Id, "Sarah", "Smith", "+447700900111");
        await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900222");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_file_read_in_by_mistake_is_taken_out_whole_and_a_second_read_in_brings_it_back()
    {
        await ImportAsync(Row(10, "Sarah Smith", "+447700900111"), Row(13, "Olivia Brown", "+447700900222"));
        using var manager = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Manager);

        var page = await GetOkAsync(manager, UndoPath);
        page.ShouldContain("data-testid=\"undo-batch\"");
        page.ShouldContain("data-brought=\"2\"");
        page.ShouldContain("data-untouched=\"2\"");

        var batchId = (await VisitsAsync()).First().ImportBatchId.ShouldNotBeNull();
        var response = await manager.PostAsync(new Uri($"{UndoPath}/{batchId}", UriKind.Relative), await AntiForgeryAsync(manager));
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var after = await GetOkAsync(manager, response.Headers.Location!.OriginalString);
        after.ShouldContain("The file is out of the diary: 2 visits removed, 0 kept.");
        after.ShouldNotContain("data-testid=\"undo-batch\"", Case.Sensitive, "the read-in is gone with its visits");
        after.ShouldContain("No file has been read into the diary yet.");

        (await VisitsAsync()).ShouldBeEmpty();
        (await StatusChangesAsync()).ShouldBeEmpty("history goes with the visits");

        // The same file again: the visits are back, as a fresh read-in.
        await ImportAsync(Row(10, "Sarah Smith", "+447700900111"), Row(13, "Olivia Brown", "+447700900222"));
        var visits = await VisitsAsync();
        visits.Count.ShouldBe(2);
        visits.Select(v => v.ImportBatchId).ShouldAllBe(b => b != null && b != batchId);
    }

    [Fact]
    public async Task A_visit_the_desk_has_touched_stays_with_its_history_and_its_money()
    {
        await ImportAsync(Row(10, "Sarah Smith", "+447700900111"), Row(13, "Olivia Brown", "+447700900222"), Row(15, "Sarah Smith", "+447700900111"));
        var visits = await VisitsAsync();
        var batchId = visits[0].ImportBatchId.ShouldNotBeNull();
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            var now = SystemClock.Instance.GetCurrentInstant();

            // The ten o'clock client arrived; the one o'clock visit took a part payment at the desk.
            var arrived = await db.Appointments.SingleAsync(a => a.Id == visits[0].Id);
            arrived.Status = AppointmentStatus.Arrived;
            db.AppointmentStatusChanges.Add(new AppointmentStatusChange { AppointmentId = arrived.Id, From = AppointmentStatus.Booked, To = AppointmentStatus.Arrived, At = now });
            db.ClientAccountEntries.Add(new ClientAccountEntry
            {
                ClientId = _sarah,
                AppointmentId = visits[1].Id,
                Kind = ClientAccountEntryKind.PartPayment,
                Amount = 5m,
                Currency = "GBP",
                At = now,
            });
            await db.SaveChangesAsync();
        }

        using var manager = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Manager);
        (await GetOkAsync(manager, UndoPath)).ShouldContain("data-untouched=\"1\"");

        var response = await manager.PostAsync(new Uri($"{UndoPath}/{batchId}", UriKind.Relative), await AntiForgeryAsync(manager));
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await GetOkAsync(manager, response.Headers.Location!.OriginalString)).ShouldContain("The file is out of the diary: 1 visit removed, 2 kept.");

        var left = await VisitsAsync();
        left.Select(v => v.Id).ShouldBe([visits[0].Id, visits[1].Id], ignoreOrder: true, "the touched visits stay");
    }

    [Fact]
    public async Task Another_salons_read_in_is_its_own_and_the_staff_role_cannot_open_the_page()
    {
        await ImportAsync(Row(10, "Sarah Smith", "+447700900111"));
        var batchId = (await VisitsAsync()).First().ImportBatchId.ShouldNotBeNull();

        using (var theirs = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner))
        {
            (await GetOkAsync(theirs, UndoPath)).ShouldNotContain("data-testid=\"undo-batch\"", Case.Sensitive, "the other salon sees nothing");
            var response = await theirs.PostAsync(new Uri($"{UndoPath}/{batchId}", UriKind.Relative), await AntiForgeryAsync(theirs));
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, "another salon's read-in looks like a missing one");
        }

        using var staff = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Staff, "staff@studio-20.test");
        var denied = await staff.GetAsync(new Uri(UndoPath, UriKind.Relative));
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");

        (await VisitsAsync()).Count.ShouldBe(1, "nothing was removed");
    }

    private static string Row(int hour, string client, string phone)
        => $"{Day.ToString("dd/MM/yyyy", null)},{hour}:00,,{client},{phone},Colour,Anna,80,booked";

    private async Task ImportAsync(params string[] rows)
    {
        var csv = "Date,Start time,End time,Client,Phone,Service,Staff,Price,Status\n" + string.Join('\n', rows) + "\n";
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var imports = scope.ServiceProvider.GetRequiredService<IAppointmentImportService>();
        var parsed = scope.ServiceProvider.GetRequiredService<IAppointmentImportProvider>().Parse(Encoding.UTF8.GetBytes(csv), await imports.FirstDayAsync());
        parsed.IsSuccess.ShouldBeTrue();
        var preview = await imports.PreviewAsync(parsed, "diary.csv");
        preview.NewCount.ShouldBe(rows.Length, "every row of the file must read in");
        await using var transaction = await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Database.BeginTransactionAsync();
        (await imports.CommitAsync(preview, null)).Added.ShouldBe(rows.Length);
        await transaction.CommitAsync();
    }

    private Task<List<Appointment>> VisitsAsync()
        => InScopeAsync(s => s.GetRequiredService<SalonDbContext>().Appointments.AsNoTracking().OrderBy(a => a.Start).ToListAsync());

    private Task<List<AppointmentStatusChange>> StatusChangesAsync()
        => InScopeAsync(s => s.GetRequiredService<SalonDbContext>().AppointmentStatusChanges.AsNoTracking().ToListAsync());

    private async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> read)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await read(scope.ServiceProvider);
    }

    /// <summary>The anti-forgery pair, harvested from the import page's form: the undo page of an empty salon has none.</summary>
    private static async Task<FormUrlEncodedContent> AntiForgeryAsync(HttpClient client)
        => new(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(await GetOkAsync(client, "/admin/appointments/import")),
        });

    private static async Task<string> GetOkAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}