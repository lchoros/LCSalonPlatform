# 0005 — Schedule tasks: one Hangfire recurring job per IScheduleTask, per-tenant scopes for jobs

Status: Decided — owner may veto
Date: 2026-11-11

## Context
Reminders, outbox dispatch and clean-ups must run on a schedule, exactly once even when a second server appears at M3,
and must respect tenant isolation (ADR 0002) although jobs have no HTTP request to resolve a tenant from.

## Decision
- `IScheduleTask` (Core) = `SystemName` + default `CronExpression` + `ExecuteAsync`. Tasks are registered in DI.
- Hangfire stores jobs in the same PostgreSQL database in its own schema `hangfire` (Hangfire.PostgreSql manages it;
  Respawn ignores it). The server runs in-process when `Jobs:Enabled` is true; tests keep storage and registrations but
  no server.
- `UseSalonScheduleTasks()` calls `AddOrUpdate` once per task at startup with the task's SystemName as job id; the
  single entry point `ScheduleTaskRunner.RunAsync(systemName)` carries `[DisableConcurrentExecution("Salon.ScheduleTask.{0}")]` (Hangfire's
  storage-backed distributed lock, one resource per task SystemName) and `[AutomaticRetry(Attempts = 0)]`, so a slow task never overlaps itself and a
  failed occurrence waits for the next tick instead of retry storms.
- Jobs run with no tenant. `ITenantScopeRunner.ForEachTenantAsync` opens a fresh DI scope per active tenant with
  `IMutableTenantContext` set, so the EF filter and Postgres RLS apply; one tenant's failure is logged and the loop continues.
- No Hangfire dashboard yet: it needs authentication (M1, Tier 2).

## Consequences
- Adding a task = one class + DI registration; CI's `ScheduleTaskRegistrationTests` proves it is scheduled.
- Cron strings are code defaults for now; a per-task override via `ISettingService` is a later, additive change.
- Hangfire pulls Newtonsoft.Json; the version is pinned centrally to a patched release.
