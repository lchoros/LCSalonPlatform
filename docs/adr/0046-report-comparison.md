# 0046 — The report against the period before

Status: Decided — owner may veto
Date: 2026-11-16

## Context
A report said what a period took, not whether that is good. "£2,400 this month" means nothing until it stands
beside last month. The plan listed "comparison with the period before" as not built (ADR 0026).

## Decision
- **A period is set against the same days one period earlier** (`ReportRules.Compare`).
- **Only days that have happened count, on both sides.** On the 13th, "this month" is the 1st to the 13th against
  the 1st to the 13th of the month before, not thirteen days against a whole month.
- **A whole calendar month goes back one month**, whatever the two lengths: March against February. **Anything
  else goes back by its own number of days**, so a week is set against the same weekdays of the week before and a
  Saturday never stands against a Tuesday.
- **Three figures:** total taken, completed visits, no-shows. All three are already past by nature.
- **The change is in words**, "Up 35%", "Down 12.5%", "No change", so it reads without colour or an arrow. A rise
  from nothing has no percentage and says "Nothing before".
- **No verdict.** The page does not call a change good or bad: more no-shows is "Up", and the owner knows what
  that means.
- **Shown only when either side has something**, and not at all for a period that has not begun.

## Not decided here
- The same period a year earlier, which is the fair comparison for a seasonal trade and needs a year of data.
  Charts. A comparison per staff member or per service.

## Consequences
- Two more small queries on the report. No migration.
- `SalonReport` gains `Comparison`.

## Rollback
Revert the PR. No data changes.
