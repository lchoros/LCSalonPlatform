using System.Net;

using Microsoft.Extensions.DependencyInjection;

using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Services.Identity;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Identity;

/// <summary>
/// [RequirePermission] and the cookie's binding to the user and the tenant (ADR 0009), seen through the CreateService
/// admin screen: anonymous → login, Receptionist → denied, Owner → in; a cookie from salon A is nothing on salon B;
/// deactivation and a role change end the session on the next request.
/// </summary>
[Collection(SharedDatabase.Name)]
public class PermissionTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string FormPath = "/admin/services/new";
    private const string HostA = "salon-a.localhost";
    private const string HostB = "salon-b.localhost";

    private Tenant _salonA = null!;
    private Tenant _salonB = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _salonA = await factory.CreateTenantAsync("salon-a", name: "Salon A");
        _salonB = await factory.CreateTenantAsync("salon-b", name: "Salon B");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Anonymous_admin_request_is_redirected_to_login_with_the_return_url()
    {
        using var client = factory.ClientFor(HostA);

        var response = await client.GetAsync(new Uri(FormPath, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location.ShouldNotBeNull();
        location.IsAbsoluteUri.ShouldBeTrue();
        location.Host.ShouldBe(HostA);
        location.AbsolutePath.ShouldBe("/account/login");
        location.Query.ShouldBe("?returnUrl=%2Fadmin%2Fservices%2Fnew");
    }

    [Fact]
    public async Task Anonymous_non_html_request_gets_401_instead_of_a_redirect()
    {
        using var client = factory.ClientFor(HostA);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        (await client.GetAsync(new Uri(FormPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task On_the_platform_domain_the_admin_screen_does_not_exist()
    {
        using var client = factory.ClientFor("localhost");

        (await client.GetAsync(new Uri(FormPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_receptionist_is_denied_with_a_localized_403_page()
    {
        using var client = await factory.SignedInClientAsync(HostA, _salonA.Id, Role.Receptionist);

        var response = await client.GetAsync(new Uri(FormPath, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location.ShouldNotBeNull();
        location.AbsolutePath.ShouldBe("/account/denied");

        var denied = await client.GetAsync(location);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync()).ShouldContain("Access denied");
    }

    [Fact]
    public async Task A_receptionist_asking_for_json_gets_a_bare_403()
    {
        using var client = await factory.SignedInClientAsync(HostA, _salonA.Id, Role.Receptionist);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        (await client.GetAsync(new Uri(FormPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_owner_opens_the_form_and_creates_a_service()
    {
        using var client = await factory.SignedInClientAsync(HostA, _salonA.Id, Role.Owner);

        var form = await client.GetAsync(new Uri(FormPath, UriKind.Relative));
        form.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await form.Content.ReadAsStringAsync();
        var response = await client.PostAsync(new Uri(FormPath, UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "Owner's cut",
            ["DurationMinutes"] = "30",
            ["Price"] = "25",
            ["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(html),
        }));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe("/admin/services");
    }

    [Fact]
    public async Task A_cookie_from_salon_A_is_anonymous_on_salon_B_and_gets_cleared()
    {
        await factory.CreateUserAsync(_salonA.Id, $"owner@{HostA}", Role.Owner);
        using var clientA = factory.ClientFor(HostA);
        var cookie = IdentityTestClient.AuthCookie(await IdentityTestClient.LoginAsync(clientA, $"owner@{HostA}", IdentityTestClient.Password)).ShouldNotBeNull();
        using var clientB = factory.ClientFor(HostB, handleCookies: false);
        clientB.DefaultRequestHeaders.Add("Cookie", cookie);

        var response = await clientB.GetAsync(new Uri(FormPath, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");
        response.Headers.GetValues("Set-Cookie").ShouldContain(v => v.StartsWith("salon.auth=;", StringComparison.Ordinal));

        var home = await clientB.GetAsync(new Uri("/", UriKind.Relative));
        (await home.Content.ReadAsStringAsync()).ShouldNotContain("data-testid=\"user-name\"");
    }

    [Fact]
    public async Task A_deactivated_user_is_anonymous_on_the_next_request()
    {
        using var client = await factory.SignedInClientAsync(HostA, _salonA.Id, Role.Owner);
        (await client.GetAsync(new Uri(FormPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using (var scope = factory.CreateTenantScope(_salonA.Id))
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserService>();
            var user = (await users.FindByEmailAsync($"owner@{HostA}")).ShouldNotBeNull();
            await users.DeactivateAsync(user.Id);
        }

        var response = await client.GetAsync(new Uri(FormPath, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");
        (await IdentityTestClient.LoginAsync(client, $"owner@{HostA}", IdentityTestClient.Password)).StatusCode.ShouldBe(HttpStatusCode.OK, "a deactivated user must not be able to sign in again");
    }

    [Fact]
    public async Task A_role_change_ends_the_session_and_the_next_sign_in_carries_the_new_permissions()
    {
        using var client = await factory.SignedInClientAsync(HostA, _salonA.Id, Role.Owner);
        (await client.GetAsync(new Uri(FormPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using (var scope = factory.CreateTenantScope(_salonA.Id))
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserService>();
            var user = (await users.FindByEmailAsync($"owner@{HostA}")).ShouldNotBeNull();
            await users.SetRoleAsync(user.Id, Role.Receptionist);
        }

        var afterChange = await client.GetAsync(new Uri(FormPath, UriKind.Relative));
        afterChange.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        afterChange.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");

        (await IdentityTestClient.LoginAsync(client, $"owner@{HostA}", IdentityTestClient.Password)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var asReceptionist = await client.GetAsync(new Uri(FormPath, UriKind.Relative));
        asReceptionist.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        asReceptionist.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
    }

    [Fact]
    public async Task The_same_email_can_exist_in_two_salons_and_each_cookie_only_works_on_its_own_host()
    {
        const string email = "shared@example.com";
        using var clientA = await factory.SignedInClientAsync(HostA, _salonA.Id, Role.Owner, email);
        using var clientB = await factory.SignedInClientAsync(HostB, _salonB.Id, Role.Staff, email);

        (await clientA.GetAsync(new Uri(FormPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var staff = await clientB.GetAsync(new Uri(FormPath, UriKind.Relative));
        staff.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        staff.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
    }
}