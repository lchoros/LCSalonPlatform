# 0022 — Cancellation policy, no-show and late-cancellation fees, and the client account

Status: Decided — owner may veto (Tier 2: payments)
Date: 2026-11-13

## Context
PLAN.md M2 asks for a "no-show / late-cancel fee with preview and audit, recorded and added to the next bill", and the
M2 gate is that the money adds up with the Manual payment provider while nothing is really charged. ADR 0021 left one
question for the owner: a client could change or cancel online until the visit starts. The owner approved a cut-off
("not within 24 hours") that applies to both and belongs with the fee rules. Nothing recorded money yet:
`IPaymentProvider` existed, nothing called it.

## Decision
- **One policy per salon, off by default.** `CancellationPolicySettings`: `FreeChangeHours` (the notice period; 0 means
  "until the visit starts", today's behaviour), `LateCancellationFeePercent` and `NoShowFeePercent` (of the price as
  booked; 0 means no fee). The owner edits it at `/admin/settings/cancellation-policy` (`Settings.Manage`). A late fee
  without a notice period is refused: it could never apply. A salon that never opens the page behaves as before.
- **The rules are pure** (`Salon.Core/Payments/CancellationPolicy`): inside the notice period or not; the fee to the
  penny, halves up, never above the price; which fee a visit may carry: the no-show fee for `NoShow`, the
  late-cancellation fee for a visit cancelled inside the notice period. A cancellation by either side qualifies, because
  the desk records a client's phone call as "cancelled by salon"; the person at the desk knows which it was.
- **The policy proposes, a person decides.** Nothing is added to a client's account automatically, not even when the
  client cancels late online. Marking a no-show or a late cancellation on the day list or the calendar card leads
  straight to the fee page of that visit (`/admin/payments/fees/{appointmentId}`, `Payments.Record` + `Clients.View`):
  the preview states the policy and the amount, which can be lowered but not raised above the price; "No fee this time"
  simply leaves. This keeps the salon's relationship with a regular in the salon's hands and means no debt ever appears
  that nobody decided on.
- **The client account is an append-only ledger.** `ClientAccountEntry` (table `client_account_entries`, tenant-owned,
  RLS): a fee line (`NoShowFee`, `LateCancellationFee`) or what settles it (`FeePayment`, `FeeWaived`), always a positive
  amount, with who and when. Lines are never edited or deleted; a mistake is corrected by another line (a fee is
  waived with a reason, not removed), so the list is the audit trail. The only update is re-pointing the client when two
  cards are merged (ADR 0017). Two partial unique indexes make "one fee per visit" and "one settlement per fee" a
  database fact, so a double click or two tills end in a refusal, not a second line. Foreign keys are `Restrict`:
  money history outlives a tidy-up.
- **A fee is settled once, in full**: by a payment or by a waiver. Partial payments are left out until the POS needs
  them; a lowered fee covers the common case ("pay half and we are fine").
- **Payments go through the plugin and charge nothing.** "Client paid" calls `IPaymentProvider.RecordNoShowFeeAsync`
  on the Manual provider with the method (cash, the salon's own card terminal, bank transfer, voucher) and stores the
  plugin's system name and reference on the line. No card data, no gateway, no key.
- **"Added to the next bill" without a POS yet.** What a client owes is the sum of their open fees
  (`IClientAccountService.GetAccountAsync`). It is shown on the client card (with every fee and its state) and on the
  card of any later visit of that client ("This client owes … from earlier visits"), which is where the desk looks when
  the client is back. The POS checkout (next in M2) reads the same number and puts it on the bill.
- **What the client sees.** Before booking, the details page states the policy ("Free up to 24 hours before the visit.
  Cancelling later may cost 50% of the price. Missing the visit may cost 100%."). Inside the notice period the change
  page no longer offers times and asks the client to contact the salon; a form left open is refused by the service.
  **Cancelling stays possible** inside the notice period, with the fee said before the client confirms and again once
  it is done: an empty chair the salon knows about is worth more than a no-show, and blocking the button would turn
  late cancellations into no-shows. Outside the notice period nothing changes.

## Consequences
- The owner's approved cut-off applies to both actions: a change is blocked, a cancellation becomes a late one that may
  carry the fee. If the owner prefers cancelling to be blocked too, it is one condition in `CancelByTokenAsync`.
- No automatic charges means a salon must act to collect; the redirect to the preview makes that one tap.
- Waiving is open to everyone who may record payments (Owner, Manager, Receptionist). The waiver names the person and
  needs a reason. A stricter permission can be added when the per-tenant role map arrives.
- The Manual provider is called before the settlement line is written. That is harmless for a provider that only logs.
  An online gateway (M3) must claim the settlement first (insert, then charge with the line's id as the idempotency
  key), or it could charge for a click that loses the race.
- Deposits are not in this change. They will add their own kinds of line to the same ledger (deposit taken, returned,
  kept against a fee) and one more method to the plugin contract.

## Alternatives considered
- **Charge the fee automatically on the event.** Fewer taps, but a debt appears on a regular's account because of a
  mis-click on "No-show", and undoing it needs a waiver with a reason. Rejected as the default; the preview keeps the
  decision human and is still one tap.
- **Block online cancellation inside the notice period.** The literal reading of "not within 24 hours". Rejected for
  cancellation because it hides from the salon exactly the information it needs, kept for changes.
- **Fee status columns on the appointment.** Smaller, but no history, nothing for deposits or the POS to build on, and
  a payment would be an edit. The ledger costs one table.
- **A balance column on the client.** Fast to read, but it drifts and proves nothing. The sum of the lines is the
  balance; a salon's account has a handful of lines per client.

## Rollback
The migration `ClientAccountEntries` only adds a table; `Down` drops it. Reverting the PR restores "change or cancel
until the visit starts" and removes the pages. Settings rows left behind are ignored.
