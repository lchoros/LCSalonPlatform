# System design — Salon Platform (as built at the end of M0)

Read with CLAUDE.md. This file describes what exists; `docs/PLAN.md` says what comes next and `docs/adr/` records why.

## 1. Shape
One ASP.NET Core MVC host (`Salon.Web`) serving every salon from one deployment. A salon is a **tenant**; its public
pages live on `{slug}.{base-domain}` (locally `studio-20.localhost`), the bare base domain serves platform pages only.
Layers follow nopCommerce, slices follow vertical-slice architecture (ADR 0001):

```
Salon.Web (host)          Features/<Module>/<UseCase>/  one use case per folder: command/query, validator, handler,
                          controller, Razor views; Localization/{en-GB,pl-PL}/<Module>.json (one file per module and culture, ADR 0006 amendment); Views/ for the host pages
Salon.Web.Framework       AddSalonFramework, tenant resolution middleware, request localization, pipeline behaviours,
                          plugin loader, widget-zone tag helper, Hangfire wiring, filters (RequireTenant, FeatureGate)
Salon.Services            cross-slice services: TenantLookup, SettingService, LocalizationService, cache, events/outbox,
                          schedule tasks, MoneyFormatter, DemoSeeder, SMTP email
Salon.Data                SalonDbContext, EF configurations, migrations, tenant connection interceptor, RLS helper
Salon.Core                entities, Money, events, CQRS contracts, plugin/payment/SMS contracts, SupportedCultures
Salon.Plugin.*            Payments.Manual (record-only), Sms.Log (log + Mailpit) — Phase 1 stand-ins behind interfaces
```
Dependencies point down only; `Salon.Tests.Architecture` fails the build on a violation, on a slice referencing another
slice, on a tenant-owned entity without `ITenantEntity`, and on any type outside `Salon.Services.Events` touching
`OutboxMessage`.

## 2. Request path
1. `TenantResolutionMiddleware` (ADR 0002): an authenticated user's `tenant_id` claim, else the subdomain slug, looked
   up through the cached `ITenantLookup`; unknown or inactive → 404; bare domain → no tenant.
2. `UseSalonLocalization` (ADR 0006): `?culture=` → culture cookie → the tenant's culture → `Accept-Language` (neutral
   `pl` maps to `pl-PL`) → `en-GB`. Only `en-GB` and `pl-PL` exist.
3. MVC routing to a slice controller (`[Route]` attributes). `[RequireTenant]` turns a tenant-less request into a 404;
   `[FeatureGate("Flag")]` hides a slice while its `Features:<Flag>` setting is off.
4. The controller sends a command or query through `IDispatcher` (ADR 0003). Behaviours, in order: logging →
   FluentValidation (all validators for the request, failures aggregated) → tenant guard (a request needs a tenant
   unless it implements `IPlatformRequest`) → transaction (commands only, `IUnitOfWork` over the scoped DbContext).
5. Razor view next to the controller (`FeatureViewLocationExpander`), text through `@T["Key"]`, Tailwind classes,
   no inline scripts. Polish characters ship as UTF-8 (encoder widened to Latin Extended).

## 3. Tenant isolation (ADR 0002)
- Every tenant-owned entity implements `ITenantEntity`; `SalonDbContext` applies a named global filter on
  `TenantId == current tenant` and refuses to save a row that belongs to another tenant (`TenantViolationException`).
- `TenantConnectionInterceptor` runs `set_config('app.tenant_id', …)` when a connection opens; every tenant table has a
  **forced** Postgres row-level-security policy keyed on that setting, so a non-superuser role cannot read across tenants
  even with raw SQL. Startup refuses connection strings that disable the pool reset (`No Reset On Close`, `Multiplexing`).
- Background work uses `ITenantScopeRunner` (fresh DI scope with the tenant set before any connection opens).
- Deliberate exception (ADR 0007, Tier 2): `outbox_messages` is platform-level with a nullable `tenant_id`, because
  the dispatcher reads across tenants; consumers still run inside the message's tenant scope.

