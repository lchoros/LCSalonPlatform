# 0052 — Profit on products: the cost of the day is kept on the bill line

Status: Decided by the owner (2026-11-16: "go with what is most suitable"); Tier 2 because it adds a column to a
payments table
Date: 2026-11-16

## Context
A product has a price and, since ADR 0049, a cost. The reports (ADR 0044) say what was sold. They do not say what
the salon kept, which is the number an owner decides by: which products are worth the shelf. Taking the product's
cost as it is today would rewrite the profit of every past sale each time a supplier changes a price.

## Decision
- **`VisitProductLine.UnitCost`**: what one cost the salon on the day the line was paid, copied from the product in
  the same transaction that records the money. Nullable: null while the line is unpaid, and for a sale made when
  nobody had said what the product costs. It never changes afterwards.
- **Profit is the price less that cost, for the units the client still has** (`ProductSaleRules.Profit`). A unit
  brought back earns nothing.
- **A unit without a cost is not guessed at.** It is in neither the profit nor the share, and the report says how
  many such units there are. With no cost on any unit the report states no profit at all, not zero.
- **In the reports**, in the products section: "Profit on products" as an amount and as a share of what those units
  were sold for, and the profit of each best seller. A loss keeps its minus.
- **Sales made before this change have no cost on their lines.** They are counted as units without a cost. Nothing
  is filled in after the fact: the cost of that day is not known.
- **Who sees it**: whoever sees the reports (owner and manager), who also see costs (ADR 0049).

## Not decided here
- VAT. The cost of what was used at the chair. Profit per staff member. A file of product sales with the profit.
- Filling in the cost of old sales from today's cost, as an action the owner takes knowingly.

## Consequences
- Migration `ProductLineUnitCost`: one nullable column on `visit_product_lines`. Nothing existing changes.
- `PayBillAsync` reads the cost of each product on the bill once, when it is paid.
- A return made in a later period lowers the profit shown for the period of the sale, because the line then has
  fewer kept units. The money of the return is still counted on its own day, as before.

## Rollback
Revert the PR and run the migration's `Down` (drops the column). The costs kept on lines in the meantime are lost.
