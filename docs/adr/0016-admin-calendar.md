# 0016 — Admin calendar: server-rendered day/week grid and moving appointments

Status: Decided — owner may veto
Date: 2026-11-12

## Context
The day list (ADR 0012, admin booking slice) shows a day as cards per staff member, which is enough to run a desk but not
to *see* a day: gaps, overlaps with the rota, who is free at 14:00. PLAN.md names FullCalendar for the admin calendar,
but phase 1 forbids downloads and the host ships no bundler; the library is not vendored yet, and rule 8 (no inline
scripts, no new JS framework) stands. Moving a visit — the one edit a receptionist does ten times a day — also needs a
service method that keeps the double-booking guarantee of the exclusion constraint.

## Options
1. Vendor FullCalendar now and build the drag-and-drop calendar on it — blocked on the owner vendoring the library; a
   large JS surface for a first version.
2. A server-rendered CSS-grid timetable with plain links and small POST forms; drag-and-drop later, on top of the same
   queries and `MoveAsync` — no JS, works on a phone, every state is a URL.
3. Keep the day list and add a "move" form to its cards — cheap, but still no picture of the day.

## Decision
Option 2 now, option 1 deferred.
- **Layout is pure (Services).** `CalendarLayout` turns the staff rota into a grid (15-minute rows from the earliest
  working start to the latest end, 08:00–20:00 when nobody has hours, widened to show a visit outside the rota) and
  appointments into positioned blocks (row, span, lane and lane count when two blocks of one column overlap). Minutes of
  the local day, so a rota that ends at midnight needs no special case. Unit-tested; the views only print numbers into
  `grid-row` / `margin-left` / `width` inline **styles** (styles, not scripts; CSP nonces apply to scripts only).
- **Slice `Appointments/Calendar`** under `admin/appointments/calendar`, `[RequireTenant]` + `Bookings.Manage` +
  `Clients.View` (client names on screen, ADR 0009/0011). `?view=day` shows one column per bookable staff member (chips
  per person and "All"); `?view=week` shows Monday–Sunday of one person (default: the first bookable). Blocks link to the
  **card** (`calendar/{id}`): details, the status buttons (posting to the day list's existing routes — the calendar does
  not duplicate `ChangeStatus`), and the **move form**: staff select + date, the free times as buttons from
  `IAvailabilityService` with the visit itself not counted as busy and its current time left out.
- **`IBookingService.MoveAsync(appointmentId, staffMemberId, newStart, byUserId)`**: active appointments only
  (`AppointmentNotActiveException` otherwise); re-checks the slot through the engine with the new overload
  `GetSlotsAsync(..., excludeAppointmentId)`; keeps the duration and price snapshot (the new end is start + booked
  minutes); writes an `AppointmentStatusChange` with `From = To = current status` and reason `"Moved"`, so the history
  shows the move without inventing a status; publishes **`AppointmentMoved(AppointmentId, TenantId, OldStart, NewStart,
  StaffMemberId)`** through the outbox; maps the exclusion violation (`23P01`) to `SlotNoLongerAvailableException` as
  `BookAsync` does, after putting the tracked appointment back as loaded.
- **Mobile.** Below `sm` the day view with "All" is a stacked list per person (the grid is hidden); a single person's day
  and the week are grids, the week scrolling sideways inside its own box so the page never scrolls horizontally.
- **Cancelled and no-show visits are not drawn**; they are visible on the client card and in the history.

## Consequences
- No JavaScript at all on the calendar; every navigation is a GET, every change a POST with an anti-forgery token.
- Drag-and-drop (FullCalendar) later reuses `CalendarQuery` for events and `MoveAsync` for the drop; the move rules will
  not fork.
- One more `AppointmentStatusChange` row per move; consumers of `AppointmentMoved` (reminders, M2) get both instants.
- The slot check uses the service's *current* duration, like a new booking, while the moved row keeps its booked
  duration; a catalogue change between booking and move can therefore offer a slot a few minutes longer or shorter than
  the visit. The exclusion constraint still refuses any real overlap.
- No migration: the event type is registered by assembly scan, the history row reuses `Reason`.
- Rollback: revert the slice folder, the nav link, the day list button, `MoveAsync`, the availability overload and the
  event; nothing persisted depends on them except history rows with reason `"Moved"`, which stay valid.
