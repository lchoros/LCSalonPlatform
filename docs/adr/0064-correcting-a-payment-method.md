# 0064 — Putting right a payment recorded under the wrong method

Status: Decided by the owner's standing word (2026-11-16: "continue with recommended, I will not veto anything";
2026-11-18: "please continue"); Tier 2 because it adds lines to the ledger of money and replaces a check
constraint on it
Date: 2026-11-18

## Context
"I pressed cash, it was card" is the commonest mistake at a till. Until now it could not be put right: the ledger
is append-only (ADR 0022), so the line said cash for ever, the cash-up came up short by that amount, and the
reports counted the money under the wrong method. With the salon's own methods (ADR 0060) and part payments
(ADR 0061) there are more methods to press by mistake. ADR 0060 and ADR 0061 both named this as not built.

## Decision
- **The line is never edited. Two lines are added.** `MethodCorrectionOut` takes the line's amount off the method
  it stands at; `MethodCorrectionIn` puts it on the method it was really paid by. Both point at the line they
  correct (`RelatedEntryId`) and carry its amount, client and visit. For money that went out (a deposit returned,
  products returned, a voucher given back) the two are the other way round.
- **The day's takings do not change; each method's total becomes right.** `TillRules.Direction` counts the two
  halves one each way. The till, the cash-up, the reports, the daily summary and the money file add up by method
  and direction already, so they are right without knowing what a correction is. A property test checks that the
  till after a correction is the till the day would have had with the right method from the start.
- **Only a line of today.** A correction counts on the till of the day it is made. One made today for a line of
  last week would change today's drawer, which never held that money. So a line of another day cannot be corrected
  here; the page says why. Backdating ledger lines was rejected: `At` is when a line was written and stays that.
- **After the day was closed too.** The count is often how the mistake is found. The count stands as it was made,
  and the till already says "money was recorded after the day was closed, the drawer should now hold X".
- **Any line that moved money**: a visit paid in full or in part, a deposit received or returned, a fee paid,
  products sold or returned on a bill or at the counter, a gift voucher sold or given back. Not a fee added or
  waived or a voucher used (no money moved), and not a correction itself: the line is corrected again instead.
- **To any method the salon has that the line does not stand at already**, built-in or its own
  (`IPaymentMethodService.ResolveAsync`). A gift voucher's own money is never moved to "voucher" (ADR 0057).
- **Corrected again is allowed.** The latest correction is where the line stands. Correcting back to what was
  recorded at first is two more lines, not a deletion.
- **One correction at a time.** The service takes a lock on the line's row for the length of the transaction, so
  two requests go one after the other and the second finds the line already at that method. A test lets 24
  requests go at the same moment, three on each of eight payments: each payment is moved once. Without the lock
  the test fails every time.
- **The same form sent twice is one correction.** The form carries the id of its first line.
- **Every page that says how something was paid says the corrected method**: the bill (payment, parts, products),
  the deposit, the fee, the client's purchases, the counter sales of a day, the gift voucher's card. One helper,
  `MethodCorrections.LatestAsync`, answers "where do these lines stand" for all of them. The till shows the line
  as it was recorded, with "corrected to …" under it, and the two lines that moved the money.
- **Who**: whoever takes payments (`Payments.Record` with `Clients.View`, as the till itself).
- **Where**: a link on each line of today's till opens a page for that one line (`/admin/payments/corrections/{id}`):
  what it is, what it was recorded as, and one choice. A form on every row was rejected: on a phone a till of
  thirty lines would be thirty lists.

## Not decided here
- **A wrong amount.** This moves an amount between methods; it does not change it. A visit payment cannot be undone
  and a part cannot be taken back (ADR 0061): that is a refund, with its own rules, and waits for the owner's word.
- A line of an earlier day. The accountant's correction of a closed month is not a till action.
- A reason typed by the person. Who and when are on the lines.
- Money taken through a payment plugin that charges (M3). When a gateway records a line, the method is the
  gateway's fact and such a line must not be offered here.

## Consequences
- Two ledger kinds, 14 and 15. No new table and no new column.
- **Migration `MethodCorrections`** replaces the check `ck_client_account_entries_client_unless_counter_sale` so
  that a correction of a line without a client (a counter sale, a gift voucher) may be without a client too.
  Nothing else changes; existing rows all pass the wider check.
- `MethodCorrectionRules` (pure), `ClientAccountRules.IsMethodCorrection` (a seventh family of kinds),
  `IMethodCorrectionService` (`GetAsync`, `CorrectAsync`), slice `Payments/CorrectMethod`.
- `CashMovement` gains `EntryId`, `CanCorrect`, `CorrectedTo`.
- The takings by kind in the reports leave the two halves out: together they are nothing.
- A method of the salon's own that money was once corrected to counts as used and cannot be removed, only taken
  out of use (ADR 0060).
- The client's copy of their data (ADR 0062) lists the two lines among the money lines; they hold nothing about
  the client beyond what the corrected line holds. The privacy notice does not change.

## Rollback
Revert the PR while no correction has been recorded. The migration's `Down` puts the narrower check back and would
fail if a correction of a line without a client exists. After the first real correction, take the link and the page
out (nobody can record another) and keep the kinds, the rules and the check: the old code would read a corrected
day's totals correctly, but would list the two lines without a name for their kind.
