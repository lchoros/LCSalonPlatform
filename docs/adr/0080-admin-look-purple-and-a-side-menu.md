# 0080. The look we are growing towards: purple, a side menu, a dashboard

Status: Decided by the owner — the shell and palette are being built from 2026-11-20 (the friend likes the MVP)
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
  a vertical menu on desktop instead of today's top nav row. On mobile, the Booksy pattern the owner pointed at
  (2026-11-20, second look — this supersedes the first instinct of drawer-only): a **slim bottom bar with the
  everyday destinations** (the diary, clients, the till, and a "menu" item) plus the **same full menu as a slide-in
  drawer** for everything else. Inside a module, the Versum pattern: **section tabs** (as their Inventory shows
  Products / Sales / Usage / Deliveries / Orders / Stocktaking) instead of burying sections in the main menu.
  A proper dashboard landing (today's bookings, takings, occupancy at a glance) belongs to this shell.
- **The sign-in page keeps the Booksy-style card as its future shape**: one centred card, a Login / Sign up toggle
  (once sign-up is public), one strong primary button. Later the same day the owner asked to "keep in mind Google
  sign-in as Booksy" — so the card is designed with room for a "Continue with Google" button, and Google sign-in
  itself is a Tier 2 feature for when the owner orders it; until then email and password only (ADR 0075/0076).
- **Staff profile photos** belong to this direction (the owner, 2026-11-20): the Booksy diary and staff picker lean
  on faces. A photo on the staff member — diary columns, the public "who with" picker, the team page — is its own
  slice later (file upload and storage are new ground); the shell leaves room for the avatars.
- **The salon's portal is the product, not a marketplace** (the owner, 2026-11-20): the platform-level public
  website and open sign-up stay completely off — too few salons for a marketplace to mean anything, and the focus
  is the appointment portal (this web app and, next, the mobile apps). `Features:SignUp` stays false and the
  platform pages stay dark.
- **The public booking flow keeps its own meaning-colours.** The emerald "free time" accent of ADR 0079 stays a
  semantic colour (free/available); when the purple theme lands, purple takes the chrome (buttons, steps, selected
  person) and emerald keeps meaning availability — unless the owner, seeing it, decides purple swallows it all.
- **Booking steps are labelled and nothing advances without Continue** (the owner, 2026-11-20, after walking the
  live flow twice): each progress bar carries its name (Service · Time · Your details), and tapping a time selects
  it on the page — the Continue button is what moves to the details form. This supersedes the three-tap instant
  advance of ADR 0079.
- **A light and a dark theme may both be needed later** (the owner, 2026-11-20): every colour stays in the shared
  component classes of `app.css`, never as per-view literals, so a dark variant can land as one stylesheet layer.
- **What the platform-level web ever needs** (the owner, 2026-11-20): only a sign-in page for customers and for
  salons, and later the place where a salon buys its subscription (the SaaS billing of the M3 plan). First, in the
  owner's words, "get admin and staffing calendars tip top".

## Consequences

- This is Tier 1 work when it starts, screen by screen: first a shared layout shell (sidebar + drawer), then the
  palette swap via the shared component classes of ADR 0032/0033 — the class vocabulary means most screens recolour
  centrally in `app.css`.
- Until then, new screens keep today's vocabulary; nobody mixes a third look in the meantime.

## Alternatives considered

- Drawer-only on mobile (no bottom bar): the owner's first instinct, withdrawn the same day after looking at
  Booksy's mobile shell — the bottom bar carries only the three or four everyday destinations, so the diary keeps
  its height while the desk never hunts through a drawer for the pages they open fifty times a day.
- Rebranding ad hoc, screen by screen, as PRs happen to touch things: rejected — the shell-then-palette order keeps
  every intermediate state coherent.
