# 0007 — Domain events through a transactional outbox

Status: Decided — owner may veto (contains one Tier 2 item, see "Tenant isolation exemption")
Date: 2026-11-11

## Context
CLAUDE.md rule 3: slices never call each other; cross-module work goes through `IEventPublisher` / `IConsumer<T>`
(Core contracts since ADR 0001). Consumers must not run inside the publishing command's transaction (a slow SMS
consumer would hold locks, a failing one would roll the booking back), yet an event must never be lost when the
command commits, nor delivered when it rolls back. Consumers are tenant code, so they must run with the publishing
tenant in scope (ADR 0002), while the dispatcher itself runs as a platform job (ADR 0005).

## Options
1. In-process publish after commit — no table, but a crash between commit and publish loses the event, and the
   publisher would need to know about the pipeline's transaction.
2. A message broker — a new runtime dependency (Tier 2) with nothing to gain at one server.
3. Transactional outbox: the publisher inserts a row in the same transaction as the handler's writes; a Hangfire task
   dispatches rows to consumers afterwards, with retries and a dead letter.

## Decision
Option 3.
- **`OutboxMessage`** (Core, table `outbox_messages`) carries `TenantId?`, `EventType`, `Payload` (jsonb),
  `OccurredAt`, `ProcessedAt`, `Attempts`, `LastError` (4000 chars), `FailedAt`, `NextAttemptAt`, and uses Postgres'
  `xmin` as its concurrency token. A filtered index on `(next_attempt_at, id) WHERE processed_at IS NULL AND
  failed_at IS NULL` serves the dispatcher's query.
- **Tenant isolation exemption (Tier 2 — the owner must accept this explicitly).** `outbox_messages` is deliberately
  *not* an `ITenantEntity`: it has no EF tenant filter and no RLS policy, because the dispatcher is a platform job that
  reads the pending rows of every tenant in one query, and a payload may contain client data of the publishing salon
  (names, phone numbers, appointment details). It is allow-listed in `TenantEntityTests` and the architecture test
  `OutboxIsolationTests` forbids any reference to `OutboxMessage` from `Salon.Web`, the plugins and `Salon.Services`
  outside `Salon.Services.Events`; only the Data mapping and the events namespace touch it, rows always carry the
  tenant id, consumers always run inside that tenant's scope, and no slice or screen exposes the table.
  *Alternative if the owner rejects the exemption:* make `OutboxMessage` an `ITenantEntity` with the forced RLS policy
  like every other table and dispatch per tenant through `ITenantScopeRunner.ForEachTenantAsync` (one query per active
  tenant per minute; platform events would need a separate `platform_outbox_messages` table). Entity, publisher,
  registry and consumers stay as they are; only the dispatcher's loading loop and the migration change.
- **`OutboxEventPublisher`** (scoped) serialises the event with System.Text.Json web defaults, stamps the tenant from
  `ITenantContext` and the time from `IClock`, adds the row to the scoped `SalonDbContext` and calls `SaveChangesAsync`.
  Inside a command, `TransactionBehavior` (ADR 0003) already holds the transaction, so the row commits or rolls back
  with the handler's writes; outside a command it commits on its own. The publisher never calls consumers.
  Payload JSON: camelCase, no type-name handling, pattern converters for NodaTime `Instant` (always UTC), `LocalDate`,
  `LocalTime`, `LocalDateTime` and `Duration`. `ZonedDateTime`, `OffsetDateTime`, `Period`, `Offset` and
  `DateTimeZone` are not supported: events carry an `Instant` plus a zone id (CLAUDE.md rule 5).
- **`IEventTypeRegistry`** maps an event's CLR `FullName` ↔ `Type`, built eagerly in `AddSalonEvents(params Assembly[])`
  from the given assemblies plus Core and Services. An unknown name throws; a full name defined in two assemblies
  fails registration; stored strings never reach `Type.GetType`, so a tampered row cannot instantiate an arbitrary
  type. The same scan registers every `IConsumer<T>` as scoped. `AddSalonFramework` passes the slice assemblies;
  plugins may call `AddSalonEvents` again for their own events and consumers.
- **`OutboxDispatcher`** loads up to `Outbox:BatchSize` (100) due rows ordered by id — approximately oldest first,
  since UUIDv7 is time-ordered only to the millisecond; no ordering is guaranteed and consumers must not rely on one —
  and, per row, deserialises the event and runs all of its consumers in a fresh DI scope: `ITenantScopeRunner.RunAsync`
  for tenant events, a plain scope for platform events (`TenantId == null`). Success sets `ProcessedAt`; a failure
  increments `Attempts`, stores "ExceptionType: message" plus the innermost cause (NUL characters removed, cut to
  4000) and schedules `NextAttemptAt = now + 30 s × 2^(Attempts−1)` capped at one hour. At `Outbox:MaxAttempts` (5)
  the row gets `FailedAt` (dead letter, logged as an error) and is never retried automatically. Each row's outcome is
  saved on its own; a save that fails (`DbUpdateException`) is logged and the row detached so the batch continues, and
  a concurrency failure (xmin moved) means another dispatcher already saved that row and ours is dropped.
  `DispatchPendingAsync` itself is not safe for concurrent callers: two of them double-dispatch the same rows. Today
  only Hangfire's per-task lock (ADR 0005) serialises the scheduled path; request code never calls the dispatcher.
- **`OutboxDispatchTask`** (`Events.DispatchOutbox`, every minute) drains the outbox batch by batch under that lock
  and stops after 50 full batches so one occurrence cannot run forever; the next tick carries on.

## Consequences
- Delivery is at-least-once: a consumer may see the same event again after a crash between its commit and the
  row update, so consumers must be idempotent (key on the event id or an aggregate state check).
- Consumers of one event run sequentially in one scope and share one failure: if the second throws, the first runs
  again on retry. Keep consumers small; fan out to further events if independence matters.
- The publisher's `SaveChangesAsync` also flushes whatever the handler has already tracked on the same context. Inside
  the command transaction that changes nothing observable, but a tenant or constraint violation can surface at the
  publish call rather than at the handler's own save.
- A deactivated tenant's pending messages still run their consumers (M0): the dispatcher opens the tenant scope by id
  and does not check `Tenant.IsActive`. Pausing dispatch for inactive tenants is a later, additive change.
- Latency is up to 60 s in M0. M1 may enqueue a one-off Hangfire job after commit for near-immediate dispatch; the
  table and dispatcher stay the same.
- Dead-lettered rows stay in the table for an operator to inspect or requeue (clear `FailedAt`, `NextAttemptAt`);
  an admin screen and a retention clean-up task are later additive work.
- `jsonb` refuses U+0000 inside strings, so an event carrying a NUL character fails at publish time (inside the
  command, which then rolls back). User-entered text should be cleaned at the input boundary, not in the publisher.
- Event payloads are versioned by their CLR type name; renaming or moving an event type strands its pending rows.
  Treat event types as public contracts, or migrate rows when renaming.
