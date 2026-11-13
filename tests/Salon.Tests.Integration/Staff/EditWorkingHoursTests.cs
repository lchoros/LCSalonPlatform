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
/// The EditWorkingHours slice end to end over HTTP as a signed-in Owner: the form pre-filled from the rota, a valid post
/// replacing every row atomically, an overlap refused with a localized message and nothing changed, "day off" removing
/// rows, another salon's id a 404, and the gates (Staff.Manage, sign-in).
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class EditWorkingHoursTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private static readonly IsoDayOfWeek[] Weekdays = [IsoDayOfWeek.Monday, IsoDayOfWeek.Tuesday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Thursday, IsoDayOfWeek.Friday];

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _tomId;
    private Guid _ewaId;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        _tomId = await AddStaffAsync(_studio.Id, "Tom Reed", Weekdays, [(9, 0, 13, 0), (14, 0, 17, 0)]);
        _ewaId = await AddStaffAsync(_polish.Id, "Ewa Nowak", Weekdays, [(9, 0, 17, 0)]);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_form_shows_the_current_rota_with_days_off_ticked()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var html = await GetFormAsync(client, _tomId);

        html.ShouldContain("data-testid=\"hours-form\"");
        html.ShouldContain("data-testid=\"staff-name\">Tom Reed<");
        html.ShouldContain("__RequestVerificationToken");
        html.ShouldContain("<legend class=\"px-1 font-semibold\">Monday</legend>");
        html.ShouldContain("Sunday</legend>");
        DayRowCount(html).ShouldBe(7);
        InputTag(html, "Days[0].Day").ShouldContain("value=\"Monday\"");
        InputTag(html, "Days[0].Ranges[0].Start").ShouldContain("value=\"09:00\"");
        InputTag(html, "Days[0].Ranges[0].End").ShouldContain("value=\"13:00\"");
        InputTag(html, "Days[0].Ranges[1].Start").ShouldContain("value=\"14:00\"");
        InputTag(html, "Days[0].Ranges[1].End").ShouldContain("value=\"17:00\"");
        InputTag(html, "Days[0].Ranges[0].Start").ShouldContain("type=\"time\"");
        InputTag(html, "Days[0].Ranges[0].Start").ShouldContain("step=\"300\"");
        InputTag(html, "Days[0].IsDayOff").ShouldNotContain("checked");
        InputTag(html, "Days[5].IsDayOff").ShouldContain("checked");
        InputTag(html, "Days[5].Ranges[0].Start").ShouldContain("value=\"\"");
        InputTag(html, "Days[6].IsDayOff").ShouldContain("checked");
    }

    [Fact]
    public async Task A_Polish_salon_shows_Polish_day_names_and_labels()
    {
        using var client = await OwnerClientAsync("salon-pl.localhost", _polish.Id);

        var html = await GetFormAsync(client, _ewaId);

        html.ShouldContain("<html lang=\"pl-PL\"");
        html.ShouldContain("Godziny pracy");
        html.ShouldContain("Poniedziałek</legend>");
        html.ShouldContain("Niedziela</legend>");
        html.ShouldContain("Dzień wolny");
        html.ShouldContain("Zapisz godziny pracy");
    }

    [Fact]
    public async Task A_valid_post_replaces_every_row_of_the_person_and_nobody_else()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);
        var before = await HoursAsync(_studio.Id);
        before.Count.ShouldBe(10);

        var response = await PostAsync(client, _tomId, Week(
            (IsoDayOfWeek.Monday, true, [("09:00", "13:00"), ("14:00", "17:00")]),
            (IsoDayOfWeek.Tuesday, false, [("10:00", "18:00"), ("", "")]),
            (IsoDayOfWeek.Wednesday, false, [("13:00", "15:00"), ("09:00", "12:00")])));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe("/admin/staff");

        var after = await HoursAsync(_studio.Id);
        after.ShouldAllBe(h => h.StaffMemberId == _tomId && h.TenantId == _studio.Id);
        after.Select(h => h.Id).Intersect(before.Select(h => h.Id)).ShouldBeEmpty("every old row is gone");
        after.OrderBy(h => h.SortOrder).Select(h => (h.Day, h.Start, h.End, h.SortOrder)).ShouldBe(
        [
            (IsoDayOfWeek.Tuesday, new LocalTime(10, 0), new LocalTime(18, 0), 1),
            (IsoDayOfWeek.Wednesday, new LocalTime(9, 0), new LocalTime(12, 0), 2),
            (IsoDayOfWeek.Wednesday, new LocalTime(13, 0), new LocalTime(15, 0), 3),
        ]);
        (await HoursAsync(_polish.Id)).Count.ShouldBe(5, "another salon's rota is untouched");

        var list = await client.GetAsync(response.Headers.Location!);
        var html = await list.Content.ReadAsStringAsync();
        html.ShouldContain("Working hours have been saved.");
        html.ShouldContain("Tue 10:00–18:00");
        html.ShouldContain("Wed 09:00–12:00, 13:00–15:00");
    }

    [Fact]
    public async Task An_overlap_is_refused_with_a_message_and_the_rota_stays_as_it_was()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);
        var before = await HoursAsync(_studio.Id);

        var response = await PostAsync(client, _tomId, Week((IsoDayOfWeek.Monday, false, [("09:00", "13:05"), ("13:00", "17:00")])));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("The ranges on this day overlap.");
        html.ShouldContain("data-testid=\"staff-name\">Tom Reed<");
        InputTag(html, "Days[0].Ranges[0].End").ShouldContain("value=\"13:05\"");
        (await HoursAsync(_studio.Id)).Select(h => h.Id).Order().ShouldBe(before.Select(h => h.Id).Order());
    }

    [Fact]
    public async Task Field_errors_come_in_the_salon_language_next_to_their_field()
    {
        using var client = await OwnerClientAsync("salon-pl.localhost", _polish.Id);

        var response = await PostAsync(client, _ewaId, Week(
            (IsoDayOfWeek.Monday, false, [("17:00", "09:00"), ("", "")]),
            (IsoDayOfWeek.Tuesday, false, [("09:03", ""), ("", "")])));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("Godzina zakończenia musi być późniejsza niż godzina rozpoczęcia.");
        html.ShouldContain("Godziny muszą być wielokrotnością 5 minut.");
        html.ShouldContain("Podaj godzinę zakończenia.");
        InputTag(html, "Days[0].Ranges[0].End").ShouldContain("input-validation-error");
        InputTag(html, "Days[1].Ranges[0].Start").ShouldContain("input-validation-error");
        InputTag(html, "Days[2].Ranges[0].Start").ShouldNotContain("input-validation-error");
        (await HoursAsync(_polish.Id)).Count.ShouldBe(5);
    }

    [Fact]
    public async Task Ticking_day_off_everywhere_removes_every_row()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var response = await PostAsync(client, _tomId, Week(WorkingHoursRules.Week.Select(d => (d, true, new[] { ("09:00", "17:00"), ("", "") })).ToArray()));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await HoursAsync(_studio.Id)).ShouldBeEmpty();
        var html = await (await client.GetAsync(new Uri("/admin/staff", UriKind.Relative))).Content.ReadAsStringAsync();
        html.ShouldContain("No working hours set");
    }

    [Fact]
    public async Task Another_salons_staff_id_and_an_unknown_id_are_not_found_on_both_verbs()
    {
        using var client = await OwnerClientAsync("salon-pl.localhost", _polish.Id);

        (await client.GetAsync(new Uri($"/admin/staff/{_tomId}/hours", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync(new Uri($"/admin/staff/{Guid.NewGuid()}/hours", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var fields = Week((IsoDayOfWeek.Monday, false, [("10:00", "11:00"), ("", "")]));
        fields["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(await GetFormAsync(client, _ewaId));
        var response = await client.PostAsync(new Uri($"/admin/staff/{_tomId}/hours", UriKind.Relative), new FormUrlEncodedContent(fields));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await HoursAsync(_studio.Id)).Count.ShouldBe(10);
    }

    [Fact]
    public async Task A_post_without_the_anti_forgery_token_is_refused()
    {
        using var client = await OwnerClientAsync("studio-20.localhost", _studio.Id);

        var response = await client.PostAsync(new Uri($"/admin/staff/{_tomId}/hours", UriKind.Relative), new FormUrlEncodedContent(Week()));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await HoursAsync(_studio.Id)).Count.ShouldBe(10);
    }

    [Fact]
    public async Task A_receptionist_is_denied_and_an_anonymous_visitor_is_asked_to_sign_in()
    {
        using var receptionist = await factory.SignedInClientAsync("studio-20.localhost", _studio.Id, Role.Receptionist);
        using var anonymous = factory.ClientFor("studio-20.localhost");

        var denied = await receptionist.GetAsync(new Uri($"/admin/staff/{_tomId}/hours", UriKind.Relative));
        denied.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");

        var login = await anonymous.GetAsync(new Uri($"/admin/staff/{_tomId}/hours", UriKind.Relative));
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        login.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");
    }

    /// <summary>The seven days as the form posts them: every day blank and not off unless overridden.</summary>
    private static Dictionary<string, string> Week(params (IsoDayOfWeek Day, bool Off, (string Start, string End)[] Ranges)[] overrides)
    {
        var fields = new Dictionary<string, string>();
        for (var i = 0; i < WorkingHoursRules.Week.Count; i++)
        {
            var day = WorkingHoursRules.Week[i];
            var match = overrides.FirstOrDefault(o => o.Day == day);
            var ranges = match.Ranges ?? [("", ""), ("", "")];
            fields[$"Days[{i}].Day"] = day.ToString();
            fields[$"Days[{i}].IsDayOff"] = match.Off ? "true" : "false";
            for (var j = 0; j < ranges.Length; j++)
            {
                fields[$"Days[{i}].Ranges[{j}].Start"] = ranges[j].Start;
                fields[$"Days[{i}].Ranges[{j}].End"] = ranges[j].End;
            }
        }

        return fields;
    }

    private static string InputTag(string html, string name)
    {
        var match = Regex.Match(html, $"<input[^>]*name=\"{Regex.Escape(name)}\"[^>]*>");
        match.Success.ShouldBeTrue($"no input named {name}");
        return match.Value;
    }

    private static int DayRowCount(string html) => DayRowPattern().Count(html);

    private static async Task<string> GetFormAsync(HttpClient client, Guid staffId)
    {
        var response = await client.GetAsync(new Uri($"/admin/staff/{staffId}/hours", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, Guid staffId, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(await GetFormAsync(client, staffId));
        return await client.PostAsync(new Uri($"/admin/staff/{staffId}/hours", UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    private Task<HttpClient> OwnerClientAsync(string host, Guid tenantId) => factory.SignedInClientAsync(host, tenantId, Role.Owner);

    private async Task<Guid> AddStaffAsync(Guid tenantId, string name, IsoDayOfWeek[] days, (int FromHour, int FromMinute, int ToHour, int ToMinute)[] ranges)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var staff = new StaffMember { DisplayName = name, CreatedAt = SystemClock.Instance.GetCurrentInstant() };
        db.StaffMembers.Add(staff);
        var sortOrder = 0;
        foreach (var day in days)
        {
            foreach (var (fromHour, fromMinute, toHour, toMinute) in ranges)
            {
                db.WorkingHours.Add(new WorkingHours { StaffMemberId = staff.Id, Day = day, Start = new LocalTime(fromHour, fromMinute), End = new LocalTime(toHour, toMinute), SortOrder = ++sortOrder });
            }
        }

        await db.SaveChangesAsync();
        return staff.Id;
    }

    private async Task<List<WorkingHours>> HoursAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().WorkingHours.AsNoTracking().ToListAsync();
    }

    [GeneratedRegex("data-testid=\"day-row\"")]
    private static partial Regex DayRowPattern();
}