## 4. Platform services
| Service | Where | Notes |
| --- | --- | --- |
| `ISettingService` (ADR 0004) | Services/Settings | typed classes (`BookingSettings`), one row per property per tenant, cached per tenant, invalidated on save |
| `IStaticCacheManager` | Services/Caching | `CacheKey` with prefixes; `MemoryCacheManager` keeps a key index for prefix removal |
| `ILocalizationService` (ADR 0006) | Services/Localization | embedded `*.Localization.<culture>.json` per assembly, merged at startup, duplicate or unsupported → startup failure, pl-PL → en-GB → key fallback |
| `IEventPublisher` / `IConsumer<T>` (ADR 0007) | Services/Events | publisher writes an `OutboxMessage` on the handler's DbContext (atomic with the command); `OutboxDispatcher` runs consumers per tenant scope with retry (30 s doubling, 1 h cap), dead-letter after 5 attempts, per-row saves; `OutboxDispatchTask` every minute |
| `IScheduleTask` (ADR 0005) | Services/Tasks | Hangfire recurring jobs, one distributed lock per task, Postgres storage schema `hangfire` |
| Plugins | Web.Framework/Plugins | `IPlugin` + embedded `plugin.json`, validated at startup; `IWidgetPlugin` renders into `<widget-zone>` |
| `MoneyFormatter` | Services/Formatting | native currency → culture symbol (`£45.00`, `45,00 zł`), foreign → `ISO amount` |

## 5. Data
PostgreSQL 17, schema `salon`, snake_case names, UUIDv7 ids, `Instant` (timestamptz) for time, `decimal` + ISO
currency for money. Tables so far: `tenants`, `services` (RLS), `settings` (RLS), `users` (RLS, ADR 0009),
`message_templates` (ADR 0008), `staff_members` and `working_hours` (RLS, ADR 0010; `LocalTime` as `time`, local to the
salon), `clients` (RLS, ADR 0011; health notes as Data Protection cipher text), `appointments` and
`appointment_status_changes` (RLS, ADR 0012; GiST exclusion constraint against overlapping active visits of one staff
member, `client_id` FK to `clients` with Restrict), `client_account_entries` (RLS, ADR 0022 and 0023; append-only fee,
settlement, deposit and visit-payment lines, partial unique indexes for one fee, one deposit and one payment per visit,
one settlement per fee and one return per deposit; `appointments.deposit_amount`, `services.deposit_percent` and `clients.deposit_percent` carry the
deposit rules), `cash_ups` (RLS, ADR 0024; one end-of-day count per salon and day), `locations` (RLS, ADR 0027; the first active
row is the salon's main location), `outbox_messages` (no RLS,
see §3). The demo salon also gets three
staff with weekly hours (Anna Kowalska, Tom Reed, Priya Shah), twelve clients and a few appointments over the coming
working days.
Migrations are additive; rollback notes live in each PR. Local: `salon_dev` (app, migrates and seeds `studio-20` on
start in Development) and `salon_test` (tests, reset by Respawn). The demo salon also gets two logins once ADR 0009 lands:
`owner@studio-20.local` (Owner) and `desk@studio-20.local` (Receptionist), password from `Demo:OwnerPassword` in
`appsettings.Development.json` (a local value, not a secret).

## 6. Slices
- `Catalog/ListServices`, `Catalog/CreateService`, `Catalog/EditService`, `Catalog/ManageCategories` — `/admin/services`,
  `/admin/services/new`, `/admin/services/{id}`, `/admin/services/categories`, behind `Catalog.Manage` (ADR 0009,
  0013); `CreateService` publishes `ServiceCreated`.
- `Booking/ListServices` — `/services`, the public price list grouped by category; one tap from the salon home page.
- `Booking/ChooseService`, `Booking/ChooseSlot`, `Booking/EnterDetails`, `Booking/VerifyCode`, `Booking/Confirmation`,
  `Booking/CancelBooking` — the public flow under `/book` (ADR 0014): service → staff or any → day and slot → name and
  phone → 6-digit SMS code → booked, with a signed cancel link (`/book/cancel/{token}`) in the confirmation SMS. Three
  taps to a slot on a phone; server-rendered, no JavaScript needed.
- `Staff/ListStaff`, `Staff/CreateStaff`, `Staff/EditStaff`, `Staff/EditWorkingHours`, `Staff/TimeOff` — `/admin/staff`,
  `/admin/staff/new`, `/admin/staff/{id}/edit`, `/admin/staff/{id}/hours`, `/admin/staff/{id}/time-off`, all behind
  `Staff.Manage` (ADR 0010, 0015); `CreateStaff` publishes `StaffMemberCreated`, the hours form replaces a person's
  whole weekly rota in one transaction, time off becomes busy intervals for the availability engine.
- `Clients/SearchClients`, `Clients/CreateClient`, `Clients/EditClient`, `Clients/ClientCard` — `/admin/clients`,
  `/admin/clients/new`, `/admin/clients/{id}/edit`, `/admin/clients/{id}`, behind `Clients.View` (ADR 0011); the card
  and the edit form are the only read paths that decrypt health notes.
