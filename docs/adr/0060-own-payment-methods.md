# 0060 — The salon's own payment methods

Status: Decided by the owner (2026-11-18: "let me know once we have functionality for salon owners to add own
payment methods"); Tier 2 because it changes the money ledger
Date: 2026-11-18

## Context
The system knew four ways money changes hands: cash, the salon's card terminal, a bank transfer and a paper
voucher. They were a fixed list in the code. A real salon takes money in other ways too. The pilot salon takes
prepayments by PayPal; others have a second card machine, take Amex apart from other cards, a cheque, Klarna. Until
now such money was recorded under the nearest wrong name, so the till and the reports could not say how much came
by PayPal.

Phase 1 charges nothing: a payment line records money the salon took itself (ADR 0022, ADR 0024).

## Decision
- **A salon names its own methods.** A new tenant-owned record, `SalonPaymentMethod`: a name of up to 40
  characters, in use or not, in the salon's order. Up to twenty in use. The owner adds, renames and stops using
  them on a settings page (`/admin/settings/payment-methods`, `Settings.Manage`).
- **A method only names how money the salon took itself changed hands.** Nothing is charged through it; no
  account, key or address is asked for or stored. Connecting a real payment account (Stripe, PayPal's own API) is
  a different thing, stays parked, and would be a payment plugin, not a row in this list.
- **The four built-in methods stay, and the owner can switch three of them off.** Cash cannot be switched off: the
  cash-up counts the drawer. Switching one off takes it out of the lists; what was recorded with it is untouched.
- **A ledger line says which method and what it was called.** `PaymentMethod` gains `Other`. A line paid by one of
  the salon's own methods has `Method = Other`, the id of the method (`CustomMethodId`) and its name on that day
  (`MethodName`). The name is kept on the line because the ledger is the record of what happened: a rename does not
  rewrite it. The database refuses a line that has one of the three without the others, and a method id that is
  not a method.
- **Every place that takes or gives back money offers the salon's list**: the checkout, a deposit and its return,
  a fee, a product brought back, a counter sale and its return, a gift voucher's sale and refund. The lists come
  from one place (`IPaymentMethodService.OptionsAsync`, rendered by `<payment-method-options />`), so a method the
  owner adds is offered everywhere at once.
- **A posted choice is checked where the money is recorded.** Every service that writes a payment line resolves the
  choice first. An id the salon does not have, another salon's id, or one of its own taken out of use records
  nothing and the desk is told in words.
- **Each own method is its own line** in the till, the cash-up, the report, the daily summary and the money file,
  under its name. A renamed method is one line, under the name on its latest line. An own method is never in the
  drawer: expected cash counts cash only.
- **A method that money was recorded with is never deleted**, only taken out of use; one never used is removed.
  Adding a name the salon had before brings the same method back, so its history stays one line.
- **A name must be new**: not another method's, whatever the capitals, and not the name of a built-in method in
  either language.
- **Code that names a built-in method reads as before.** `PaymentChoice` is what a form posts (a built-in method by
  name, an own method by id); a `PaymentMethod` converts to it by itself. No existing test was changed.

## Not decided here
- **A built-in method the owner switched off is hidden, not refused.** A form loaded before the switch can still
  record with it. Money is not lost by that, and refusing would need the same check for refunds of old lines.
- An own method that counts as cash in the drawer (a second till), or one that needs a reference typed.
- The order of the methods chosen by the owner; they are listed in the order added.
- Which methods the public booking page offers: it takes no money in phase 1.
- Fees per method (what PayPal keeps), so the profit could say what a method costs.
- Paying one bill by two methods (ADR 0024 stands: one payment per visit).

## Consequences
- Migration `OwnPaymentMethods`: a new table with the tenant policy; two nullable columns, a foreign key and a
  check constraint on the ledger. No existing row changes: every line already there has a built-in method or none.
- `IPaymentMethodService` in `Salon.Services`; the three services that record money take a `PaymentChoice`.
- Read models that carry a method also carry the own method's name; views write a method through
  `PaymentMethodNames.Name`.
- Three typed settings (`PaymentMethodSettings`) for the built-in switches.
- A test found two faults on the way: a posted "Cash,Voucher" would have been read as a method, and the first
  database check let through a line with no method but an own method's name. Both are closed and tested.

## Rollback
Before any own method is used: revert the PR and run `Down`. After: lines with `Method = Other` would lose which
method they were, so revert the settings page only (nobody can add or pick one) and keep the schema.
