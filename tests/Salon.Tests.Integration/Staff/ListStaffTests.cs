using System.Net;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NodaTime;

using Salon.Core.Identity;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Staff;

/// <summary>
/// The staff list over HTTP as a signed-in Owner: every person of the salon with the rota compressed to lines, inactive
/// people greyed and badged, Polish labels on a Polish salon, the empty state, nothing of another salon, the admin nav, and
/// the gates (Staff.Manage, sign-in, tenant).
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class ListStaffTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string ListPath = "/admin/staff";

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _tomId;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        await factory.CreateTenantAsync("empty-salon", name: "Empty Salon");

        await AddStaffAsync(_studio.Id, "Anna Kowalska", sortOrder: 1, colour: "#BE185D",
            days: [IsoDayOfWeek.Tuesday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Thursday, IsoDayOfWeek.Friday, IsoDayOfWeek.Saturday], ranges: [(10, 0, 18, 0)]);
        _tomId = await AddStaffAsync(_studio.Id, "Tom Reed", sortOrder: 2, colour: "#1D4ED8",
            days: [IsoDayOfWeek.Monday, IsoDayOfWeek.Tuesday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Thursday, IsoDayOfWeek.Friday], ranges: [(14, 0, 17, 0), (9, 0, 13, 0)]);
        await AddStaffAsync(_studio.Id, "Rita Retired", sortOrder: 3, colour: "#78716C", isActive: false, isBookable: false, days: [], ranges: []);
        await AddStaffAsync(_polish.Id, "Ewa Nowak", sortOrder: 1, colour: "#047857",
            days: [IsoDayOfWeek.Monday, IsoDayOfWeek.Tuesday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Thursday, IsoDayOfWeek.Friday], ranges: [(9, 0, 17, 0)]);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Shows_every_staff_member_in_order_with_a_compressed_rota()
    {
        using var client = await factory.SignedInClientAsync("studio-20.localhost", _studio.Id, Role.Owner);

        var html = await GetListAsync(client);

        html.ShouldContain("<html lang=\"en-GB\"");
        html.ShouldContain("data-testid=\"staff-list\"");
        CardNames(html).ShouldBe(["Anna Kowalska", "Tom Reed", "Rita Retired"]);
        html.ShouldContain("Tue–Sat 10:00–18:00");
        html.ShouldContain("Mon–Fri 09:00–13:00, 14:00–17:00");
        html.ShouldContain("background-color: #1D4ED8");
        html.ShouldContain($"href=\"/admin/staff/{_tomId}/hours\"");
        html.ShouldContain("href=\"/admin/staff/new\"");
        html.ShouldContain("Edit hours");
        html.ShouldNotContain("data-testid=\"empty-state\"");
        html.ShouldNotContain("Ewa Nowak");
    }

    [Fact]
    public async Task An_inactive_person_is_greyed_badged_and_has_no_hours()
    {
        using var client = await factory.SignedInClientAsync("studio-20.localhost", _studio.Id, Role.Owner);

        var html = await GetListAsync(client);

        var cards = CardPattern().Matches(html).Select(m => m.Value).ToList();
        cards.Count.ShouldBe(3);
        cards[0].ShouldContain("data-active=\"true\"");
        cards[0].ShouldNotContain("opacity-60");
        cards[2].ShouldContain("data-active=\"false\"");
        cards[2].ShouldContain("opacity-60");
        cards[2].ShouldContain("data-testid=\"badge-inactive\"");
        cards[2].ShouldContain("Inactive");
        cards[2].ShouldContain("data-testid=\"badge-not-bookable\"");
        cards[2].ShouldContain("Not bookable");
        cards[2].ShouldContain("No working hours set");
        cards[1].ShouldNotContain("badge-");
    }

    [Fact]
    public async Task A_Polish_salon_shows_Polish_labels_and_day_names()
    {
        using var client = await factory.SignedInClientAsync("salon-pl.localhost", _polish.Id, Role.Owner);

        var html = await GetListAsync(client);

        html.ShouldContain("<html lang=\"pl-PL\"");
        html.ShouldContain("Pracownicy");
        html.ShouldContain("Twój zespół i tygodniowe godziny pracy.");
        html.ShouldContain("Dodaj pracownika");
        html.ShouldContain("Edytuj godziny");
        html.ShouldContain("Pn–Pt 09:00–17:00");
        CardNames(html).ShouldBe(["Ewa Nowak"]);
        html.ShouldNotContain("Anna Kowalska");
    }

    [Fact]
    public async Task A_salon_without_staff_shows_the_empty_state()
    {
        var empty = await TenantIdAsync("empty-salon");
        using var client = await factory.SignedInClientAsync("empty-salon.localhost", empty, Role.Manager);

        var html = await GetListAsync(client);

        html.ShouldContain("data-testid=\"empty-state\"");
        html.ShouldContain("No staff members yet.");
        html.ShouldNotContain("data-testid=\"staff-card\"");
        html.ShouldContain("href=\"/admin/staff/new\"");
    }

    [Fact]
    public async Task The_admin_nav_shows_the_staff_link_to_an_owner_and_not_to_a_receptionist()
    {
        using var owner = await factory.SignedInClientAsync("studio-20.localhost", _studio.Id, Role.Owner, "owner-nav@studio-20.localhost");
        using var receptionist = await factory.SignedInClientAsync("studio-20.localhost", _studio.Id, Role.Receptionist, "desk-nav@studio-20.localhost");

        var ownerHtml = await GetListAsync(owner);
        var receptionistHome = await receptionist.GetAsync(new Uri("/services", UriKind.Relative));
        var receptionistHtml = await receptionistHome.Content.ReadAsStringAsync();

        ownerHtml.ShouldContain("data-testid=\"admin-nav\"");
        ownerHtml.ShouldContain("data-testid=\"nav-staff\"");
        ownerHtml.ShouldContain("data-testid=\"nav-services\"");
        ownerHtml.ShouldContain("href=\"/admin/services\"");
        receptionistHtml.ShouldContain("data-testid=\"admin-nav\"");
        receptionistHtml.ShouldNotContain("data-testid=\"nav-staff\"");
        receptionistHtml.ShouldNotContain("data-testid=\"nav-services\"");

        using var anonymous = factory.ClientFor("studio-20.localhost");
        (await (await anonymous.GetAsync(new Uri("/services", UriKind.Relative))).Content.ReadAsStringAsync()).ShouldNotContain("data-testid=\"admin-nav\"");
    }

    [Fact]
    public async Task A_receptionist_is_sent_to_the_denied_page()
    {
        using var client = await factory.SignedInClientAsync("studio-20.localhost", _studio.Id, Role.Receptionist);

        var response = await client.GetAsync(new Uri(ListPath, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
    }

    [Fact]
    public async Task A_staff_login_is_denied_too()
    {
        using var client = await factory.SignedInClientAsync("studio-20.localhost", _studio.Id, Role.Staff);

        var response = await client.GetAsync(new Uri(ListPath, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
    }

    [Fact]
    public async Task Without_a_sign_in_the_screen_asks_for_login()
    {
        using var client = factory.ClientFor("studio-20.localhost");

        var response = await client.GetAsync(new Uri(ListPath, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location.ShouldNotBeNull();
        location.AbsolutePath.ShouldBe("/account/login");
        location.Query.ShouldBe("?returnUrl=%2Fadmin%2Fstaff");
    }

    [Fact]
    public async Task The_bare_platform_domain_has_no_staff_list()
    {
        using var client = factory.ClientFor("localhost");

        (await client.GetAsync(new Uri(ListPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static List<string> CardNames(string html)
        => CardNamePattern().Matches(html).Select(m => m.Groups["name"].Value).ToList();

    private static async Task<string> GetListAsync(HttpClient client)
    {
        var response = await client.GetAsync(new Uri(ListPath, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<Guid> TenantIdAsync(string slug)
    {
        await using var scope = factory.CreateTenantScope(null);
        return (await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Tenants.AsNoTracking().SingleAsync(t => t.Slug == slug)).Id;
    }

    private async Task<Guid> AddStaffAsync(
        Guid tenantId,
        string name,
        int sortOrder,
        string colour,
        IsoDayOfWeek[] days,
        (int FromHour, int FromMinute, int ToHour, int ToMinute)[] ranges,
        bool isActive = true,
        bool isBookable = true)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var staff = new StaffMember { DisplayName = name, Colour = colour, SortOrder = sortOrder, IsActive = isActive, IsBookable = isBookable, CreatedAt = SystemClock.Instance.GetCurrentInstant() };
        db.StaffMembers.Add(staff);
        foreach (var day in days)
        {
            foreach (var (fromHour, fromMinute, toHour, toMinute) in ranges)
            {
                db.WorkingHours.Add(new WorkingHours { StaffMemberId = staff.Id, Day = day, Start = new LocalTime(fromHour, fromMinute), End = new LocalTime(toHour, toMinute) });
            }
        }

        await db.SaveChangesAsync();
        return staff.Id;
    }

    /// <summary>The h2 inside each staff card, in document order.</summary>
    [GeneratedRegex("data-testid=\"staff-name\">(?<name>[^<]+)</h2>")]
    private static partial Regex CardNamePattern();

    /// <summary>Each card from its opening tag to its "Edit hours" link.</summary>
    [GeneratedRegex("<li[^>]*data-testid=\"staff-card\".*?data-testid=\"edit-hours\"", RegexOptions.Singleline)]
    private static partial Regex CardPattern();
}