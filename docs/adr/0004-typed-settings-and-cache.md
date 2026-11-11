# 0004 — Typed per-tenant settings and a prefix-invalidated static cache

Status: Decided — owner may veto
Date: 2026-11-11

## Context
CLAUDE.md rule 7: settings live in typed classes behind `ISettingService`, never hard-coded. Every salon tunes its own
rules (notice period, deposits, SMS verification), so settings are tenant data and fall under ADR 0002. Tenants and
settings are read on almost every request, so they need a cache that can be invalidated as a group.

## Decision
- **One row per property.** `Setting { TenantId, Name, Value }` in table `settings`, unique on `(tenant_id, name)`,
  `Name` = `"{ClassName}.{Property}"` (nopCommerce style). Adding a property to a settings class needs no migration;
  a missing row means "use the class default". The table has the forced RLS policy like every tenant table.
- **Per tenant only.** There are no platform-wide settings rows. `ISettingService` throws `TenantViolationException`
  when no tenant is in scope instead of letting the EF filter return defaults silently. Platform configuration stays
  in `appsettings` + typed options.
- **InvariantCulture text.** `SettingsBinder` serialises with the invariant culture (`12.5`, `True`, ISO-8601 dates,
  NodaTime `LocalTime` extended-ISO and `Duration` round-trip patterns, enums by name) so a Polish and a British salon
  share one storage format. Supported types: string, int, long, decimal, bool, Guid, enums, DateOnly, TimeOnly,
  LocalTime, Duration and their nullable forms.
- **Cache with prefixes.** `IStaticCacheManager` (`CacheKey` = key + prefixes + 60-minute default TTL) in
  `Salon.Services`; `MemoryCacheManager` wraps `IMemoryCache` and keeps a key index so `RemoveByPrefixAsync` works.
  Settings cache under `Salon.settings.{tenantId}` (prefix `Salon.settings.`) and are dropped on every save;
  tenant snapshots cache under `Salon.tenant.*` (prefix `Salon.tenant.`, 5 minutes). Per-tenant keys always embed the
  tenant id because the cache sits above the tenant filter.

## Consequences
- Reading a settings class costs one cached dictionary lookup per tenant; saving writes N small rows in one transaction.
- Settings rows are plain text, so a typo in a property type's text form fails loudly at bind time (FormatException).
- A distributed cache (Redis/Garnet) is deferred to M3; it plugs in behind `IStaticCacheManager` with the same prefixes.
- Platform-wide settings, if ever needed, are a separate decision (they would bypass the tenant filter: Tier 2).
