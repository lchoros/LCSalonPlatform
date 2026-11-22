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

## Addendum, 2026-11-22: who with is asked, not assumed (stage 3)

The owner on the time page: "it is not clear to select staff during booking; instead of default all it should not be
selected but a mandatory field."

- Where **more than one person** offers the service, the page opens with nobody chosen. The question "Who with" stands
  in a brand-edged box marked "Required", its answers wrapped so all are in sight; no day, no time and no Continue
  show until it is answered. The summary reads "Staff: Not chosen yet".
- **"Anyone available" is an answer like the others** and travels in the address as `staff=any`; the day strip, the
  times and the way back from the details form keep it. Only a person's id goes on to the details step, as before.
- Where **one person** offers the service there is nothing to ask and the page is as it was.
- A person in the address who is not bookable (or does not offer the service) is no answer: the question again.
- A day asked for in the address (the waiting list's "a time came up" message links to one) is kept through the answer.
- The booking itself is unchanged: this is a rule of the page, not of the engine. A hand-written address to the
  details step without a person still books with whoever is free, as "anyone" always did.
- Cost: one more tap for a client who does not mind who. Taken on purpose, so the choice is seen. The change-a-booking
  page keeps its own picker, where the person the visit is with is already the answer.
