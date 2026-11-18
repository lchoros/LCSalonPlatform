using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using NodaTime;
using Salon.Core.Appointments;

namespace Salon.Tests.Unit.Appointments;

/// <summary>For how long a freed time is kept for a waiting client, and whether it still is (ADR 0065).</summary>
public class WaitlistHoldRulesTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 11, 18, 12, 0);

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(30, true)]
    [InlineData(240, true)]
    [InlineData(241, false)]
    [InlineData(-1, false)]
    public void A_time_is_kept_for_up_to_four_hours_and_nought_keeps_nothing(int minutes, bool valid)
    {
        WaitlistHoldRules.IsHoldMinutes(minutes).ShouldBe(valid);
    }

    [Fact]
    public void A_time_is_kept_for_the_salons_minutes_but_never_past_the_last_moment_it_can_be_booked()
    {
        var start = Now + Duration.FromHours(3);

        // Three hours away with an hour's notice: thirty minutes fit.
        WaitlistHoldRules.HeldUntil(Now, 30, start, 60).ShouldBe(Now + Duration.FromMinutes(30));

        // Four hours would run past the last bookable moment, two hours from now.
        WaitlistHoldRules.HeldUntil(Now, 240, start, 60).ShouldBe(Now + Duration.FromHours(2));

        // With no notice period a time is kept at most until it starts.
        WaitlistHoldRules.HeldUntil(Now, 240, start, 0).ShouldBe(start);
    }

    [Property(MaxTest = 200)]
    public Property A_hold_never_outlives_what_it_holds_and_never_lasts_longer_than_asked()
    {
        var input =
            from hold in Gen.Choose(1, WaitlistHoldRules.MaxHoldMinutes)
            from notice in Gen.Choose(0, 24 * 60)
            from ahead in Gen.Choose(0, 7 * 24 * 60)
            select (Hold: hold, Notice: notice, Start: Now + Duration.FromMinutes(notice + ahead));

        return Prop.ForAll(input.ToArbitrary(), i =>
        {
            var until = WaitlistHoldRules.HeldUntil(Now, i.Hold, i.Start, i.Notice);
            return until <= Now + Duration.FromMinutes(i.Hold)
                && until <= i.Start - Duration.FromMinutes(i.Notice)
                && until >= Now;
        });
    }

    [Fact]
    public void A_time_is_kept_while_the_offer_stands_and_its_minutes_have_not_run_out()
    {
        var until = Now + Duration.FromMinutes(30);

        WaitlistHoldRules.IsHeld(null, until, Now).ShouldBeTrue();
        WaitlistHoldRules.IsHeld(null, until, until - Duration.FromSeconds(1)).ShouldBeTrue();
        WaitlistHoldRules.IsHeld(null, until, until).ShouldBeFalse("the last moment is not kept");
        WaitlistHoldRules.IsHeld(null, until, until + Duration.FromMinutes(1)).ShouldBeFalse();
        WaitlistHoldRules.IsHeld(Now, until, Now).ShouldBeFalse("an ended offer keeps nothing");
    }

    [Fact]
    public void The_client_sees_a_time_as_kept_theirs_or_over()
    {
        var until = Now + Duration.FromMinutes(30);

        WaitlistHoldRules.State(null, null, until, Now).ShouldBe(WaitlistOfferState.Held);
        WaitlistHoldRules.State(null, null, until, until).ShouldBe(WaitlistOfferState.Over, "ran out, the clock has not ended it yet");
        WaitlistHoldRules.State(Now, WaitlistOfferEnd.Taken, until, until + Duration.FromDays(1)).ShouldBe(WaitlistOfferState.Taken);
        foreach (var end in new[] { WaitlistOfferEnd.NotTaken, WaitlistOfferEnd.Released, WaitlistOfferEnd.Gone })
        {
            WaitlistHoldRules.State(Now, end, until, Now).ShouldBe(WaitlistOfferState.Over, end.ToString());
        }
    }
}