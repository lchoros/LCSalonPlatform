# 0015 — Staff time off

Status: Decided — owner may veto
Date: 2026-11-12

## Context
The rota (ADR 0010) says when a person normally works; the availability engine (ADR 0012) offers every slot inside it
that no appointment holds. Real weeks have holidays, sick days and a dentist at 14:00, and a salon must be able to
block them before a client books into them. Booksy and Versum both model this as "time off" per person, separate from
the weekly pattern.

## Options
1. Edit the rota for the affected week — the weekly pattern is not per date, so the person would be "off every Tuesday".
2. Insert fake appointments — pollutes history, counts in reports, needs a client, and the exclusion constraint would
   make a partial-day block collide with real bookings in odd ways.
3. A `StaffTimeOff` row per absence (date span plus optional daily time range) that the engine adds to the busy list.

## Decision
Option 3.
- **`StaffTimeOff : BaseEntity, ITenantEntity`** (`Salon.Core.Staff`): `StaffMemberId` (FK → `staff_members`,
  cascade), `FromDate`/`ToDate` (`LocalDate`, inclusive, `ToDate ≥ FromDate`, at most 366 days), `Start`/`End`
  (`LocalTime?`; both null = whole days, both set = that range on each day, `End > Start`, 5-minute grid), `Reason`
  (≤ 200, salon-only), `CreatedAt`. Table `staff_time_off`, forced RLS, index `(tenant_id, staff_member_id, from_date)`.
  Dates and times are local to the salon, like the rota, so an entry survives DST changes unchanged.
- **Rules in Core** (`StaffTimeOffRules`): span and time checks, `Problems(...)` for the full table, `Overlaps(a, b)`
  (two entries of one person may not share a moment; whole-day beats any range, partial ranges compare as `TimeRange`),
  and `ToIntervals(entry, zone)` producing one `Interval` per day via `AtStartOfDay` (whole days) or
  `InZoneLeniently` (partial), which is exactly what the engine's busy list takes. Pure and unit-tested on both DST days.
- **Engine.** `AvailabilityService` loads the entries that touch the requested window and appends their intervals to each
  person's busy list. The pure calculator already excludes busy intervals, so nothing changes there; `BookAsync` re-runs
  the same slot list and therefore refuses a start inside time off with `SlotNoLongerAvailableException`.
- **Slice** `Features/Staff/TimeOff` at `admin/staff/{id}/time-off` (`Staff.Manage`): upcoming entries, past entries
  collapsed in a `<details>` (last 10), an add form (from, to, optional start/end, reason), delete per row with
  anti-forgery. Overlapping entries are refused by the validator with a localized message. Adding or deleting publishes
  `StaffTimeOffChanged(StaffMemberId, TenantId)` through the outbox.

## Consequences
- An appointment already booked inside a new absence stays booked: time off only removes future slots. Warning about the
  clash is a follow-up (the day list can show it); moving or cancelling is the salon's call.
- One row per absence, one interval per day: a year-long leave adds 366 small intervals to one person's busy list for the
  requested window only, which is negligible.
- Recurring absences ("every other Saturday") are not modelled; they are rota edits or several rows.
- Rollback: `Down` drops the table and its policy; nothing else references it.
