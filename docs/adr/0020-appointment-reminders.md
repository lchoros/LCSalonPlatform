# 0020 — Appointment reminders with quiet hours

Status: Decided — owner may veto
Date: 2026-11-13

## Context
PLAN.md M2 asks for reminders a day and two hours before a visit, with quiet hours. Reminders are the cheapest way to cut
no-shows, and they are the first message the platform sends on a timer rather than in answer to a click, so two things
matter more than anywhere else: a client must never get the same text twice, and nobody is texted at night. Phase 1 has
no real SMS provider; everything goes through `ISmsSender` to the log provider.

## Options
1. **Plan rows up front.** Consumers of `AppointmentBooked` / `Moved` / `Cancelled` write "send at" rows; a task sends
   the due ones. Explicit, but three consumers must stay in step with the appointment, and a changed setting leaves
   stale rows behind.
2. **Compute when due.** A task looks at the active appointments in the coming days, works out each reminder's moment
   from the current settings, and writes a row only when it sends. No consumers, nothing to keep in step: a move, a
   cancellation or a new lead time is picked up on the next pass because the appointment itself is the source.
3. **Hangfire delayed jobs per appointment.** No table, but jobs must be found and cancelled on every change and are
   invisible to the tenant filter and RLS.

## Decision
Option 2.
- **Settings** (`NotificationSettings`, ADR 0004): reminders on/off, first and second lead time in hours (24 and 2; 0
  switches one off), quiet hours as minutes of the day in the salon's zone (21:00–08:00, may wrap midnight; equal start
  and end means none). Page `admin/settings/notifications` behind `Settings.Manage` (Owner only), linked from the nav.
- **Rules are pure** (`Salon.Core/Notifications/ReminderRules`): send at start minus lead; if that is inside quiet hours,
  wait for them to end; if that leaves less than 30 minutes before the visit, drop the reminder. A reminder is *due* when
  its moment has come, the visit is still at least 30 minutes away, and the visit was booked (or last moved) before that
  moment — a booking made inside the lead time already got its confirmation. Unit and FsCheck property tests: never in
  quiet hours, never closer than 30 minutes, never earlier than the lead time; London and Warsaw clock changes.
- **One row is the claim** (`appointment_reminders`, tenant-owned, forced RLS): unique on appointment, kind and the
  start the reminder was about. The service inserts the row *before* handing the SMS to the provider; a second pass or a
  second worker hits the unique index and skips. A moved appointment has a new start, so it is reminded again for the
  new time. Outcomes: Sent, Failed (provider threw; not retried), NoPhone, Superseded.
- **At most once, deliberately.** If the provider fails after the claim, the reminder is lost and logged rather than
  retried: a missed reminder is a small cost, a duplicate or a late one at night is a complaint. The same reasoning
  makes two reminders that become due in one pass (the task was down, or quiet hours pushed them together) one message:
  the one nearest the visit is sent, the other is recorded as Superseded.
- **Task** `Notifications.SendReminders`, every five minutes, salon by salon through `ITenantScopeRunner`; one salon's
  failure does not stop the others. The service takes "now" as a parameter, so tests choose the moment.
- **Template**: the existing `Booking.Reminder` (ADR 0008) for both lead times, the salon's culture and zone, with the
  cancel link for online bookings and the salon's booking page for admin bookings, which have no public page yet.
- **Who is reminded**: every active appointment whose client has a phone, online or booked at the desk. A reminder is a
  service message about an existing booking, not marketing, so it does not depend on marketing consent.

## Consequences
- No consumers and no stale state; changing a lead time takes effect on the next pass, including for visits already
  booked (a visit whose reminder moment has passed under the new setting is reminded once, if still 30 minutes away).
- The inbox pattern (idempotent consumers) is not needed here: the unique claim gives the same guarantee with less
  machinery. It stays a candidate for the confirmation SMS consumer, which can still double-send on an outbox retry.
- Email reminders, per-client opt-out, "reply C to confirm" and a delivery report screen are later work (M2/M3).
- Tier 2 note for the owner: this adds a message that goes out without a person clicking. With the log provider nothing
  leaves the machine; before a real SMS plugin is switched on (M3) the lead times, quiet hours and wording should be
  looked at once with the pilot salon.

## Rollback
Switch reminders off per salon on the settings page, or `dotnet ef database update StaffServiceOverrides` to drop the
table; the task finds nothing to do without it being registered.
