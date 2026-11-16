using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Salon.Core.Identity;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Services.Settings;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Settings;

/// <summary>
/// The "Daily summary" settings page (ADR 0050): the owner switches the day's email on and sets its time. Owner only,
/// off and 20:00 by default, saved per salon, in the salon's language, with what today's email would say on the page.
/// </summary>
[Collection(SharedDatabase.Name)]
public class DailySummaryPageTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string Path = "/admin/settings/daily-summary";
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
    public async Task The_owner_sees_it_off_at_eight_in_the_evening_with_what_todays_email_would_say()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var html = await GetAsync(client, Path);

        html.ShouldContain("Daily summary by email");
        html.ShouldContain("No client is named.");
        html.ShouldNotContain("checked=\"checked\"", Case.Sensitive, "off until the owner switches it on");
        html.ShouldContain("type=\"time\" value=\"20:00\"");
        html.ShouldContain("data-testid=\"summary-recipients\">It goes to the 1 owner login of this salon.<");
        html.ShouldContain("data-testid=\"summary-subject\">Studio 20: £0.00 taken on ");
        html.ShouldContain("No money was recorded.");
        html.ShouldContain("Nothing is booked for tomorrow.");
        html.ShouldContain("href=\"/admin/settings/daily-summary\" data-testid=\"settings-nav-daily-summary\" aria-current=\"page\"");
        (await GetAsync(client, "/admin/settings/notifications")).ShouldContain("href=\"/admin/settings/daily-summary\" data-testid=\"settings-nav-daily-summary\"");
    }

    [Fact]
    public async Task Saving_switches_it_on_at_the_chosen_time_for_this_salon_only_and_confirms()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var response = await PostAsync(client, enabled: true, "07:30");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe(Path);
        var after = await GetAsync(client, Path);
        after.ShouldContain("The daily summary settings have been saved.");
        after.ShouldContain("type=\"time\" value=\"07:30\"");
        after.ShouldContain("checked=\"checked\"");
        var saved = await SettingsAsync(_studio.Id);
        (saved.Enabled, saved.SendAtMinute).ShouldBe((true, 450));
        var other = await SettingsAsync(_polish.Id);
        (other.Enabled, other.SendAtMinute).ShouldBe((false, 1200));

        // An unticked box is not posted: saving without it switches the summary off and keeps the time.
        (await PostAsync(client, enabled: false, "07:30")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var off = await SettingsAsync(_studio.Id);
        (off.Enabled, off.SendAtMinute).ShouldBe((false, 450));
    }

    [Theory]
    [InlineData("")]
    [InlineData("25:00")]
    [InlineData("eight")]
    public async Task A_time_that_is_not_a_time_is_refused_and_nothing_is_saved(string time)
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var response = await PostAsync(client, enabled: true, time);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        html.ShouldContain("data-testid=\"send-at-error\">Enter a time such as 20:00.<");
        html.ShouldContain("aria-invalid=\"true\"");
        html.ShouldContain("data-testid=\"summary-subject\">Studio 20: ", Case.Sensitive, "the preview is still on the page");
        (await SettingsAsync(_studio.Id)).Enabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Only_the_owner_gets_in_and_the_anti_forgery_token_is_needed()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        using var manager = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Manager);
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        using var anonymous = factory.ClientFor(HostA);

        foreach (var refused in new[] { manager, desk })
        {
            var denied = await refused.GetAsync(new Uri(Path, UriKind.Relative));
            denied.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        }

        var login = await anonymous.GetAsync(new Uri(Path, UriKind.Relative));
        login.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");
        (await GetAsync(manager, "/admin/settings/messages")).ShouldNotContain("settings-nav-daily-summary");

        var noToken = await owner.PostAsync(new Uri(Path, UriKind.Relative), new FormUrlEncodedContent([new("Enabled", "true"), new("SendAt", "07:30")]));
        noToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SettingsAsync(_studio.Id)).Enabled.ShouldBeFalse();
    }

    [Fact]
    public async Task A_Polish_salon_reads_the_page_and_the_preview_in_Polish()
    {
        using var client = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);

        var html = await GetAsync(client, Path);

        html.ShouldContain("Podsumowanie dnia e-mailem");
        html.ShouldContain("Godzina wysyłki");
        html.ShouldContain("data-testid=\"summary-subject\">Salon PL: utarg 0,00");
        html.ShouldContain("Nie zapisano żadnych wpłat.");
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, bool enabled, string sendAt)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(await GetAsync(client, Path))),
            new("SendAt", sendAt),
        };
        if (enabled)
        {
            fields.Add(new("Enabled", "true"));
        }

        return await client.PostAsync(new Uri(Path, UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    private async Task<DailySummarySettings> SettingsAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<ISettingService>().LoadAsync<DailySummarySettings>();
    }
}