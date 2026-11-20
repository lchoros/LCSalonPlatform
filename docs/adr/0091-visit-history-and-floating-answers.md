# 0091. A visit's history on its card, and answers that float

Status: Decided — owner may veto
Date: 2026-11-20
Issue: the owner's screenshot of the calendar — "is this best place? also we need history of booking for admin yes?"

## Context

Two things were on that screenshot. A green "Moved Lily Jones to …" lay above the title and a red "That time is not
free …" lay above the grid, both at once: the first was the answer to the move before, never taken away; the second
had pushed the whole grid down by its own height at the very moment the pointer let go of a block.

And the question under it: who booked this, who moved it, when? The diary has kept a row for every booking, move
and change of status since ADR 0012 (`appointment_status_changes`) — for the reminders and the no-show policy — but
no page showed them, and a move's row did not say from where to where.

## Decision

- **The card of a visit ends with its history**, oldest first, in the salon's time: booked at the desk / online by
  the client / read in from a file, moved, confirmed, arrived, completed, no-show, cancelled by the client or the
  salon. Each line names the login that did it; nobody is named when the client did it online or a job did. A
  cancellation shows the words given with it. `VisitHistoryRules` (Core) reads a row as a kind of line; the card's
  handler adds names and times.
- **A move now records its two ends** — four nullable columns on the existing row (`moved_from_start`,
  `moved_to_start`, `moved_from_staff_member_id`, `moved_to_staff_member_id`), filled by `MoveAsync`. The line reads
  "From Sat 21 Nov, 11:15 with Anna to Mon 23 Nov, 10:00 with Tom". Moves from before tonight read "Moved" and no
  more: nothing is invented for them.
- **The calendar's answers float.** The confirmation after a move and the refusal after a drop sit in one stack
  fixed above the page (above the phone's bottom bar; bottom centre on a desk, clear of every control at the top).
  One answer at a time: a new one replaces the one before, starting a drag clears what was there, a tap closes it,
  and it leaves by itself after seven seconds. Nothing in the flow of the page moves any more. Without script the
  stack falls back into the flow.
- **Only the calendar for now.** On form pages the confirmation in the flow sits where the eye already is and
  pushes nothing that is being pointed at; they keep it.

## Consequences

- The desk can answer "who moved Mrs Jones?" from the card. Who sees it is who sees the card (Bookings.Manage and
  Clients.View); it shows staff logins' display names and nothing of the client beyond what the card already has.
- Additive migration `VisitMoveHistory`: four nullable columns, no backfill, no index. Rollback drops them; the
  history then reads "Moved" for every move again.
- The history is read-only and is not an audit log of everything (notes edited, prices changed, payments): those
  have their own records (the ledger, the receipt). If the owner wants one list of all of it, that is a next step.

## Alternatives considered

- A separate "History" page per visit: one more tap for something read at a glance.
- Writing the two ends into `Reason` as text: unreadable for the reminder service that looks for the move marker,
  and not translatable.
- Toasts for every confirmation in the admin: a wider change than the fault shown; forms do not have the problem.
- An "Undo" button in the move's answer: wanted, and now easy (the row knows where the visit was) — next.
