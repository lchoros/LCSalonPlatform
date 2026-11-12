# 0013 — Catalogue categories, buffers and processing time

Status: Decided — owner may veto
Date: 2026-11-12

## Context
A salon's price list is not flat: clients scan "Hair", "Nails", "Barbering" headings, and the availability engine
(ADR 0012) needs to know how long a stylist is really tied up. A colour service blocks the chair for 90 minutes but the
stylist is free for 30 of them while the colour develops (glossary: processing time, "czas działania"), and most services
need a few minutes of clean-up afterwards (buffer, "przerwa techniczna"). M0 shipped `Service` with a single `Duration`
and a create form only; M1 needs edit, categories and the timing fields the engine will read.

## Options
1. A free-text "category" column on the service — no spelling drift control, no ordering, no rename.
2. A `ServiceCategory` table with a nullable FK from the service — one more tenant table, but renames are one update,
   ordering is a column, and "uncategorised" is simply `NULL`.
3. Separate "service variants" with their own durations now — needed later for staff-specific prices, too much for M1.

## Decision
Option 2.
- **`ServiceCategory : BaseEntity, ITenantEntity`** (`Name` ≤ 100, `SortOrder`, `IsActive`), table `service_categories`,
  forced RLS, unique `(tenant_id, name)`. `Service` gains `CategoryId?` (FK `SetNull`: deleting a category leaves its
  services uncategorised), `BufferAfterMinutes` (0–120), `ProcessingMinutes` (0–240) and `ProcessingStartsAfterMinutes`
  (`0 ≤ x ≤ Duration − Processing`; stored as 0 when there is no processing time). `Duration` stays the total calendar
  time the client is in the salon. New columns default to 0, so existing rows migrate without a data step (migration
  `CatalogDepth`, additive).
- **Rules in Core, messages in the slices.** `ServiceRules` (trimming, limits, 5-minute grid, processing window,
  literal LIKE escaping) and `ServiceGrouping` (categories in sort order, services in theirs, uncategorised last, empty
  categories skipped) are pure and unit-tested; the Create and Edit validators map each rule to a resource key. Rule 3
  forbids one slice referencing another, which is why nothing shared lives under `Features/`.
- **Slices** under `admin/services`, all `[RequireTenant]` + `[RequirePermission(Permissions.CatalogManage)]`:
  `Catalog/ListServices` (grouped list), `Catalog/EditService` (every field incl. the active flag; publishes
  `ServiceUpdated` and, on active → inactive, `ServiceDeactivated`), `Catalog/ManageCategories` (list with inline add,
  rename, deactivate/restore). `CreateService` takes the new fields and lands on the admin list. The public `/services`
  page groups by category when the salon has at least one and keeps hiding inactive services.
- **Inactive categories** stay out of the pickers but keep grouping the services that still point at them; "restore"
  exists so a mis-click is reversible without a duplicate name. Reordering categories and services is a later slice.
- **Events carry ids only** (`ServiceUpdated(ServiceId, TenantId)`, `ServiceDeactivated(...)`) through the outbox.

## Consequences
- The availability engine reads `BufferAfterMinutes` now and can add a "split busy interval" for processing time later
  without a schema change.
- A service name is unique per salon, case-insensitively, on create and on edit (renaming to its own name is fine).
- The demo salon gets three categories and realistic buffers/processing (Colour: 30 min developing after 20 min;
  Balayage: 45 after 45); an existing demo salon receives them once, only while it has no categories at all.
- Rollback: `Down` drops the FK, the table and the four columns; nothing else references them yet.
