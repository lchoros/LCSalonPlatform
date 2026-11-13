# 0025 — Email channel for client messages, and delivery tracked per consumer

Status: Decided — owner may veto
Date: 2026-11-13

## Context
PLAN.md M2 notifications still listed "email channel" and "idempotent confirmation consumer". The templates of
ADR 0008 already carry an email subject and body for every booking message, and `IEmailSender` (SMTP to Mailpit in
Phase 1) existed, but nothing sent an email. Adding a second channel exposed a weakness of the outbox (ADR 0007): all
consumers of an event ran in one go and any exception made the whole row retry, so a mail server that was down would
have texted the client the same confirmation on every attempt.

## Decision
- **Delivery is tracked per consumer, on the outbox row.** `outbox_messages.completed_consumers` (`text[]`) lists the
  consumers that have handled the event. The dispatcher skips them on a retry, and it gives every consumer its turn
  in each attempt even when another one throws; the row fails (and is retried) while any consumer is outstanding. No
  new table and nothing tenant-specific: the dispatcher already owns and saves that row, success or failure.
- **One consumer per channel.** The rule of each message (when it is due, what it says) lives once, in
  `AppointmentBookedNotice`, `AppointmentCancelledNotice` and `AppointmentMovedNotice`; an SMS and an email consumer
  derive from each and differ only in the channel. That is what lets the outbox retry the email alone.
- **`AppointmentNotifier` knows how a message goes out**: it renders the template for the channel in the salon's
  culture and hands it to the SMS or the email sender. It returns false, without failing, when the client has no
  phone or no address for that channel or the salon has emails switched off.
- **Emails are plain templates rendered to safe HTML.** `EmailBodies.ToHtml` encodes every piece, turns line breaks
  into `<br>` and web addresses into links; the plain text goes along as the alternative part. An owner can edit the
  text of a template without HTML, and nothing in a template or a token can inject markup.
- **Reminders use both channels inside one claim** (ADR 0020). The unique reminder row is still claimed first; then
  each channel is tried on its own, so a failing mail server does not cost the client their text. The reminder counts
  as sent when at least one channel took it. A client with only an email address is now reminded too.
- **One switch.** `NotificationSettings.EmailEnabled` (default on), on the reminders settings page: "Also email
  clients". Off means SMS only, for all four messages.
- **The address stays out of the logs.** The SMTP sender logged the recipient; it now logs that a message was handed
  over. Message log lines carry appointment and tenant ids only.

## Consequences
- Still at-least-once for the consumer that failed: one that sent its message and crashed before the row was saved
  will send again. That window is one consumer wide now instead of the whole event.
- Renaming a consumer class while its events are pending runs it again for those events (the list holds type names).
- The confirmation is still sent for online bookings only, as before; a booking made at the desk gets no message.
- Phase 1 email goes to Mailpit. A real provider (Postmark/SES, M3) plugs in behind `IEmailSender` unchanged.

## Alternatives considered
- **An inbox table** (event id + consumer, unique), as in the modular-monolith reference the owner pointed to. The
  textbook answer when consumers live in other modules or databases. Here the dispatcher and the outbox row are in one
  place, so a column on the row gives the same guarantee without a table that would need its own RLS exemption.
- **One consumer sending both channels and swallowing email errors.** Simple, but a failed email would be lost
  instead of retried, and it hides a broken mail server.
- **HTML templates.** Prettier, but owners would edit HTML, every token would need encoding rules and both languages
  would need two bodies each. Plain text rendered safely is enough for a booking message.

## Rollback
`Down` drops the column; the dispatcher then behaves as before (all consumers per attempt). Reverting the PR removes
the email consumers and the switch.
