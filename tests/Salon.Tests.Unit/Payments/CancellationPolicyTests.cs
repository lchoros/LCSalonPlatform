using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

using NodaTime;

using Salon.Core.Appointments;
using Salon.Core.Domain;
using Salon.Core.Payments;
using Salon.Core.Settings;

namespace Salon.Tests.Unit.Payments;

/// <summary>The cancellation policy as rules (ADR 0022): the notice period, the fee to the penny, and which fee a visit may carry.</summary>
public class CancellationPolicyTests
{
    private static readonly Instant Start = Instant.FromUtc(2026, 11, 20, 14, 0);

    private static readonly Money Price = new(25m, "GBP");

    [Fact]
    public void The_defaults_are_no_policy_at_all()
    {
        var policy = new CancellationPolicySettings();

        policy.FreeChangeHours.ShouldBe(0);
        policy.LateCancellationFeePercent.ShouldBe(0m);
        policy.NoShowFeePercent.ShouldBe(0m);
        CancellationPolicy.IsInsideNotice(Start, Start - Duration.FromMinutes(1), policy.FreeChangeHours).ShouldBeFalse();
        CancellationPolicy.Propose(AppointmentStatus.NoShow, Start, null, Price, policy).ShouldBeNull();
    }

    [Theory]
    [InlineData(24, 25 * 60, false)]
    [InlineData(24, 24 * 60, false)]
    [InlineData(24, (24 * 60) - 1, true)]
    [InlineData(24, 0, true)]
    [InlineData(24, -30, true)]
    [InlineData(0, 1, false)]
    public void The_notice_period_ends_exactly_that_many_hours_before_the_visit(int hours, int minutesBeforeStart, bool inside)
    {
        CancellationPolicy.IsInsideNotice(Start, Start - Duration.FromMinutes(minutesBeforeStart), hours).ShouldBe(inside);
    }

    [Theory]
    [InlineData(25, 50, 12.50)]
    [InlineData(25, 100, 25)]
    [InlineData(25, 150, 25)]
    [InlineData(19.99, 50, 10.00)]
    [InlineData(0.01, 50, 0.01)]
    [InlineData(33.33, 33, 11.00)]
    [InlineData(25, 0, 0)]
    [InlineData(0, 50, 0)]
    [InlineData(25, -10, 0)]
    public void A_fee_is_a_percentage_of_the_price_to_the_penny_and_never_more_than_the_price(decimal price, decimal percent, decimal expected)
    {
        CancellationPolicy.FeeAmount(price, percent).ShouldBe(expected);
    }

    [Property(MaxTest = 300)]
    public Property A_fee_is_never_negative_never_above_the_price_and_has_at_most_two_decimals()
    {
        var gen =
            from pennies in Gen.Choose(0, 500_000)
            from percentTenths in Gen.Choose(-100, 2_000)
            select (Price: pennies / 100m, Percent: percentTenths / 10m);

        return Prop.ForAll(gen.ToArbitrary(), x =>
        {
            var fee = CancellationPolicy.FeeAmount(x.Price, x.Percent);
            return fee >= 0m && fee <= x.Price && decimal.Round(fee, 2) == fee;
        });
    }

    [Fact]
    public void A_missed_visit_may_carry_the_no_show_fee()
    {
        var policy = new CancellationPolicySettings { NoShowFeePercent = 100m, LateCancellationFeePercent = 50m, FreeChangeHours = 24 };

        var proposal = CancellationPolicy.Propose(AppointmentStatus.NoShow, Start, null, Price, policy).ShouldNotBeNull();

        proposal.Kind.ShouldBe(ClientAccountEntryKind.NoShowFee);
        proposal.Amount.ShouldBe(new Money(25m, "GBP"));
        proposal.Percent.ShouldBe(100m);
    }

    [Theory]
    [InlineData(AppointmentStatus.CancelledByClient)]
    [InlineData(AppointmentStatus.CancelledBySalon)]
    public void A_visit_cancelled_inside_the_notice_period_may_carry_the_late_cancellation_fee(AppointmentStatus status)
    {
        var policy = new CancellationPolicySettings { NoShowFeePercent = 100m, LateCancellationFeePercent = 50m, FreeChangeHours = 24 };

        var late = CancellationPolicy.Propose(status, Start, Start - Duration.FromHours(3), Price, policy).ShouldNotBeNull();
        var inTime = CancellationPolicy.Propose(status, Start, Start - Duration.FromHours(24), Price, policy);

        late.Kind.ShouldBe(ClientAccountEntryKind.LateCancellationFee);
        late.Amount.ShouldBe(new Money(12.50m, "GBP"));
        inTime.ShouldBeNull("cancelled in time");
    }

    [Theory]
    [InlineData(AppointmentStatus.Booked)]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.Arrived)]
    [InlineData(AppointmentStatus.Completed)]
    public void A_visit_that_is_still_on_or_took_place_carries_no_fee(AppointmentStatus status)
    {
        var policy = new CancellationPolicySettings { NoShowFeePercent = 100m, LateCancellationFeePercent = 50m, FreeChangeHours = 24 };

        CancellationPolicy.Propose(status, Start, Start - Duration.FromHours(1), Price, policy).ShouldBeNull();
    }

    [Fact]
    public void A_policy_without_a_percentage_or_a_free_visit_proposes_nothing()
    {
        var noFee = new CancellationPolicySettings { FreeChangeHours = 24 };
        var policy = new CancellationPolicySettings { NoShowFeePercent = 50m, LateCancellationFeePercent = 50m, FreeChangeHours = 24 };

        CancellationPolicy.Propose(AppointmentStatus.NoShow, Start, null, Price, noFee).ShouldBeNull();
        CancellationPolicy.Propose(AppointmentStatus.CancelledByClient, Start, Start - Duration.FromHours(1), Price, noFee).ShouldBeNull();
        CancellationPolicy.Propose(AppointmentStatus.NoShow, Start, null, new Money(0m, "GBP"), policy).ShouldBeNull();
    }

    [Fact]
    public void Every_kind_of_line_is_a_fee_what_settles_one_or_a_deposit()
    {
        foreach (var kind in Enum.GetValues<ClientAccountEntryKind>())
        {
            new[] { ClientAccountRules.IsFee(kind), ClientAccountRules.IsSettlement(kind), ClientAccountRules.IsDeposit(kind) }.Count(family => family).ShouldBe(1, kind.ToString());
        }
    }
}