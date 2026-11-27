using System.Net;
using Salon.Core.Identity;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Help;

/// <summary>
/// The guide for the salon's owner (ADR 0099): a short page of what to do first, in the salon's language, for anybody
/// signed in to the salon and nobody else, reached from the foot of the menu.
/// </summary>
[Collection(SharedDatabase.Name)]
public class GuideTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_guide_says_each_first_job_in_steps_with_a_picture_and_a_way_to_the_page()
    {
        var studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        using var client = await factory.SignedInClientAsync(HostA, studio.Id, Role.Owner);

        var response = await client.GetAsync(new Uri("/admin/guide", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        foreach (var title in new[] { "Today&#x27;s appointments", "Booking for a client", "Services and prices", "Your team", "What your clients see" })
        {
            html.ShouldContain(title);
        }

        html.ShouldNotContain(">Guide.", Case.Sensitive, "every word is there, not its key");
        html.ShouldContain("<img src=\"/img/guide/day.jpg\" alt=\"\"");
        html.ShouldContain("<a href=\"/admin/appointments\" class=\"btn btn-compact btn-secondary\" data-testid=\"guide-open\">");
        html.ShouldContain("<a href=\"/book\" class=\"btn btn-compact btn-secondary\" data-testid=\"guide-open\">");
        // The way in: the foot of the menu, on every page of the admin.
        html.ShouldContain("<a href=\"/admin/guide\" data-testid=\"nav-guide\" aria-current=\"true\" class=\"nav-link\">Guide</a>");
    }

    [Fact]
    public async Task The_guide_is_in_the_salons_language_and_only_for_people_signed_in()
    {
        var polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        using var owner = await factory.SignedInClientAsync(HostB, polish.Id, Role.Owner);

        var html = await (await owner.GetAsync(new Uri("/admin/guide", UriKind.Relative))).Content.ReadAsStringAsync();
        html.ShouldContain("Przewodnik");
        html.ShouldContain("Dzisiejsze wizyty");
        html.ShouldNotContain(">Guide.", Case.Sensitive);

        using var stranger = factory.ClientFor(HostB);
        var refused = await stranger.GetAsync(new Uri("/admin/guide", UriKind.Relative));
        refused.StatusCode.ShouldBe(HttpStatusCode.Redirect, "nobody reads the admin without signing in");
        refused.Headers.Location.ShouldNotBeNull().OriginalString.ShouldContain("/account/login");
    }
}