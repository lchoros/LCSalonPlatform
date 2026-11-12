# 0018 — Staff-specific price and duration per service

Status: Decided — owner may veto
Date: 2026-11-12

## Context
A price list is the salon's default, not the whole truth. A senior stylist takes an hour over a cut the list says takes
45 minutes and charges £45 instead of £40; the nail technician never cuts hair at all. Booksy and Versum both let a salon
say this per person and per service, and without it the availability engine (ADR 0012) hands out slots that are too short
for the slow stylist and offers "any staff" to people who do not do the service. M1's catalogue item names
"staff-specific price/duration" explicitly. Inputs already exist: `Service` with `Duration` and `Price` (ADR 0013),
`StaffMember` with `IsBookable` (ADR 0010), the pure `AvailabilityCalculator` and `IBookingService` with its price and
duration snapshot (ADR 0012). Two other open slices (staff time off, admin calendar) also edit `AvailabilityService`, so the
change there must stay small and local.

## Options
1. Separate "service variants" (one `Service` row per stylist) — the public list would show the same name several
   times, categories and buffers would have to be kept in sync, and "the salon's price list" stops being one thing.
2. Nullable `Duration`/`Price` columns on a staff-to-service join row, read wherever a duration or price is needed —
   the list stays one list, the engine reads one row per (person, service), and a blank means "the default".
3. A JSON map on the staff row — one column, no FK to the service, no unique constraint and awkward queries for "who
   offers this".

## Decision
Option 2.
- **`StaffServiceOverride : BaseEntity, ITenantEntity`** (`Salon.Core.Catalog`): `StaffMemberId`, `ServiceId`,
  `Duration?` (NodaTime, null = the service duration), `decimal? Price` (null = the service price, always in the service's
  currency), `Offers` (default true; false means this person never does this service). Table `staff_service_overrides`,
  forced RLS (ADR 0002), unique `(tenant_id, staff_member_id, service_id)`, FKs to `staff_members` and `services` with
  cascade (an appointment keeps its own snapshot, so nothing downstream breaks). A row with `Offers` true and both nulls
  (`IsDefault`) says nothing and is deleted rather than stored. Migration `StaffServiceOverrides` is additive.
- **Rules in Core.** `ServiceRules.EffectiveDuration/EffectivePrice(service, override)`, `IsOffered(override)`, and
  `IsOverrideDurationValid/IsOverridePriceValid`: an override obeys exactly the bounds of a service (5–600 minutes on
  the 5-minute grid; 0–100 000 with two decimals), so nothing a person offers can be something the list itself could not say.
- **One resolver in Services.** `IServiceOfferingResolver` (`ResolveAsync(service, staff)`, `ResolveAsync(service, staff[])`,
  `PriceSpansAsync(services)`) is the only place that joins a service to its overrides; it returns a
  `ServiceOffering(Offers, Duration, Price, Currency, HasOverride)` per person and a `PriceSpan(Min, Max)` per service
  over the active, bookable staff who offer it. The pure halves (`Resolve`, `PriceSpanFor`) are unit-tested.
- **Engine and booking read it.** `StaffAvailabilityInput` gains an optional `ServiceDuration`; the calculator uses it
  for that person's slot length (and the held `[start, end + buffer)`), the request's `ServiceDuration` otherwise.
  `AvailabilityService.GetSlotsAsync` resolves offerings for the candidate staff, drops those with `Offers == false`
  (so "any staff" never lands on them) and passes each person's duration through. `BookingService.BookAsync` snapshots
  `End`, `DurationMinutes` and `PriceAmount` from the offering and throws `InvalidOperationException` for a person who
  does not offer the service (not `SlotNoLongerAvailableException`: the client should not be told to pick another time).
  An FsCheck property holds: every slot lasts its staff member's effective duration, per person and under "any staff".
- **Slice `Catalog/StaffOverrides`** at `admin/services/{id}/staff`, `[RequireTenant]` + `Catalog.Manage`: a table with
  one row per active, bookable staff member — "offers" box, minutes, price, blanks meaning the defaults — saved in one
  POST with anti-forgery; the handler upserts, deletes rows set back to the defaults, ignores people the salon cannot
  book, and publishes `ServiceUpdated`. The service edit page links to it. Messages are resource keys in both cultures.
- **What clients and the desk see.** The public `/services` list shows "from £40.00" (`od 80,00 zł`) while bookable staff
  charge differently and the single price otherwise (when everyone who offers it overrides to the same price, that
  price). The admin book-for-client page shows, under the slots heading, the chosen person's own "60 min · £45.00",
  "45 min · from £40.00" under "any available", or "Tom Reed does not offer this service." with no slots. The public
  booking page (ADR 0014, in flight) reads the same resolver once it merges.
- **Demo.** Anna Kowalska does "Cut & blow-dry" in 60 minutes for £55; Priya Shah does not offer it. Added once, only
  while the demo salon has no override at all, like the other seed pieces.

## Consequences
- A salon changes one row per person and service; the list, the engine, the snapshot and both booking pages agree
  because they all go through one resolver.
- `appointments` is untouched: the snapshot columns already hold the effective values, so history stays correct when
  an override changes later.
- Processing time and buffer stay per service, not per person; a per-person buffer is a later additive column.
- "From" is computed over bookable staff at render time (one query for the whole list); with many staff and services
  this is still a single `WHERE service_id IN (...)` and can be cached per tenant later.
- A person set to "does not offer" who has future bookings keeps them (the snapshot is theirs); only new bookings are
  refused. Moving those appointments is a desk decision, not an automatic one.
- Rollback: `Down` drops the policy and the table; nothing else references it.
