using System.Net;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Identity;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Services.Locations;
using Salon.Services.Settings;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Settings;

/// <summary>
/// The note on the diary while set-up is unfinished (ADR 0072): the owner of a new salon sees on the day list and
/// the calendar how far the set-up has come and where to carry on; a set-up salon's diary is quiet, and so is the
/// diary of anybody who is not the owner, who could not open the page the note leads to.
/// </summary>
[Collection(SharedDatabase.Name)]
public class SetupNoteTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string DayList = "/admin/appointments";
    private const string Calendar = "/admin/appointments/calendar";
    private const string HostA = "studio-20.localhost";

    private Tenant _studio = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_owner_of_a_new_salon_is_told_on_the_diary_how_far_the_set_up_has_come()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        foreach (var page in new[] { DayList, Calendar })
        {
            var html = await GetOkAsync(owner, page);
            html.ShouldContain("data-testid=\"setup-note\"", Case.Sensitive, page);
            html.ShouldContain("Your salon is not set up yet: 1 of 8 steps done.", Case.Sensitive, page);
            html.ShouldContain("href=\"/admin/settings/getting-started\" class=\"inline-flex min-h-11 items-center font-semibold underline underline-offset-4\" data-testid=\"setup-note-link\"", Case.Sensitive, page);
        }
    }

    [Fact]
    public async Task A_set_up_salon_and_everyone_who_is_not_the_owner_see_no_note()
    {
        // A manager runs the diary but could not open the page the note leads to.
        using (var manager = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Manager, "manager@studio-20.test"))
        {
            (await GetOkAsync(manager, DayList)).ShouldNotContain("data-testid=\"setup-note\"", Case.Sensitive, "a manager, new salon");
        }

        var (staffId, serviceId) = await VisitSeed.SalonAsync(factory, _studio.Id);
        await HoursAsync(staffId);
        var clientId = await VisitSeed.ClientAsync(factory, _studio.Id, "Sarah", "Smith", "+447700900111");
        await VisitSeed.AppointmentAsync(factory, _studio.Id, clientId, staffId, serviceId, SystemClock.Instance.GetCurrentInstant() + Duration.FromDays(2), AppointmentStatus.Booked);
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            await scope.ServiceProvider.GetRequiredService<ILocationService>().SaveMainAsync(new LocationDetails(null, "20 Drake Street", null, "Rochdale", "OL16 1NT", "01632 960020", null));
            await scope.ServiceProvider.GetRequiredService<ISettingService>().SaveAsync(new PrivacyNoticeSettings { UpdatedOn = "2026-11-19" });
        }

        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        foreach (var page in new[] { DayList, Calendar })
        {
            (await GetOkAsync(owner, page)).ShouldNotContain("data-testid=\"setup-note\"", Case.Sensitive, $"set up: {page}");
        }
    }

    /// <summary>Monday to Friday, nine to five.</summary>
    private async Task HoursAsync(Guid staffId)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<Salon.Data.SalonDbContext>();
        foreach (var day in new[] { IsoDayOfWeek.Monday, IsoDayOfWeek.Tuesday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Thursday, IsoDayOfWeek.Friday })
        {
            db.WorkingHours.Add(new Salon.Core.Staff.WorkingHours { StaffMemberId = staffId, Day = day, Start = new LocalTime(9, 0), End = new LocalTime(17, 0) });
        }

        await db.SaveChangesAsync();
    }

    private static async Task<string> GetOkAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}