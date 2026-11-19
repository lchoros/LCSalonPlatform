# 0069 — A time kept for a waiting client is marked on the diary

Status: Decided — owner may veto
Date: 2026-11-19

## Context
When a visit is cancelled, its time can be kept for the first client waiting for it (ADR 0065). While it is kept,
the time is busy for everybody else: it is off the booking pages and off the desk's booking form. The waitlist page
says whose it is and has a button to let it go.

The diary did not say so. The day list and the calendar showed an empty slot where the cancelled visit had been,
but the desk's booking form would not offer it. A receptionist with a client at the counter saw a free time that
could not be booked, and nothing on the page said why. ADR 0065 left this mark out.

## Decision
- **The diary shows the kept time where the visit was**, in the person's column, with a dashed amber border and a
  "Kept" badge:
  - on the day list, above that person's visits, with the client's full name, the service, until when, and a
    button to let it go;
  - on the calendar (day and week), placed on the grid like a visit, with the client's first name. On a phone it
    is in the day's list. A tap opens that day's list.
- **Only while it is really kept**: a time whose minutes have run out is on the booking pages again, so it is not
  marked, even before the clock has passed it on (the task runs every minute).
- **Letting it go from the day list goes back to the same day.** The release form sends the day. Only a well-formed
  date is put in the address; anything else goes back to the waitlist, as before. The rule and the permissions are
  ADR 0065's: `Bookings.Manage` and `Clients.View`, which the diary already needs.
- **One read**, `IWaitlistOfferService.KeptBetweenAsync(windowStart, windowEnd, now)`, gives both pages the kept
  times in their window through the tenant filter; each page puts them in the salon's zone. Names come from the
  lookups each page already makes. No migration.

## Consequences
- What the desk sees on the diary and what it can book now agree.
- A kept time takes a lane on the calendar like a visit. If a visit ends up in the same time anyway (ADR 0065 lists
  how; the offer is then "gone" when the client taps), both show side by side until the offer ends.

## Not decided here
- A mark for a time kept on a day outside the window shown (the waitlist lists them all).
- Letting a time go from the calendar itself; the calendar leads to the day's list.
