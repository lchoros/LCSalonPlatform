# 0079. The public time picker pages, counts and reads in parts of the day

Status: Decided — owner may veto
Date: 2026-11-19
Issue: the strip stopped at two weeks though salons allow sixty days, and the owner wanted the picker friendlier

## Context

The salon's booking horizon (`BookingSettings.MaxAdvanceDays`, default 60) reached far past the date strip, which
always showed the first fourteen days and nothing else — a client could not book the cut three weeks out that the
salon explicitly allows. Trying the flow on the live pilot, the owner also found the day tiles cramped, wanted the
picker to feel more like the polished booking pages they know (round paddles, a count on each day, times broken up
by part of day), and confirmed the three-tap flow itself stays exactly as designed.

## Decision

- **The strip is a sliding window.** `from` on the slot page starts the window; it is clamped between today and the
  horizon, and the window never shows more than fourteen days or reaches past the horizon. Round ‹ › paddles page
  by window; they render only where there is somewhere to go, so the first page has no back arrow and the last no
  forward arrow. Day and paddle links carry `from` and the chosen person, so the window and staff survive every tap.
- **Every day tile carries its count of free starts** — a small badge, green when there is something, grey on zero —
  so a client sees the good days before tapping. The tiles grew to 64 px with real padding.
- **A day's times read in parts of the day**: "Until 12:00", "12:00–16:00", "After 16:00", only parts that have
  free times. The grouping lives in the handler (thin views, ADR 0043).
- **The free colour is emerald** (the Swagger green the owner pointed at, kept AA-contrast: emerald-700 on white
  and white on emerald-700): available tiles and slot pills are white with emerald borders and lift on hover, the
  chosen day fills emerald with a soft glow. People pills stay stone — the colour means "time", not "person".
- **Step 3's "Change time" became a visible button** that returns to the same day, window and person, so a
  mis-tapped slot costs exactly one tap. No confirm step was added: the owner tested and kept three taps.

## Consequences

- A salon's whole booking horizon is reachable; the fourteen-day page is just a page.
- The slot page stays JavaScript-free: paging, badges and groups are all plain links and server markup under CSP.
- The emerald accent is scoped to `.tile` and `.slot`, so the admin's stone-and-gradient look is untouched.

## Alternatives considered

- A month-grid calendar: heavier, needs JS for usability, and the window answers "when can I get in" faster.
- Tapping a time selecting it with a separate confirm button: one more tap for every booking; the owner tried the
  flow live and chose to keep the instant advance.
