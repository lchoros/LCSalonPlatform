using NodaTime;

namespace Salon.Tests.E2E.Infrastructure;

/// <summary>
/// The fixture's own promise: a test that asks for "a visit so far from now" gets one whatever else is in the
/// diary at that time. The demo data books its own visits into the first free times from now on, so without this a
/// browser test passed or failed by the hour it ran.
/// </summary>
public class KestrelAppFixtureTests(KestrelAppFixture app) : IClassFixture<KestrelAppFixture>
{
    private const string Slug = "studio-20";

    [Theory]
    [InlineData(26 * 60)]
    [InlineData(-20)]
    public async Task Two_visits_asked_for_the_same_time_are_both_booked_one_after_the_other(int minutesFromNow)
    {
        var tenantId = await app.EnsureTenantAsync(Slug, "Studio 20", [("Cut & blow-dry", 45, 45m)]);
        await app.EnsureDemoStaffAsync(tenantId);
        var fromNow = Duration.FromMinutes(minutesFromNow);

        // The same person, the same moment: the second one would break the rule that a person is in one place at a time.
        var first = await app.AddVisitAsync(tenantId, "First", "Asked", fromNow, deposit: 0m);
        var second = await app.AddVisitAsync(tenantId, "Second", "Asked", fromNow, deposit: 0m);
        var third = await app.AddVisitAsync(tenantId, "Third", "Asked", fromNow, deposit: 0m);

        new[] { first.AppointmentId, second.AppointmentId, third.AppointmentId }.Distinct().Count().ShouldBe(3);
    }
}