# 0021 — Client reschedule link and messages for cancelled and moved visits

Status: Decided — owner may veto
Date: 2026-11-13

## Context
PLAN.md M2 notifications ask for "confirm/reschedule/cancel links". A client could cancel online (ADR 0014) but not
change the time, so a client whose plans shifted by an hour had to cancel and book again, or phone. And two changes of
plan were silent: a visit cancelled by the salon, and a visit moved in the calendar (ADR 0016), told the client nothing.

## Decision
- **One link, two actions.** The secret token of the cancel link also opens `/book/change/{token}`: the booking as it
  is, the person it is with pre-selected ("anyone" one tap away), two weeks of days and the free times as buttons of one
  form. The confirmation page and the cancel page link to it ("Change the time instead"), so no SMS grows a second URL.
- **Same engine, same guarantees.** Times come from `IAvailabilityService` with the visit itself not counted as busy and
  its current time left out; the move goes through `IBookingService.MoveAsync`, so the slot is re-checked and the
  exclusion constraint has the last word. Duration and price stay as booked. A time taken meanwhile re-renders the page
  with fresh times; a visit that has started, is no longer active, or a link that is unknown (or another salon's) gets
  its own state and nothing moves. Posting the time the visit already has is a success that changes nothing, so a double
  tap cannot move it twice or send two messages.
- **The visit keeps its time until another is chosen.** Nothing is released when the page opens.
- **Two SMS consumers** (`Salon.Services/Notifications`), both through the outbox:
  - `AppointmentCancelled` → `Booking.Cancelled` to the client, whoever cancelled, with the salon's booking page. Not
    sent for a visit that had already started (tidying the diary) or a card without a phone.
  - `AppointmentMoved` → new template `Booking.Rescheduled` ("is now on …, change or cancel: link"), whether the client
    moved it through the link or the salon in the calendar. Skipped when the visit has been moved again since (the later
    event tells the news), is in the past, is no longer active, or has no phone.
  `AppointmentMessageReader` reads what a message about one appointment needs (salon, zone, culture, client, service,
  person) so each consumer is only its own rule.
- **No new limit on late changes.** A change is allowed until the visit starts, like a cancellation today. A cut-off
  ("no changes within 24 hours") belongs with the late-cancellation and no-show fee rules of M2 and will apply to both.

## Consequences
- Fewer no-shows and phone calls for the commonest change; the old time is free for others at once.
- Outbox delivery is at least once (ADR 0007). Each of these events has exactly one SMS consumer, so a retry happens
  only when that consumer itself failed before the provider accepted the message; a duplicate would need a failure
  between the hand-over and marking the event processed. Accepted for now; the inbox pattern stays the answer if a
  second consumer of the same event ever sends something.
- Reminders (ADR 0020) follow a move on their own: the new start has no claim yet.
- No migration.

## Rollback
Revert the PR; the token and the cancel flow are unchanged.
