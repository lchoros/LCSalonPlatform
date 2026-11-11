# System design — Salon Platform (as built at the end of M0)

Read with CLAUDE.md. This file describes what exists; `docs/PLAN.md` says what comes next and `docs/adr/` records why.

## 1. Shape
One ASP.NET Core MVC host (`Salon.Web`) serving every salon from one deployment. A salon is a **tenant**; its public
pages live on `{slug}.{base-domain}` (locally `studio-20.localhost`), the bare base domain serves platform pages only.
Layers follow nopCommerce, slices follow vertical-slice architecture (ADR 0001):

```
Salon.Web (host)          Features/<Module>/<UseCase>/  one use case per folder: command/query, validator, handler,
                          controller, Razor views; Localization/{en-GB,pl-PL}.json; Views/ for the host pages
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
currency for money. Tables so far: `tenants`, `services` (RLS), `settings` (RLS), `outbox_messages` (no RLS, see §3).
Migrations are additive; rollback notes live in each PR. Local: `salon_dev` (app, migrates and seeds `studio-20` on
start in Development) and `salon_test` (tests, reset by Respawn).

## 6. Slices
- `Catalog/CreateService` — `/admin/services/new`, behind `Features:CatalogAdmin` (off by default until permissions
  exist), publishes `ServiceCreated`.
- `Booking/ListServices` — `/services`, the public price list; one tap from the salon home page.

## 7. Testing
Unit (xUnit + Shouldly + NSubstitute), Architecture (NetArchTest + resource parity + outbox isolation), Integration
(`WebApplicationFactory` against local Postgres, Respawn reset, tenant-scope helpers, RLS proven as a non-superuser
role), E2E (Playwright, 390 px and 1280 px, axe, screenshots). CI runs all four on `ubuntu-24.04` with a Postgres 17
service container, plus gitleaks and CodeQL (findings fail the job).

## 8. Not yet built (M0 remainder and M1)
Authentication, permissions and `[RequirePermission]`; message templates; staff, locations, availability and the
booking flow; payments beyond the record-only provider; real SMS/email; deployment (M3).
