using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using NodaTime;
using Salon.Core.Notifications;
using Salon.Core.Settings;

namespace Salon.Tests.Unit.Notifications;

/// <summary>When reminders go out (ADR 0020): lead time, quiet hours, minimum notice, and never twice for one moment.</summary>
public class ReminderRulesTests
{
    private const int NinePm = 21 * 60;
    private const int EightAm = 8 * 60;

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly DateTimeZone Warsaw = DateTimeZoneProviders.Tzdb["Europe/Warsaw"];

    [Fact]
    public void The_default_plan_is_a_day_before_and_two_hours_before()
    {
        ReminderRules.Plan(new NotificationSettings()).ShouldBe([(ReminderKind.First, Duration.FromHours(24)), (ReminderKind.Second, Duration.FromHours(2))]);
    }

    [Theory]
    [InlineData(false, 24, 2, 0)]
    [InlineData(true, 0, 0, 0)]
    [InlineData(true, 24, 0, 1)]
    [InlineData(true, 0, 2, 1)]
    [InlineData(true, 24, 24, 1)]
    public void Switched_off_reminders_and_equal_lead_times_shrink_the_plan(bool enabled, int first, int second, int expected)
    {
        var settings = new NotificationSettings { RemindersEnabled = enabled, FirstReminderHours = first, SecondReminderHours = second };

        ReminderRules.Plan(settings).Count.ShouldBe(expected);
    }

    [Fact]
    public void Outside_quiet_hours_the_reminder_goes_exactly_the_lead_time_before()
    {
        var start = At(London, 2026, 11, 20, 14, 0);

        ReminderRules.SendAt(start, Duration.FromHours(24), NinePm, EightAm, London).ShouldBe(At(London, 2026, 11, 19, 14, 0));
        ReminderRules.SendAt(start, Duration.FromHours(2), NinePm, EightAm, London).ShouldBe(At(London, 2026, 11, 20, 12, 0));
    }

    [Fact]
    public void A_reminder_that_would_land_in_quiet_hours_waits_for_them_to_end()
    {
        // 07:00 visit: a day before is 07:00 (quiet until 08:00), so 08:00 the day before.
        ReminderRules.SendAt(At(London, 2026, 11, 20, 7, 0), Duration.FromHours(24), NinePm, EightAm, London).ShouldBe(At(London, 2026, 11, 19, 8, 0));
        // 09:30 visit: two hours before is 07:30, so 08:00, still 90 minutes ahead.
        ReminderRules.SendAt(At(London, 2026, 11, 20, 9, 30), Duration.FromHours(2), NinePm, EightAm, London).ShouldBe(At(London, 2026, 11, 20, 8, 0));
        // Evening side of the window: 22:30 the day before waits until 08:00 on the day.
        ReminderRules.SendAt(At(London, 2026, 11, 20, 22, 30), Duration.FromHours(24), NinePm, EightAm, London).ShouldBe(At(London, 2026, 11, 20, 8, 0));
    }

    [Fact]
    public void A_reminder_pushed_too_close_to_the_visit_is_dropped()
    {
        // 08:15 visit: two hours before is 06:15, pushed to 08:00, only 15 minutes ahead.
        ReminderRules.SendAt(At(London, 2026, 11, 20, 8, 15), Duration.FromHours(2), NinePm, EightAm, London).ShouldBeNull();
        // Exactly the minimum notice is still fine.
        ReminderRules.SendAt(At(London, 2026, 11, 20, 8, 30), Duration.FromHours(2), NinePm, EightAm, London).ShouldBe(At(London, 2026, 11, 20, 8, 0));
    }

