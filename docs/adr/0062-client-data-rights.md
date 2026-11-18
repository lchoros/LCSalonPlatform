# 0062 — A client's rights over their data: a copy, and being forgotten

Status: Decided by the owner's standing word (2026-11-16: "continue with recommended, I will not veto anything";
2026-11-18: "please continue"); Tier 2 because it is personal data and erasure
Date: 2026-11-18

## Context
ADR 0017 left this for M3: "Long-term retention and erasure requests ('forget this client') are decided at M3."
ADR 0058 said the same about consultation forms. A salon in England that holds a client's name, number, health
notes and visit history will sooner or later be asked two things: "what do you hold about me?" and "delete me".
UK GDPR gives the client both rights, with a month to answer. A salon that has to say "our software cannot do
that" has a problem; a salon that can do both from the client's card owns its data in the sense this product
promises.

The salon must also keep its books. A visit that took place and what was paid for it are business and tax records;
erasing them would make the till, the reports and the commission of past months wrong.

This is how the system behaves, not legal advice. Whether a particular request must be met, and how long records
are kept, is the salon's decision.

## Decision

### A copy
- **One page with everything the salon holds about the client** (`/admin/clients/{id}/data`): details, notes,
  health notes, consent and its wording, every visit with its notes, every line on their account, products on
  their bills, every consultation form with its answers, every waitlist request, and how many duplicate cards were
  merged into this one. The owner prints it or saves it as a PDF from the browser and hands it over.
- **Owner and manager** (`Clients.Manage`). It shows health data, so it is never cached and making one is logged
  by id: who made a copy of whose data.

### Being forgotten
- **The row stays, the person goes.** The client's row is kept, inactive, with "Erased client" in the salon's
  language in place of a name, and `ErasedAt` and `ErasedByUserId`. Everything else on it is cleared. Visits and
  lines of money keep pointing at it, so every total is the same before and after.
- **What happens to each table**:

| Table | What it holds about a client | On erasure |
| --- | --- | --- |
| `clients` | name, phone, email, notes, health notes (cipher text), patch test date, consent and wording, own deposit rule | cleared; the row stays as a marker |
| `clients` merged into this one | the duplicate's name (ADR 0017 cleared the rest) | name cleared, marked erased |
| `consultation_records` | health answers (cipher text), what the form was for, consent wording | deleted |
| `waitlist_entries` | what they waited for, days, a note | deleted |
| `phone_verifications` | what they typed on the booking pages: name, number, email | deleted (by number, and by their visits) |
| `appointments` | that a visit took place, when, with whom, the price; a note; a cancellation reason; the cancel link | kept; note, reason and link cleared |
| `appointment_status_changes` | who changed a visit's status and when | kept: no personal data of the client |
| `client_account_entries` | fees, deposits, payments | kept: the salon's books |
| `visit_product_lines` | products on their bills | kept: the salon's books |
| `appointment_reminders` | that a reminder was sent for a visit | kept: ids and an outcome only |
| `outbox_messages` | events about the client | kept: ids only (ADR 0007, ADR 0011) |

- **It waits for three things**, each of which the desk can settle: a visit still to come (cancel it), a deposit
  the salon holds (give it back), a fee the client owes (take it or waive it). A card merged into another is
  erased through the card that kept it. Erasing twice changes nothing.
- **Owner only** (`Settings.Manage` with `Clients.Manage`), on a page that lists what goes and what stays, behind
  a tick that says it cannot be undone.
- **An erased client is not found again**: not in the search, the client file, the booking pickers or by phone
  number. The card cannot be edited, merged or given a consultation form. The same person can become a client
  again, as a new card.
- **The log says who erased whom and how much went**, by id and count. Never what it was.
- **A test searches every text column of every table** in the salon's schema for the erased client's name, number,
  email and notes, and finds nothing. A new table that holds something about a client, and that erasure forgets,
  fails that test once the test's history writes to it.

## Not decided here
- **Text messages and emails already sent** are with the phone company and the mail provider, not in this
  database. The copy page says so.
- **Log files** written before erasure carry ids, not names (ADR 0011), and are not rewritten.
- **Backups** taken before the erasure still hold the client until they expire. There is no hosting yet (phase 1);
  when there is, the backup retention must be short enough to say so honestly.
- **Notes on ledger lines** (why a fee was waived) are the salon's own words and stay. A salon that writes a
  client's name into such a note has to live with it; the ledger is not edited.
- **Gift vouchers** have no client. A voucher's note ("For Mia") is not tied to anybody and stays.
- **Erasing on a timer** (clients not seen for some years) and a retention period chosen by the salon.
- **A file** of the copy in a format a machine reads (JSON). A printed page answers the request; portability to
  another salon's software is the client file of ADR 0051, for the whole list.
- An event for plugins when a client is erased. There is no plugin that holds client data yet.

## Consequences
- Migration `ClientErasure`: two nullable columns on `clients`. No existing row changes.
- `IClientDataService` in `Salon.Services`; slices `Clients/DataCopy` and `Clients/EraseClient`; two links on the
  client card for the logins that may use them.
- `ClientCard.IsErased`; the search, the client file and the count on "Your data" leave erased rows out.
- Every later table that holds something about a client must be added to the erasure and to the table above.

## Rollback
Before any client is erased: revert the PR and run `Down`. After: an erased row would look like an inactive client
called "Erased client" to the old code, which is harmless; revert the pages only and keep the columns. Erasure
itself cannot be rolled back: that is its purpose.
