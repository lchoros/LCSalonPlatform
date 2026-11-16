# 0049 — What a product costs the salon: margin and the worth of the shelves

Status: Decided — owner may veto
Date: 2026-11-16

## Context
A product has a price to clients (ADR 0036). What the salon pays its supplier was left out of ADR 0036 and ADR
0045 ("not built: cost prices"). Without it the owner cannot see what is kept of a bottle sold, and the accountant's
question at the year end, "what is the stock worth", has no answer.

## Decision
- **`Product.CostPrice`**: optional, in the salon's currency, the same rules as the price (0 to 100,000, two
  decimals). An additive, nullable column.
- **Only the owner and the managers see costs** (`Catalog.Manage`). The desk and the staff see the shelves and the
  prices as before. The handlers read the cost; the controllers take it off the page for anybody else, so it is not
  in the markup either.
- **The product form** has "What it costs you" under the price.
- **The product's page** says the cost and what the salon keeps of each one sold, as an amount and as a share of the
  price (`ProductRules.Margin`). A product sold under its cost is said plainly. A product the salon only uses
  itself has a cost and no margin.
- **The products list** says what the shelves are worth: units times cost, and units times the price to clients
  (`ProductRules.Shelf`). Every product with units counts, in use or not, because a stocktake counts what is there.
  Products with units and no cost are counted and named as missing, so the figure at cost is never taken for the
  whole.
- **Files**: the import (ADR 0045) reads a cost column ("Cost", "Cost price", "Trade price", "Koszt", "Cena
  zakupu" and others); a cost that is not money makes the row invalid. The stock file (ADR 0047) has a cost column
  after the price, so the round trip keeps it. The stock file is already behind `Catalog.Manage`.

## Not decided here
- **Profit in the reports.** The cost can change after a sale, so an honest profit needs the cost of the day on the
  bill line. That is a column on a payments table and the owner's call (Tier 2).
- VAT, cost per delivery (a delivery at a new price), suppliers, the worth of what was used at the chair.
- Demo products have no cost; the list says so, which shows the owner where to type them.

## Consequences
- Migration `ProductCostPrice`: one nullable column on `products`. Nothing existing changes.
- The stock file and the import's example line gain a column; their tests were updated to the new format.
- The worth of the shelves uses the cost as it is today, not what each unit was bought for.

## Rollback
Revert the PR and run the migration's `Down` (drops the column). Costs typed in the meantime are lost; nothing else
depends on them.
