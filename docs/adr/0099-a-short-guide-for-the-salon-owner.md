# 0099. A short guide for the salon's owner, inside the admin

Status: Decided — owner may veto
Date: 2026-11-27
Issue: the owner — "could you create a guide for admins? and do screenshots? so some icon and guide at the bottom of
the menu … I mean salon owner … maybe not all, just a bit"

## Context

A salon owner who signs in for the first time has a menu of ten places and nobody beside them. "Getting started"
(ADR 0068) says what is set up and what is left; it does not say how the everyday jobs are done.

## Decision

- **One short page, `/admin/guide`**, with five sections — the first things an owner does: today's appointments, a
  booking for a client, services and prices, the team, and what clients see when they book. Each has a line of what it
  is for, numbered steps in plain words, a picture of the screen and a button to the page itself. Deliberately not
  everything: the calendar, clients, the till, messages and settings can be added a section at a time.
- **At the foot of the menu**, with an open book for its icon, above the person's own name; in the drawer on a phone.
- **Anybody signed in to the salon may read it.** It shows nothing of the salon's own data.
- **The pictures are of a made-up salon**, taken by a walk in the browser tests (`AdminGuideE2ETests`) that runs only
  when asked (`GUIDE_SHOTS=1`) and writes five small JPEGs into `wwwroot/img/guide` (about 50 KB each, committed).
  When a screen the guide shows changes, run it again, look at the pictures and commit them.
- **The words are resource keys** (`Guide.json`, en-GB and pl-PL) like every other text; the list of sections and
  their addresses is the controller's.

## Consequences

- The pictures go stale when a screen changes and nobody retakes them; the walk makes retaking one command, and the
  ordinary test checks that every picture the guide points at exists.
- The Polish text names buttons as a translator would; it has not been checked against every Polish label on the
  screens (England first, ADR 0038's order of work).
- The pictures show English screens also to a Polish salon.

## Alternatives considered

- **A PDF or a page outside the app**: goes out of date unseen and is not where the owner is when they need it.
- **Tips on each page**: wanted later; a guide is the smaller first step and the place those tips can link to.
- **Screenshots of the live pilot salon**: real clients' names in a public repository.
