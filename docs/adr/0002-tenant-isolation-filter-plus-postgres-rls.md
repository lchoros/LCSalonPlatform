# 0002 — Tenant isolation: scoped context, named EF filter and Postgres row-level security

Status: Decided — owner may veto
Date: 2026-11-11

## Context
Every salon's data lives in one PostgreSQL database. CLAUDE.md rule 4 says every tenant-owned entity implements
`ITenantEntity` and nothing may bypass the global filter. A filter alone is one bug away from a leak (a raw SQL
query, a forgotten `IgnoreQueryFilters`, a job with the wrong scope), so the database has to enforce the same rule.

## Options
1. EF global query filter only. Simple, but a single bypass leaks data across salons.
2. Schema per tenant. Strong isolation, but migrations × tenants and connection routing are heavy for one person.
3. Shared tables + EF filter + Postgres row-level security keyed on a session setting. Two independent guards,
   one schema, one migration path.

## Decision
Option 3.
- `ITenantContext` (read) and `IMutableTenantContext` (set) are scoped services. Only infrastructure sets the tenant:
  HTTP middleware (next PR), the job runner and test helpers. Slices never set it.
- `SalonDbContext` adds the **named** query filter `"Tenant"` (`TenantId == CurrentTenantId`) to every `ITenantEntity`
  at model build time, plus an index on `tenant_id`. With no tenant in scope the filter matches nothing.
- `SaveChanges` refuses writes with no tenant, writes for another tenant, and changes to `TenantId`
  (`TenantViolationException`). New rows get the current tenant automatically.
- `TenantConnectionInterceptor` runs `set_config('app.tenant_id', …)` whenever a connection opens. Npgsql resets session
  state when a connection returns to the pool, so a pooled connection never carries a stale tenant.
- Every tenant table gets `ENABLE` + `FORCE ROW LEVEL SECURITY` and the policy `tenant_isolation`
  (`tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid`) through `RowLevelSecurity.Enable` in its
  migration. `FORCE` means the table owner is covered too; only superusers bypass RLS.
- `Service` is introduced now as the first tenant-owned entity so the isolation tests run against a real table.
  The Catalog slice extends it in M1.

## Consequences
- Production must connect as a non-superuser role (`salon_app`, created at M3). Local dev uses `postgres`, which bypasses
  RLS; the integration tests therefore create and use a dedicated non-superuser role to prove the policies.
- `RowLevelSecurityTests` fails if any `ITenantEntity` table lacks a forced policy, so new tables cannot forget it.
- Cross-tenant work (platform admin, reports) needs an explicit `IgnoreQueryFilters([SalonDbContext.TenantFilterName])`
  and a superuser-free way to clear `app.tenant_id`; both are Tier 2 and not available yet.
- Setting the tenant after a connection is already open in the same scope does not re-run `set_config`; infrastructure
  sets the tenant before the first query. A job runner that switches tenants must use a new scope per tenant.