- `Appointments/DayList`, `Appointments/BookForClient` — `/admin/appointments?date=` (one column per bookable staff
  member, status buttons posting to `/admin/appointments/{id}/{confirm|arrived|complete|no-show|cancel}`) and
  `/admin/appointments/new` (client picker, service, staff or any, date, slots from `IAvailabilityService` as small POST
  forms, booked through `IBookingService` as `Source.Admin`), behind `Bookings.Manage` + `Clients.View` because both
  show client names (ADR 0012).
- `Appointments/Calendar` — `/admin/appointments/calendar?view=day|week&date=&staff=` (ADR 0016): a CSS-grid timetable
  computed by the pure `CalendarLayout` in Services, an appointment card at `calendar/{id}` with the day list's status
  buttons and a move form posting to `calendar/{id}/move` (`IBookingService.MoveAsync`, re-checked through the engine,
  publishes `AppointmentMoved`). Same permissions as the day list. FullCalendar drag-and-drop is deferred until the
  library is vendored.
- `Catalog/StaffOverrides` — `/admin/services/{id}/staff` behind `Catalog.Manage` (ADR 0018): per person "offers",
  duration and price for one service. `IServiceOfferingResolver` (Services) is the single read path; the availability
  engine, `BookAsync`, the public price list ("from") and the public flow all quote through it.
- `Clients/MergeClients` — `/admin/clients/{id}/merge` behind `Clients.View` + `Clients.Merge` (ADR 0017): pick the card
  to keep, confirm, appointments move in one transaction and the duplicate stays as an inactive tombstone. The card
  (`Clients/ClientCard`) shows visit history.
- `Clients/ImportClients` — `/admin/clients/import` behind `Clients.Manage` and `Features:ClientImport` (ADR 0019):
  upload a Booksy or Versum CSV, preview (new / duplicate / invalid), confirm. Parsing lives in the plugin
  `Salon.Plugin.Import`; the preview is kept server-side under a random token bound to the salon.
- `Settings/SalonDetails` — `/admin/settings/salon-details` behind `Settings.Manage` (ADR 0027): address, phone and
  email of the salon's main location through `ILocationService`. The `SalonContact` view component shows them as
  "Where to find us" on the public service list, details step, confirmation, cancel and change pages (nothing while
  there is no address), and `AppointmentNotifier` ends every booking email with them.
- `Settings/MessageTemplates` — `/admin/settings/messages` and `/{key}/{sms|email}` behind `Templates.Manage`
  (ADR 0030): the salon's own wording of each client message in the tenant's culture through
  `IMessageTemplateService`, placeholders checked by the pure `MessageTemplateRules`, a preview with made-up details
  that is not saved, and the number of texts it costs (`SmsLengthRules`). The Settings tabs show each role only the
  pages it may open; a manager's Settings link leads here.
- `Settings/Notifications` — `/admin/settings/notifications` behind `Settings.Manage` (ADR 0020): reminders on/off, two
  lead times, quiet hours (`NotificationSettings`).
- `Settings/CancellationPolicy` — `/admin/settings/cancellation-policy` behind `Settings.Manage` (ADR 0022): notice
  period in hours and the late-cancellation and no-show fee as a percentage of the booked price
  (`CancellationPolicySettings`; all zero by default, which is "no policy"). Both settings pages share `_SettingsNav`.
- `Payments/VisitFee` — `/admin/payments/fees/{appointmentId}` behind `Payments.Record` + `Clients.View` (ADR 0022): the
  fee the policy allows for a missed or late-cancelled visit (pure `CancellationPolicy` in Core), a form to add it
  (amount can be lowered), "client paid" (through `IPaymentProvider`, Manual: record only) and "waive" (reason
  required), with who did what. `IClientAccountService` (Services) owns the ledger; the day list redirects here after
  a no-show or late cancellation, the client card lists the fees and what is owed, and the calendar card of a later
  visit says when the client owes something.
- `Payments/VisitDeposit` — `/admin/payments/deposits/{appointmentId}` behind `Payments.Record` + `Clients.View`
  (ADR 0023): the deposit a booking asks for (pure `DepositRules` in Core: client, else service, else salon; snapshot
  on the appointment), a form to record it received and one to record it returned, both through `IPaymentProvider`
  (Manual: record only). The calendar card shows what is due or received; a deposit the salon holds is taken off the
  fee the fee page offers.
