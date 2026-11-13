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
member, `client_id` FK to `clients` with Restrict), `outbox_messages` (no RLS, see §3). The demo salon also gets three
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

## 7. Testing
Unit (xUnit + Shouldly + NSubstitute), Architecture (NetArchTest + resource parity + outbox isolation), Integration
(`WebApplicationFactory` against local Postgres, Respawn reset, tenant-scope helpers, RLS proven as a non-superuser
role), E2E (Playwright, 390 px and 1280 px, axe, screenshots). CI runs all four on `ubuntu-24.04` with a Postgres 17
service container, plus gitleaks and CodeQL (findings fail the job).

## 8. Not yet built (M1 remainder)
Locations, staff breaks inside a working day, staff-specific price and duration (in progress), client visit history and
duplicate merge (in progress), FullCalendar drag-and-drop with live updates; payments beyond the record-only provider;
real SMS/email; deployment (M3).
