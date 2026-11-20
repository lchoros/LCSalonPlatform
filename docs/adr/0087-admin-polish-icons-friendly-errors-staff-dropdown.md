# 0087. Admin polish: menu icons, friendly error pages, a staff dropdown that submits itself

Status: Decided — owner may veto
Date: 2026-11-20
Issue: three items from the owner's improvements queue of ADR 0080 — "where are nice purple icons", "nice user friendly error messages", "is it not better to show staff like a dropdown"

## Context

The dashboard shell had words but no icons; an unknown address, a refused page or a rate limit answered with a
blank screen (the status alone); and the calendar's staff filter was a row of chips that grows with the team. All
three are small alone and belong together as one pass over the admin's manners.

## Decision

- **Menu icons are CSS masks keyed on each link's test id.** No markup changes, no image files, no icon font: a
  data-URI stroke icon per destination, painted with the link's own colour — brand purple at rest, white on the
  chosen item, current colour on the phone's bottom bar. A link without an icon simply has none.
- **An empty error status becomes a page with a sentence** (`UseStatusCodePagesWithReExecute` → `Home/Status/{code}`):
  four voices — not found, not yours to open, too many tries, anything else — each with one way on (the diary for
  signed-in staff, the front page for everyone). The status code is kept. A caller that did not ask for HTML
  (a script's fetch, htmx) still gets the bare status it can act on. The unhandled-error page wears the same card
  and explains the request number as "quote this if you write to us".
- **The calendar's staff filter is a dropdown** in a plain GET form. `auto-submit.js` — the second tiny module —
  submits any `form[data-auto-submit]` on change and hides its fallback button; without script the button does
  the same job. The appointment card's move form uses it too, so choosing a person or a day shows times at once.

## Consequences

- A team of eight takes the room of a team of two on the calendar; a phone gains back a row.
- Scripts stay honest helpers: both modules only press buttons a person could press.
- Tests that read the body of a 404 through the HTML-accepting test client now see the friendly page; the status
  assertions are unchanged.

## Alternatives considered

- Inline SVG in the layout: every link line changes (conflicts with parallel work on the menu) and the icons could
  not follow the link's colour without duplication.
- A custom 404 only: the 403 and 429 blank screens are just as unfriendly.
- Keeping the chips: fine for three people, a wall for ten.