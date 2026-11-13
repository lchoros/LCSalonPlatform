# 0023 — Deposits: rules per salon, service and client, recorded on the client account

Status: Decided — owner may veto (Tier 2: payments)
Date: 2026-11-13

## Context
PLAN.md M2 asks for "deposit rules per service/client, recorded by staff via ManualPaymentProvider". The glossary
fixes the word: a deposit is a *zadatek* (kept when the client withdraws), not a *zaliczka*. ADR 0022 gave clients an
account: an append-only ledger of fees and what settled them. `BookingSettings.DefaultDepositPercent` existed but
nothing read it. In Phase 1 nothing is charged online, so a deposit is money the salon collects by its own means
(cash, its card terminal, a transfer) and the app only records.

## Decision
- **Three levels, the most specific wins** (`DepositRules`, pure): the client's own percentage, else the service's,
  else the salon's default. Zero at any level means "no deposit" there, so a trusted regular can be exempted
  (client = 0), a problem client can always be asked (client = 100) and one service can opt out of the salon's rule.
  The salon's default is on the policy page (now "Deposits and cancellations"), the service's on the service forms,
  the client's on the client form. All whole percentages of the price.
- **Snapshot at booking.** `Appointment.DepositAmount` is computed when the visit is booked (online or at the desk) from
  the price that person charges, and never rewritten, like the price. Zero means none.
- **The client is told, the booking is not blocked.** The details page says the deposit before the client commits and
  the confirmation repeats it ("The salon will tell you how to pay it"). A booking with an unpaid deposit stays booked:
  without online payment the app cannot know why money has not arrived, and cancelling on its own would lose bookings.
  The visit card shows "£9.00 due, not received" so the desk can chase it.
- **Recorded on the client account.** Two new kinds of line on the ledger of ADR 0022: `DepositPaid` and
  `DepositReturned`, at `/admin/payments/deposits/{appointmentId}` (`Payments.Record` + `Clients.View`). The desk
  records the amount asked for (or another, never more than the price) with the method, through
  `IPaymentProvider.RecordDepositAsync`; a return goes through the new `RecordDepositReturnAsync` (Manual: record
  only; a gateway refunds there at M3). One deposit per visit and one return per deposit are unique indexes, like fees.
  A deposit is taken only for a visit that is still on.
- **A deposit kept counts toward the fee.** That is what a zadatek is: when the client misses the visit or cancels
  late, the deposit the salon holds stays with the salon. The fee page shows the policy's fee in full, says the deposit
  counts toward it and offers only what is left; deposit and fee together never exceed the price. If the deposit
  covers the fee there is nothing to add. A deposit that went back counts for nothing.
- **What a client owes is still only open fees.** A deposit is not a debt and not shown as one; the client card lists
  deposits (held or returned) next to fees.

## Consequences
- The plugin contract grows by one method (`RecordDepositReturnAsync`). Every provider must implement it; today there
  is one.
- Additive migration `Deposits`: `services.deposit_percent`, `clients.deposit_percent`, `appointments.deposit_amount`
  (default 0) and two partial unique indexes. Existing appointments ask for nothing.
- A deposit for a visit that took place is meant to come off the bill. Until the POS checkout exists the desk does
  that by hand; the POS will read the held deposit from the same ledger.
- Polish law doubles a zadatek the *salon* fails to honour. The app does not automate that; the desk returns what it
  decides and the line says how much went back (always the full deposit for now).

## Left for the owner
1. Should an unpaid deposit cancel the booking after some hours? Not done: see above. Easy to add as a schedule task
   once the owner wants it.
2. Should a client with several no-shows be asked for a deposit automatically? Not done: today it is the per-client
   percentage, set by a person.
3. Payment instructions (account number, BLIK phone) are not shown to the client yet; the text says the salon will be
   in touch. A settings text field would do.

## Alternatives considered
- **Fixed amounts instead of percentages.** Simpler to say ("£10"), but wrong for a £15 fringe trim and a £180
  colour alike; a percentage of the booked price scales. Can be added per service later.
- **Deposit as a separate table.** It is money on the client's account with the same audit needs as fees; a second
  table would duplicate the ledger and the POS would have to read two places.
- **Hold the slot only once the deposit is recorded.** Needs online payment to be fair to the client; M3.

## Rollback
`Down` drops the three columns and two indexes; deposit lines would stay in the ledger as unknown kinds, so revert
before any are recorded or delete them by hand. Reverting the PR removes the pages and the fields.
