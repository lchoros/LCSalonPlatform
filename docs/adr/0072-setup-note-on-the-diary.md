# 0072. A note on the diary while set-up is unfinished

Status: Decided — owner may veto
Date: 2026-11-19
Issue: the "Not built" line of ADR 0068

## Context

The getting started page (ADR 0068) says what is set up of a salon and what is left, but a new owner has to know it
exists: the diary is where they live, and nothing there pointed at the steps. ADR 0068 left "a note on the diary
while set-up is unfinished" unbuilt.

## Decision

- The checklist rules move from the getting started slice to `Salon.Services.Settings.ISetupProgressService`
  (`SetupStepState`, `SetupProgress`): two modules now read them, and a slice may not reference another slice.
  `SetupStepKey` moves to `Salon.Core.Settings` beside the settings the steps read. The getting started handler
  keeps only what is its own: which address each step links to.
- A `SetupNote` view component (the `VisitsWithoutOutcome` pattern, ADR 0034) on the day list and the calendar:
  a warning notice, "Your salon is not set up yet: {done} of {required} steps done.", with a link to the getting
  started page. It renders nothing once every required step is done, and nothing for anybody without
  `Settings.Manage`: only the owner could open the page it leads to.
- Nothing is cached and nothing is stored: the same handful of indexed counts the getting started page already runs
  per load, run per diary load for the owner only. If a pilot salon's diary ever feels it, the terminal "all done"
  state can be cached per tenant then.

## Consequences

- The owner is nudged exactly while there is something to do, and the nudge retires itself from the salon's own data.
- A manager or receptionist never sees a link they cannot open.
- The getting started page and the note can never disagree: one service answers both.

## Alternatives considered

- A dismissable banner: a box to store, a box to forget, and a salon half set up with no nudge. The data already
  says when to stop.
- Caching the counts: premature; the getting started page itself reads them fresh per load today.
- The note on every admin page: the diary is where an owner starts the day; everywhere else it would be noise.