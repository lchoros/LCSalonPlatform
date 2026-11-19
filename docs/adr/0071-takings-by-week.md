# 0071 — A longer period's takings are drawn week by week

Status: Decided — owner may veto
Date: 2026-11-19

## Context
The report draws the takings of each day as bars (ADR 0048), but only for periods of 2 to 62 days: more bars than
that do not fit a phone. A quarter, a half year or a year (the report allows up to 366 days) had no picture at all,
and those are the periods an owner looks at to see whether the salon is growing or a season is quiet.

## Decision
- **More than 62 days are drawn by week**, Monday to Sunday, with the first and the last week cut at the period's
  ends, so every day of the period is in exactly one bar. A year is at most 53 bars. `ReportRules.ChartWeeks` is
  pure; `TakingsReport.Weekly` holds each week's net takings, a week without money as zero.
- **The same picture as by day**: the same shapes, the same rules for a week with more money out than in, and the
  chart's title says "Takings each week". Pointing at a bar says "2 Mar to 8 Mar 2026: £200.00"; the sentence under
  it names the best week.
- **Nothing else changes**: periods up to 62 days are drawn by day as before, and one day is one number.

## Consequences
- Every period the report can show has a picture, except a single day.
- A cut first or last week has fewer days, so its bar is smaller for that reason alone. The bar's text gives its
  dates.

## Not decided here
- Bars per month for a year; the same period a year earlier; other charts.
