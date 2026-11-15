# 0034 — Visits without an outcome

Status: Decided — owner may veto
Date: 2026-11-15

## Context
A visit ends as completed, as a no-show or as cancelled only when somebody at the desk says so. A busy day leaves
some unmarked: they stay "booked" for ever. The reports count them as "not finished yet", the no-show rate leaves
them out, a no-show fee is never proposed for them, and the client's history says nothing. The desk had no way to
find them except by paging back through the day list one day at a time.

## Decision
- **The rule.** A visit is without an outcome when it still holds its time (booked, confirmed or arrived) and it
  ended before today began in the salon's zone. `VisitOutcomeRules` in Core, pure. Today's visits are left out on
  purpose: they are on the day list in front of the desk, and one that ended ten minutes ago is not forgotten yet.
- **A list.** `/admin/appointments/without-outcome`: the visits, the latest first, twenty a page with the shared
  pager (ADR 0032). The latest first because yesterday is what the desk still remembers.
- **Two answers.** "Client came" and "Did not come". A visit marked as arrived offers only the first: someone who
  arrived is not a no-show.
- **No shortcut in the state machine.** "Client came" on a booked visit walks booked, arrived, completed, in one
  transaction, with a history row and an event for each step, by the signed-in user. The state machine of ADR 0012
  is unchanged; the history of a visit marked late reads like one marked on the day.
- **A no-show marked late is still a no-show.** When the salon's policy has a no-show fee, the desk lands on the fee
  preview of ADR 0022, where a person decides. Nothing is charged by this page.
- **A completed visit is not a paid visit.** It appears under "Completed visits not paid" in the reports, with its
  checkout link, as any other completed visit does. The confirmation says so.
- **A notice where the desk looks.** The day list and the calendar say "3 earlier visits have no outcome yet" with a
  link, and say nothing when there are none. A view component with one count query, because two slices show it.
- **`IVisitOutcomeService`** in Services owns the count, the list and the recording, and calls `IBookingService`
  for every move. The slice and the notice both use it; neither references the other.
- **Permissions:** the same as the day list, `Bookings.Manage` and `Clients.View`.
- No cancel button here. A visit that was cancelled by phone is opened from its row ("Open visit") and cancelled on
  its card, where the reason is asked for.

## Consequences
- No migration and no new setting.
- A request that is repeated, or arrives for a visit of today or one that already has an outcome, changes nothing
  and says so.
- Marking old visits changes old numbers in the reports: more completed visits, more no-shows, a truer no-show
  rate. That is the point, but an owner who compares a report with a printout made earlier will see a difference.
- The list reads every unmarked visit to cut a page. A salon that imports years of open visits could have thousands;
  the query then moves to the database.

## Alternatives considered
- **Mark them automatically after some days.** The system does not know whether the client came; guessing "completed"
  inflates the takings still to collect, guessing "no-show" proposes fees to clients who paid in cash.
- **A direct booked-to-completed move.** One step fewer, but a second way into "completed" that every consumer of
  the events would have to know about.
- **Include today's ended visits.** The notice would be on the screen all day and stop meaning anything.
- **A filter on the day list instead of a page.** The day list is one day; the forgotten visits are spread over many.

## Rollback
Revert the PR. Outcomes already recorded stay: they are ordinary status changes.
