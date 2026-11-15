using System.Text;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Staff;

namespace Salon.Tests.Unit.Staff;

/// <summary>A staff member's calendar address (ADR 0038): the secret, what it shows, and the iCalendar text itself.</summary>
public class CalendarFeedTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 11, 16, 9, 30, 0);

    [Fact]
    public void A_token_is_43_url_safe_characters_and_never_the_same_twice()
    {
        var tokens = Enumerable.Range(0, 50).Select(_ => CalendarFeedToken.Generate()).ToList();

        tokens.ShouldAllBe(t => CalendarFeedToken.LooksValid(t));
        tokens.ShouldAllBe(t => t.Length == CalendarFeedToken.Length);
        tokens.Distinct().Count().ShouldBe(50);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA A")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Text_of_the_wrong_length_or_alphabet_is_not_a_token(string? text)
    {
        CalendarFeedToken.LooksValid(text).ShouldBeFalse();
    }

    [Theory]
    [InlineData(AppointmentStatus.Booked, true)]
    [InlineData(AppointmentStatus.Confirmed, true)]
    [InlineData(AppointmentStatus.Arrived, true)]
    [InlineData(AppointmentStatus.Completed, true)]
    [InlineData(AppointmentStatus.NoShow, false)]
    [InlineData(AppointmentStatus.CancelledByClient, false)]
    [InlineData(AppointmentStatus.CancelledBySalon, false)]
    public void Only_a_visit_that_holds_its_time_or_took_place_is_in_the_diary(AppointmentStatus status, bool expected)
    {
        CalendarFeedRules.Shows(status).ShouldBe(expected);
    }

    [Fact]
    public void Every_status_is_decided()
    {
        CalendarFeedRules.ShownStatuses.ShouldBe(Enum.GetValues<AppointmentStatus>().Where(CalendarFeedRules.Shows));
    }

    [Theory]
    [InlineData("Cut & blow-dry", "Olivia", "Brown", "Cut & blow-dry · Olivia B.")]
    [InlineData("Cut & blow-dry", " Olivia ", " brown ", "Cut & blow-dry · Olivia B.")]
    [InlineData("Cut & blow-dry", "Olivia", "", "Cut & blow-dry · Olivia")]
    [InlineData("Cut & blow-dry", "", "", "Cut & blow-dry")]
    public void A_title_names_the_service_and_the_client_by_first_name_and_initial_only(string service, string first, string last, string expected)
    {
        CalendarFeedRules.Summary(service, first, last).ShouldBe(expected);
    }

    [Fact]
    public void The_window_is_a_month_back_and_a_year_ahead()
    {
        CalendarFeedRules.From(Now).ShouldBe(Now - Duration.FromDays(30));
        CalendarFeedRules.Until(Now).ShouldBe(Now + Duration.FromDays(365));
    }

    [Fact]
    public void A_calendar_is_written_in_the_format_calendar_apps_subscribe_to()
    {
        var id = Guid.Parse("0193a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b");
        var visit = new CalendarFeedEvent(id, Instant.FromUtc(2026, 11, 17, 10, 0, 0), Instant.FromUtc(2026, 11, 17, 10, 45, 0), "Cut & blow-dry · Olivia B.", "20 Drake Street, Rochdale OL16 1NT");

        var text = CalendarFeedWriter.Write("Tom Reed · Studio 20", "studio-20.example", [visit], Now);

        text.ShouldBe(string.Join("\r\n",
        [
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//Salon Platform//Calendar feed//EN",
            "CALSCALE:GREGORIAN",
            "METHOD:PUBLISH",
            "X-WR-CALNAME:Tom Reed · Studio 20",
            "BEGIN:VEVENT",
            "UID:0193a1b2c3d47e5f8a9b0c1d2e3f4a5b@studio-20.example",
            "DTSTAMP:20261116T093000Z",
            "DTSTART:20261117T100000Z",
            "DTEND:20261117T104500Z",
            "SUMMARY:Cut & blow-dry · Olivia B.",
            "LOCATION:20 Drake Street\\, Rochdale OL16 1NT",
            "END:VEVENT",
            "END:VCALENDAR",
            string.Empty,
        ]));
    }

    [Fact]
    public void A_calendar_without_visits_is_still_a_calendar_and_a_visit_without_a_place_has_no_location()
    {
        var empty = CalendarFeedWriter.Write("Tom Reed", "studio-20.example", [], Now);
        empty.ShouldStartWith("BEGIN:VCALENDAR\r\n");
        empty.ShouldEndWith("END:VCALENDAR\r\n");
        empty.ShouldNotContain("VEVENT");

        var noPlace = CalendarFeedWriter.Write("Tom Reed", "studio-20.example", [new CalendarFeedEvent(Guid.NewGuid(), Now, Now + Duration.FromMinutes(30), "Fringe trim", null)], Now);
        noPlace.ShouldNotContain("LOCATION");
    }

    [Theory]
    [InlineData("Cut, colour; blow-dry", "Cut\\, colour\\; blow-dry")]
    [InlineData(@"Back\slash", @"Back\\slash")]
    [InlineData("Line one\r\nline two\nline three", "Line one\\nline two\\nline three")]
    public void Text_is_escaped_so_it_cannot_break_out_of_its_line(string value, string expected)
    {
        CalendarFeedWriter.Escape(value).ShouldBe(expected);
    }

    [Fact]
    public void Long_lines_are_folded_at_75_octets_without_splitting_a_character()
    {
        // 40 two-octet letters and an emoji of four octets: the fold must fall between characters.
        var summary = new string('é', 40) + " 💇 " + new string('ł', 40);
        var visit = new CalendarFeedEvent(Guid.NewGuid(), Now, Now + Duration.FromMinutes(30), summary, null);

        var text = CalendarFeedWriter.Write("Tom Reed", "studio-20.example", [visit], Now);

        var lines = text.Split("\r\n");
        lines.ShouldAllBe(line => Encoding.UTF8.GetByteCount(line) <= 75);
        // Unfolding (removing CR LF and the one space after it) gives the original line back, intact.
        text.Replace("\r\n ", string.Empty, StringComparison.Ordinal).ShouldContain("SUMMARY:" + summary + "\r\n");
        text.ShouldNotContain("�");
        text.Replace("\r\n", string.Empty, StringComparison.Ordinal).ShouldNotContain("\n", Case.Sensitive, "every line ends with CR LF, never a bare LF");
    }
}