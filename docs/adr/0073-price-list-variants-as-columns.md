# 0073. Variants as columns in the price list file

Status: Decided — owner may veto
Date: 2026-11-19
Issue: the "Not built" line of ADR 0066

## Context

A salon's price list often has one treatment at several prices — "Keratin" for long and short hair. The catalogue
models this (ADR 0032): sibling `Service` rows share a `VariantGroup`, each with its `VariantLabel`, and `Name`
stays the full "Keratin · Long hair". The price list file (ADR 0066) knew nothing of it: a variant exported as its
full name in one cell, and a file could not create a proper variant — a row "Keratin · Long hair" became a plain
service that never grouped on the booking page.

## Decision

- The file gains one optional column, **Variant** (aliases: Variant, Option, Wariant, Opcja). A row with it is the
  service "Name · Variant": the name cell is the shared title, the variant cell what tells this one from its
  siblings. `ImportedServiceRow.FullName` composes it in one place with `ServiceRules.VariantSeparator`.
- The classifier matches, caps and de-duplicates by the full name, so two variants of one name are two services,
  not a repeated row. A new service from a variant row is saved with `VariantGroup` and `VariantLabel`, so it
  groups on the lists and the booking page. New problem: `VariantTooLong` (label over 100 or group over 150); a
  composed name over 200 stays `NameTooLong`.
- The export writes the two columns the same way: the Service cell is `VariantGroup ?? Name`, the Variant cell is
  `VariantLabel`. The salon's own file reads back with nothing to do, variants included.
- A service the salon already has is still matched by its full name and keeps its own grouping: the file changes
  the time, the price and the deposit, nothing else (ADR 0066). Regrouping by file is not built.

## Consequences

- A spreadsheet price list with a variant column reads in as real variants; the booking page shows one entry with
  a price range, as if the owner had typed them in the form.
- A file without the column behaves exactly as before; the example on the upload page now names the column.
- The round trip stays whole: export, edit in a spreadsheet, read back, nothing to do.

## Alternatives considered

- Splitting "Keratin · Long hair" in the name cell on the separator: silently wrong for salons whose names use
  "·" for something else; a column is explicit.
- One column per variant ("Long hair","Short hair" as price columns): a different file shape per salon, unreadable
  by headings.
- Letting the file regroup existing services: the form owns names and grouping (ADR 0066 drew this line).