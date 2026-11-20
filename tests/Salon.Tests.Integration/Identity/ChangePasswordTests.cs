using System.Net;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Identity;

/// <summary>
/// Changing your own password (ADR 0077): the current one proves it is you, the device in hand stays signed in,
/// every other session ends, and guessing at an unlocked screen counts toward the lockout exactly like a failed
/// sign-in. Any signed-in role may change their own; nobody anonymous gets the page.
/// </summary>
[Collection(SharedDatabase.Name)]
public class ChangePasswordTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string Path = "/account/password";
    private const string Host = "studio-20.localhost";
    private const string RitaEmail = "rita@studio-20.local";
    private const string RitaPassword = "Front-desk-9";

    private Tenant _studio = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        await factory.CreateUserAsync(_studio.Id, RitaEmail, Role.Receptionist, RitaPassword, "Rita Reception");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_person_changes_their_own_password_stays_signed_in_and_every_other_session_ends()
    {
        using var here = await SignedInAsync();
        using var elsewhere = await SignedInAsync();
        (await GetOkAsync(elsewhere, "/admin/appointments")).ShouldContain("Rita Reception");

        var response = await PostChangeAsync(here, RitaPassword, "Chair-three-9", "Chair-three-9");
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe(Path);

        // The device in hand stays signed in — the page itself admits only a signed-in user — and says what happened.
        (await GetOkAsync(here, Path)).ShouldContain("Your password has been changed.");

        // The other session carries the old security stamp and is signed out on its next request.
        (await elsewhere.GetAsync(new Uri("/admin/appointments", UriKind.Relative)))
            .Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");

        // The old password is gone; the new one signs in.
        using (var fresh = factory.ClientFor(Host))
        {
            (await IdentityTestClient.LoginAsync(fresh, RitaEmail, RitaPassword)).StatusCode.ShouldBe(HttpStatusCode.OK, "the old password is gone");
        }

        using (var fresh = factory.ClientFor(Host))
        {
            (await IdentityTestClient.LoginAsync(fresh, RitaEmail, "Chair-three-9")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }
    }

    [Fact]
    public async Task The_wrong_current_password_is_refused_and_five_in_a_row_lock_the_account()
    {
        using var rita = await SignedInAsync();

        var refused = await PostChangeAsync(rita, "Not-her-password-9", "Chair-three-9", "Chair-three-9");
        refused.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(refused)).ShouldContain("That is not your current password.");

        // Four more guesses: the fifth failure locks the account, exactly like five failed sign-ins (ADR 0009).
        for (var attempt = 2; attempt <= 4; attempt++)
        {
            (await PostChangeAsync(rita, "Not-her-password-9", "Chair-three-9", "Chair-three-9")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var locked = await PostChangeAsync(rita, "Not-her-password-9", "Chair-three-9", "Chair-three-9");
        (await ReadAsync(locked)).ShouldContain("Too many failed attempts.");

        // While locked, even the right current password changes nothing, and a fresh sign-in is refused too.
        var during = await PostChangeAsync(rita, RitaPassword, "Chair-three-9", "Chair-three-9");
        (await ReadAsync(during)).ShouldContain("Too many failed attempts.");
        using var fresh = factory.ClientFor(Host);
        (await IdentityTestClient.LoginAsync(fresh, RitaEmail, RitaPassword)).StatusCode.ShouldBe(HttpStatusCode.OK, "a locked account cannot sign in");
    }

    [Fact]
    public async Task A_mismatched_short_or_unchanged_new_password_is_refused_before_anything_happens()
    {
        using var rita = await SignedInAsync();

        (await ReadAsync(await PostChangeAsync(rita, RitaPassword, "Chair-three-9", "Chair-four-9")))
            .ShouldContain("The two new passwords do not match.");
        (await ReadAsync(await PostChangeAsync(rita, RitaPassword, "Short-7", "Short-7")))
            .ShouldContain("Use at least 8 characters.");
        (await ReadAsync(await PostChangeAsync(rita, RitaPassword, RitaPassword, RitaPassword)))
            .ShouldContain("The new password is the same as the current one.");

        // None of it touched the password.
        using var fresh = factory.ClientFor(Host);
        (await IdentityTestClient.LoginAsync(fresh, RitaEmail, RitaPassword)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Anonymous_is_sent_to_the_login_and_the_platform_domain_has_no_page()
    {
        using var anonymous = factory.ClientFor(Host);
        (await anonymous.GetAsync(new Uri(Path, UriKind.Relative)))
            .Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");

        using var platform = factory.ClientFor("localhost");
        (await platform.GetAsync(new Uri(Path, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<HttpClient> SignedInAsync()
    {
        var client = factory.ClientFor(Host);
        (await IdentityTestClient.LoginAsync(client, RitaEmail, RitaPassword)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return client;
    }

    /// <summary>The fields plus the anti-forgery token harvested from the page's own form.</summary>
    private static async Task<HttpResponseMessage> PostChangeAsync(HttpClient client, string current, string newPassword, string confirm)
    {
        var token = IdentityTestClient.AntiForgeryToken(await GetOkAsync(client, Path));
        return await client.PostAsync(new Uri(Path, UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["CurrentPassword"] = current,
            ["NewPassword"] = newPassword,
            ["ConfirmPassword"] = confirm,
            ["__RequestVerificationToken"] = token,
        }));
    }

    private static async Task<string> GetOkAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static async Task<string> ReadAsync(HttpResponseMessage response)
        => WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
}