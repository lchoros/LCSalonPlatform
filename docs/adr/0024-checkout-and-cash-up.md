# 0024 — Checkout and cash-up on the client account ledger

Status: Decided — owner may veto (Tier 2: payments)
Date: 2026-11-13

## Context
PLAN.md M2 ends with "POS checkout + cash-up", and its gate is that "deposits, no-show fees and the cash-up add up
with the Manual payment provider. Nothing is really charged." ADR 0022 and 0023 put fees, their settlements and
deposits on one append-only ledger per client, each money line carrying a payment method. What was missing is the
ordinary case, a client who came and pays, and a way to see that a day's money adds up.

## Decision
- **The bill of a visit is computed, not stored.** Price as booked, less the deposit the salon holds for the visit,
  is what is left to pay (`TillRules.Outstanding`). The client's open fees from earlier visits are listed on the same
  page, ticked by default; that is how a fee is "added to the next bill" (ADR 0022). Unticking one leaves it on the
  account.
- **One payment, one method, once.** `/admin/payments/checkout/{appointmentId}` (`Payments.Record` + `Clients.View`)
  records a new kind of line, `VisitPayment`, for what is left of the visit, through
  `IPaymentProvider.RecordInSalonPaymentAsync`, and settles each ticked fee with its own `FeePayment` line in the same
  transaction. One payment per visit is a partial unique index, so a double tap is refused, not doubled.
- **Paying ends the visit.** Only a visit the client turned up for (arrived or completed) can be paid. Paying an
  arrived visit completes it in the same transaction, so the desk does not tap "Complete" as well. A visit that did
  not take place has no bill; its page points to the fee instead.
- **The till is a view of the ledger.** `/admin/payments/cash-up?date=` adds up, for one salon day in the salon's
  zone, every line that carries a method: deposits in, visit and fee payments in, deposit returns out
  (`TillRules.Direction`). Totals per method, takings, and the cash the drawer should hold. There is no second set of
  books to drift from the first, which is what makes the M2 gate hold by construction.
- **A day is closed once, with a count.** The person types the cash counted and, if it differs, a note. `CashUp`
  (table `cash_ups`, RLS, unique per salon and day) stores what the lines said at that moment, the count and who
  closed it. The row is the claim: a second close is refused and the first count stands. A day that has not come
  cannot be closed.
- **Money recorded after a close is allowed and flagged.** A late entry is not blocked (the desk must be able to
  record the truth); the closed day then shows what the drawer should hold now next to what it held at the count.
- **Record only.** No gateway, no card data, no keys. The Manual provider logs and returns a reference, which the
  line keeps.

## Consequences
- Staff can still tap "Complete" without the checkout. The visit is then completed and unpaid; the checkout stays
  available from the visit card and the client's history. Nothing chases unpaid completed visits yet (a report, M4).
- No reversal. A payment recorded with the wrong method or for the wrong visit cannot be undone in the app; the
  cash-up note is where the difference is explained. Reversals belong with refunds (M3, Tier 2).
- No split payments, tips, discounts, price changes at the desk or retail products. Each is an addition to the bill
  computation and a new kind of line, none changes what exists.
- The count is of the day's takings only; an opening float is not modelled.
- Additive migration `CheckoutAndCashUp`: table `cash_ups`, one partial unique index and one plain index on
  `client_account_entries`.

## Alternatives considered
- **Invoice and payment tables.** The textbook shape, and needed once there are products, VAT lines and split
  payments. Today a bill is one service; a stored invoice would duplicate the appointment and the ledger and give the
  two something to disagree about.
- **Totals stored per day as payments happen.** Faster to read, but it is a second set of books. A salon day has
  tens of lines; summing them is instant and cannot be wrong.
- **Block entries on a closed day.** Tidy, but it forces the desk to record a late payment on the wrong day or not at
  all. Flagging keeps the books honest.

## Rollback
`Down` drops `cash_ups` and the two indexes. Visit-payment lines would stay in the ledger as an unknown kind, so
revert before any are recorded or delete them by hand. Reverting the PR removes both pages and their links.
