using NodaTime;
using Salon.Core.Appointments;

namespace Salon.Tests.Unit.Appointments;

/// <summary>
/// ADR 0056: whether a freed time is one a waiting client asked for, and whether they may be sent a text about it
/// now. Pure rules.
/// </summary>
public class WaitlistOfferRulesTests
{
    private static readonly LocalDate Day = new(2026, 11, 19);
    private static readonly Guid Anna = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Tom = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Instant Now = Instant.FromUtc(2026, 11, 16, 12, 0);

    [Fact]
    public void Somebody_with_no_days_and_no_person_in_mind_wants_any_freed_time()
    {
        WaitlistRules.Wants(null, null, null, Day, Anna).ShouldBeTrue();
    }

    [Theory]
    [InlineData(-1, null, true)]
    [InlineData(0, null, true)]
    [InlineData(1, null, false)]
    [InlineData(null, 1, true)]
    [InlineData(null, 0, true)]
    [InlineData(null, -1, false)]
    [InlineData(0, 0, true)]
    public void The_day_must_be_inside_the_days_they_gave(int? notBeforeOffset, int? notAfterOffset, bool wants)
    {
        var notBefore = notBeforeOffset is { } before ? Day.PlusDays(before) : (LocalDate?)null;
        var notAfter = notAfterOffset is { } after ? Day.PlusDays(after) : (LocalDate?)null;

        WaitlistRules.Wants(notBefore, notAfter, null, Day, Anna).ShouldBe(wants);
    }

    [Fact]
    public void Somebody_who_named_a_person_wants_only_that_persons_time()
    {
        WaitlistRules.Wants(null, null, Anna, Day, Anna).ShouldBeTrue();
        WaitlistRules.Wants(null, null, Tom, Day, Anna).ShouldBeFalse();
    }

    [Fact]
    public void A_client_is_told_at_most_once_in_a_day()
    {
        WaitlistRules.CanBeTold(null, Now).ShouldBeTrue("never told");
        WaitlistRules.CanBeTold(Now - Duration.FromHours(1), Now).ShouldBeFalse();
        WaitlistRules.CanBeTold(Now - Duration.FromHours(23), Now).ShouldBeFalse();
        WaitlistRules.CanBeTold(Now - Duration.FromHours(24), Now).ShouldBeTrue();
    }
}