# 0086. The calendar is dragged, the server decides — and the brand moves towards indigo

Status: Decided — owner may veto
Date: 2026-11-20
Issue: the owner wanted the diary to feel like Booksy's (drag a visit, smooth), found the card's one-tap move jumpy, and picked Cinolla's indigo over our first violet

## Context

Moving a visit meant opening its card, choosing a person and a day, pressing "Show times" and tapping a time — and
that tap moved the visit at once, which the owner called buggy. Booksy's diary is a full JavaScript calendar; the
view we would need from FullCalendar (one column per person, drag between people) is its paid Premium tier. Our grid
is already server-rendered, tested and fast. The owner also asked whether such a script is a security risk, whether
a React front end would be better, and for the brand to sit nearer Cinolla's `#4f46e5`.

## Decision

- **One small vanilla ES module** (`wwwroot/js/calendar-drag.js`, no dependency, no bundler, no inline script — the
  CSP is untouched) lets a booked or confirmed visit be dragged to another row or column with mouse, pen or a
  press-and-hold finger. The block lifts, a dashed outline snaps to quarter-hour rows, a non-working row shows the
  outline in red.
- **The script only proposes; the server decides.** A drop posts the column's person and day and the row's minute
  of the salon's clock to `POST admin/appointments/calendar/{id}/drop` with the anti-forgery token. The server
  turns that into an instant in the salon's zone (no time-zone arithmetic in a script) and runs the same
  `MoveAppointmentCommand` as the card: permission, tenant, the availability engine, the history row and the
  outbox event. 204 means moved (the reload shows the queued confirmation); 409 carries one localized sentence and
  the block glides back. A forged or stale request is refused exactly like any other.
- **The card's move form chooses, then confirms.** A tapped free time is only marked; "Move the visit" on a
  sticky bar does the move. It remains the complete no-script path.
- **The calendar speaks the brand.** A block's fill is a shade of the brand that deepens with the visit's status
  (booked, confirmed, arrived); done and missed visits step out of the purple; the staff member's own colour stays
  as the block's left edge. The last black chip ("All") is gone.
- **The brand is retuned in the one `@theme` block** to sit between the first violet and Cinolla's indigo
  (`#5b3fe0`), with Cinolla's night indigo as the deep text tone; AA contrast holds on both pairs.
- **No React, no component library.** The app is forms, lists and a diary; server-rendered pages with small
  islands of script keep one system to secure and test. FullCalendar Premium is the door to knock on if resizing,
  recurring visits or timelines are ever wanted.

## Consequences

- Moving a visit is one gesture on a desk and still two honest taps on the card.
- The drop endpoint is a second way into an existing command, not a new capability: nothing it accepts is trusted.
- Touch dragging needs a press-and-hold so a swipe still scrolls; on a phone's all-staff day the grid is replaced
  by the list anyway (ADR 0016), where the card remains the way.

## Alternatives considered

- FullCalendar (free tier): no per-person columns; Premium costs yearly per developer and replaces a grid we own.
- A React or other SPA front end: a second system (JSON API, token auth, duplicated validation, a build pipeline)
  for no gain the pilot can see.
- Sending the instant from the browser: the browser would need the salon's time-zone rules; a row's minute and
  day are unambiguous and keep clock arithmetic on the server.