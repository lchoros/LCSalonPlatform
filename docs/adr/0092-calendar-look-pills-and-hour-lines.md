# 0092. The calendar's look: a white card, hour lines only, visits as pills

Status: Decided — owner may veto
Date: 2026-11-20
Issue: the owner on the live calendar — "can we use some kind of purple on calendar as well? everything looks not modern, more like old … I think because of the grid" — with a picture of the planner they like (Cinolla's)

## Context

The timetable drew a line for every quarter of an hour in every column, a hard grey slab outside working hours, a
bordered heading row, and visits as flat rectangles with a thick edge in the staff member's own colour (pink, blue,
green beside the brand's purple). Each choice was defensible; together they read as a spreadsheet from another decade.

The reference shows what carries a modern planner: a white card with room around it, small capital labels, almost no
lines, and bookings as soft rounded pills in a few depths of one colour.

## Decision

- **The grid is a card with air**: rounded, padded, one thin border. Headings are small capitals without a rule
  under every cell; in the week view today is a filled pill of the brand.
- **Hour lines only.** The quarter-hour rows still exist — they are what a visit sits on and what a drag snaps to —
  but they draw nothing. Rows are a quarter taller (1.25rem), so a half-hour visit holds two lines of text.
- **Non-working time is a breath of grey** (`slate-50`), not a slab.
- **A visit is a pill in a depth of the brand**: booked the palest lavender, confirmed the middle one, arrived the
  full brand in white bold; completed and no-show step out of the purple; a time kept for a waiting client is the
  planner's amber. A thin white rim keeps neighbours apart without a line. The status stays in words on visits
  tall enough to say it, so colour alone never carries the meaning.
- **The staff member's colour leaves the visits** and stays as the dot in the column's heading. In the day view
  the column already says whose visit it is; in the week view there is one person. `CalendarBlockStyle` now only
  places a block; every colour is the stylesheet's.
- All of it as named classes in the one stylesheet (`.cal`, `.cal-head`, `.cal-cell`, `.cal-block`, `.cal-kept`,
  `.cal-badge`), so the look can be tuned in one place.

## Consequences

- No behaviour changes: the same links, test ids, drag and drop, phone list for the all-staff day.
- Two contrast faults were caught by axe on the way (hour labels, the status word) and fixed; the page passes on a
  phone and a desk.
- A salon that relied on the coloured edge to tell people apart in the all-staff day view reads the column
  heading instead. If the pilot salon misses it, a small dot inside the pill is a one-line addition.

## Alternatives considered

- Keeping the staff colour as a thin edge: it is the single loudest non-purple thing on the page, on every visit.
- A "now" line and staff photos in the headings: both wanted, both need data the view does not have yet — next.
- FullCalendar's premium resource view: rejected in ADR 0086 for the licence and the bundle.

## Addendum, the same evening: the bar, the booking form, the time buttons

The owner went on through the pages while the look was being built; these belong to the same pass.

- **One bar on the calendar.** "Day · Week · Day list · Waitlist · Book" sat beside tabs that already say "Day list ·
  Calendar · Waitlist": two things called "Day", two ways to the same pages. The bar now holds only what belongs to
  the calendar: Today, a step back and forward, where you are, **any date straight from the browser's own
  calendar** (a date field in the same GET form as the person, so choosing either keeps the other), Day/Week as a
  two-part switch, and Book. The waiting-list link appears in the bar only when somebody is waiting. The week reads
  "16 Nov – 22 Nov 2026". Cost: the day-list tab opens today, not the day the calendar was on.
- **The booking form says why there are no times** — in the voice of a refusal, as the owner asked: "No free times
  with Priya Shah on Saturday 28 November 2026." and a button to the **next day that has one** (looked for over 30
  days, same service and person). "Show times" pressed without a service marks the field and says what is missing
  instead of repeating a grey hint.
- **Time buttons are compact everywhere** (public booking, the desk's booking form, the move form): four across on
  a phone and six on a desk, 44 px high, one shared `.slot-grid`.
- **The public day bar keeps its shape**: both arrows always hold their places (faint when there is nowhere to go),
  drawn as centred chevrons, and every day or week link returns to the bar (`#days`) instead of the top of the page.
- **The phone's bottom bar wears the brand**: purple icons, the current place marked by a line above it.
- Tried and taken back out: a cross-fade between pages (`@view-transition`). It works in the browser but lets the
  E2E walks and their screenshots catch a page mid-fade; it needs the test contexts to ask for reduced motion first.
- **Back in, properly** (the owner again: "not smooth when changing weeks on the book page"): the cross-fade
  returned behind `prefers-reduced-motion: no-preference`, with the booking page's days and times as named parts so
  they glide while the rest stays still. The E2E browser is launched asking for reduced motion, so no walk or
  screenshot meets a fade. Browsers without cross-document transitions (Firefox today) simply navigate as before.
  The day strip inside the bar shows no scrollbar of its own; the arrows page it and a finger still swipes it.
- **Cancelling asks first** (the owner, 2026-11-21: "where is the confirmation … too quick"): on the card and on
  the day list the Cancel button is the summary of a disclosure — it opens "Cancel this visit? The client is told,
  and the time becomes free." with a red "Yes, cancel the visit"; a second tap closes it. No script. The other
  steps (confirm, arrived, complete, no-show) stay one tap.
