# 0026 — Reports read the ledger and the diary, with the till's own rules

Status: Decided — owner may veto (Tier 2: adds a permission to the role map)
Date: 2026-11-13

## Context
PLAN.md M3 lists "Reports: takings, bookings, no-shows, top services, utilisation". ADR 0024 left one gap on purpose:
a visit completed without the checkout stays unpaid and "nothing chases unpaid completed visits yet (a report)". The
data is all there: money on the client account ledger (ADR 0022 to 0024), visits on `appointments`, working time on
the rota and time off (ADR 0010, 0015). The question was only how to add it up without creating numbers that can
disagree with the till.

## Decision
- **One page, one period.** `/admin/reports` shows takings, visits, fees, completed visits nobody took payment for,
  top services and staff, for one span of salon days. Four one-tap periods (this week, last week, this month, last
  month; a week starts on Monday in both markets) and a pair of dates, at most 366 days. This month is the default.
  An address that cannot be read shows the default rather than failing a bookmark; dates that were given but cannot
  be used say so.
- **Money counts on the day it was recorded, a visit on the day it starts**, both in the salon's zone. That is the
  till's rule (ADR 0024), so the takings of a day in a report are the till of that day, and each day in the report
  links to its till.
- **The till's rules add the money up.** `ReportService` reads the period's ledger lines and sums them in memory
  with `TillRules.Direction` and `TillRules.Totals`: per method, per kind of line and per day. No SQL aggregate, no
  stored totals, no second definition of "takings". A test asserts that a one-day report equals the till of that day.
- **Rates are unknown, not zero, without a base.** The no-show rate is no-shows over visits that had an outcome
  (completed plus no-shows); cancelled visits freed the time and visits still to come have no outcome. Pure
  `ReportRules` in Core.
- **Utilisation is booked time over working time.** Booked: visits that took place or still stand; no-shows and
  cancellations are left out. Working: the weekly rota applied to each day of the period, less whole-day and
  partial-day time off. It can pass 100% when the desk books outside the rota. The meter on the page stops at 100.
- **Completed visits not paid** are those without a payment line whose price the deposit held does not cover. They
  are listed with what is left and a link to their checkout; the count and the sum cover all of them, the list names
  the first 50.
- **A new permission, `Reports.View`.** Owner and Manager have it (the Manager's "everything but users and
  settings" rule covers it); Receptionist and Staff do not. The page also needs `Clients.View`, because the unpaid
  list names clients. A salon's numbers are not for everyone at the desk; the till of the day already is.
- **Money that went out shows a minus sign in front of everything** (−£10.00). `MoneyFormatter` used to produce
  £-10.00; the till's "deposit returned" lines change with it.

## Consequences
- Working time comes from the rota as it is today. A report on an old period is an estimate if the rota changed
  since; the page says where the hours come from. Storing rota history would fix it and is not worth it before a
  salon asks.
- A person signed in before this release does not have the new permission in their cookie and sees no "Reports"
  link until they sign in again.
- Everything is computed per request from a few thousand rows at most. If a salon grows past that, the per-day sums
  are the first thing to move into SQL; the rules stay the same.
- No export, no charts, no comparison with the period before, no per-location split (locations do not exist yet).
- No migration.

## Alternatives considered
- **SQL aggregates per section.** Fewer rows over the wire, but every `GROUP BY` would restate the sign of each
  kind of line, which is exactly the rule that must exist once.
- **Reuse `Payments.Record` for access.** No change to the role map, but it would show a receptionist the salon's
  monthly takings and each colleague's revenue.
- **A report per topic on its own page.** More slices and more navigation for numbers an owner reads together on a
  phone.

## Rollback
Revert the PR: the page, its link and the permission go. No data is written, so nothing is left behind.
