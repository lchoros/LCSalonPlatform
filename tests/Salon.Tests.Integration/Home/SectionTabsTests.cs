using System.Net;
using System.Text.RegularExpressions;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Tests.Integration.Infrastructure;
using Salon.Web.Framework.Ui;

namespace Salon.Tests.Integration.Home;

/// <summary>
/// The pages of a module as one row of tabs (ADR 0089). A tab promises a page, so the row is held to the pages
/// themselves: for every role, a tab is drawn exactly when that role may open its page — never a tab onto a refusal,
/// never a page the row forgot — the page being shown is the one tab marked, and a role left with a single page of
/// a module sees no row at all.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class SectionTabsTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private Tenant _studio = null!;

    public static TheoryData<Role> Roles => [.. Enum.GetValues<Role>()];

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [MemberData(nameof(Roles))]
    public async Task A_role_sees_a_tab_exactly_when_it_may_open_the_page_behind_it(Role role)
    {
        using var client = await factory.SignedInClientAsync("studio-20.localhost", _studio.Id, role);
        var held = RolePermissions.For(role);

        foreach (var section in AdminSections.All)
        {
            var open = section.Tabs.Where(tab => tab.Permissions.All(held.Contains)).Select(tab => tab.Key).ToArray();

            foreach (var tab in section.Tabs)
            {
                using var response = await client.GetAsync(new Uri(tab.Url, UriKind.Relative));
                var may = open.Contains(tab.Key);

                // The row and the page must agree: this is what keeps a tab from leading a role onto a refusal.
                response.StatusCode.ShouldBe(may ? HttpStatusCode.OK : HttpStatusCode.Redirect, $"{role} at {tab.Url}");
                if (!may)
                {
                    response.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied", $"{role} at {tab.Url}");
                    continue;
                }

                var html = await response.Content.ReadAsStringAsync();
                if (open.Length < 2)
                {
                    html.ShouldNotContain($"data-testid=\"section-tabs-{section.Name}\"", customMessage: $"{role} at {tab.Url}: one page needs no tabs");
                    continue;
                }

                html.ShouldContain($"data-testid=\"section-tabs-{section.Name}\"", customMessage: $"{role} at {tab.Url}");
                TabPattern().Matches(html).Select(m => m.Groups[1].Value).ShouldBe(open, $"{role} at {tab.Url}");
                CurrentTabPattern().Matches(html).Select(m => m.Groups[1].Value).ShouldBe([tab.Key], $"{role} at {tab.Url}");
            }
        }
    }

    [Fact]
    public async Task The_tabs_carry_the_words_of_the_language_the_page_is_shown_in()
    {
        using var client = await factory.SignedInClientAsync("studio-20.localhost", _studio.Id, Role.Owner);

        var english = await client.GetStringAsync(new Uri("/admin/appointments", UriKind.Relative));

        english.ShouldContain("data-testid=\"section-tab-waitlist\" class=\"section-tab\">Waitlist</a>");
        english.ShouldContain("data-testid=\"section-tab-day\" aria-current=\"page\" class=\"section-tab\">Day list</a>");
        english.ShouldContain("<nav aria-label=\"Appointments: pages\" data-testid=\"section-tabs-appointments\"");
    }

    [GeneratedRegex("data-testid=\"section-tab-([a-z-]+)\"")]
    private static partial Regex TabPattern();

    [GeneratedRegex("data-testid=\"section-tab-([a-z-]+)\" aria-current=\"page\"")]
    private static partial Regex CurrentTabPattern();
}