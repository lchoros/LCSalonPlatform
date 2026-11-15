# 0037 — Products on a visit's bill

Status: Decided — owner may veto (Tier 2: payments)
Date: 2026-11-15

## Context
ADR 0036 gave the salon its shelves but no way to sell from them: a bottle sold had to come off as "used" and its
money was nowhere. The owner chose the smaller of two ways to sell: on the bill of a client's visit. A sale to
somebody without a visit needs a line of money without a client, which the ledger does not allow today; that is
left for its own decision.

## Decision
- **A product is rung up at the checkout** of a visit the client turned up for (arrived or completed), by whoever
  may take the payment (`Payments.Record` and `Clients.View`). Only a product the salon still uses and has priced
  above zero is offered. From 1 to 99 units a line.
- **A bill line** (`VisitProductLine`, table `visit_product_lines`) copies the product's name and price when it is
  rung up, so a bill reads the same after the product is renamed or repriced. Ringing the same product up again
  raises the quantity of its unpaid line; a unique index keeps it one line when two tills do it at once.
- **Unpaid is a draft, paid is history.** An unpaid line can be taken off; no money and no stock has moved. A paid
  line points at the money line that settled it and never changes.
- **One payment, two lines of money.** Paying the bill records what is left of the visit as before
  (`VisitPayment`) and the products as their own line, `ProductSale`, with the same method. The till and the
  reports already add up by kind, so they show "Products sold" apart from "Visit paid" with no further change. The
  rule "a visit is paid once" is untouched.
- **A bottle after the visit was paid** is simply rung up on the same bill and paid: a second `ProductSale` line.
- **Stock leaves the shelf when the bill is paid**, not when the product is rung up, in the same transaction as
  the money: a `Sold` line per product, carrying the visit. Until then the bottle may still go back on the shelf.
- **A sale is never refused for a shelf that reads too low.** The client is holding the bottle, so the record was
  wrong. The sale first adds a count line for exactly what the record was short ("Found at a sale", with the
  visit), then the sale. The shelf never goes below zero and the history says what happened. Refusing would stop a
  payment at the desk for a bookkeeping reason that only a manager could fix.
- **A busy shelf does not fail a payment.** The slower of two simultaneous changes to a product is tried again, up
  to three times, against the shelf as it then is. If that still fails, the payment is refused as a whole and
  nothing is recorded.
- **Nothing is charged.** As in ADR 0024, the manual payment plugin only records what the salon took itself.

## Not decided here
- **A sale without a visit** (a walk-in buys a bottle). Next, as its own Tier 2 change to the ledger.
- **Refunds and returns** of a product that was paid for.
- Discounts, VAT lines, receipts.
- Showing a client's purchases on their card; commission on retail per staff member.

## Consequences
- One additive migration, `ProductsOnTheBill`: the table `visit_product_lines` with row-level security, and a
  nullable `appointment_id` on `stock_movements`.
- `ClientAccountEntryKind.ProductSale = 8` and `StockMovementKind.Sold = 5`: new values, no existing row changes.
- Four new refusals with their texts: a product not for sale, a quantity out of range, a paid line, a shelf that
  kept changing.
- The checkout page reads the salon's sellable products for a bill that can be paid: one more small query.
- A price changed between two rings of the same product applies to the whole unpaid line: one line has one price.

## Rollback
Revert the PR and run the migration's `Down`: it drops the table and the column. Money lines of kind 8 and stock
lines of kind 5 written meanwhile would remain as rows the old code does not name; with no production data in
Phase 1 there are none to keep.
