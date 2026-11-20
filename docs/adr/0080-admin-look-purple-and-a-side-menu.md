# 0080. The look we are growing towards: purple, a side menu, a dashboard

Status: Decided by the owner — direction only, nothing built yet
Date: 2026-11-20
Issue: the owner tested the live pilot against Booksy and Cinolla and set the visual direction

## Context

With the pilot live, the owner put our screens next to the products they respect and said what to keep and what to
grow towards. Nothing here is a build order: this ADR exists so that every future screen is designed against one
recorded direction instead of each PR guessing.

## Decision (the owner's direction, 2026-11-20)

- **Purple is the brand colour.** Like Cinolla's look (cinolla.com): a violet/indigo primary on light surfaces,
  carried through buttons, active navigation, the calendar's accents, progress steps — wherever today's screens use
  the stone-900 black primary. The Tailwind violet/indigo range is the palette to draw from when this is built.
- **The admin becomes a dashboard with a left side menu**, the way Booksy Biz and Cinolla lay out their back office:
  a vertical menu on desktop instead of today's top nav row, and the same menu as a slide-in drawer behind a
  hamburger on mobile — the owner weighed a bottom bar and chose the drawer, because the diary needs every pixel of
  phone height. A proper dashboard landing (today's bookings, takings, occupancy at a glance) belongs to this shell.
- **The sign-in page keeps the Booksy-style card as its future shape**: one centred card, a Login / Sign up toggle
  (once sign-up is public), one strong primary button. No social sign-in — email and password only, as decided in
  ADR 0075/0076.
- **The public booking flow keeps its own meaning-colours.** The emerald "free time" accent of ADR 0079 stays a
  semantic colour (free/available); when the purple theme lands, purple takes the chrome (buttons, steps, selected
  person) and emerald keeps meaning availability — unless the owner, seeing it, decides purple swallows it all.

## Consequences

- This is Tier 1 work when it starts, screen by screen: first a shared layout shell (sidebar + drawer), then the
  palette swap via the shared component classes of ADR 0032/0033 — the class vocabulary means most screens recolour
  centrally in `app.css`.
- Until then, new screens keep today's vocabulary; nobody mixes a third look in the meantime.

## Alternatives considered

- Bottom tab bar on mobile (the common app pattern): rejected by the owner — vertical space on the diary matters
  more, and one drawer serves both widths with one menu to maintain.
- Rebranding ad hoc, screen by screen, as PRs happen to touch things: rejected — the shell-then-palette order keeps
  every intermediate state coherent.
