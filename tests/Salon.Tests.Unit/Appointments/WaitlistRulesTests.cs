using NodaTime;
using Salon.Core.Appointments;

namespace Salon.Tests.Unit.Appointments;

/// <summary>Who is waiting and for what (ADR 0041).</summary>
public class WaitlistRulesTests
{
    private static readonly LocalDate Today = new(2026, 11, 16);
    private static readonly Instant Closed = Instant.FromUtc(2026, 11, 10, 9, 0);

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("2026-11-20", null, true)]
    [InlineData(null, "2026-11-20", true)]
    [InlineData("2026-11-20", "2026-11-20", true)]
    [InlineData("2026-11-20", "2026-12-01", true)]
    [InlineData("2026-11-20", "2026-11-19", false)]
    public void The_last_day_is_not_before_the_first_and_either_may_be_left_open(string? notBefore, string? notAfter, bool expected)
    {
        WaitlistRules.DaysFit(Day(notBefore), Day(notAfter)).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("2026-11-16", true)]
    [InlineData("2026-12-01", true)]
    [InlineData("2026-11-15", false)]
    public void A_last_day_that_has_passed_is_a_wish_nobody_can_meet(string? notAfter, bool expected)
    {
        WaitlistRules.IsStillPossible(Day(notAfter), Today).ShouldBe(expected);
    }

    [Fact]
    public void Waiting_means_not_closed_and_not_past_its_last_day()
    {
        WaitlistRules.IsWaiting(null, null, Today).ShouldBeTrue();
        WaitlistRules.IsWaiting(null, Today, Today).ShouldBeTrue();
        WaitlistRules.IsWaiting(null, Today.PlusDays(-1), Today).ShouldBeFalse();
        WaitlistRules.IsWaiting(Closed, null, Today).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null, "2026-11-16")]
    [InlineData("2026-11-10", "2026-11-16")]
    [InlineData("2026-11-16", "2026-11-16")]
    [InlineData("2026-12-01", "2026-12-01")]
    public void The_booking_form_opens_on_the_first_day_the_client_can_come_never_in_the_past(string? notBefore, string expected)
    {
        WaitlistRules.BookingDay(Day(notBefore), Today).ShouldBe(Day(expected)!.Value);
    }

    [Fact]
    public void A_remark_is_stored_trimmed_or_not_at_all_and_has_a_limit()
    {
        WaitlistRules.NormalizeNote("  mornings only ").ShouldBe("mornings only");
        WaitlistRules.NormalizeNote("   ").ShouldBeNull();
        WaitlistRules.NormalizeNote(null).ShouldBeNull();
        WaitlistRules.NoteFits(new string('a', WaitlistEntry.NoteMaxLength) + " ").ShouldBeTrue();
        WaitlistRules.NoteFits(new string('a', WaitlistEntry.NoteMaxLength + 1)).ShouldBeFalse();
    }

    private static LocalDate? Day(string? iso) => iso is null ? null : NodaTime.Text.LocalDatePattern.Iso.Parse(iso).Value;
}