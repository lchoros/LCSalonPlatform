# CLAUDE.md — Salon Platform working agreement

Salon Platform is a multi-tenant booking and management SaaS for beauty salons (UK + Poland).
It is a Booksy/Versum alternative: **no marketplace, no commission, flat price, the salon owns its data.**
Owner: Lukasz (solo developer, product owner, final approver). Everything else is done by AI agents.

Read before any work: this file → `docs/system-design.md` → `docs/PLAN.md` → `docs/adr/` → `docs/glossary.md`.

## Phase 1: local only (until M3)
- No Docker. Run natively: .NET 10 SDK, PostgreSQL 17 installed locally, Mailpit exe (SMTP 1025, UI 8025).
- No real payments. Use `ManualPaymentProvider` (implements `IPaymentProvider`): it only records deposits, card/cash
  taken at the salon's own terminal, and no-show fees. No Stripe keys, no card data, no external payment calls.
- No real SMS/email. `LogSmsProvider` writes to the log and Mailpit; email goes to Mailpit.
- No deployment. CI builds and tests only.
- Keep every provider behind its interface so Stripe/Twilio/SMSAPI plugins drop in at M3 without touching slices.

## Stack (do not change without an ADR approved by the owner)
- .NET 10, ASP.NET Core MVC + Razor views, C# latest, nullable enabled, warnings as errors
- PostgreSQL 17 + EF Core 10, NodaTime, UUIDv7 ids
- Tailwind CSS 4 via the standalone CLI (no Node in production), plain ES modules, htmx, Alpine.js, FullCalendar
- Hangfire (Postgres) for jobs; payments/SMS/email via plugins (Manual + Log/Mailpit now; Stripe Connect, Twilio / SMSAPI.pl, Postmark/SES at M3)
- xUnit, Shouldly, Respawn (resets the local test database), Playwright, NetArchTest, FsCheck, Verify

## Architecture (nopCommerce layers + vertical slices)
```
src/Libraries/Salon.Core          entities, domain rules, events, plugin contracts — no EF, no ASP.NET
src/Libraries/Salon.Data          DbContext, mappings, migrations, tenant filter, outbox
src/Libraries/Salon.Services      cross-slice services (availability, pricing, settings, localization, notifications)
src/Presentation/Salon.Web.Framework  base controllers, tenant resolution, auth, pipeline behaviours, tag helpers, plugin loader
src/Presentation/Salon.Web        host; Features/<Module>/<UseCase>/ slices; Areas/Admin; Areas/Booking
src/Plugins/Salon.Plugin.*        payments, SMS, import, calendar sync
tests/*                           Unit, Integration, Architecture, E2E
```
Rules (enforced by `Salon.Tests.Architecture`):
1. Dependencies only point down: Web → Web.Framework → Services → Data → Core. Plugins reference Services/Core only.
2. A slice folder holds its command/query, validator, handler, controller/endpoint, views, JS. One use case per folder.
3. A slice never references another slice. Cross-module = domain event (`IEventPublisher`/`IConsumer<T>`) or a service in `Salon.Services`.
4. Every tenant-owned entity implements `ITenantEntity`. Never bypass the global query filter (`IgnoreQueryFilters` is Tier 2).
5. Times are `Instant` in storage, shown in the location's zone. Money is `decimal` + currency code.
6. All UI text goes through `ILocalizationService` resource keys, with both `en-GB` and `pl-PL` values.
7. Settings go in typed settings classes via `ISettingService`, never hard-coded.
8. No inline `<script>` (CSP nonces). No new JS framework, no bundler.

## Definition of done (every PR)
- Acceptance criteria from the issue covered by tests that failed first
- `dotnet format`, build, unit, integration, architecture and E2E tests green; coverage not lower
- Both languages have strings; mobile view checked (Playwright screenshot attached)
- Migration is additive or has an expand/contract plan; rollback note in the PR
- ADR written for any non-obvious choice
- PR < 600 changed lines (excluding generated migrations/snapshots); split otherwise

## Autonomy tiers (agents decide; CI enforces via `scripts/tier-check.sh`)
- **Tier 0 — auto-merge:** docs, tests, copy/translations, no-behaviour refactors, patch dependency bumps, a bug fix inside one slice with a regression test. Needs: green CI + Reviewer approve.
- **Tier 1 — auto-merge, guarded:** new slices/features behind a feature flag, additive migrations, new screens, plugins behind a flag. Needs: green CI + Reviewer + Tester + Architect approve.
- **Tier 2 — owner only:** payments/refunds, auth/identity, tenancy/RLS, destructive or altering migrations, billing/pricing, GDPR/consent, infra/secrets, CI and this file, new runtime dependency, anything an agent is unsure about. Label `needs-owner`; never merge.
An agent may raise a tier, never lower one.

## How to decide when unsure
Prefer the simpler, reversible option that keeps salon data safe. Write `docs/adr/NNNN-title.md`
with status `Decided — owner may veto`, link it in the PR, and continue. Do not stop to ask unless it is Tier 2.

## Hard limits
- Never commit secrets; never use production credentials; never deploy anything in phase 1.
- Never add a real payment gateway, real SMS sender or Docker files before M3 (Tier 2 if proposed).
- Never weaken, skip or delete a test to get green. Fix the code or escalate.
- Never store card numbers; only Stripe ids.
- Max 5 open agent PRs. Max 3 review rounds, then label `needs-owner`.
- Stop immediately if repo variable `AGENTS_ENABLED` is `false` or the issue/PR has label `agent:stop`.

## Commands
```
# one-time: create databases salon_dev and salon_test in your local Postgres (user/password in appsettings.Development.json / user-secrets)
mailpit                              # local inbox at http://localhost:8025
dotnet watch --project src/Presentation/Salon.Web
dotnet build -warnaserror
dotnet test tests/Salon.Tests.Unit
dotnet test tests/Salon.Tests.Integration
dotnet test tests/Salon.Tests.Architecture
pwsh tests/Salon.Tests.E2E/bin/Debug/net10.0/playwright.ps1 install && dotnet test tests/Salon.Tests.E2E
./tools/tailwindcss -i src/Presentation/Salon.Web/Styles/app.css -o src/Presentation/Salon.Web/wwwroot/css/app.css --minify
dotnet ef migrations add <Name> -p src/Libraries/Salon.Data -s src/Presentation/Salon.Web
```

## Branches, commits, PRs
- Branch: `agent/<issue>-<slug>`; one worktree per Builder (`git worktree add ../wt-<issue> -b agent/<issue>-<slug>`)
- Conventional commits (`feat(booking): ...`), one slice per commit where possible
- PR uses `.github/pull_request_template.md`; label `tier:0|1|2`; stacked PRs merge bottom-up with merge commits
- Digest in `docs/digests/` at the end of each run: merged (tier:2 called out with ADR links), decisions made, blocked
