namespace Salon.Tests.E2E.Infrastructure;

/// <summary>The URL globs the E2E tests wait on: one star stays inside a path segment, two cross them, the rest is literal.</summary>
public class PageNavigationExtensionsTests
{
    [Theory]
    [InlineData("**/admin/services", "http://studio-20.localhost:5000/admin/services", true)]
    [InlineData("**/admin/services", "http://studio-20.localhost:5000/admin/services/new", false)]
    [InlineData("**/admin/services/*", "http://studio-20.localhost:5000/admin/services/0192f3a1", true)]
    [InlineData("**/admin/services/*", "http://studio-20.localhost:5000/admin/services", false)]
    [InlineData("**/admin/services/*", "http://studio-20.localhost:5000/admin/services/0192f3a1/staff", false)]
    [InlineData("**/admin/services/*/staff", "http://studio-20.localhost:5000/admin/services/0192f3a1/staff", true)]
    [InlineData("**/details?*", "http://studio-20.localhost:5000/book/0192f3a1/details?start=2026-11-20T10%3A00%3A00Z", true)]
    [InlineData("**/details?*", "http://studio-20.localhost:5000/book/0192f3a1/detailsXstart=1", false)]
    [InlineData("**/admin/appointments?date=2026-11-20", "http://studio-20.localhost:5000/admin/appointments?date=2026-11-20", true)]
    [InlineData("**/admin/appointments/new?*q=Sop*", "http://studio-20.localhost:5000/admin/appointments/new?date=2026-11-20&q=Sop&x=1", true)]
    [InlineData("**/admin/appointments/calendar?view=week*", "http://studio-20.localhost:5000/admin/appointments/calendar?view=day", false)]
    [InlineData("http://studio-20.localhost:5000/admin/clients/0192f3a1", "http://studio-20.localhost:5000/admin/clients/0192f3a1", true)]
    [InlineData("http://studio-20.localhost:5000/admin/clients/0192f3a1", "http://studio-20.localhost:5000/admin/clients/0192f3a1/merge", false)]
    public void A_glob_matches_like_the_browser_helper_it_replaces(string glob, string url, bool expected)
    {
        PageNavigationExtensions.GlobToRegex(glob).IsMatch(url).ShouldBe(expected);
    }
}