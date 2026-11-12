# PLAN — Salon Platform

Status key: [ ] todo · [~] in progress · [x] done · [!] needs owner

## M0 Walking skeleton (weeks 1–2)
Gate: CI green and agents merge a Tier 0 PR on their own. — Met 2026-11-11: CI green on main after the run-1 stack merged (the owner clicks merge; see docs/digests/2026-11-11-m0-run-2.md).
- [x] Solution layers + Directory.Build.props / Directory.Packages.props (2026-11-10: Salon.slnx, 7 src + 4 test projects, layer tests green)
- [x] Tenancy: ITenantContext, global filter, Postgres RLS, tenant-isolation test helper — `agent/m0-02-tenancy`, `agent/m0-02b-tenancy-tests`, `agent/m0-03-tenant-resolution`; merged to main 2026-11-11, CI green on main
- [~] Platform services: settings, localization (en-GB, pl-PL), events + outbox, cache, schedule tasks, plugin loader, widget zones, message templates, permissions — done: settings (`agent/m0-05b-settings-impl`), cache (`agent/m0-05-cache`), schedule tasks (`agent/m0-07-schedule-tasks`), plugin loader + widget zones (`agent/m0-06-plugins-widgets`), localization (`agent/m0-09-localization`, ADR 0006), events + outbox (`agent/m0-10-events-outbox`, ADR 0007), message templates (`agent/m0-15-message-templates` + `-15b-message-template-service` + `-15c-message-template-tests`, ADR 0008: token renderer, catalogue, embedded defaults, per-tenant `message_templates` overrides behind `IMessageTemplateService`); permissions (`agent/m0-16-identity`, ADR 0009) (last open M0 item)
- [x] Slice pipeline behaviours — `agent/m0-08-slice-pipeline` + tests branch; merged to main 2026-11-11, CI green on main
- [x] Catalog/CreateService + Booking/ListServices end to end — `agent/m0-11-catalog-create-service` (+ `-tests`): admin form (first behind the `Features:CatalogAdmin` flag, now `[RequirePermission(Catalog.Manage)]`, ADR 0009), `[RequireTenant]` + `[FeatureGate]` filters, `ServiceCreated` through the outbox; `agent/m0-11b-booking-list-services`: public `/services` with localized prices and durations, home → services link, mobile E2E with screenshots
- [x] Run locally without Docker: local Postgres 17, Mailpit, dotnet watch; CI and agent workflows (no deploy) — README "Run locally" written; merged to main 2026-11-11, CI green on main
- [~] ManualPaymentProvider + LogSmsProvider behind their interfaces — contracts + providers + unit tests in place; not yet wired to appointments or settings
- [x] Demo tenant seed "studio-20" — `agent/m0-11c-demo-seed`: `DemoSeeder` (Development only, `Database:SeedDemo`, idempotent) creates Studio 20 (Europe/London, en-GB, GBP) with six services; since `agent/m1-01-staff` (ADR 0010) also three staff with weekly hours (Anna Kowalska, Tom Reed, Priya Shah), added as well to an existing demo salon that has none

## M1 Booking core (weeks 3–6)
Gate: a test client books on a phone in under 60 seconds.
- [~] Catalog: categories, services, staff-specific price/duration, processing time, buffers — done: `ServiceCategory`, buffer and processing time on `Service`, `Catalog/ListServices`, `Catalog/EditService`, `Catalog/ManageCategories` under `/admin/services`, grouped public list (ADR 0013, PR #43); remaining: staff-specific price/duration (in progress, `agent/m1-14-staff-service-overrides`, ADR 0018)
- [~] Staff: working hours, exceptions, breaks, roles — done: `StaffMember` + `WorkingHours` (ADR 0010), `Staff/ListStaff`, `Staff/CreateStaff`, `Staff/EditWorkingHours` under `/admin/staff` behind `Staff.Manage`, admin nav, demo staff (`agent/m1-01-staff`); `Staff/EditStaff` edit/deactivate (PR #44); `Staff/TimeOff` whole-day and partial-day absences as busy time (ADR 0015, PR #49); remaining: breaks inside a working day, roles beyond Owner/Receptionist
- [x] Availability engine + exclusion constraint + property tests — pure `AvailabilityCalculator` (FsCheck properties), `IAvailabilityService`, GiST exclusion constraint on `appointments` (ADR 0012, PR #41)
- [x] Public booking page (service → staff/any → slot → SMS code → confirm) — `Booking/ChooseService`, `ChooseSlot`, `EnterDetails`, `VerifyCode`, `Confirmation`, `CancelBooking` under `/book`, SMS code through `ISmsProvider`, confirmation SMS consumer, signed cancel link (ADR 0014, PR #47; **gate met**: the 390 px E2E books in about one second of interaction) + phone-first UX pass (PR #48)
- [~] Admin calendar (FullCalendar day/week, drag to move, block time) with live updates — done: server-rendered day/week grid, appointment card, move form through `IBookingService.MoveAsync` (ADR 0016, PR #50); block time is covered by staff time off (ADR 0015); remaining: FullCalendar drag-and-drop and live updates once the owner vendors the library
  - [x] Day list + book for client (precursor to FullCalendar) — `Appointments/DayList` (status actions through the state machine) and `Appointments/BookForClient` (client picker, live slots) under `/admin/appointments` behind `Bookings.Manage` + `Clients.View`; FK `appointments.client_id → clients`; demo clients and appointments (`agent/m1-07-admin-booking`)
- [~] Clients: card, history, notes, allergies, patch test, merge duplicates — done: `Client` with encrypted health notes and consent record (ADR 0011), `Clients/SearchClients`, `Clients/CreateClient`, `Clients/ClientCard` under `/admin/clients` behind `Clients.View` (PR #42), `Clients/EditClient` (PR #46); remaining: visit history on the card, merge duplicates, verification-code retention (in progress, `agent/m1-13-client-history-merge`, ADR 0017)
- [x] Appointment state machine + history + events — `Appointment`, `AppointmentStatusChange`, `AppointmentStateMachine`, `IBookingService` publishing `AppointmentBooked/Confirmed/Arrived/Completed/NoShow/Cancelled` through the outbox (ADR 0012, PR #41)

## M2 Money and messages (weeks 7–9)
Gate: deposits, no-show fees and the cash-up add up with the Manual payment provider. Nothing is really charged.
- [ ] Deposit rules per service/client, recorded by staff via ManualPaymentProvider
- [ ] No-show / late-cancel fee with preview and audit, recorded and added to the next bill
- [ ] Notifications: templates with tokens, 24h + 2h reminders, quiet hours, confirm/reschedule/cancel links (to Mailpit/log)
- [ ] POS checkout + cash-up
- [ ] Import plugin: Booksy/Versum client CSV with preview

## M3 Pilot (weeks 10–12)
Gate: the pilot salon pays; then 3 paying salons before scaling up.
- [!] First hosting: Dockerfile, staging + prod, deploy job (Tier 2)
- [!] Stripe Connect plugin: onboarding, online deposits, card on file, refunds (Tier 2)
- [!] Real SMS/email plugins: Twilio (UK), SMSAPI.pl (PL), Postmark/SES (Tier 2)
- [ ] Polish UI complete, legal pages, DPA in sign-up
- [!] Own SaaS billing (Tier 2)
- [ ] Reports: takings, bookings, no-shows, top services, utilisation
- [ ] Pilot salon onboarding, daily fix list

## v1.1 Grow
Waitlist · gift vouchers · Przelewy24 + BLIK · consultation forms · resources · public API + webhooks · AI receptionist
