# 0044 — Products in the reports

Status: Decided — owner may veto
Date: 2026-11-16

## Context
The salon sells products on a visit's bill (ADR 0037) and takes them back (ADR 0040). The reports (ADR 0026) showed
that money only as two lines of "What it was for". An owner could not see which product sells, how many units left
the shelf, or how much of the takings is retail.

## Decision
- **A "Products sold" section on the report**, shown only when the period has a product sale or return.
- **The money is the ledger's.** "Sold" is the period's `ProductSale` lines and "Given back for returns" its
  `ProductReturn` lines, the same lines the takings add up. A product counts on the day it was paid for, a return on
  the day the money went back, like every other number of the takings.
- **The units come from the bill lines those sales paid for.** A paid `VisitProductLine` points at its sale, and a
  sale's amount is the sum of its lines, so product by product adds up to "Sold". A test holds this.
- **Best sellers by value**, at most ten, grouped by product under the name it has today. A line says so when more
  products were sold than are listed.
- **Share of takings** is products after returns as a part of everything taken (`ReportRules.Share`): unknown while
  nothing was taken, never below 0 or above 100, and not given for a period with returns only.
- **Returns are a sum of money, not units per product.** A return line points at a sale, and a sale can hold several
  products, so the ledger does not say which product came back on which day. Showing it per product needs a new
  column on the return line. That is an altering change to the money table for a number few salons will ask for.

## Not decided here
- Units returned per product and day. A file of product sales for a spreadsheet. Retail per staff member (who sold
  it): the bill line knows who rang it up, not who recommended it.

## Consequences
- No migration; one more query on the report when something was sold.
- `SalonReport` gains `Retail`.

## Rollback
Revert the PR. No data changes.
