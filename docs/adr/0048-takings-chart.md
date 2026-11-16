# 0048 — The takings of each day as a bar chart, without a script

Status: Decided — owner may veto
Date: 2026-11-16

## Context
The reports (ADR 0026) list the takings of each day as figures. An owner wants to see the shape of a week or a
month at a glance: which days carry the salon and which are empty. "Charts" was left out of every report ADR so
far. The working agreement allows no new JavaScript framework and no bundler, and a chart library is a new runtime
dependency (Tier 2).

## Decision
- **One chart: the takings of each day of the period as bars**, in the takings section, above the figures. One bar
  per day in order, with or without money, so a week always has seven slots and an empty day is seen as empty.
- **Plain SVG shapes written by the server.** No script, no library, no style attribute. Where the bars go is
  worked out by `BarChart` in Core, which is pure and unit tested: numbers in, rectangles out. `TakingsCharts`
  beside the view turns the report into the shapes and the words (ADR 0043: the view only writes them down).
- **The best day reaches the top; zero is a line.** A day with more money out than in (a deposit returned) hangs
  under the line in red. A day with a little money is drawn at least two units tall, so it is not taken for an
  empty one.
- **The numbers of the shapes always use a point as the decimal sign**, whatever the reader's language. They are
  SVG attributes; "12,5" is not a number there. A test with the Polish salon keeps it so.
- **Said in words too.** The picture has one sentence as its accessible name and under it: the best day and its
  takings. Each bar says its day and amount when pointed at. The list of days with their figures and links to the
  till stays below, so nothing is only in the picture.
- **Periods of 2 to 62 days.** One day is one number. More than two months of bars do not fit a phone.

## Not decided here
- Bars per week or per month for long periods. Other charts: visits, staff, products. The same period a year
  earlier in the same picture. Tapping a bar to open that day's till: at 31 bars on a phone a bar is too narrow to
  be a fair target, and the list below has the links.

## Consequences
- No migration, no new dependency. `TakingsReport` gains `Daily`; `ReportRules` gains `ChartDays`.
- The chart's colours come from the stylesheet's own classes, so it follows the page's look.

## Rollback
Revert the PR. Nothing is stored by it.
