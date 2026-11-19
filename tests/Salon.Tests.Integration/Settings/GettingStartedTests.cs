using System.Net;
using System.Text.RegularExpressions;
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
/// The getting started page (ADR 0068): each step is read from the salon's own data, so a new salon sees what is
/// left and a set-up salon sees everything done, with no box for anybody to tick. Owner only; the other salon's data
/// never counts.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class GettingStartedTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string Path = "/admin/settings/getting-started";
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";

    private Tenant _studio = null!;
    private Tenant _polish = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_new_salon_sees_what_is_left_and_where_to_do_it()
    {
        // Reminders are on to begin with; everything else is still to do.
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var html = await GetOkAsync(owner, Path);

        html.ShouldContain("href=\"/admin/settings/getting-started\" data-testid=\"settings-nav-getting-started\" aria-current=\"page\"");
        html.ShouldContain("1 of 8 steps done.");
        foreach (var step in new[] { "SalonDetails", "Team", "Hours", "PriceList", "Clients", "Diary", "PrivacyNotice" })
        {
            Done(html, step).ShouldBe("false", step);
        }

        Done(html, "Reminders").ShouldBe("true");
        Done(html, "Policy").ShouldBe("false");
        html.ShouldContain("Add your address and a phone number or an email: clients see them when they book.");
        html.ShouldContain("Clients get a reminder 24 hours before their visit.");
        html.ShouldContain("href=\"/admin/services/import\" class=\"inline-flex min-h-11 items-center text-sm font-medium underline underline-offset-2\" data-testid=\"setup-step-import\"");
        html.ShouldContain("href=\"/admin/appointments/import\"");
        html.ShouldContain("href=\"/book\"");
    }

    [Fact]
    public async Task A_salon_that_is_set_up_sees_every_step_done_from_its_own_data()
    {
        var (staffId, serviceId) = await VisitSeed.SalonAsync(factory, _studio.Id);
        await HoursAsync(staffId);
        var clientId = await VisitSeed.ClientAsync(factory, _studio.Id, "Sarah", "Smith", "+447700900111");
        await VisitSeed.AppointmentAsync(factory, _studio.Id, clientId, staffId, serviceId, SystemClock.Instance.GetCurrentInstant() + Duration.FromDays(2), AppointmentStatus.Booked);
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            await scope.ServiceProvider.GetRequiredService<ILocationService>().SaveMainAsync(new LocationDetails(null, "20 Drake Street", null, "Rochdale", "OL16 1NT", "01632 960020", null));
            var settings = scope.ServiceProvider.GetRequiredService<ISettingService>();
            await settings.SaveAsync(new PrivacyNoticeSettings { UpdatedOn = "2026-11-19" });
            await settings.SaveAsync(new CancellationPolicySettings { NoShowFeePercent = 50m });
        }

        // The other salon has nothing, and its emptiness changes nothing here.
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var html = await GetOkAsync(owner, Path);

        html.ShouldContain("Everything is set up. Your salon is ready for its clients.");
        Regex.Count(html, "data-done=\"true\"").ShouldBe(9);
        html.ShouldContain("Clients: 1.");
        html.ShouldContain("Visits booked from now on: 1.");
        html.ShouldNotContain("data-testid=\"setup-step-import\"", Case.Sensitive, "a step that is done offers no file");

        using var theirs = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        Regex.Count(await GetOkAsync(theirs, Path), "data-done=\"true\"").ShouldBe(1, "only reminders, which are on to begin with");
    }

    [Fact]
    public async Task People_without_working_hours_and_reminders_switched_off_are_steps_to_do()
    {
        // Anna has her hours; Tom, added after her, has none yet.
        var (anna, _) = await VisitSeed.SalonAsync(factory, _studio.Id);
        await HoursAsync(anna);
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<Salon.Data.SalonDbContext>();
            db.StaffMembers.Add(new Salon.Core.Staff.StaffMember { DisplayName = "Tom", CreatedAt = SystemClock.Instance.GetCurrentInstant() });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<ISettingService>().SaveAsync(new NotificationSettings { RemindersEnabled = false });
        }

        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var html = await GetOkAsync(owner, Path);

        (Done(html, "Team"), Done(html, "Hours"), Done(html, "Reminders")).ShouldBe(("true", "false", "false"));
        html.ShouldContain("People without working hours: 1.");
        html.ShouldContain("Reminders are off: clients get no message before their visit.");
    }

    [Fact]
    public async Task Only_the_owner_gets_in()
    {
        foreach (var role in new[] { Role.Manager, Role.Receptionist, Role.Staff })
        {
            using var client = await factory.SignedInClientAsync(HostA, _studio.Id, role, $"{role}@studio-20.test".ToLowerInvariant());
            var response = await client.GetAsync(new Uri(Path, UriKind.Relative));
            response.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied", role.ToString());
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

    private static string Done(string html, string step)
    {
        var match = Regex.Match(html, $"data-step=\"{step}\" data-done=\"(?<done>true|false)\"");
        match.Success.ShouldBeTrue($"no step {step}");
        return match.Groups["done"].Value;
    }

    private static async Task<string> GetOkAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}