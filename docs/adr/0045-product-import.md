# 0045 — Importing the product list from a file

Status: Decided — owner may veto
Date: 2026-11-16

## Context
A salon that starts with us has its products in a spreadsheet or in its old system: fifty to two hundred lines.
Typing them into the product form one by one is an afternoon nobody has in the week they switch. Clients already
come over from a file (ADR 0019).

## Decision
- **The same three steps as the client import**: upload a CSV file, check a preview, confirm. The preview is kept
  on the server under a random token for fifteen minutes and carries its salon; the browser posts only the token.
- **Columns are found by their headings**, in English or Polish and in any order. Only a name column is needed.
  Brand, code, price, stock and the low stock mark are optional. Other columns are ignored.
- **Numbers are read as a person typed them**: `12.50`, `12,50`, `£1,250.00`, `1 250,00 zł` (`ImportNumbers`). A
  price in the file is in the salon's currency whatever sign stands beside it.
- **A product the salon already has is never changed.** The name decides, without case. A second run of the same
  file adds nothing and changes no price and no shelf. Updating prices from a file is a different, riskier feature.
- **A name repeated in the file is imported once**, the first row.
- **The rules are the product form's** (`ProductRules`, `StockRules`), so what the preview calls new will save.
- **The opening count is a line of the stock history** (ADR 0036), with the note "Imported from <file>" and the
  login that confirmed. The shelf's total is never set directly.
- **One transaction.** If one opening count cannot be recorded, nothing of the file is added.
- **Who:** `Catalog.Manage`, as the product form. No feature flag: the preview is the guard, and the page changes
  nothing that is already there.
- **Where the code is:** the file reader is in `Salon.Plugin.Import` beside the client one and shares its CSV
  reading; it is registered as `IProductImportProvider`, not as a plugin of its own. The token store became
  `TokenStore<T>` in `Salon.Web.Framework`; the client import keeps its own copy for now.

## Not decided here
- Updating existing products from a file. Cost prices and suppliers. Reading the export of one named system.
  Moving the client import onto `TokenStore<T>`.

## Consequences
- No migration.
- Up to 2 000 rows and 2 MB per file.

## Rollback
Revert the PR. Products that were imported stay; they are ordinary products with an ordinary history.
