using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Salon.Core.Appointments;
using Salon.Core.Identity;
using Salon.Data;
using Salon.Services.Appointments;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Appointments;

/// <summary>
/// A visit's history on its card, and where the diary puts its answers (ADR 0091). The card tells the story the rows
/// already kept: who booked, who moved it and from where to where, who cancelled and why — in order, in the salon's
/// time. A move recorded before its two ends were kept still reads "Moved". And the calendar's confirmation sits in the
/// floating stack, not in the flow of the page, where it used to push the grid down under the pointer.
/// </summary>
public partial class CalendarTests
{
    [Fact]
    public async Task The_card_tells_the_story_of_the_visit_booking_move_and_cancellation_in_order()
    {
        var appointment = await BookAsync(_annaId, At(10, 0));
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        (await PostMoveAsync(client, appointment.Id, _tomId, At(11, 0))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        await InTenantAsync(_studio.Id, sp => sp.GetRequiredService<IBookingService>().CancelAsync(appointment.Id, byClient: true, "Client is ill"));

        var card = await GetAsync(client, $"{CalendarPath}/{appointment.Id}");

        card.ShouldContain("data-testid=\"card-history\"");
        HistoryKindPattern().Matches(card).Select(m => m.Groups[1].Value)
            .ShouldBe(["BookedOnline", "Moved", "CancelledByClient"], "oldest first, each row named by what happened");
        card.ShouldContain("<p class=\"history-what\">Booked online by the client</p>", Case.Sensitive, "nobody is named when the client did it");
        card.ShouldContain("<p class=\"history-what\">Moved by ", Case.Sensitive, "the login that moved it is named");
        var move = HistoryMovePattern().Match(card).Groups[1].Value;
        move.ShouldStartWith("From ");
        move.ShouldContain("10:00 with Anna Kowalska to ");
        move.ShouldEndWith("11:00 with Tom Reed");
        card.ShouldContain("data-testid=\"history-reason\">Reason: Client is ill</p>");

        var moved = await InTenantAsync(_studio.Id, sp => sp.GetRequiredService<SalonDbContext>().AppointmentStatusChanges.AsNoTracking()
            .SingleAsync(c => c.AppointmentId == appointment.Id && c.From == c.To));
        moved.MovedFromStart.ShouldBe(At(10, 0));
        moved.MovedToStart.ShouldBe(At(11, 0));
        moved.MovedFromStaffMemberId.ShouldBe(_annaId);
        moved.MovedToStaffMemberId.ShouldBe(_tomId);
    }

    [Fact]
    public async Task A_move_recorded_before_its_ends_were_kept_still_reads_as_a_move_without_the_detail()
    {
        var appointment = await BookAsync(_annaId, At(10, 0));
        await InTenantAsync(_studio.Id, async sp =>
        {
            var db = sp.GetRequiredService<SalonDbContext>();
            db.AppointmentStatusChanges.Add(new AppointmentStatusChange
            {
                TenantId = _studio.Id,
                AppointmentId = appointment.Id,
                From = AppointmentStatus.Booked,
                To = AppointmentStatus.Booked,
                At = appointment.CreatedAt.Plus(NodaTime.Duration.FromMinutes(1)),
                Reason = IBookingService.MoveReason,
            });
            return await db.SaveChangesAsync();
        });
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var card = await GetAsync(client, $"{CalendarPath}/{appointment.Id}");

        HistoryKindPattern().Matches(card).Select(m => m.Groups[1].Value).ShouldBe(["BookedOnline", "Moved"]);
        card.ShouldContain("<p class=\"history-what\">Moved</p>", Case.Sensitive, "no login, no detail: only what is known");
        card.ShouldNotContain("data-testid=\"history-move\"");
        card.ShouldNotContain("data-testid=\"history-reason\"", Case.Sensitive, "a move's marker is not a reason a person wrote");
    }

    [Fact]
    public async Task The_calendar_puts_its_confirmation_in_the_floating_stack_and_keeps_the_stack_for_refusals()
    {
        var appointment = await BookAsync(_annaId, At(10, 0));
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var quiet = await GetAsync(client, DayPath(_day, null));
        quiet.ShouldContain("<div class=\"toast-stack\" data-testid=\"toasts\"", Case.Sensitive, "the drag script needs the stack before anything was said");
        quiet.ShouldNotContain("data-testid=\"flash\"");

        var response = await PostMoveAsync(client, appointment.Id, _tomId, At(11, 0));
        var after = await GetAsync(client, response.Headers.Location.ShouldNotBeNull().OriginalString);

        ToastPattern().IsMatch(after).ShouldBeTrue("the confirmation floats in the stack instead of pushing the grid down");
    }

    [Fact]
    public async Task The_bar_offers_any_date_and_leaves_the_sibling_pages_to_the_tabs()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var html = await GetAsync(client, DayPath(_day, _annaId));

        // One plain GET form carries the date and the person, so either choice keeps the other (ADR 0092).
        html.ShouldContain($"<input id=\"jump\" type=\"date\" name=\"date\" value=\"{Iso(_day)}\"");
        html.ShouldContain($"<option value=\"{_annaId}\" selected=\"selected\">Anna Kowalska</option>");
        html.ShouldContain("data-testid=\"view-day\" aria-current=\"page\"");
        html.ShouldContain("data-testid=\"book\"");
        html.ShouldNotContain("data-testid=\"day-list\"", Case.Sensitive, "the day list is the tab above, not a second button beside \"Day\"");
        html.ShouldNotContain("data-testid=\"waitlist-link\"", Case.Sensitive, "the waiting list is a tab too; its link shows here only when somebody waits");
        html.ShouldContain("data-testid=\"section-tab-day\"");
        html.ShouldContain("data-testid=\"section-tab-waitlist\"");

        var jumped = await GetAsync(client, $"{CalendarPath}?view=day&date={Iso(_day.PlusDays(9))}&staff={_annaId}");
        jumped.ShouldContain($"name=\"date\" value=\"{Iso(_day.PlusDays(9))}\"");
    }

    [GeneratedRegex("data-testid=\"history-line\" data-kind=\"([A-Za-z]+)\"")]
    private static partial Regex HistoryKindPattern();

    [GeneratedRegex("data-testid=\"history-move\">([^<]+)</p>")]
    private static partial Regex HistoryMovePattern();

    [GeneratedRegex("data-testid=\"toasts\"[^>]*>\\s*<p role=\"status\" data-testid=\"flash\" class=\"notice notice-ok toast\">")]
    private static partial Regex ToastPattern();
}