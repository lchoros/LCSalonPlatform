# 0038 — A calendar address for each staff member

Status: Decided — owner may veto (Tier 2: personal data leaves the salon)
Date: 2026-11-15

## Context
The pilot salon's current system shows visits in Google Calendar, and the staff plan their own days around that.
A real two-way sync needs a Google project, keys and consent screens: an outside service, which Phase 1 forbids.
Every calendar app can also subscribe to an address that returns a calendar file. That needs nothing from Google.

## Decision
- **Each staff member can have a private address**, `https://{salon}/calendar/{token}.ics`, that returns their
  visits in the iCalendar format (RFC 5545). Google Calendar, Apple Calendar and Outlook subscribe to it. It is one
  way: the salon's diary is the truth, the phone shows a copy.
- **Off by default.** Somebody with `Staff.Manage` switches it on from the staff list ("Calendar"). The same page
  makes a new address or switches it off; either stops the old address at once.
- **The address is the credential.** A calendar app cannot sign in. The token is 32 random bytes (43 characters),
  unique, stored on the staff member, looked up through the tenant filter. An address of one salon is a 404 on
  another salon's host. A person who no longer works there has no address that answers.
- **As little as possible leaves the salon.** A visit shows as the service, the client's first name and the initial
  of the last name, and the salon's address: "Cut & blow-dry · Olivia B.". No surname, phone, email, note or health
  note is read by the query that builds the file.
- **What it holds**: visits that hold their time or took place (booked, confirmed, arrived, completed), from 30
  days back to a year ahead. Cancelled and missed visits are left out, so they vanish from the phone on its next
  read.
- **The answer is private**: `Cache-Control: private`, `X-Robots-Tag: noindex`. Unknown addresses get a plain 404.
- **The writer is pure** (`CalendarFeedWriter` in Core): UTC times, escaped text, CR LF line ends, lines folded at
  75 octets without splitting a character. Unit tests hold it to the format.

## What the owner should know
- This sends first names and visit times to the calendar provider the staff member chooses. The salon decides per
  person; the page says what is sent. That is why this is marked Tier 2.
- Google reads a subscribed address a few times a day. A new visit can take hours to show on the phone. The page
  says so. A faster, two-way sync is the Google plugin of a later phase.
- The token is part of the address, so it appears in the web server's request log like the tokens in clients'
  own links (ADR 0014). The application itself never logs it.

## Not decided here
- Time off and working hours in the feed. A feed for the whole salon. Two-way sync.

## Consequences
- One additive migration, `StaffCalendarFeed`: a nullable column and a unique index on `staff_members`.
- Two slices under `Features/Staff`: `ManageCalendarFeed` (admin) and `CalendarFeedFile` (the address).

## Rollback
Revert the PR and run the migration's `Down`. Phones that subscribed show an error and keep their last copy until
the subscription is removed.
