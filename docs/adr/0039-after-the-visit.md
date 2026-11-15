# 0039 — After the visit: the next booking and what the client bought

Status: Decided — owner may veto
Date: 2026-11-15

## Context
The best moment to book a client's next visit is while they are paying. The desk had to leave the checkout, open
the booking form, find the client again and pick the service again. And once a product was sold on a bill (ADR
0037), the client's card did not show it.

## Decision
- **A completed visit's checkout offers the next visit** in three one-tap choices, by default in four, six and
  eight weeks. Each opens the booking form with the client, the same service, the same person and that day filled
  in; the desk only picks the time. Nothing is booked by the tap itself.
- **The day is the same weekday**, so many weeks after the visit. For a visit of long ago it moves on by whole
  weeks until it is ahead of today.
- **The weeks are a setting** (`RebookSettings`, three numbers, 0 leaves one out), read through `ISettingService`.
  There is no settings page for them yet; the defaults suit a salon that cuts and colours.
- **Shown only to people who may book** (`Bookings.Manage`) and only when the visit is completed.
- **The client card has "Book a visit"**, which opens the booking form for that client.
- **The client card lists "Products bought"**: the latest twenty paid product lines, newest first, each linking to
  the bill it was on. It reads paid bill lines through the account line that settled them, so a product rung up but
  not paid is not shown.

## Not decided here
- A settings page for the weeks. A reminder to clients who did not rebook. Booking the next visit automatically.

## Consequences
- No migration. `VisitBill` carries the service and the staff member's ids. `IClientAccountService` gains
  `GetPurchasesAsync`.
- The checkout reads one more setting for a completed visit; the client card runs one more query.

## Rollback
Revert the PR. Nothing is stored by it.
