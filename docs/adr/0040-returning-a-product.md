# 0040 — Returning a product that was paid for

Status: Decided — owner may veto (Tier 2: payments)
Date: 2026-11-15

## Context
ADR 0037 let the desk sell a product on a visit's bill and said a paid line never changes. Clients bring things
back: the wrong shade, an unwanted gift. Without a return the till is wrong by the money handed back and the shelf
is wrong by the bottle.

## Decision
- **A return is recorded on the paid line** of the bill it was sold on, by whoever may take a payment there. It
  sits behind a disclosure ("Return this product"), so it is never one stray tap away.
- **Part or all of a line.** The desk says how many came back, from one to what the client still has. The line
  counts them (`VisitProductLine.ReturnedQuantity`); a check constraint keeps it within what was bought.
- **The money goes back as its own line**, `ProductReturn`, for the units times the price the line was rung up
  at, not today's price. It carries the method the money went back with and points at the sale. The sale line is
  never edited: the ledger stays append-only.
- **The shelf gets the unit back only if it can be sold again.** A ticked box ("It can be sold again") adds a
  `Returned` stock line with the visit. An opened or damaged product gets no stock line: it left the shelf when it
  was sold and it does not come back.
- **The till and the reports** count a return against the day it was given back, by its method, as they do a
  deposit returned. "Products returned" appears beside "Products sold".
- **The client card** shows what the client still has; a line returned in full is no longer listed.
- **The payment plugin** gains `RecordRefundAsync`. The manual plugin only records that the salon gave the money
  back itself; a gateway will refund here.

## Not decided here
- A reason for the return. A time limit for returns. Exchanging one product for another in one step (it is a
  return and a new sale). Returning a product sold without a visit (no such sale exists yet).

## Consequences
- One additive migration, `ProductReturns`: `returned_quantity` on `visit_product_lines`, default 0, with its
  check constraint.
- `ClientAccountEntryKind.ProductReturn = 9`, `StockMovementKind.Returned = 6`, two new refusals with their texts.
- A day on which more cash went back than came in shows a negative "cash expected", which is true.
- Also in this change, because the same view was open: the checkout view lost its small calculations and address
  building to the bill and the view model, and the three payment views share one date formatter. No behaviour
  changed.

## Rollback
Revert the PR and run the migration's `Down`. Returns recorded meanwhile would remain as ledger rows of a kind the
old code does not name; with no production data in Phase 1 there are none to keep.

## Amendment (2026-11-16): the returned units are claimed in one statement
Found while building ADR 0054: two requests for the same return at the same moment (a double click on "Record the
return", or two tills) could both read "nothing returned yet" and both record the money given back. A test that
starts six returns of one bottle at once saw four of them recorded. The cap in the database stopped the count of
returned units going over, but not the extra lines of money.

The units are now claimed by one `UPDATE ... WHERE quantity - returned_quantity >= n`, before the refund is recorded.
Only one request can win; the others are refused as "more than the client still has". No schema change.
