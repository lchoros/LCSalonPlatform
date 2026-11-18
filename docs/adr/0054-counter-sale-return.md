# 0054 — Giving the money back for a counter sale

Status: Decided by the owner (2026-11-16: "go with what is most suitable"; named as next in line in run 12); Tier 2
because it changes the guard on the money ledger
Date: 2026-11-16

## Context
A product sold at the counter (ADR 0053) could not be returned in the system: there is no bill to return it from.
The desk gave the money back by hand and corrected the shelf with a count, so the till was right only by accident
and the reports still showed the sale.

## Decision
- **The return works like the return on a visit's bill** (ADR 0040): the money goes back at the price the line was
  sold at, as one `ProductReturn` line that points at the sale, and the units go back on the shelf only when they
  can be sold again.
- **The return line has no client either.** The database guard of ADR 0053 is widened by one kind: a line may lack
  a client only if it is a product sale or a product return with no appointment. Every other line still needs one.
- **`CounterSaleLine.ReturnedQuantity`** says how many units came back; never more than were sold (a check
  constraint).
- **Two tills cannot give the money back for the same unit.** The units are claimed in one statement that only
  succeeds while the buyer still has them; the slower request is refused. A test races six.
- **The desk finds the sale by its day.** A counter sale has no client and no visit, so `admin/payments/sales?date=`
  lists the counter sales of a day, newest first, with who took them and what was given back so far. Each line that
  can still come back has the return form. Linked from the counter sale page. `Payments.Record`, like the sale.
- **In the reports** the money of the return is counted on the day it was given back, like any return, and the
  profit of the sale counts only the units kept (ADR 0052).
- **`IStockService.ReturnAsync` takes an optional appointment**, as `SellAsync` already does.

## Not decided here
- A reason for the return, a time limit, an exchange in one step: as for ADR 0040.
- Finding a counter sale by product or by amount instead of by day. A receipt with a number to look it up by.
- The return on a visit's bill still adds up its returned units without the one-statement claim. Two tills at once
  can at worst both be refused by the database's cap; making it the same claim is a small follow-up in payments.

## Consequences
- Migration `CounterSaleReturn`: one column with a default of 0 on `counter_sale_lines`, one check constraint on
  it, and the ledger's check constraint replaced by the wider one. No row changes.
- The limit of ADR 0053 stays: the ledger cannot go back to "every line has a client" while a counter sale or its
  return exists.

## Rollback
Revert the PR and run `Down` while no counter sale return exists; `Down` puts the narrower guard back and fails,
on purpose, if a return without a client is there.
