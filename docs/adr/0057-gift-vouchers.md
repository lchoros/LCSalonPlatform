# 0057 — Gift vouchers: sold at the desk, used toward a visit

Status: Decided by the owner (2026-11-16: "continue with recommended, I will not veto anything"); Tier 2 because it
is payments and changes the money ledger
Date: 2026-11-16

## Context
A salon sells gift vouchers, most of them in the weeks before Christmas. Until now the system only had "Voucher" as
a word in the list of payment methods: nothing said which voucher, how much was on it, or whether it had been used
before. A voucher is money the salon has taken for a visit it still owes, so it has to be right.

The checkout takes one payment per visit (ADR 0024). A voucher is often worth less than the bill, or more.

## Decision
- **A voucher is sold for money today.** One line of the ledger, `VoucherSale`, with a method (cash, card, bank
  transfer) and no client: the buyer is rarely the person who will use it. It is money in on the till and in the
  takings of that day.
- **A voucher has a code, an amount, what is left, and a last day.** Ten characters from an alphabet without 0, O,
  1, I and L, drawn from the system's secure random source, printed in two groups of five. Whoever holds the code
  holds the voucher, like a paper one. It is good for twelve months unless the desk says otherwise; an empty date
  means it never expires.
- **Using a voucher moves no money.** At the checkout the desk types the code. What is left to pay for the visit,
  or all the voucher has if that is less, goes onto the bill as a `VoucherRedeemed` line on the client's account,
  with no method. The bill counts it like a deposit the salon holds: the visit has that much less left to pay. The
  rest is paid as usual, in one payment, so ADR 0024 stands. What is left on the voucher stays on it for next time.
- **The money is counted once.** The takings have the sale on the day the voucher was sold and nothing on the day
  it is used. A visit a voucher covered in full is not a "completed visit nobody paid for".
- **A visit a voucher covers in full is ended**, as paying would have ended it: no second tap on "Complete".
- **Nobody uses more than a voucher has.** What is left is claimed in one statement that only succeeds while the
  money is there, and a voucher goes onto a visit once (a unique index). A test races six requests.
- **The money goes back only for a voucher nobody has used**, once, as a `VoucherRefund` line with a method and no
  client. A used or expired voucher is not given back by the system.
- **The same sale form sent twice is one voucher**: the form carries the id of its line of money, as the counter
  sale does (ADR 0053).
- **The database guard is widened by two kinds**: a line may lack a client only if it is a product sale or return
  at the counter, a voucher sale or a voucher refund, with no appointment. A voucher's use always has a client and
  a visit.
- **The list says what the salon owes**: the sum of what is left on vouchers that can still be used.
- **Who**: `Payments.Record` (owner, manager, receptionist) sells, looks up, uses and gives back.

## Not decided here
- **Vouchers pay toward the visit, not toward products on the bill, and not at the counter.** One sale line covers
  all products of a bill (ADR 0037); splitting it between a voucher and a method is its own change.
- Taking a voucher off a bill it was put on by mistake. Extending or re-issuing an expired voucher. What happens to
  the money on expired vouchers in the books.
- Selling vouchers online, sending one by email, a design to print. A voucher for a named service instead of an
  amount.
- "Voucher" stays in the list of payment methods for paper vouchers sold before this; it tracks nothing.

## Consequences
- Migration `GiftVouchers`: a new table with the tenant policy, the ledger's check constraint replaced by the wider
  one, one unique index on the ledger for a voucher's use. No row changes.
- Three new kinds of ledger line; the families of lines gain one (`ClientAccountRules.IsVoucher`).
- `VisitBill` knows what vouchers were used toward the visit; the unpaid-visits report counts it.
- An owner can see at any moment how much the salon has been paid for work it has not done yet.

## Rollback
Before any voucher is sold: revert the PR and run `Down`. After: `Down` fails on purpose while a voucher sale or
refund without a client exists, as for counter sales (ADR 0053). In that case revert the pages so no more are sold
and keep the schema; vouchers already sold must still be honoured.
