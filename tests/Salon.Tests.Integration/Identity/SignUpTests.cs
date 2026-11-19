using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Salon.Data;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Identity;

/// <summary>
/// Public sign-up (ADR 0075): a salon and its owner's login in one form on the bare base domain, behind
/// <c>Features:SignUp</c>. The slug and the email are normalized, the country sets zone, language and currency, a
/// taken or reserved address is refused with nothing created, and the owner can sign in at the new salon's own
/// address straight away. On a salon's subdomain, or with the flag off, the page does not exist.
/// </summary>
[Collection(SharedDatabase.Name)]
public class SignUpTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string BaseHost = "localhost";
    private const string Path = "/signup";

    public async Task InitializeAsync() => await factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_salon_and_its_owner_are_made_in_one_step_and_the_owner_signs_in_at_its_own_address()
    {
        using var client = factory.ClientFor(BaseHost);
        var form = await GetOkAsync(client, Path);
        form.ShouldContain("data-testid=\"signup-form\"");

        var response = await PostAsync(client, new()
        {
            ["SalonName"] = "Pilot Salon",
            ["Slug"] = " Pilot-Salon ",
            ["Country"] = "England",
            ["DisplayName"] = "Pat Owner",
            ["Email"] = "Owner@Pilot-Salon.Local",
            ["Password"] = "Correct-horse-9",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe("/signup/done?salon=pilot-salon");
        var done = await GetOkAsync(client, response.Headers.Location!.OriginalString);
        done.ShouldContain("Pilot Salon is ready");
        done.ShouldContain("href=\"https://pilot-salon.localhost/admin/settings/getting-started\"");

        var tenant = await TenantAsync("pilot-salon");
        (tenant.Name, tenant.TimeZoneId, tenant.Culture, tenant.Currency, tenant.IsActive).ShouldBe(("Pilot Salon", "Europe/London", "en-GB", "GBP", true));
        await using (var scope = factory.CreateTenantScope(tenant.Id))
        {
            var owner = (await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Users.SingleAsync());
            (owner.Email, owner.DisplayName, owner.Role, owner.TenantId).ShouldBe(("owner@pilot-salon.local", "Pat Owner", Salon.Core.Identity.Role.Owner, tenant.Id));
        }

        // The owner signs in at the salon's own address with what they just chose, and lands on getting started.
        using var salon = factory.ClientFor("pilot-salon.localhost");
        var login = await IdentityTestClient.LoginAsync(salon, "owner@pilot-salon.local", "Correct-horse-9", "/admin/settings/getting-started");
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await GetOkAsync(salon, "/admin/settings/getting-started")).ShouldContain("1 of 8 steps done.");
    }

    [Fact]
    public async Task Poland_sets_the_zone_the_language_and_the_currency()
    {
        using var client = factory.ClientFor(BaseHost);

        (await PostAsync(client, Fields("salon-pl", country: "Poland"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var tenant = await TenantAsync("salon-pl");
        (tenant.TimeZoneId, tenant.Culture, tenant.Currency).ShouldBe(("Europe/Warsaw", "pl-PL", "PLN"));
    }

    [Fact]
    public async Task A_taken_or_reserved_address_or_a_weak_password_is_refused_and_nothing_is_created()
    {
        await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        using var client = factory.ClientFor(BaseHost);

        var taken = await PostAsync(client, Fields("Studio-20"));
        taken.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await taken.Content.ReadAsStringAsync()).ShouldContain("That web address is taken. Pick another.");

        var reserved = await PostAsync(client, Fields("admin"));
        (await reserved.Content.ReadAsStringAsync()).ShouldContain("That web address is taken. Pick another.");

        var malformed = await PostAsync(client, Fields("x!"));
        (await malformed.Content.ReadAsStringAsync()).ShouldContain("letters, digits and hyphens");

        var weak = await PostAsync(client, Fields("fine-salon", password: "short"));
        (await weak.Content.ReadAsStringAsync()).ShouldContain("Use at least 8 characters.");

        await using var scope = factory.CreateTenantScope(null);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        (await db.Tenants.CountAsync()).ShouldBe(1, "only the salon that was there already");
        (await db.Users.IgnoreQueryFilters().CountAsync()).ShouldBe(0, "no owner was written for a refused form");
    }

    [Fact]
    public async Task On_a_salons_own_address_or_with_the_flag_off_the_page_does_not_exist()
    {
        await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        using (var salon = factory.ClientFor("studio-20.localhost"))
        {
            (await salon.GetAsync(new Uri(Path, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using var derived = factory.WithWebHostBuilder(b => b.UseSetting("Features:SignUp", "false"));
        using var off = derived.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri($"https://{BaseHost}"),
        });
        (await off.GetAsync(new Uri(Path, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static Dictionary<string, string> Fields(string slug, string country = "England", string password = "Correct-horse-9") => new()
    {
        ["SalonName"] = "A Salon",
        ["Slug"] = slug,
        ["Country"] = country,
        ["DisplayName"] = "Pat Owner",
        ["Email"] = $"owner@{Guid.NewGuid():N}.local",
        ["Password"] = password,
    };

    private async Task<Salon.Core.Tenancy.Tenant> TenantAsync(string slug)
    {
        await using var scope = factory.CreateTenantScope(null);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Tenants.SingleAsync(t => t.Slug == slug);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(await GetOkAsync(client, Path));
        return await client.PostAsync(new Uri(Path, UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    private static async Task<string> GetOkAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}