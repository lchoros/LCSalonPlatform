# 0097. A client booking app: native screens over a public booking API

Status: Decided by the owner in direction (2026-11-25); the shape below is the Orchestrator's — owner may veto
Date: 2026-11-25
Issue: the owner — "let's go with mobile apps, focus on that book process for now only"

## Context

ADR 0081 planned a staff app in stages and left a client app out ("the owner decides if ever"). The owner has now
decided: an app, and the booking process first. The booking flow has just been reworked on the web (ADR 0094 to
0096) and its rules live in the services: availability for a visit, a person for each service, the code by text.
An app must use those rules, not copy them.

## Decision

- **A client app for one salon's booking**, on .NET MAUI (ADR 0081's choice of toolkit stands): native screens for
  services → time → details → code → done. No marketplace, no discovery: the app is told which salon it belongs to
  (its host) when it is built.
- **A public booking API, `/api/v1/booking`**, on the salon's host: the same things the public pages do, as JSON.
  Anonymous like those pages, tenant by host like those pages, the same rate limits. Six endpoints
  (`docs/mobile/BOOKING-API.md`). The server decides; the app shows.
- **One definition of the shapes**: `src/Libraries/Salon.Api.Contracts` — plain records with no dependencies,
  referenced by the web host and by the app.
- **The app's logic is a plain library**, `src/Mobile/Salon.Mobile.Core`: the API client and the state of each screen,
  with unit tests and no MAUI in it. `src/Mobile/Salon.Mobile` is the screens on top.
- **Not in this version**: signing in, a client's own list of visits, moving or cancelling in the app (the manage link
  opens the web), payments, push, the staff diary.

## Consequences

- **Tier 2**, for two reasons ADR 0081 named: a new runtime (the MAUI projects in the repository) and an `/api/v1`.
  This API carries no sign-in and no tenant choice of its own, which is the narrow reading of that concern — the
  owner confirms it by merging. Nothing is published to a store by an agent.
- The MAUI project builds only where the workloads are installed. It is kept out of the main solution's build and
  gets a CI job of its own later, so a mobile break never blocks a web merge (ADR 0081).
- iOS needs a Mac to build; Android and Windows build on the owner's machine.
- The owner still has to buy the store accounts (Apple $99 a year, Google Play $25 once) before anything ships.

## Alternatives considered

- **A WebView around `/book`** (ADR 0081's stage 1 pattern): days of work instead of weeks, one place for every
  fix — and an app that is a web page in a frame. Kept as the fallback if native screens prove too slow to keep up.
- **The app calling the HTML pages**: no contract, breaks with every look change.
