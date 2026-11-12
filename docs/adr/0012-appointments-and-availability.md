# 0012 — Appointments, double-booking prevention and the availability engine

Status: Decided — owner may veto
Date: 2026-11-11

## Context
M1's gate is "a test client books on a phone in under 60 seconds". That needs three things no slice has yet: an
appointment record with a clear life cycle, a guarantee that two bookings can never hold the same stylist at the same
time (even from two browsers at once), and a fast, correct answer to "when is this service bookable with this person".
Inputs already exist: services with durations (`Service`), staff with weekly working hours (ADR 0010), clients
(ADR 0011), the salon's time zone on the tenant, and `BookingSettings` (min notice, max advance days).

## Options
1. Check for overlaps in C# before insert — races between two requests still double-book; only safe under a
   serializable transaction with retries, which is the worst of both worlds.
2. A per-staff advisory lock around booking — correct on one database but invisible in the schema and easy to forget
   in a new code path (admin move, import, calendar drag).
3. A Postgres exclusion constraint on the staff member and the booked time range — the database refuses the second
   booking whatever code path produced it; the application turns the violation into a friendly "slot just taken".

## Decision
- **Entities (Core, `Salon.Core/Appointments/`).** `Appointment : BaseEntity, ITenantEntity` — `ClientId`,
  `StaffMemberId`, `ServiceId`, `Start` and `End` as `Instant`, `Status`, `Source` (`Online`, `Admin`), `Notes`
  (≤ 2000, operational, never health data), a price snapshot (`PriceAmount`, `Currency`, `DurationMinutes`) so later
  catalogue edits do not rewrite history, `CreatedAt`, `CreatedByUserId?`, `CancelledAt?`, `CancellationReason?`
  (≤ 500). `AppointmentStatusChange : BaseEntity, ITenantEntity` — one row per transition (`AppointmentId`, `From`,
  `To`, `At`, `ByUserId?`, `Reason?`), so the card and the no-show policy (M2) have a history.
- **State machine (Core, pure).** `Booked → Confirmed → Arrived → Completed` (a walk-in may go `Booked → Arrived` directly); `Booked|Confirmed → NoShow`;
  `Booked|Confirmed|Arrived → CancelledByClient | CancelledBySalon`; terminal: `Completed`, `NoShow`, both cancelled.
  `AppointmentStateMachine.CanTransition(from, to)` and `Transition(appointment, to, now, byUserId, reason)` are the
  only way to change `Status`; a wrong transition throws `InvalidAppointmentTransitionException`. Every transition
  publishes an event (`AppointmentBooked`, `AppointmentConfirmed`, `AppointmentCancelled`, `AppointmentCompleted`,
  `AppointmentNoShow`) carrying ids and instants only (no names, phones or notes), through the outbox (ADR 0007).
- **Exclusion constraint (Data).** Migration `Appointments` creates `appointments` and `appointment_status_changes`
  with forced RLS, `CREATE EXTENSION IF NOT EXISTS btree_gist` (a trusted extension in PostgreSQL 13+, so the
  migrating role needs only `CREATE` on the database), and
  `EXCLUDE USING gist (staff_member_id WITH =, tstzrange(start_at, end_at, '[)') WITH &&) WHERE (status IN (1,2,3))`
  (Booked, Confirmed, Arrived; cancelled and finished rows free the time). `tenant_id` is implied by the staff
  member. `IBookingService.BookAsync` catches the unique/exclusion violation (`23P01`) and throws
  `SlotNoLongerAvailableException`, which the UI shows as a friendly message and a fresh slot list.
- **Availability engine (Services, pure).** `AvailabilityCalculator.Slots(request)` takes in-memory inputs only:
  the salon zone (`DateTimeZone`), the date range, the service duration (+ `BufferAfterMinutes`, default 0 until the
  catalogue slice adds it), each candidate staff member's weekly working ranges (ADR 0010) and busy intervals
  (active appointments as `Interval`s), `BookingSettings.MinNoticeMinutes`, `MaxAdvanceDays` and the new
  `SlotStepMinutes` (default 15), and `now`. It returns `Slot(StaffMemberId, Instant Start, Instant End)` ordered by
  start; "any staff" is the union de-duplicated by start keeping the staff member with the fewest bookings that day.
  Rules: a slot starts on a step boundary inside a working range, the whole `[start, end + buffer)` lies inside one
  working range, overlaps no busy interval, starts no earlier than `now + MinNotice` and no later than `MaxAdvanceDays`;
  local times are converted with `LocalDateTime.InZoneLeniently` so DST gaps and overlaps never throw. Loading the
  inputs (`IAvailabilityService.GetSlotsAsync`) runs through the tenant filter and never exposes other tenants' busy
  intervals. Property tests (FsCheck, written by Claude, not the local model) must hold: no slot overlaps a busy
  interval; every slot lies inside a working range; slots are step-aligned; adding a busy interval never adds a slot
  (monotonicity); the union for "any staff" equals the union of per-staff results.
- **Booking (Services).** `IBookingService.BookAsync(BookAppointmentRequest)` re-checks availability for the chosen
  slot in the same transaction, snapshots price and duration, inserts the appointment and the first status change,
  publishes `AppointmentBooked`; `CancelAsync`, `ConfirmAsync`, `MarkArrivedAsync`, `CompleteAsync`, `MarkNoShowAsync`
  go through the state machine. All run inside the pipeline's transaction when called from a command.
- **Out of scope here.** The public booking page, SMS code verification, the admin calendar and deposits are the next
  slices; this slice ships the core (entities, state machine, constraint, engine, booking service) with no UI. The
  minimal admin "book for a client" form and the day list (`IBookingService.GetDayAsync`) follow in the next slice so the
  engine can be exercised end to end on a phone.

## Consequences
- Double-booking is impossible by construction; the cost is one GiST index per tenant table of appointments and a
  dependency on `btree_gist` (standard contrib, present on every managed PostgreSQL).
- Status history grows one row per transition; retention is an M3 concern.
- The calculator is pure, so availability can be tested exhaustively and later cached per staff/day.
- Processing time (the client waits while colour develops, the stylist is free) is deliberately not modelled yet;
  when the catalogue slice adds it, the engine gains "split" busy intervals without changing the constraint.
- `appointments.client_id` has no foreign key yet: the clients table (ADR 0011) is built in a parallel slice. A small
  additive migration adds `FK → clients (Restrict)` once both are merged; until then the booking slice is responsible
  for passing an existing client id.
- `MaxAdvanceDays` is read as "the last bookable local day is today plus N days", so a salon open 60 days ahead shows
  the whole of that sixtieth day rather than cutting it at the current time of day.
- "Any staff" ties (two people free at the same start with the same number of bookings that day) go to the staff member
  listed first (sort order), so the result is deterministic.
- The engine applies `MinNoticeMinutes` to admin bookings too (`BookAsync` re-checks the same slot list); an "ignore
  notice for staff" switch is a later additive change if walk-ins need it.
