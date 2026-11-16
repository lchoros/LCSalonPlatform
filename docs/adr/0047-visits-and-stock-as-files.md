# 0047 — The visits of a period and the stock list as spreadsheet files

Status: Decided — owner may veto
Date: 2026-11-16

## Context
ADR 0042 gave the salon its money lines as a file and left the other files open: visits, clients, stock. An
accountant asks for the visits behind the takings. A stocktake is done on paper against a list. A salon that opens a
second chair wants its product list there without typing it again. "The salon owns its data" is the product's
promise.

## Decision
- **Two more files, written exactly like the money file** (ADR 0042): CSV, column names and words in the reader's
  language, their list separator and decimal sign, ISO dates, UTF-8 with a byte order mark, typed text made safe
  against formulas, never cached. The writing sits once in `SalonControllerBase.CsvDownload`; the money file now
  uses it too.
- **Visits: one row per visit that starts in the period**, oldest first, whatever became of it: date, time, client,
  service, with whom, minutes, status, booked online or at the salon, the price booked, the currency. The same
  visits the report counts, so the file has as many rows as the report says visits. A button in the report's visits
  section, for the period on the screen, not shown while the period has no visit. Behind `Reports.View` and
  `Clients.View`, like the money file: the rows name clients.
- **Stock: every product with what is on the shelf now**, by name: name, brand, code, price, currency, in stock,
  low stock at, in use. Products the salon stopped using are in the file, marked "No". The file is named after the
  salon's day. A button on the products page beside the import, behind `Catalog.Manage`.
- **The stock file's headings are the ones the import reads** (ADR 0045), in both languages. So the file of one
  salon is the product list of another, and a test proves the round trip. The import ignores the columns it does
  not know (currency, in use).

## Not decided here
- **A file of clients.** It would hold phone numbers, email addresses and consent. That is the owner's call
  (personal data), so it is not built.
- Stock history as a file, product sales as a file, an Excel file instead of CSV, a scheduled email.

## Consequences
- No migration. `IReportService` gains `GetVisitLinesAsync`; two new slices, `Reports/ExportVisits` and
  `Products/ExportProducts`.
- A file on somebody's laptop is outside the platform's control. The visits file holds client names, no phone
  numbers and no notes. The stock file holds no personal data.
- A product read back by the import in another salon arrives as "in use", whatever the file says: the import has no
  such column, and a salon importing a list wants to use it.

## Rollback
Revert the PR. Nothing is stored by it.
