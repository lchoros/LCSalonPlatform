using NodaTime;
using Salon.Core.Appointments;

namespace Salon.Tests.Unit.Appointments;

/// <summary>
/// Which visits still need an outcome and how one is recorded (ADR 0034): the cutoff is the start of today in the
/// salon's zone, only visits that still hold their time count, and every outcome is a walk the state machine allows.
/// </summary>
public class VisitOutcomeRulesTests
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];

    [Theory]
    [InlineData("2026-01-15T15:00:00Z", "2026-01-15T00:00:00Z")]
    [InlineData("2026-01-15T00:00:00Z", "2026-01-15T00:00:00Z")]
    [InlineData("2026-07-01T10:00:00Z", "2026-06-30T23:00:00Z")]
    [InlineData("2026-06-30T23:30:00Z", "2026-06-30T23:00:00Z")]
    [InlineData("2026-06-30T22:59:59Z", "2026-06-29T23:00:00Z")]
    public void The_cutoff_is_the_first_moment_of_today_in_the_salon_zone(string now, string expected)
    {
        var cutoff = VisitOutcomeRules.Cutoff(Parse(now), London);

        cutoff.ShouldBe(Parse(expected));
    }

    [Theory]
    [InlineData(AppointmentStatus.Booked, true)]
    [InlineData(AppointmentStatus.Confirmed, true)]
    [InlineData(AppointmentStatus.Arrived, true)]
    [InlineData(AppointmentStatus.Completed, false)]
    [InlineData(AppointmentStatus.NoShow, false)]
    [InlineData(AppointmentStatus.CancelledByClient, false)]
    [InlineData(AppointmentStatus.CancelledBySalon, false)]
    public void Only_a_visit_that_still_holds_its_time_can_be_without_an_outcome(AppointmentStatus status, bool expected)
    {
        var cutoff = Parse("2026-01-15T00:00:00Z");

        VisitOutcomeRules.IsMissing(status, cutoff - Duration.FromHours(3), cutoff).ShouldBe(expected);
    }

    [Fact]
    public void A_visit_of_today_or_later_is_not_without_an_outcome_yet()
    {
        var cutoff = Parse("2026-01-15T00:00:00Z");

        VisitOutcomeRules.IsMissing(AppointmentStatus.Booked, cutoff, cutoff).ShouldBeTrue("it ended as yesterday did");
        VisitOutcomeRules.IsMissing(AppointmentStatus.Booked, cutoff + Duration.FromSeconds(1), cutoff).ShouldBeFalse("it ended today");
        VisitOutcomeRules.IsMissing(AppointmentStatus.Arrived, cutoff + Duration.FromDays(2), cutoff).ShouldBeFalse();
    }

    [Fact]
    public void The_desk_chooses_between_came_and_did_not_come_unless_the_client_was_marked_as_arrived()
    {
        VisitOutcomeRules.Choices(AppointmentStatus.Booked).ShouldBe([VisitOutcome.Came, VisitOutcome.DidNotCome]);
        VisitOutcomeRules.Choices(AppointmentStatus.Confirmed).ShouldBe([VisitOutcome.Came, VisitOutcome.DidNotCome]);
        VisitOutcomeRules.Choices(AppointmentStatus.Arrived).ShouldBe([VisitOutcome.Came]);
        foreach (var ended in Enum.GetValues<AppointmentStatus>().Where(AppointmentStateMachine.IsTerminal))
        {
            VisitOutcomeRules.Choices(ended).ShouldBeEmpty(ended.ToString());
        }
    }

    [Fact]
    public void A_client_who_came_is_first_arrived_and_then_completed()
    {
        VisitOutcomeRules.Steps(AppointmentStatus.Booked, VisitOutcome.Came).ShouldBe([AppointmentStatus.Arrived, AppointmentStatus.Completed]);
        VisitOutcomeRules.Steps(AppointmentStatus.Confirmed, VisitOutcome.Came).ShouldBe([AppointmentStatus.Arrived, AppointmentStatus.Completed]);
        VisitOutcomeRules.Steps(AppointmentStatus.Arrived, VisitOutcome.Came).ShouldBe([AppointmentStatus.Completed]);
        VisitOutcomeRules.Steps(AppointmentStatus.Booked, VisitOutcome.DidNotCome).ShouldBe([AppointmentStatus.NoShow]);
        VisitOutcomeRules.Steps(AppointmentStatus.Arrived, VisitOutcome.DidNotCome).ShouldBeEmpty("someone who arrived is not a no-show");
    }

    [Fact]
    public void Every_outcome_is_a_walk_the_state_machine_allows_and_ends_where_it_says()
    {
        foreach (var status in Enum.GetValues<AppointmentStatus>())
        {
            foreach (var outcome in Enum.GetValues<VisitOutcome>())
            {
                var steps = VisitOutcomeRules.Steps(status, outcome);
                var offered = VisitOutcomeRules.Choices(status).Contains(outcome);

                (steps.Count > 0).ShouldBe(offered, $"{status} {outcome}");
                var at = status;
                foreach (var step in steps)
                {
                    AppointmentStateMachine.CanTransition(at, step).ShouldBeTrue($"{at} to {step} for {outcome}");
                    at = step;
                }

                if (offered)
                {
                    at.ShouldBe(outcome == VisitOutcome.Came ? AppointmentStatus.Completed : AppointmentStatus.NoShow);
                    AppointmentStateMachine.IsTerminal(at).ShouldBeTrue();
                }
            }
        }
    }

    private static Instant Parse(string text) => NodaTime.Text.InstantPattern.General.Parse(text).Value;
}