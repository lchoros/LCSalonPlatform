# 0050 — The day's numbers by email to the salon's owners

Status: Decided — owner may veto
Date: 2026-11-16

## Context
An owner who is not at the desk in the evening wants to know how the day went without signing in: what was taken,
who did not turn up, what tomorrow looks like. The reports (ADR 0026) have the numbers, the job runner and the email
sender exist (ADR 0020, ADR 0025). In phase 1 every email goes to Mailpit.

## Decision
- **One email a day to every active owner login of the salon**, to the address they sign in with. Managers and
  staff do not get it. No new address is collected.
- **Off until the owner switches it on**, at `admin/settings/daily-summary` (`Settings.Manage`): a switch and a time
  of day on the salon's clock, 20:00 to begin with. `DailySummarySettings` through `ISettingService`. The switch is
  the feature flag.
- **Numbers only. No client is named.** What was taken and per method, visits completed, no-shows, cancelled,
  completed visits not paid, how many visits are booked for tomorrow and when the first starts, how many products
  are running low, and a link to the day's report, which still asks for a sign-in. A line with nothing to say (no
  unpaid visits, nothing running low) is left out.
- **The numbers are the report's** (`IReportService` for the one day), so the email and the report cannot disagree.
- **The page shows what today's email would say now**, so the owner reads it before switching it on.
- **Sent once a day, claimed before it is sent.** A job runs every five minutes, salon by salon
  (`DailySummaryTask`). From the chosen minute until midnight the day is due. A row in `daily_summary_deliveries`,
  unique per salon and day, is written before the email is handed over, as for reminders (ADR 0020): two workers or
  a retry can never send a day twice. If the mail server is down the day is missed, not repeated; the report has
  the numbers.
- **After midnight a missed day is not sent late.** A summary of yesterday at breakfast reads as today's.
- **In the salon's language and money**, plain text with the same safe HTML part as the other emails
  (`EmailBodies`). The wording is not in the message catalogue (ADR 0030): it is a list of figures, not a message
  to a client.

## Not decided here
- A weekly summary, a summary by text message, choosing who gets it, a summary for a manager without the money.
- Sending a missed day the next morning, labelled as yesterday's.

## Consequences
- Migration `DailySummary`: one new table with the tenant policy (row-level security) like every tenant table.
- `IPublicUrlBuilder` gains `SalonUrl` for a link to a page of the salon's own site.
- A salon that has not switched it on costs one settings read every five minutes.
- The email holds the salon's takings. It goes only to owner logins, and in phase 1 only to Mailpit.

## Rollback
Switch it off on the settings page; nothing more is sent. To remove it: revert the PR and run the migration's
`Down`, which drops the table of sent days.