- `Payments/Checkout` — `/admin/payments/checkout/{appointmentId}` behind `Payments.Record` + `Clients.View`
  (ADR 0024): the bill of a visit the client turned up for (pure `TillRules` in Core: price less the deposit held),
  the client's open fees ticked onto it, one payment through `IPaymentProvider` (Manual: record only). Paying an
  arrived visit completes it in the same transaction. Linked from the day list, the calendar card and the client's
  history.
- `Payments/CashUp` — `/admin/payments/cash-up?date=` behind the same permissions (ADR 0024): `ICashUpService` adds up
  the day's ledger lines that carry a method (in the salon's zone), per method, with the cash the drawer should hold;
  the day is closed once with the cash counted (`CashUp`), and money recorded later is flagged. "Till" in the nav.
- `Reports/Overview` — `/admin/reports?period=` or `?from=&to=` behind `Reports.View` + `Clients.View` (ADR 0026), read
  only: `IReportService` reads the period's ledger lines and visits and adds them up with `TillRules` and the pure
  `ReportRules` in Core (periods, rates, working minutes). Takings per method, kind and day (each day links to its
  till), visits by outcome, fees, completed visits not paid (linked to their checkout), top services, and per person
  revenue and utilisation from the rota less time off. Owner and Manager only. "Reports" in the nav.
- `Booking/Reschedule` — `/book/change/{token}` (ADR 0021): the client's own link moves the visit through
  `IBookingService.MoveAsync`; inside the policy's notice period it offers no times (ADR 0022), while
  `Booking/CancelBooking` still cancels and states the late fee first. The details page states the policy before the
  client books.

Background work (ADR 0005 schedule tasks, all per salon through `ITenantScopeRunner`): `Outbox.Dispatch` (ADR 0007),
`Booking.CleanupVerifications` (hourly, ADR 0017) and `Notifications.SendReminders` (every five minutes, ADR 0020).
Reminders are computed from the appointment when due and claimed by a unique `appointment_reminders` row before the SMS
is handed over, so a client is never texted twice for one moment.

Client messages (ADR 0025): each booking message has one rule (`AppointmentBookedNotice`, `AppointmentCancelledNotice`,
`AppointmentMovedNotice` in `Salon.Services/Notifications`) and one consumer per channel, SMS and email, so the outbox
retries a failed channel alone: `outbox_messages.completed_consumers` lists the consumers that are done with an event,
the dispatcher gives every consumer its turn per attempt and skips the finished ones on a retry. `AppointmentNotifier`
renders the template for the channel and hands it to `ISmsSender` or `IEmailSender`; emails are the plain template
text rendered to safe HTML (`EmailBodies`). `NotificationSettings.EmailEnabled` switches the email channel per salon. Every booking
carries a public token, so a booking made at the desk is confirmed like an online one and its client can change or
cancel through the same link (ADR 0029); `NotificationSettings.ConfirmDeskBookings` switches the desk confirmation off. Messages carry the short
address `/v/{token}` (ADR 0031), which forwards to `/book/cancel/{token}`; the token is 16 random bytes (22
characters), and 43-character tokens from before keep working.

## 7. Testing
Unit (xUnit + Shouldly + NSubstitute, FsCheck properties for availability and reminder rules), Architecture (NetArchTest
+ resource parity per module file + outbox isolation + the E2E wait guard), Integration (`WebApplicationFactory` against
local Postgres, Respawn reset, tenant-scope helpers, RLS proven as a non-superuser role), E2E (Playwright, 390 px and
1280 px, axe, screenshots). CI runs all four on `ubuntu-24.04` with a Postgres 17 service container, plus gitleaks on
PRs and CodeQL on `main` (findings fail the job).

E2E rules that keep the suite stable: wait for navigation with `page.ReachAsync(...)`
(`tests/Salon.Tests.E2E/Infrastructure/PageNavigationExtensions.cs`), never Playwright's `WaitForURLAsync`, which can
miss the load event on a cold process (an architecture test enforces this); click the link or button itself, because CI
pages are unstyled; assert whole phrases, never three or four letters that random tokens can contain. To watch a run in
a visible browser: `pwsh scripts/watch-e2e.ps1 -Filter <name>`.

## 8. Not yet built
M1 remainder: several locations per salon (one exists, ADR 0027), FullCalendar drag-and-drop with live updates. M2 leftovers: payment reversals, split
payments, tips, discounts and retail products at the checkout. Reports:
export, charts, comparison with the period before. M3:
payments beyond the record-only provider, real SMS/email, deployment.
