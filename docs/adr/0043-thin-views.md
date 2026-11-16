# 0043 — Thin views

Status: Decided — owner may veto
Date: 2026-11-16

## Context
The owner opened the checkout view and asked whether the block of code at its top is normal. Partly: a view may say
which title and which CSS classes it uses. It should not work out totals, build addresses or carry its own
formatting. 21 of 58 views had an `@functions` block, and the same six helpers were copied between them: a time
format in seven views, a "messages for this field" lookup in six, an ISO date in five.

## Decision
A view says **what** to show. Where the answer comes from:

| The view needs | It asks |
| --- | --- |
| a number worked out from the model | a property on the model or the service record (`VisitBill.ToPayWithFees`) |
| an address | a static method on the controller that owns or links the route (`CashUpController.DayPath`) or the view model |
| a time, a date for an address, hours, a dash for "nothing" | `Salon.Services.Formatting` (`ClockFormatter`, `DayFormatter`, `MoneyFormatter`) |
| which colour a status wears, which state a tile is in | `Salon.Web.Framework.Ui.Badges` |
| the messages of a field, the class of an invalid field | `ModelState.ErrorsFor`, `ModelState.InvalidClass` |
| whether a menu link is the current area | `Request.Current(area)` |
| words that depend on a value and are used by one page | a small static class next to the view (`ImportText`, `ReportText`, `WorkingHoursText`, `TimeOffText`, `CalendarBlockStyle`) |

What stays in a view: the page title, constants for class names used several times on that page, and a variable
that holds an answer one of the above gave.

- **No `@functions` in any view.** An architecture test refuses it, and four patterns that the shared formatters
  replace (`LocalDatePattern.Iso.Format(`, `InstantPattern.ExtendedIso.Format(`, `.ToString("t", `,
  `ViewData.ModelState.TryGetValue(`).
- **Three badge palettes for a visit's status are kept as they were**: on the visit's own card every status has
  its colour; in a list of the day only "confirmed" and "arrived" stand out; in a client's history "completed" is
  green. They are now named and in one file, so making them one palette is a one-line decision for later.

## Consequences
- No behaviour change: 677 integration and 47 browser tests pass unchanged.
- 22 views shrank by about 150 lines of code; the helpers are unit tested for the first time.
- A new view has nowhere to hide logic, which is the point.

## Rollback
Revert the PR.
