using System.Net;

using Microsoft.Extensions.DependencyInjection;

using Salon.Core.Identity;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Services.Settings;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Settings;

/// <summary>The reminder settings page (ADR 0020): owner only, defaults shown, saved per salon, rules in the salon's language.</summary>
[Collection(SharedDatabase.Name)]
public class NotificationSettingsPageTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string Path = "/admin/settings/notifications";
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
    public async Task The_owner_sees_the_defaults_and_a_link_in_the_admin_nav()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var html = await GetAsync(client);

        html.ShouldContain("data-testid=\"notification-settings-form\"");
        html.ShouldContain("data-testid=\"nav-settings\"");
        Input(html, "RemindersEnabled").ShouldContain("checked");
        Input(html, "FirstReminderHours").ShouldContain("value=\"24\"");
        Input(html, "SecondReminderHours").ShouldContain("value=\"2\"");
        Input(html, "QuietFrom").ShouldContain("value=\"21:00\"");
        Input(html, "QuietTo").ShouldContain("value=\"08:00\"");
        html.ShouldContain("__RequestVerificationToken");
    }

    [Fact]
    public async Task Saving_stores_the_settings_for_this_salon_only_and_confirms()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var response = await PostAsync(client, new()
        {
            ["FirstReminderHours"] = "48",
            ["SecondReminderHours"] = "3",
            ["QuietFrom"] = "20:30",
            ["QuietTo"] = "09:00",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe(Path);
        var after = await GetAsync(client);
        after.ShouldContain("Reminder settings have been saved.");
        Input(after, "RemindersEnabled").ShouldNotContain("checked", Case.Sensitive, "the box was left unticked");
        Input(after, "FirstReminderHours").ShouldContain("value=\"48\"");

        var saved = await LoadAsync(_studio.Id);
        saved.RemindersEnabled.ShouldBeFalse();
        saved.FirstReminderHours.ShouldBe(48);
        saved.SecondReminderHours.ShouldBe(3);
        saved.QuietHoursStartMinute.ShouldBe((20 * 60) + 30);
        saved.QuietHoursEndMinute.ShouldBe(9 * 60);
        var other = await LoadAsync(_polish.Id);
        other.RemindersEnabled.ShouldBeTrue("the other salon keeps its defaults");
        other.FirstReminderHours.ShouldBe(24);
    }

    [Theory]
    [InlineData("", "2", "21:00", "08:00", "Enter a number of hours.")]
    [InlineData("200", "2", "21:00", "08:00", "Hours must be between 0 and 168.")]
    [InlineData("24", "24", "21:00", "08:00", "The second reminder must be closer to the visit than the first.")]
    [InlineData("2", "24", "21:00", "08:00", "The second reminder must be closer to the visit than the first.")]
    [InlineData("24", "2", "25:00", "08:00", "Enter a time such as 21:00.")]
    [InlineData("24", "2", "21:00", "", "Enter a time such as 21:00.")]
    public async Task Invalid_input_re_renders_with_the_message_and_saves_nothing(string first, string second, string from, string to, string message)
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var response = await PostAsync(client, new()
        {
            ["RemindersEnabled"] = "true",
            ["FirstReminderHours"] = first,
            ["SecondReminderHours"] = second,
            ["QuietFrom"] = from,
            ["QuietTo"] = to,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()).ShouldContain(message);
        (await LoadAsync(_studio.Id)).FirstReminderHours.ShouldBe(24);
    }

    [Fact]
    public async Task Either_reminder_can_be_switched_off_with_zero_and_quiet_hours_with_equal_times()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var response = await PostAsync(client, new()
        {
            ["RemindersEnabled"] = "true",
            ["FirstReminderHours"] = "0",
            ["SecondReminderHours"] = "2",
            ["QuietFrom"] = "00:00",
            ["QuietTo"] = "00:00",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var saved = await LoadAsync(_studio.Id);
        saved.FirstReminderHours.ShouldBe(0);
        saved.SecondReminderHours.ShouldBe(2);
        saved.QuietHoursStartMinute.ShouldBe(saved.QuietHoursEndMinute);
    }

    [Fact]
    public async Task The_page_speaks_the_salon_language()
    {
        using var client = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);

        var html = await GetAsync(client);

        html.ShouldContain("Przypomnienia");
        html.ShouldContain("Cisza nocna");
    }

    [Theory]
    [InlineData(Role.Manager)]
    [InlineData(Role.Receptionist)]
    public async Task Only_the_owner_may_open_or_post_the_page(Role role)
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner, "owner@studio-20.test");
        var token = IdentityTestClient.AntiForgeryToken(await GetAsync(owner));
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, role, $"{role}@studio-20.test");

        var get = await client.GetAsync(new Uri(Path, UriKind.Relative));
        var post = await client.PostAsync(new Uri(Path, UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FirstReminderHours"] = "1",
            ["SecondReminderHours"] = "0",
            ["QuietFrom"] = "21:00",
            ["QuietTo"] = "08:00",
            ["__RequestVerificationToken"] = token,
        }));

        // A signed-in role without the permission is sent to the access-denied page (ADR 0009), for reads and writes alike.
        get.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        get.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        post.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        post.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        (await LoadAsync(_studio.Id)).FirstReminderHours.ShouldBe(24);

        // And the nav does not offer what the role cannot open.
        var home = await client.GetAsync(new Uri("/admin/appointments", UriKind.Relative));
        (await home.Content.ReadAsStringAsync()).ShouldNotContain("data-testid=\"nav-settings\"");
    }

    [Fact]
    public async Task A_post_without_the_anti_forgery_token_is_refused()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var response = await client.PostAsync(new Uri(Path, UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FirstReminderHours"] = "1",
            ["SecondReminderHours"] = "0",
            ["QuietFrom"] = "21:00",
            ["QuietTo"] = "08:00",
        }));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await LoadAsync(_studio.Id)).FirstReminderHours.ShouldBe(24);
    }

    private static async Task<string> GetAsync(HttpClient client)
    {
        var response = await client.GetAsync(new Uri(Path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(await GetAsync(client));
        return await client.PostAsync(new Uri(Path, UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    /// <summary>The whole input tag with this id, so an assertion reads one field and not its neighbour.</summary>
    private static string Input(string html, string id)
    {
        var at = html.IndexOf($"id=\"{id}\"", StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, $"no input with id {id}");
        var open = html.LastIndexOf('<', at);
        var close = html.IndexOf('>', at);
        return html[open..(close + 1)];
    }

    private async Task<NotificationSettings> LoadAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<ISettingService>().LoadAsync<NotificationSettings>();
    }
}