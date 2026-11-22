# 0094. The booking as a focused flow

Status: Decided — owner may veto
Date: 2026-11-22
Issue: the owner, with four screenshots of Booksy's booking — "our main focus will be now to perfect the book process for the client … once you get into the booking you do not see the menu or the salon logo"

## Decision (stage 1, built)

- **Inside a booking the page is only the booking.** The time page drops the salon bar; the footer with the privacy
  notice stays (ADR 0063). Its way out is a close button that asks first — "Discard booking?" with "Continue
  booking" and "Yes, discard" — as a `:target` dialog, no script.
- **The month is said once**, above the days; a day tile holds a weekday, a number and a small green bar when the
  day has times, with room around them. The same tall tile on a phone and a desk.
- **Gentler type** in the flow: slate and semibold instead of black and bold.

## Not built yet (stages 2–4, in the owner's order)

2. The summary card under the times (service, price, the time range once a time is tapped, "Staff: no preference"
   with Change) and the total above Continue.
3. The same focused frame on the details and code pages; the service list with a description under the name and
   softer type; `/services` folded into `/book`.
4. **Several services in one booking** ("Add another service"). Today a visit is one service with one person: this
   changes the availability engine (back-to-back times, possibly different people), the visit model, prices,
   reminders and the till. It needs its own design and is the owner's decision.

## Consequences

- A client cannot wander off mid-booking by accident, and cannot lose a half-made booking with one stray tap.
- We take the pattern, not the look: our brand, our tiles, our words.
