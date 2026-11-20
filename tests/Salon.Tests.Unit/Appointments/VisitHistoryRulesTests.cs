using Salon.Core.Appointments;

namespace Salon.Tests.Unit.Appointments;

/// <summary>
/// The rows the diary keeps for a visit, read as the lines of its history (ADR 0091): the first row is the booking
/// and the visit's source says who made it, a row that leaves the status as it was is a move, and every other row
/// is named by the status it led to.
/// </summary>
public class VisitHistoryRulesTests
{
    [Theory]
    [InlineData(AppointmentSource.Online, VisitHistoryKind.BookedOnline)]
    [InlineData(AppointmentSource.Admin, VisitHistoryKind.Booked)]
    [InlineData(AppointmentSource.Import, VisitHistoryKind.Imported)]
    public void The_first_row_is_the_booking_and_the_source_says_who_made_it(AppointmentSource source, VisitHistoryKind expected)
    {
        VisitHistoryRules.KindOf(null, AppointmentStatus.Booked, source).ShouldBe(expected);
        VisitHistoryRules.KindOf(null, AppointmentStatus.Confirmed, source).ShouldBe(expected, "an imported visit may start confirmed; it is still the booking");
    }

    [Theory]
    [InlineData(AppointmentStatus.Booked)]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.Arrived)]
    public void A_row_that_leaves_the_status_as_it_was_is_a_move(AppointmentStatus status)
        => VisitHistoryRules.KindOf(status, status, AppointmentSource.Admin).ShouldBe(VisitHistoryKind.Moved);

    [Theory]
    [InlineData(AppointmentStatus.Booked, AppointmentStatus.Confirmed, VisitHistoryKind.Confirmed)]
    [InlineData(AppointmentStatus.Confirmed, AppointmentStatus.Arrived, VisitHistoryKind.Arrived)]
    [InlineData(AppointmentStatus.Arrived, AppointmentStatus.Completed, VisitHistoryKind.Completed)]
    [InlineData(AppointmentStatus.Booked, AppointmentStatus.NoShow, VisitHistoryKind.NoShow)]
    [InlineData(AppointmentStatus.Booked, AppointmentStatus.CancelledByClient, VisitHistoryKind.CancelledByClient)]
    [InlineData(AppointmentStatus.Confirmed, AppointmentStatus.CancelledBySalon, VisitHistoryKind.CancelledBySalon)]
    public void Every_other_row_is_named_by_the_status_it_led_to(AppointmentStatus from, AppointmentStatus to, VisitHistoryKind expected)
        => VisitHistoryRules.KindOf(from, to, AppointmentSource.Online).ShouldBe(expected, "the source only names the booking");

    [Fact]
    public void Only_a_cancellation_shows_the_words_kept_with_its_row()
    {
        var showing = Enum.GetValues<VisitHistoryKind>().Where(VisitHistoryRules.ShowsReason);

        showing.ShouldBe([VisitHistoryKind.CancelledByClient, VisitHistoryKind.CancelledBySalon]);
    }
}