# 0074. Undoing a diary import

Status: Decided — owner may veto
Date: 2026-11-19
Issue: the "undoing a file" line of ADR 0067

## Context

Reading the old system's diary in from a file (ADR 0067) is the riskiest moment of onboarding: a wrong file — the
wrong salon's export, an old export, a draft — fills the coming weeks with visits, and there was no way back but
deleting them one by one, which does not exist either. The import itself is careful (preview, all-or-nothing
write), but a careful write of the wrong file is still the wrong diary.

## Decision

- Every commit of a diary file stamps its visits with one `ImportBatchId` (UUIDv7; additive migration, a filtered
  index on tenant and batch). A visit not from a file keeps null.
- `IAppointmentImportService` gains `BatchesAsync` (the last ten read-ins: when, how many brought, how many an undo
  would remove now) and `UndoAsync(batchId)`: **one `ExecuteDelete`** that removes the batch's visits that are
  still exactly as the file left them — booked, exactly one history line (a desk move writes a second), no client
  account entries, no product lines. History and reminder rows go by cascade; everything else about the visit was
  never created (imported visits have no deposit and no messages at import).
- The slice `Appointments/UndoImport` at `/admin/appointments/import/undo`, behind the same permissions as the
  import, lists the read-ins and takes one out with one tap; the flash says what was removed and what was kept.
  Another salon's batch id answers 404. Linked from the import page.
- Safety is layered: the delete re-checks its rules in the statement itself, and the database stands behind it —
  `ClientAccountEntry`, `VisitProductLine` and `StockMovement` all reference appointments with `Restrict`, so a
  payment racing the undo makes Postgres refuse and the command's transaction roll back whole.
- Removal is physical, not a cancellation: a cancelled visit would stay in histories, exports and counts, and the
  point of the undo is a diary as if the file had never been read. Reading the same file in again brings the
  removed visits back (ADR 0067's "a second file adds nothing" sees an empty diary again).

## Consequences

- A wrong file during onboarding is a one-tap mistake, not a support call.
- A visit a client already lived through — arrived, paid, rearranged — never disappears.
- Old imports from before this migration have no batch id and are not listed; nothing to undo from before the
  feature existed, which is acceptable for a pilot that has not onboarded yet.
- If the salon's reminders were already sent for a removed visit, the client was told of a visit that is now gone;
  the page's lead says a read-in should be taken out as soon as the mistake is seen.

## Alternatives considered

- Cancelling instead of deleting: leaves hundreds of cancelled ghosts in every list and report.
- Identifying a batch by `CreatedAt` equality: works today (one clock read per commit) but is an accident of the
  implementation, not a contract.
- A confirm page before the delete: the list already says exactly what will happen, the flash says what did, and
  reading the file in again restores it; a second screen would not make it safer, only slower.