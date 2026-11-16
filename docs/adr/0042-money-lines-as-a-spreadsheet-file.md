# 0042 — The money lines of a period as a spreadsheet file

Status: Decided — owner may veto
Date: 2026-11-16

## Context
The reports (ADR 0026) show totals. A salon's accountant wants the lines behind them, in a file. "The salon owns
its data" is the product's promise, and until now the money could be read on a screen only.

## Decision
- **One file: every line of the period that moved money**, oldest first. One row per line: date, time, client, what
  it was for, the service of the visit, how it was paid, the amount, the currency, who recorded it and the
  reference. Money out (a deposit or a product returned) is a negative amount.
- **The same lines the report adds up.** The file is read from the ledger with the till's own rule for "moved
  money", for the same period, so the file's sum is the report's takings.
- **CSV, written for the reader's spreadsheet.** Column names, kinds and payment methods are in the reader's
  language. The separator and the decimal sign follow their culture: a comma and a point in Britain, a semicolon and
  a comma in Poland. Dates are ISO, times are 24-hour. UTF-8 with a byte order mark, so Excel reads the letters.
- **Typed text cannot run as a formula.** A cell that a person typed and that starts with `=`, `+`, `-` or `@`
  gets an apostrophe in front; the amount, which the application formats itself, keeps its minus. `Csv` in Core is
  pure and unit tested.
- **A button on the report**, "Download for a spreadsheet", beside the takings, for the period on the screen. It is
  not shown while the period has no money.
- **The report's own permissions**: `Reports.View` and `Clients.View` (owner and manager). The rows name clients.
  The answer is never cached.

## Not decided here
- Other files: visits, clients, stock. An Excel file instead of CSV. A scheduled email with the file.

## Consequences
- No migration. `IReportService` gains `GetMoneyLinesAsync`; a new slice `Reports/ExportMoney`.
- A file on somebody's laptop is outside the platform's control. It holds client names and amounts, no phone
  numbers, no notes.

## Rollback
Revert the PR. Nothing is stored by it.
