using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Services.Identity;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Identity;

/// <summary>
/// Team logins (ADR 0076): the owner sees who can sign in, adds a login with a starting password, changes a role or
/// a password, turns a login off and back on - and can never demote or turn off the salon's last active owner
/// login, so no salon locks itself out. Owner only; another salon's login looks like a missing one.
/// </summary>
[Collection(SharedDatabase.Name)]
public class TeamLoginsTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string ListPath = "/admin/settings/team";
    private const string HostA = "studio-20.localhost";
    private const string OwnerEmail = "owner@studio-20.local";

    private Tenant _studio = null!;
    private User _owner = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _owner = await factory.CreateUserAsync(_studio.Id, OwnerEmail, Role.Owner, displayName: "Olivia Owner");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_owner_sees_the_logins_adds_one_and_the_new_person_signs_in_with_their_own_reach()
    {
        using var owner = await SignedInOwnerAsync();
        var page = await GetOkAsync(owner, ListPath);
        page.ShouldContain("data-testid=\"team-login\"");
        page.ShouldContain("Olivia Owner");
        page.ShouldContain("· you");
        page.ShouldContain("Last signed in", customMessage: "the owner's own sign-in moments ago");

        var response = await owner.PostAsync(new Uri($"{ListPath}/new", UriKind.Relative), await FormAsync(owner, $"{ListPath}/new", new()
        {
            ["DisplayName"] = "Rita Reception",
            ["Email"] = "Rita@Studio-20.Local",
            ["Role"] = "Receptionist",
            ["Password"] = "Front-desk-9",
        }));
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var list = await GetOkAsync(owner, ListPath);
        list.ShouldContain("Rita Reception can sign in now.");
        list.ShouldContain("rita@studio-20.local");
        list.ShouldContain("Never signed in yet.", customMessage: "the new login has not been used");

        // The new login works, runs the diary, and cannot open the team page.
        using var rita = factory.ClientFor(HostA);
        (await IdentityTestClient.LoginAsync(rita, "rita@studio-20.local", "Front-desk-9")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await rita.GetAsync(new Uri("/admin/appointments", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await rita.GetAsync(new Uri(ListPath, UriKind.Relative))).Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
    }

    [Fact]
    public async Task A_login_is_changed_turned_off_and_turned_back_on()
    {
        var rita = await factory.CreateUserAsync(_studio.Id, "rita@studio-20.local", Role.Receptionist, "Front-desk-9", "Rita Reception");
        using var owner = await SignedInOwnerAsync();

        // A new role and a new password in one save.
        (await PostEditAsync(owner, rita.Id, "Manager", password: "Back-office-9", active: true)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        using (var asRita = factory.ClientFor(HostA))
        {
            (await IdentityTestClient.LoginAsync(asRita, rita.Email, "Front-desk-9")).StatusCode.ShouldBe(HttpStatusCode.OK, "the old password is gone");
        }

        using (var asRita = factory.ClientFor(HostA))
        {
            (await IdentityTestClient.LoginAsync(asRita, rita.Email, "Back-office-9")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        // Off: the sign-in is refused like a wrong password. On again: it works.
        (await PostEditAsync(owner, rita.Id, "Manager", password: null, active: false)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await GetOkAsync(owner, ListPath)).ShouldContain("data-active=\"false\"");
        using (var asRita = factory.ClientFor(HostA))
        {
            (await IdentityTestClient.LoginAsync(asRita, rita.Email, "Back-office-9")).StatusCode.ShouldBe(HttpStatusCode.OK, "a turned-off login cannot sign in");
        }

        (await PostEditAsync(owner, rita.Id, "Manager", password: null, active: true)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        using (var asRita = factory.ClientFor(HostA))
        {
            (await IdentityTestClient.LoginAsync(asRita, rita.Email, "Back-office-9")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }
    }

    [Fact]
    public async Task The_last_active_owner_login_can_be_neither_demoted_nor_turned_off()
    {
        using var owner = await SignedInOwnerAsync();

        (await GetOkAsync(owner, $"{ListPath}/{_owner.Id}")).ShouldContain("only owner login");
        var refused = await PostEditAsync(owner, _owner.Id, "Manager", password: null, active: true);
        refused.StatusCode.ShouldBe(HttpStatusCode.OK);
        (WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync())).ShouldContain("The salon must keep at least one active owner login.");

        // With a second owner in place, the first may step down.
        await factory.CreateUserAsync(_studio.Id, "second@studio-20.local", Role.Owner, displayName: "Second Owner");
        (await PostEditAsync(owner, _owner.Id, "Manager", password: null, active: true)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        await using var scope = factory.CreateTenantScope(_studio.Id);
        (await scope.ServiceProvider.GetRequiredService<IUserService>().FindByIdAsync(_owner.Id))!.Role.ShouldBe(Role.Manager);
    }

    [Fact]
    public async Task Only_the_owner_gets_in_and_another_salons_login_is_not_found()
    {
        var other = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        var theirs = await factory.CreateUserAsync(other.Id, "owner@salon-pl.local", Role.Owner, displayName: "Their Owner");

        using var owner = await SignedInOwnerAsync();
        (await PostEditAsync(owner, theirs.Id, "Staff", password: null, active: true)).StatusCode.ShouldBe(HttpStatusCode.NotFound, "another salon's login looks like a missing one");

        using var manager = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Manager, "manager@studio-20.test");
        (await manager.GetAsync(new Uri(ListPath, UriKind.Relative))).Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
    }

    private async Task<HttpClient> SignedInOwnerAsync()
    {
        var client = factory.ClientFor(HostA);
        (await IdentityTestClient.LoginAsync(client, OwnerEmail, IdentityTestClient.Password)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return client;
    }

    /// <summary>The token comes from the new-login page: the edit page of another salon's id would 404 before the POST could.</summary>
    private static async Task<HttpResponseMessage> PostEditAsync(HttpClient client, Guid userId, string role, string? password, bool active)
        => await client.PostAsync(new Uri($"{ListPath}/{userId}", UriKind.Relative), await FormAsync(client, $"{ListPath}/new", new()
        {
            ["UserId"] = userId.ToString(),
            ["Role"] = role,
            ["Password"] = password ?? string.Empty,
            ["IsActive"] = active ? "true" : "false",
        }));

    /// <summary>The fields plus the anti-forgery token harvested from a page that has a form.</summary>
    private static async Task<FormUrlEncodedContent> FormAsync(HttpClient client, string tokenPath, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(await GetOkAsync(client, tokenPath));
        return new FormUrlEncodedContent(fields);
    }

    private static async Task<string> GetOkAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}