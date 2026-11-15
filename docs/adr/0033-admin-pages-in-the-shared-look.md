# 0033 — The admin pages in the shared look

Status: Decided — owner may veto
Date: 2026-11-15

## Context
ADR 0032 gave the public booking flow a look: white surfaces with a thin two-colour border and a soft glow, squared
tiles, dark chosen states. The admin pages kept the plain style they were built in, each view carrying its own long
strings of utility classes (the same button string appeared in 28 places, the same field string in 25). The owner
asked for the whole product in the look of his reference pages, with compact, square controls, and called the
system the pilot salon uses today "pretty basic".

## Decision
- **One vocabulary for both sides.** The admin views use the classes of `app.css` instead of their own utility
  strings: `btn` with `btn-primary`, `btn-secondary` or `btn-danger`, `field`, `field-label`, `field-error`, `notice`
  with `notice-ok`, `notice-warn` or `notice-error`. A change of the look is now a change of one file.
- **One size down at the desk.** `btn-compact` and `field-compact` keep admin controls 44 px tall with 14 px text on
  buttons; the public flow keeps 48 px. The desk works with a mouse as often as with a thumb, and 44 px is still
  the touch target the mobile checks ask for.
- **New words:** `page-title` (a page's name with a short mark in the brand colours), `panel` (a section, with the
  border and glow of ADR 0032, neutral unless it takes an accent), `list-card` (rows that belong together),
  `empty-state`, `back-link`, `nav-link`, and `stat` with `stat-label` and `stat-value` (one number that matters).
- **The menu says where you are.** The link of the area the page belongs to is the dark one and carries
  `aria-current="true"`. It is "true" and not "page" because the link names an area: on "Add a service" the
  marked link is "Services", which is not the page itself. The calendar lives under the appointments address, so
  the day list is marked only when the calendar is not.
- **A line in the brand colours** across the top of every page, painted as the header's background. It is not an
  element of its own, so the page gains nothing that carries no content.
- **Reports** open with four tiles (taken, visits, no-show rate, not paid), each with an accent, and lay their
  sections out in two columns on a desk. The numbers are the ones the sections below already state; no new query.
- **Tabs and chips** (settings pages, report periods, people on the calendar) are filter tiles, as on the booking
  page.
- Accents are used sparingly in the admin: the report tiles and the two money panels of the till. Everything else
  is neutral, so colour keeps meaning something.

## Consequences
- 35 admin views changed their class attributes and nothing else: no handler, query, address, test id or text
  changed, and no string was added in either language.
- Buttons in the admin are a little smaller in text (14 px, was 16 px) and bolder; corners are rounder.
- A view that needs a control the vocabulary lacks adds it to `app.css`, not to the view.
- The appointment card and the two time pickers keep a few local utility strings (the slot buttons); they are next
  if the look changes again.

## Alternatives considered
- **`aria-current="page"` on the menu.** The usual value for a menu, but wrong on every page deeper than an area's
  first, and it would make "no report period is chosen" indistinguishable from "the Reports link is marked" for
  anything that reads the page.
- **A side menu.** More room for eight areas on a desk, but a second layout to keep right on a phone, where the desk
  staff also work. The wrapped row is kept.
- **A component library.** A new runtime dependency (Tier 2) for what is twelve small classes.
- **Restyling by hand, view by view.** The strings were identical, so they were replaced by a script and the result
  read as a diff; the few that differed were done by hand.

## Rollback
Revert the PR. No data, migration or setting is involved.
