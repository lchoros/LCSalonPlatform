# 0017 — Client merge and data retention

Status: Decided — owner may veto (Tier 2: merging rewrites personal data and the retention story touches GDPR/RODO)
Date: 2026-11-12

## Context
Duplicates are a fact of life at the front desk: the same person booked online with a mobile and was typed in by a
receptionist with a landline, or a name was misspelt. Booksy/Versum imports (M3) will bring more. Each duplicate splits
the visit history, the health notes and the consent record, so the card the stylist opens is incomplete. At the same
time ADR 0014 left a loose end: `phone_verifications` keeps the typed details of every online booking attempt and nothing
removes them. Both are "what happens to a client's data over time" questions, so they share one record.

## Options
1. Delete the duplicate row and re-point its appointments — loses the audit trail; old links, imports and messages that
   carry the id break.
2. Keep both rows, mark the duplicate as merged with a pointer to the kept card, move the appointments — the row stays
   as a tombstone without contact details; every reference still resolves.
3. A free "primary card" flag with a many-to-many "same person" table — flexible, but every read path would need to
   union across the group; far too much for M1.

## Decision
Option 2, with the rules below written as a pure `ClientMergeRules` (Core) so they are tested without a database and
read like a policy.

- **Columns**: `clients.merged_into_client_id` (nullable self FK, `Restrict`, indexed) and `merged_at`. Additive
  migration `ClientMerge`; no new table, so RLS is unchanged.
- **Vocabulary**: the *target* is kept; the *source* is merged away. `IClientService.MergeAsync(targetId, sourceId, byUserId)`
  runs in one transaction (joins the command's, ADR 0003) and refuses the same id on both sides and any card already
  merged (`ClientMergeRefusedException`). Both ids resolve through the tenant filter, so another salon's id is "not found".
- **Appointments** of the source move to the target with one `UPDATE`; status-change rows hang off the appointment and
  follow it. Nothing is deleted.
- **Notes**: both kept, target first, joined with `\n---\n`, cut to the column.
- **Health notes** (special-category data): the target's cipher text stays; only when the target has none does the
  source's move, as cipher text, without decryption. The patch-test date follows the same rule.
- **Consent**: the target's state and proof stay. A target that never consented does not gain consent from the source:
  the salon cannot show which wording *this* card agreed to. Consent is never gained by a merge.
- **Phone and email** are unique per salon, so the source's are cleared first; the target takes them only where it has
  none. The source's number is otherwise not kept anywhere: the confirmation page shows both cards before the merge,
  and a dead card must not be what the public booking flow (ADR 0014, `FindByPhoneAsync`) recognises.
- **Source** becomes `IsActive = false`, `MergedIntoClientId = target`, `MergedAt = now`; search and the booking
  client picker skip it (`MergedIntoClientId IS NULL`); its card shows a banner linking to the kept card. The flow is
  Owner/Manager only: new permission `Clients.Merge` (Receptionist keeps `Clients.View`).
- **Event**: `ClientsMerged(TargetId, SourceId, TenantId)` through the outbox, ids only (ADR 0007, ADR 0011).
- **History on the card**: `IClientService.GetHistoryAsync` returns upcoming (active, from now) and past visits with
  service and staff names resolved through the filter, plus visits completed, no-shows and last visit over every row.

### Retention
- `phone_verifications`: `PhoneVerificationCleanupTask` (`Booking.CleanupVerifications`, hourly, ADR 0005) deletes rows
  created more than `PublicBooking:VerificationRetention` ago (default 24 h, never less than the code lifetime), per
  tenant through `ITenantScopeRunner`. After a day a row is spent, dead or expired whatever its state, and the
  appointment it produced has its own record.
- Appointments, their status history, merged client tombstones and outbox rows are **kept** in M1: they are the salon's
  operational record and the basis of no-show policies (M2). Long-term retention and erasure requests ("forget this
  client") are decided at M3 together with the import/export work; until then nothing deletes personal data except the
  verification clean-up above.

## Consequences
- A merge is not undone by the system; the confirmation page says so. The tombstone keeps enough to see what happened
  (who it was merged into and when) without keeping contact details.
- `ClientCard` gains `MergedIntoClientId`/`MergedAt`; read paths that must hide tombstones filter on the pointer, not on
  `IsActive`, which the salon may toggle for other reasons later.
- The clean-up task is the first job that deletes tenant rows; it goes through the tenant scope runner like the
  heartbeat, so RLS applies.
