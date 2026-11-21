# 0093. Telling the developer: a health address and failure emails

Status: Decided — owner may veto
Date: 2026-11-21
Issue: the owner — "I need something that will let me know that the service is down or something failed … for developer messages"

## Context

The pilot site runs unattended. An error page at nine in the evening, a reminder job that gave up, a database that
stopped answering: today nobody knows until a salon complains. Two different things need telling, and one of them the
site cannot tell by itself — a site that is down sends no email.

## Decision

- **`/health` for an outside monitor.** Anonymous, on every host; it answers "Healthy" only while the database can
  be reached, 503 otherwise. A free uptime monitor (the owner's account) asks every few minutes and does the
  telling when the site is down. Only the shared framework's health checks are used; no new package.
- **Failure emails from inside, to the developer** (`Alerts:Email` in the server's configuration, never the
  repository; empty = off). Two sources to start with:
  - an error nobody caught (`AlertingExceptionHandler`): the kind of error, the path, the request number — then the
    usual error page is shown;
  - a background job that gave up after its retries (`JobFailureAlertFilter`): which job, which kind of error.
- **One message per fault per hour**, the next one saying how often it happened meanwhile. Sending never throws
  into the request or the job that reported.
- **Types, ids and numbers only**: no exception message (it can quote data), no query string, no job arguments.

## Consequences

- The developer hears of a broken page or job within the minute, through the mail sending the pilot already has.
- Not covered yet: a client email or SMS that could not be sent (the outbox retries those and has its own record),
  a phone message (Telegram or SMS need an account and a token — the owner's decision), full telemetry
  (OpenTelemetry → Application Insights, when there are several salons).
- If the mail server itself is down, the alert is only a log line: the outside monitor is the backstop.

## Alternatives considered

- Application Insights now: a new runtime dependency and data to a third party for one pilot salon; the right step
  later, through OpenTelemetry.
- The exception's message in the email: useful, but it can carry a client's name or number.
