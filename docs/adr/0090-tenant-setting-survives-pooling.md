# 0090. The tenant setting travels with every statement

Status: Decided — owner may veto
Date: 2026-11-20
Issue: m3-70 — the first `/book` after a restart of the pilot host showed an empty salon (Tier 2, tenancy)

## What was seen on the pilot host

On 2026-11-20, straight after an app restart, the first request to a salon's public `/book` rendered its empty
state — "This salon has not opened online booking yet" — although the salon had 66 active services and 3 bookable
people. The same page a minute later was right, and `/services` was right throughout. No data was lost and no other
salon's data was shown: the page saw nothing, not something wrong.

Two facts about the host matter. Its database sits behind PgBouncer (application connections arrive from 127.0.0.1
and a pgbouncer row is in `pg_stat_activity`); which pooling mode it runs is not known. And at start the app is busy
on many connections at once: the Hangfire server, the outbox dispatcher, the scheduled tasks.

## How the tenant reached Postgres until now (ADR 0002)

`TenantConnectionInterceptor` ran `set_config('app.tenant_id', <tenant>, false)` — a **session** setting — each time
EF opened a connection, and the statement itself followed as a separate round trip. EF opens and closes the
connection around every query, so in one request the order on the wire is: setting, query, setting, query. That is
sound as long as "the session" is one Postgres server process for as long as the connection is open.

## Hypotheses, in the order the evidence leaves them

1. **A pooler in transaction mode separates the setting from the statement — leading.** In that mode PgBouncer gives
   each transaction, and each statement sent on its own, whichever server connection is free. The setting lands on
   one server connection, the query may run on another whose session holds nothing (or holds what somebody else left
   there). The row-level security policy then matches no row, and the page is empty — exactly what was seen, and
   exactly fail-closed. It fits the timing as well: with one request and a quiet pool the pooler hands back the same
   server connection every time and everything works by accident; in the seconds after a restart a dozen background
   connections shuffle the server connections between two statements of one request. It also explains why another
   page at another moment was fine. On the first request only, the middleware's own salon lookup (not cached yet)
   adds one more statement, sent with the empty setting, just before the page's queries.
   Against: the pooling mode is not confirmed, and this was not reproduced against a real PgBouncer (none is
   installed locally, and phase 1 has no containers).
2. **The tenant set after the connection was already open — real, but not this page.** ADR 0002 names the limit: a
   tenant set while a connection is open is not sent again. `/book` opens no transaction, every query reopens the
   connection, so it does not apply there. It does apply to public sign-up (ADR 0075): the command's transaction opens
   before the salon exists, the salon becomes the current tenant halfway, and the owner's login is then written with
   the session still saying "no tenant". As a non-superuser that insert is refused by row-level security. Tests never
   saw it because the test host logs in as a superuser, which bypasses the policies; the pilot has sign-up switched off.
3. **A cache filled without a tenant and served to the first request — ruled out.** `/book` reads services and
   categories straight from the database. The salon lookup is cached by slug and by id, and reads `tenants`, which has
   no policy. Settings are cached under the tenant's id and refuse to load without one. A poisoned cache would also
   not have healed within a minute (the lifetimes are 5 and 60 minutes).
4. **A pooled connection keeping the previous tenant — ruled out** on a direct connection: Npgsql resets the session
   on reuse and the setting was sent again on every open (`PooledConnectionIsolationTests`).

## What was reproduced, and what was not

- **Reproduced by simulation.** `TransactionPoolerSimulator` (tests) does to one physical connection what a
  transaction-mode pooler does to session state: before every statement sent outside a transaction, and before
  every BEGIN, the session's `app.tenant_id` is replaced by what "another server connection" would hold. With it, a
  freshly started host whose database login is a non-superuser serves `/book` as its very first request **empty** on
  the old code — the live symptom — and correctly on the new. With another salon's id left in the session and the
  EF filter switched off, the old code showed that other salon's rows: under such a pooler the second wall was not
  only failing closed, it could point at the wrong salon.
- **Reproduced directly** (no pooler): the sign-up shape of hypothesis 2 — insert refused with 42501 on the old code.
- **Not reproduced:** the same first request on a direct connection is correct on the old code too (kept as a test).
  The simulation shows what the code does when session state does not follow it; it does not prove that the host's
  PgBouncer runs in transaction mode. The fix below is right in either mode; the cause on the host stays "most
  likely", not "proven", until the host's mode is known.
