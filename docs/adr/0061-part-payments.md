# 0061 — Paying one bill two ways: a part first, then the rest

Status: Decided by the owner's standing word (2026-11-16: "continue with recommended, I will not veto anything";
2026-11-18: "please continue"); Tier 2 because it changes how a visit is paid
Date: 2026-11-18

## Context
"Twenty in cash and the rest on the card" is said at every till every day. The checkout takes one payment per
visit (ADR 0024), so the desk had to record the whole bill under one method and the till's cash was wrong by the
other part. The salon's own methods (ADR 0060) made this more common: a prepayment by PayPal, then cash, then card.

A deposit already works like a part paid ahead (ADR 0023), but a visit has one deposit, taken before the visit, and
the pilot salon's visits often have one already.

## Decision
- **A part is its own kind of line: `PartPayment`.** Money in, toward the visit, on the client's account, with a
  method. It counts toward the bill as a deposit held and a voucher used do: the visit has that much less left to
  pay. No new table and no new column: a kind is a number in the ledger.
- **A part is less than what is left.** Above zero, to the penny, and strictly less than what is left to pay for
  the visit. All that is left is not a part, it is the payment. So a part never ends a visit, and ADR 0024 stands:
  the visit is settled by one `VisitPayment`, which is what was left after the parts.
- **Any number of parts, each by its own method**, built-in or the salon's own.
- **A part is toward the visit's price.** Products on the bill and fees from earlier visits are paid with the rest,
  by the method of the final payment. One sale line covers all products of a bill (ADR 0037); splitting it is not
  done here.
- **Nobody takes more than the visit costs.** Recording a part, and paying the rest, first take a lock on the
  visit's row for the length of the transaction, so two requests go one after the other and the second sees what
  the first wrote. A test races six requests for £15 of a £20 visit: one fits. Without the lock the test fails.
- **The same form sent twice is one part.** The form carries the id of its line, as the counter sale and the gift
  voucher do.
- **Counted once, under each method.** The till, the cash-up, the reports and the money file take a part like any
  line that moved money: cash in the drawer is the cash part. A completed visit with a part paid and the rest not
  owes what is left, not its whole price.
- **Who**: whoever takes payments (`Payments.Record` with `Clients.View`), on the checkout.

## Not decided here
- **Taking a part back.** A part recorded by mistake is corrected at the till by hand (give the money back, pay the
  rest as it should have been); the ledger has no line for it yet. A visit payment cannot be undone either.
- A part toward products or fees. A tip.
- A part before the client has arrived: that is a deposit (ADR 0023).
- Typing both amounts in one form. Two steps keep each line one decision and one click.

## Consequences
- No migration.
- `IClientAccountService.PayPartAsync`; `VisitBill.PartPayments` and `PartPaid`; `Outstanding` counts them.
- Slice `Payments/PartPayment`; a form on the checkout.
- `ClientAccountRules.IsVisitPayment` covers both kinds; `TillRules.Direction` counts a part as money in.
- The unpaid-visits report counts a part as paid toward the visit.
- ADR 0060's "paying one bill by two methods" is decided here.

## Rollback
Revert the PR. Parts already recorded stay in the ledger as kind 13; the old code would not count them toward a
bill, so after the first real part is taken, revert the form only (nobody can record another) and keep the rest.
