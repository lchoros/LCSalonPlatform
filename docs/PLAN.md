# PLAN — Salon Platform

Status key: [ ] todo · [~] in progress · [x] done · [!] needs owner

## M0 Walking skeleton (weeks 1–2)
Gate: CI green and agents merge a Tier 0 PR on their own.
- [ ] Solution layers + Directory.Build.props / Directory.Packages.props
- [ ] Tenancy: ITenantContext, global filter, Postgres RLS, tenant-isolation test helper
- [ ] Platform services: settings, localization (en-GB, pl-PL), events + outbox, cache, schedule tasks, plugin loader, widget zones, permissions
- [ ] Slice pipeline behaviours
- [ ] Catalog/CreateService + Booking/ListServices end to end
- [ ] Run locally without Docker: local Postgres 17, Mailpit, dotnet watch; CI and agent workflows (no deploy)
- [ ] ManualPaymentProvider + LogSmsProvider behind their interfaces
- [ ] Demo tenant seed "studio-20"

## M1 Booking core (weeks 3–6)
Gate: a test client books on a phone in under 60 seconds.
- [ ] Catalog: categories, services, staff-specific price/duration, processing time, buffers
- [ ] Staff: working hours, exceptions, breaks, roles
- [ ] Availability engine + exclusion constraint + property tests
- [ ] Public booking page (service → staff/any → slot → SMS code → confirm)
- [ ] Admin calendar (FullCalendar day/week, drag to move, block time) with live updates
- [ ] Clients: card, history, notes, allergies, patch test, merge duplicates
- [ ] Appointment state machine + history + events

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