    [Fact]
    public void Equal_start_and_end_means_no_quiet_hours_and_a_daytime_window_does_not_wrap()
    {
        var start = At(London, 2026, 11, 20, 7, 0);
        ReminderRules.SendAt(start, Duration.FromHours(24), 600, 600, London).ShouldBe(At(London, 2026, 11, 19, 7, 0));

        // Lunchtime quiet window 12:00 to 14:00 (not wrapping).
        ReminderRules.IsQuiet(At(London, 2026, 11, 20, 13, 0), 12 * 60, 14 * 60, London).ShouldBeTrue();
        ReminderRules.IsQuiet(At(London, 2026, 11, 20, 14, 0), 12 * 60, 14 * 60, London).ShouldBeFalse();
        ReminderRules.IsQuiet(At(London, 2026, 11, 20, 23, 0), 12 * 60, 14 * 60, London).ShouldBeFalse();
    }

    [Fact]
    public void Quiet_hours_follow_the_salon_zone_across_the_clock_change()
    {
        // Warsaw, the night the clocks go back (25 October 2026): the day-before moment is at night, 08:00 local ends it.
        var start = At(Warsaw, 2026, 10, 26, 2, 30);

        var sendAt = ReminderRules.SendAt(start, Duration.FromHours(24), NinePm, EightAm, Warsaw).ShouldNotBeNull();

        sendAt.InZone(Warsaw).LocalDateTime.ShouldBe(new LocalDateTime(2026, 10, 25, 8, 0));
    }

    [Fact]
    public void Due_means_the_moment_has_come_the_visit_is_not_too_close_and_it_was_booked_before_that_moment()
    {
        var start = At(London, 2026, 11, 20, 14, 0);
        var sendAt = At(London, 2026, 11, 19, 14, 0);
        var bookedEarlier = At(London, 2026, 11, 10, 9, 0);

        ReminderRules.IsDue(sendAt, sendAt - Duration.FromMinutes(1), start, bookedEarlier).ShouldBeFalse("not yet");
        ReminderRules.IsDue(sendAt, sendAt, start, bookedEarlier).ShouldBeTrue();
        ReminderRules.IsDue(sendAt, start - Duration.FromMinutes(31), start, bookedEarlier).ShouldBeTrue("late but still useful");
        ReminderRules.IsDue(sendAt, start - Duration.FromMinutes(29), start, bookedEarlier).ShouldBeFalse("too close to the visit");
        ReminderRules.IsDue(sendAt, sendAt + Duration.FromHours(1), start, sendAt + Duration.FromMinutes(30)).ShouldBeFalse("booked after the reminder moment: the confirmation covers it");
        ReminderRules.IsDue(null, sendAt, start, bookedEarlier).ShouldBeFalse();
    }

    [Property(MaxTest = 500)]
    public Property A_reminder_is_never_sent_in_quiet_hours_never_too_close_and_never_before_the_lead_time()
    {
        var gen =
            from startMinutes in Gen.Choose(0, 60 * 24 * 365)
            from leadHours in Gen.Choose(1, NotificationSettings.MaxLeadHours)
            from quietStart in Gen.Choose(0, NotificationSettings.MinutesPerDay - 1)
            from quietEnd in Gen.Choose(0, NotificationSettings.MinutesPerDay - 1)
            from warsaw in Gen.Elements(true, false)
            select (startMinutes, leadHours, quietStart, quietEnd, warsaw);

        return Prop.ForAll(gen.ToArbitrary(), input =>
        {
            var zone = input.warsaw ? Warsaw : London;
            var start = Instant.FromUtc(2026, 1, 1, 0, 0) + Duration.FromMinutes(input.startMinutes);
            var lead = Duration.FromHours(input.leadHours);

            var sendAt = ReminderRules.SendAt(start, lead, input.quietStart, input.quietEnd, zone);

            if (sendAt is not { } at)
            {
                return true;
            }

            return !ReminderRules.IsQuiet(at, input.quietStart, input.quietEnd, zone)
                && at <= start - ReminderRules.MinimumNotice
                && at >= start - lead;
        });
    }

    private static Instant At(DateTimeZone zone, int year, int month, int day, int hour, int minute)
        => zone.AtLeniently(new LocalDateTime(year, month, day, hour, minute)).ToInstant();
}