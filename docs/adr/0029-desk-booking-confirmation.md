# 0029 — A booking made at the desk is confirmed like one made online

Status: Decided — owner may veto
Date: 2026-11-13

## Context
A client who books online gets a confirmation by text and email with their own link to change or cancel (ADR 0014,
0021, 0025). A client the salon books in by phone or in person got nothing until the reminder a day before, and that
reminder linked to the salon's booking page because a desk booking had no link of its own. ADR 0024 and 0025 listed
this as a known gap ("a desk booking gets no confirmation"). Most bookings in a salon that is moving from paper or
from the phone are desk bookings, so most clients never had their visit in writing.

## Decision
- **Every booking gets the client's own link.** `BookingService.BookAsync` gives every appointment a public token,
  whatever its source. The cancel and change pages already work by token, with the same rules for everybody: the
  notice period, the late-cancellation warning, no change inside the notice period (ADR 0022).
- **Desk bookings are confirmed by default.** `AppointmentBookedNotice` sends the same `Booking.Confirmation`
  template, on both channels, for a booking made in the admin. Online bookings are confirmed as before.
- **One switch.** `NotificationSettings.ConfirmDeskBookings`, on by default, as a tick box on the reminders settings
  page: "Confirm bookings made at the desk". Off means only online bookings are confirmed. The link exists either way.
- **Nobody to tell is not an error.** A client without a phone number and without an email address gets nothing and
  the event is done; nothing is retried.

## Consequences
- A salon that upgrades starts texting clients it books at the desk. That is the point of the feature, and it is one
  more text per desk booking once texts cost money (M3). The switch is the way out.
- A client booked at the desk can now cancel or move the visit themselves. The salon sees it on the day list like any
  other change and the client is told by the usual messages.
- Appointments made at the desk before this change keep having no link; their reminders lead to the booking page as
  before.
- These are messages about the client's own booking, sent to the contact details the salon holds for that purpose.
  They are not marketing and the marketing consent flag is not involved, the same as for reminders.
- No migration: the column and its unique index exist since ADR 0014.

## Alternatives considered
- **A tick box on each desk booking** ("send a confirmation"). One more thing to decide at the desk on every booking;
  a salon-wide rule with a switch is what a busy receptionist needs.
- **Off by default.** Safer on upgrade, but the gap would stay for every salon that does not find the setting, and a
  confirmation is what a client expects.
- **A separate template for desk bookings.** The message says the same thing; a second template would only be a
  second text for the owner to maintain.

## Rollback
Revert the PR: desk bookings stop being confirmed and new ones get no token. Tokens already given keep working.
