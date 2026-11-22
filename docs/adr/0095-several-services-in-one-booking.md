# 0095. Several services in one online booking

Status: Decided — owner may veto
Date: 2026-11-22
Issue: the owner on the booking flow (the Booksy screenshots, "Add another service"): "multiple services in one go";
agreed as its own project after the focused flow (ADR 0094); "please continue and publish".

## Context

A client who wants a cut and a beard trim had to book twice: two codes by text, two confirmations, and no promise
that the two times would sit next to each other. The diary, the till, commission and the reports all know one
service per appointment (ADR 0012), and that is what keeps the money right.

## Decision

- **A visit of several services is several appointments in a row.** Each service is an appointment of its own, with
  its own price, duration and deposit, starting where the one before ends, with **one person**. They share a new
  `appointments.visit_group_id`. Nothing that reads one appointment changes: the calendar shows the parts side by
  side, the till takes each, commission and reports stay per service.
- **One person does the whole visit.** Only people who offer every chosen service are on the "who with" picker;
  "anyone available" means any one such person. If nobody offers them all, the page says so and offers no time.
- **The times offered are those the whole visit fits**: `IAvailabilityService.GetVisitSlotsAsync` asks the same
  calculator for the sum of that person's own durations. `IBookingService.BookVisitAsync` checks again and writes
  all the appointments in one batch — a visit is booked **whole or not at all** (the exclusion constraint refuses the
  batch). `BookAsync` is now `BookVisitAsync` with one service, so there is one path.
- **In the address**, the first service stays in the route and the further ones ride along:
  `/book/{first}?also={second},{third}`. Up to four services (`VisitRules.MaxServices`), each once. The details form
  posts them as `Also`; the pending row keeps them in `phone_verifications.extra_service_ids`.
- **On the page**: the summary under the times lists each service with its price, length and "Remove", then the time
  for the whole visit and one total. "Add another service" opens the same service list in an adding mode (what is
  already chosen is not offered again; "who with" is not asked again).
- **One voice for the visit.** The confirmation and the reminder go out once, for the first part, naming every
  service ("Cut + Colour"); the later parts are skipped (`ReminderOutcome.PartOfVisit`). The client's link stands for
  the visit: its page names every service with the summed price, and cancelling by it cancels every part.
- **Behind `Features:SeveralServices`**, off by default; on for the pilot. Also closed while a payment gateway takes
  deposits online (`IOnlineDepositCheckout.IsAvailable`): its pay step knows one appointment.

## Consequences

- Additive migration `VisitGroups`: `appointments.visit_group_id` (nullable, filtered index) and
  `phone_verifications.extra_service_ids` (`uuid[]`, default empty). Rollback drops both; visits already booked stay
  as separate appointments in a row.
- **Not in this step, on purpose:**
  - A visit of several services **cannot be moved online**; the change page says so and offers to cancel. The desk
    moves each part on the calendar as before.
  - **Different people for different services** (hair with one, nails with another).
  - **The desk's own booking form** still books one service; `BookVisitAsync` is ready for it.
  - A part **cancelled or moved at the desk** is told to the client by its own message, as for any appointment; the
    visit's page then shows the parts that still stand.
  - Deposits stay per appointment (ADR 0023): the details page shows their sum, each part carries its own.
  - The waiting list (ADR 0055) stays one service: it is not offered on a visit of several.
- The buffer after a visit (`BufferAfterMinutes`) applies after the last part, not between parts.
- Not covered by a test yet: the reminder skipping the later parts (the confirmation doing so is).

## Alternatives considered

- **One appointment with service lines**: one unit for messages and cancelling, but checkout, commission, reports,
  the client card and the export all read `Appointment.ServiceId` and one price — every one of them would have to
  learn lines, or would quietly credit the whole visit to its first service.
- **Chaining the single-service slot lists** (a start for the second where the first ends): starts are aligned to the
  slot step, so a 20-minute first service would never line up with a second one.
- **Two separate bookings and a hint to pick adjacent times**: what there was.
