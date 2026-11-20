# 0084. The dashboard: today at a glance as the admin landing page

Status: Decided — owner may veto
Date: 2026-11-20
Issue: the owner's order of 2026-11-20 — "a proper dashboard landing (today's bookings, takings, occupancy at a
glance) belongs to this shell" (ADR 0080)

## Context

The shell of ADR 0080 gave the back office a side menu and a bottom bar, but no front door: after signing in, staff
landed on the public price list a client reads, and `/admin` itself was a 404. The owner asked for the page an owner
or a receptionist opens first: what is booked today, who is next, what was taken, how full the day is, and the
everyday actions one tap away. Phone first.

## Decision

- **A new slice, `Features/Dashboard/Today`, at `/admin`**, behind `[RequireTenant]` and `Bookings.Manage`, which
  every role has. It reads and never writes. Signing in without a return address now lands here
  (`LoginController.DefaultLanding`); a local return address is still followed and anything else still ignored. The
  sidebar and the bottom bar list "Dashboard" first, and the sidebar's brand leads to it. The menu marks it as the
  current place only at exactly `/admin`, because every other admin address lies under it.
- **The numbers come from `Salon.Services`, not from the slice** (a slice may not reference another, and the till and
  the reports already own these sums): a new `ITodayService` returns the day in one call.
  - *Visits*: every visit that starts today in the salon's zone, counted by status; cancelled by the client and by
    the salon are one number here.
  - *Next visits*: the visits of today that still hold their time (booked, confirmed, arrived) **and have not
    ended**, soonest first, at most five, with how many more there are. A client who is five minutes late or in
    the chair is still the desk's next concern, so "has not started" would be the wrong rule; a visit whose time
    has passed is not next, whatever its status says (the "visits without an outcome" notice of ADR 0034 covers
    those).
  - *Occupancy*: per person who takes bookings, booked minutes (visits that took place or still stand) over today's
    rota less time off: the report's own rule. The working-minutes sum and the "which completed visits are unpaid"
    rule moved out of `ReportService` into an internal `ReportSums` that both services call, so the report of today
    and the dashboard cannot drift apart. Somebody without working time today reads "Not in today" instead of a
    bar; the salon-wide figure counts only people who are in.
  - *Money*: the day's ledger lines that carry a method, added up with the till's own rule
    (`TillRules.TotalsByMethod`), as the report does; a test holds the result against `ICashUpService` for the same
    day. One query and nobody named, where asking the till itself would read every movement with the client's full
    name. Unpaid completed visits use the shared rule above.
- **A role sees only what it may open, and nothing else is read.** The controller turns the permission claims into
  four flags on the query; the handler asks the service for exactly those parts and builds every address.
  - `Clients.View` (the diary's own gate): first names beside the next visits and each row a link to the visit's
    card. Without it (the Staff role) the rows are plain text with the service and the person only.
  - `Payments.Record` + `Clients.View` (the till's gate): the money panel and the "Taken today" tile. Without it
    there is no money on the page in any form; a "Completed" tile takes the place of the takings tile.
  - `Reports.View` + `Clients.View`: the link to today's report, where every unpaid visit is listed.
  - `Settings.Manage`: the way to the getting started page.
- **Unpaid visits are listed here, not only counted.** The one existing place that lists them is the report, which a
  receptionist may not open. So the money panel names the first five (time, first name, service, what is left) with
  a "Take payment" link to the checkout, which the till's permissions already open, and adds "See them in Reports"
  for those who may. The count and the sum always cover all of them.
- **First names only.** No surname, phone number or email address is read for this page; the service never selects
  them.
- **An honest empty state.** A salon with nobody who takes bookings and nothing booked today has no day to show: the
  page says so instead of a row of zeros, and the owner gets the set-up progress (`ISetupProgressService`, ADR 0068)
  with the page's one strong button to getting started. Anybody else is told the owner has to finish first, with no
  link they could not open. Once the salon works, an unfinished set-up is a quiet panel *under* the day, so the day
  leads; it disappears when every required step is done.
- **Look**: the shared component classes only (`panel`, `stat`, `list-card`, `btn`, `notice`, `empty-state`) and the
  brand names of ADR 0080 (`bg-brand-soft`, `bg-brand-bright`, `hover:bg-brand-mist`); no new tokens, no script. The
  occupancy bar is a `role="progressbar"` element whose width the handler works out. `<widget-zone
  name="admin_dashboard_top">`, declared since M0, finally has its page.

## Consequences

- Staff start the day on their own numbers. The sidebar's brand used to lead to the salon's public home page; it now
  leads here, so the shell itself no longer links to the public pages (getting started still links the booking page).
- The dashboard, the till and the report of today add up from the same code; a change to one rule changes all three.
- One page load costs the day's visits, the staff with rota and time off, the day's money lines when money is shown, and
  up to three small name lookups. Nothing is cached; a salon's day is a few dozen rows.
- "Now" is the server clock in the salon's zone at the moment of the request: the page does not refresh itself. A
  reload is the refresh until the first page script exists (ADR 0080 queue).
- Tier 1: a new read-only screen, no migration. Rollback is reverting the PR; the only behaviour that changes
  outside the new page is where a sign-in without a return address lands.

## Alternatives considered

- Calling `IReportService.GetAsync(today, today)` for everything: it answers a different question (top services,
  retail, comparison with the day before — a dozen queries nobody looks at here), names people rather than
  identifying them, has no colours and leaves out people with an empty day. Sharing the two sums was smaller.
- Linking the unpaid count to the report for everybody: a dead end for a receptionist, who takes most payments.
- "Next" as visits that have not started: drops the late client and the one in the chair exactly when the desk
  needs them.
- A feature flag: the page replaces a 404 and the landing of a sign-in; a flag would leave two landings to test and
  nothing to protect — the page writes nothing.
- Redirecting `/admin` to the day list: the day list needs `Clients.View`, so the Staff role would land on
  "denied".

## Not built

- Auto-refresh, a "now" line, tomorrow at a glance, takings against the same weekday last week (the report has the
  comparison), staff photos (ADR 0080) and a per-person view for the Staff role ("my day").
- The "Salon admin" button on the public pages still leads to the day list, not here: it is outside this order's
  shell edits and worth one line when the owner agrees.
