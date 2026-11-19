# 0068 — Getting started: what is set up of a salon and what is left

Status: Decided — owner may veto
Date: 2026-11-19

## Context
Everything a salon needs before its first client can now be done: its details (ADR 0027), the team and their
hours, the price list (by hand or from a file, ADR 0066), clients (ADR 0019), the coming visits (ADR 0067),
reminders (ADR 0020), a policy for late cancellations (ADR 0022) and a privacy notice (ADR 0063). It is spread over
a dozen pages, and nothing told a new owner, or Lukasz setting up the pilot salon, which of them were done.

## Decision
- **One page, `/admin/settings/getting-started`, the first tab of the settings.** Owner only, like the other
  settings.
- **Every step is read from the salon's own data, not ticked by hand.** So the list is never out of date, needs no
  table, and a step done anywhere else is done here too:
  - the salon's details: the main location has a phone number or an email;
  - the people who take bookings: at least one;
  - working hours: everybody who takes bookings has some (the count of those without is shown);
  - the price list: at least one service in use;
  - clients: at least one in use;
  - coming visits: at least one booked from now on;
  - reminders: switched on;
  - the privacy notice: saved at least once (the page's own "last updated" date), which means it was read;
  - late cancellations and no-shows: a fee or a notice period set. **Optional**, not counted toward "all done":
    a salon may charge nothing.
- **Each step links to where it is done**, and, while it is not done, to the file import where there is one
  (price list, clients, diary; the last two only when the client import is switched on).
- **The page ends with the booking page as a client sees it**: the one check the data cannot make.
- **No migration, no writes, no feature flag**: it only reads, and it shows nothing a salon cannot already see.

## Not decided here
- Showing the page, or a "set-up is not finished" note, on the diary or after signing in.
- Steps for payment methods, messages' wording, gift vouchers, products: they have sensible defaults or are not
  needed to open.
- Platform-side checks (a domain, a real text sender): the owner's, at M3.

## Consequences
- One slice, `Settings/GettingStarted`, with its query handler reading counts through the tenant filter.
- An architecture test lists each step's title and both of its sentences, so a new step cannot ship without them.

## Rollback
Revert the PR. Nothing was stored.
