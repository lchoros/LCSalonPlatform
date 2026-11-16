# 0041 — A waitlist kept by the desk

Status: Decided — owner may veto
Date: 2026-11-16

## Context
When a client calls and the day they want is full, the salon either loses the visit or writes the name on paper.
When somebody then cancels, the freed time is filled by whoever happens to call next, not by the client who asked
first. The plan lists a waitlist under "grow"; the owner asked for what is left to be built.

## Decision
- **The desk keeps the list.** An entry is an existing client, a service, the person they want if it matters, the
  first and last day they can come if it matters, and a remark ("mornings only"). It is added from the client's
  card, or from the booking form when the chosen day has no free time.
- **First asked, first offered.** The list is shown oldest first. The desk works it from the top.
- **One wish per client and service.** Adding the same client for the same service again keeps their place and
  takes the new days, person and remark. A unique index holds this when two desks do it at once.
- **A booking ends the wait by itself.** When a visit is booked for the client and the service, online or at the
  desk, a consumer of the `AppointmentBooked` event closes the entry as "booked" and notes the visit. It runs
  through the outbox, so the list can be up to a minute behind a booking.
- **The desk can take a client off by hand** ("removed"), and a wish whose last day has passed simply stops
  showing. Entries are closed, never deleted, so the salon can later see what became of its waiting clients.
- **"Book" on a row** opens the booking form with the client, the service, the person and the first day they can
  come filled in.
- **A link with the count** sits beside the buttons of the day list and the calendar ("Waitlist 3").
- **Permissions**: `Bookings.Manage` and `Clients.View`, like the day list. The rows show names and phone numbers.
- **`IWaitlistService`** in Services owns the list; two slices (`Waitlist`, `AddToWaitlist`), a view component for
  the link and the event consumer use it.

## Not decided here
- **A public "tell me if a time comes free" form.** It would take a name and a phone number from somebody who is
  not a client yet, without the code by text that a booking has. That is a consent and spam question for the owner.
- **Telling a waiting client automatically** when a time is freed, and matching freed times to wishes.
- A report of how waits ended.

## Consequences
- One additive migration, `Waitlist`: the table `waitlist_entries` with row-level security, two check constraints
  and two partial indexes.
- One more count query on the day list and the calendar.
- No personal data is collected that the salon did not already hold: an entry points at a client card.

## Rollback
Revert the PR and run the migration's `Down`: it drops the table. Nothing else refers to it.
