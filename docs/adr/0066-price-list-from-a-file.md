# 0066 — The price list from a file, and as a file

Status: Decided — owner may veto; raised to Tier 2 by the agent because one confirm changes many prices at once
Date: 2026-11-18

## Context
A salon that starts with us has a price list of fifty to two hundred services in its old system or in a
spreadsheet. Clients (ADR 0019) and products (ADR 0045) come over from a file; services had to be typed one by one
into the service form. The pilot salon's list is in the demo data with guessed times, and correcting it by hand is
an afternoon nobody has in the week they switch.

The same salon raises its prices once or twice a year. In the service form that is one page per service.

ADR 0045 left "updating from a file" out for products as "a different, riskier feature". For a price list it is
the point, so the risk is met here: nothing changes before the owner has read, row by row, what would.

## Decision
- **The same three steps as the other imports**: upload a CSV file, check a preview, confirm. The preview is kept
  on the server under a random token for fifteen minutes and carries its salon; the browser posts only the token.
- **Columns are found by their headings**, in English or Polish and in any order: the service's name (the only one
  that is needed), category, minutes, price, deposit %. Other columns are ignored. Numbers are read as a person
  typed them (`ImportNumbers`): `45`, `45 min`, `£45.00`, `120,50 zł`, `20%`.
- **A service the salon does not have is added**: under the file's category (found without case, added when the
  salon has none of that name), at the end of the list, in the file's order, in the salon's currency. It needs its
  minutes and its price.
- **A service the salon has is updated**, matched by its name without case, in use or not: its **time, its price
  and its deposit** take the file's values. An empty cell leaves that value as it is. Nothing else changes: not
  the name, the category, the description, whether it is in use, or who does it.
- **The preview shows every change as it is and as it will be** ("£45.00 → £47.50"), and counts what is added,
  what changes, what stays and what is left out with the reason. The button says how many of each. A file that
  would do nothing has no button.
- **Nothing is ever removed**, and a service the file does not name is left alone. A file is an addition and a
  correction, never a replacement.
- **Visits already booked keep their price**: a visit stores the price of the day it was booked. A person's own
  price or time for a service (ADR 0018) is not touched; the upload page says both.
- **The rules are the service form's** (`ServiceRules`, `DepositRules`): five-minute steps, 5 to 600 minutes, a
  price to the penny within the cap, a deposit from 0 to 100. A service with waiting time inside it cannot be made
  shorter than that time needs. A name said twice in the file is taken once, the first row.
- **Decided again at the confirm.** The rows are classified a second time against the services as they are then,
  so a service added by hand since the preview is updated, not added twice.
- **The salon's own file can be read back.** `admin/services/price-list.csv` is the price list with the headings
  the import knows, in the reader's language, separator and decimal sign (ADR 0042, ADR 0047). Download, change the
  numbers in a spreadsheet, upload. The file is also on "Your data" (ADR 0051).
- **Who:** `Catalog.Manage`, as the service form. No feature flag: the preview is the guard.
- **Where the code is:** the file reader is in `Salon.Plugin.Import` beside the other two, registered as
  `IServiceImportProvider`; the rules of a row are the pure `ServiceImportClassifier`; `IServiceImportService`
  previews and commits; slices `Catalog/ImportServices` and `Catalog/ExportServices`.

## Not decided here
- Variants ("Keratin · Long hair") as two columns: a variant is read as a service with its full name.
- Changing a name, a category or whether a service is in use from a file. Removing services that are not in it.
- A person's own prices from a file. Reading the export of one named system.
- Undoing a confirmed file. The preview is the moment to stop; the previous file, read in again, puts the old
  numbers back.

## Consequences
- No migration.
- Up to 1 000 rows and 2 MB per file.
- A `ServiceCreated` or `ServiceUpdated` event per service, as from the form.
- The log has the counts and ids; never a file name or a service's name.

## Rollback
Revert the PR. Services that were added stay; they are ordinary services. Prices that were changed stay changed;
the salon's file from before, read in again, restores them (or the service form does, one by one).