- **One experiment, not committed:** the whole integration suite with every EF session switched to the
  non-superuser role (`SET ROLE`), on a direct connection. Old code: 2 of 939 fail, both sign-up tests (hypothesis
  2). New code: 4 of 952 fail, all one test helper that searches the schema with commands built by hand on EF's
  connection — which now, correctly, see no tenant row. The everyday suite logs in as a superuser and so never
  meets the policies; running it as the role production resembles is a follow-up worth its own PR.

## Decision

The tenant is sent **with every statement, transaction-local, and never as session state**
(`TenantCommandInterceptor`, replacing `TenantConnectionInterceptor`).

- **Queries, bulk changes (`ExecuteUpdate`/`ExecuteDelete`) and raw SQL** get
  `SELECT set_config('app.tenant_id', '<tenant>', true);` as the first statement of their own batch. A batch is one
  transaction to Postgres and to a pooler: the two cannot be separated, and the setting ends with the batch. The
  interceptor steps over the setting's result set, so the caller reads exactly what it asked for.
- **Saves** always run in a transaction (`AutoTransactionBehavior.Always` on `SalonDbContext`, also for a single
  row), and the same statement is sent inside that transaction just before the save's own. Nothing is put in front
  of a save's statements because the provider reads their results by position. A save that arrives without a
  transaction is refused with an exception instead of being written under no tenant.
- **The tenant is read when the statement is sent**, from the same scoped `ITenantContext` the EF filter reads. A
  tenant set halfway through a transaction counts from the next statement on, which removes the limit ADR 0002 had
  to document and makes sign-up work as a non-superuser.
- **Fail closed.** No tenant sends the empty value, which matches no row. A statement that does not pass the
  interceptor (migrations, a command built by hand) finds no setting and sees no tenant row. Nothing of a tenant is
  ever left on a connection for the next user, pooler or not.
- Both walls stay exactly as they were: the EF filter is untouched, the policies are untouched, no migration.
- The refusal of `No Reset On Close` and `Multiplexing` stays as a second guard, although isolation no longer
  depends on the reset.

## Finding out what the host really does

The hourly heartbeat now writes one more log line (`DatabaseSessionProbe`): whether a pooler shares server
sessions between the app's connections (`Seen` / `NotSeen`), and whether the database login bypasses row-level
security. It holds two connections open and asks each, in turn, which Postgres server process answers. Two
connections open at once always have a process each and keep it — unless a pooler in transaction mode sits in
between; one process answering both, or one connection answered by two, is proof. `Seen` on the pilot host
confirms hypothesis 1; `NotSeen` hour after hour says it is session mode and the cause is still open. Ids and
enums only, read-only, and a failure of the probe is logged and never stops the heartbeat.

The heartbeat's existing line ("tenant … has N services") is worth a look in the host's log as well: if hypothesis
1 is right, the old version should now and then have logged 0 for a salon that has services.

## Consequences

- One more statement travels with each query (no extra round trip), and a save costs a BEGIN, the setting and a
  COMMIT around it (two extra round trips for a single-row save). At a salon's scale this is not measurable; the
  database is on the same machine as the pooler.
- EF's "Executed DbCommand" log lines now start with the setting, so they show the tenant id. That is an id, not
  personal data.
- Raw SQL that must not run inside a transaction (for example `VACUUM`) cannot go through this context any more.
  Nothing in the code base does that; migrations are exempt.
- For up to an hour after this ships (PgBouncer's default server lifetime), server connections may still carry a
  session setting written by the old version. Nothing reads it: every statement brings its own, and a
  transaction-local value wins over the session's.
- ADR 0002's last consequence ("infrastructure sets the tenant before the first query") is no longer a
  correctness rule, only good order.

## Rollback

No schema or data change. Revert the commit: `TenantConnectionInterceptor` comes back and the session setting
with it. Behind a transaction-mode pooler that brings the empty first page back, so the safer undo there is to
bypass the pooler (connect to Postgres's own port) or switch it to session mode.

## Alternatives considered

- **Keep the session setting and ask the host for session pooling.** Depends on a host setting we do not control
  and cannot see, and leaves the sign-up fault.
- **Both: session setting on open and the transaction-local one.** Works on a direct connection, but behind a
  pooler it keeps writing one salon's id into shared server sessions — the very thing that can point the second
  wall at the wrong salon for any statement that slips past the interceptor. With no session state there is nothing
  to slip to.
- **`SET LOCAL` in front of the statement.** No result set to step over, but outside an explicit transaction
  Postgres answers every one with a warning in the server log.
- **A `DO` block in front of the statement.** Also no result set, but it needs PL/pgSQL usage rights we have never
  exercised on the host.
- **Wrap every query in its own explicit transaction.** Two more round trips per query and a transaction to
  complete when a reader closes, for the same guarantee the batch already gives.