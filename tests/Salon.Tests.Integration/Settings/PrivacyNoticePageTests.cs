using System.Net;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Identity;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Services.Privacy;
using Salon.Services.Settings;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Settings;

/// <summary>
/// The privacy notice settings page (ADR 0063): the owner chooses between the standard notice and their own text.
/// Owner only; the standard notice to begin with; "use my own" needs a text; the text is kept while the standard
/// notice is shown; saved per salon; the page says the standard notice is not legal advice.
/// </summary>
[Collection(SharedDatabase.Name)]
public class PrivacyNoticePageTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string Path = "/admin/settings/privacy";
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
    public async Task The_owner_sees_the_standard_notice_chosen_and_is_told_it_is_not_legal_advice()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var page = await GetAsync(owner, Path);

        page.ShouldContain("data-testid=\"privacy-form\"");
        page.ShouldNotContain("data-testid=\"use-own-text\" checked");
        page.ShouldContain("It is a starting text, not legal advice.");
        page.ShouldContain("href=\"/privacy\"");
        page.ShouldContain("href=\"/admin/settings/privacy\" data-testid=\"settings-nav-privacy\" aria-current=\"page\"");
        (await NoticeAsync(_studio.Id)).ShouldSatisfyAllConditions(
            notice => notice.IsOwnText.ShouldBeFalse(),
            notice => notice.UpdatedOn.ShouldBe(PrivacyNoticeRules.StandardTextDate));
    }

    [Fact]
    public async Task Saving_the_owners_text_shows_it_to_clients_of_this_salon_only_and_moves_the_date()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var page = await GetAsync(owner, Path);

        var saved = await PostAsync(owner, page, ("UseOwnText", "true"), ("OwnText", "  Our own notice.\n\nSecond paragraph.  "));

        saved.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        saved.Headers.Location!.OriginalString.ShouldBe(Path);
        var after = await GetAsync(owner, Path);
        after.ShouldContain("The privacy notice is saved.");
        after.ShouldContain("Our own notice.");
        var notice = await NoticeAsync(_studio.Id);
        notice.OwnParagraphs.ShouldBe(["Our own notice.", "Second paragraph."]);
        notice.UpdatedOn.ShouldBe(SystemClock.Instance.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb["Europe/London"]).Date);
        (await NoticeAsync(_polish.Id)).IsOwnText.ShouldBeFalse();

        // Unticking shows the standard notice again; the text stays in the box for later.
        (await PostAsync(owner, after, ("OwnText", "Our own notice.\n\nSecond paragraph."))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await NoticeAsync(_studio.Id)).IsOwnText.ShouldBeFalse();
        (await GetAsync(owner, Path)).ShouldContain("Our own notice.");
    }

    [Fact]
    public async Task Using_own_text_needs_a_text_that_fits()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var page = await GetAsync(owner, Path);

        var empty = await PostAsync(owner, page, ("UseOwnText", "true"), ("OwnText", "   "));
        empty.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await empty.Content.ReadAsStringAsync()).ShouldContain("Write your own text, or untick the box to show the standard notice.");

        var tooLong = await PostAsync(owner, page, ("UseOwnText", "true"), ("OwnText", new string('a', PrivacyNoticeRules.OwnTextMaxLength + 1)));
        tooLong.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await tooLong.Content.ReadAsStringAsync()).ShouldContain("Use at most 20000 characters.");

        (await NoticeAsync(_studio.Id)).IsOwnText.ShouldBeFalse();
        await using var scope = factory.CreateTenantScope(_studio.Id);
        (await scope.ServiceProvider.GetRequiredService<ISettingService>().LoadAsync<PrivacyNoticeSettings>()).UpdatedOn.ShouldBeNull("a refused save changes nothing");
    }

    [Fact]
    public async Task Only_the_owner_reaches_the_page_and_a_post_needs_the_token()
    {
        foreach (var role in new[] { Role.Manager, Role.Receptionist, Role.Staff })
        {
            using var other = await factory.SignedInClientAsync(HostA, _studio.Id, role);
            var denied = await other.GetAsync(new Uri(Path, UriKind.Relative));
            denied.StatusCode.ShouldBe(HttpStatusCode.Redirect, role.ToString());
            denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        }

        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var noToken = await owner.PostAsync(new Uri(Path, UriKind.Relative), new FormUrlEncodedContent([new("UseOwnText", "true"), new("OwnText", "Sneaked in")]));
        noToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await NoticeAsync(_studio.Id)).IsOwnText.ShouldBeFalse();
    }

    [Fact]
    public async Task The_page_speaks_the_salon_language()
    {
        using var owner = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);

        var page = await GetAsync(owner, Path);

        page.ShouldContain("Informacja o prywatności");
        page.ShouldContain("a nie porada prawna");
        page.ShouldNotContain("Privacy notice");
    }

    private async Task<PrivacyNotice> NoticeAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<IPrivacyNoticeService>().GetAsync();
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string pageWithToken, params (string Key, string Value)[] fields)
        => client.PostAsync(new Uri(Path, UriKind.Relative), new FormUrlEncodedContent(
            [new KeyValuePair<string, string>("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(pageWithToken)), .. fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))]));

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}