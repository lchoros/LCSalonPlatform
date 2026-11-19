# 0067 — The coming weeks of the old system's diary from a file

Status: Decided — owner may veto; Tier 2 (it writes many bookings at once and handles client details)
Date: 2026-11-19

## Context
A salon that switches has clients (ADR 0019), products (ADR 0045) and a price list (ADR 0066) that come over from
a file. Its coming weeks of bookings did not: somebody had to type every visit of the next months into the new
diary while the old one kept taking bookings. That is the step that keeps a salon on its old system.

ADR 0019 decided that visit history is not imported, because appointments need services and staff that do not
exist yet. Once the clients and the price list are in, future visits can be.

What the old systems give is not public. Versum and Booksy export appointments only on request from their support,
with no published columns; Fresha, Timely and Phorest have report exports whose columns the user can change.
Versum's own calendar sync works with a start and an end. These facts come from the vendors' help pages and
competitors' migration guides, read for this decision.

The design was attacked before it was coded by three reviewers (data safety, a salon owner on the day, edge
cases) and a judge who checked each finding against the code. Ten findings changed the design; they are marked
"(review)" below.

## Decision
- **Only visits from now on.** Rows of earlier days are counted and dropped as the file is read, so an export of
  years of history is not refused for its size (review). Rows earlier today are "past". Visit history stays out,
  as ADR 0019 decided.
- **The reader finds columns by their headings**, in English or Polish and in any order, Fresha's booking names
  and Versum's Polish words among them. Needed: a date and a start time (or both in one cell), a service, and the
  client's name, phone or email. Also read: an end time or a length, the person, the price, the status. **A notes
  column is never read**: it can hold health details (ADR 0012, ADR 0019). No column-mapping step: a heading the
  reader does not know is renamed in the spreadsheet, and the refusal lists the headings found.
- **Dates are read ISO or day-first; one date that can only be month-first refuses the whole file.** 11/20/2026
  cannot be a day-first date, so a file that has one is month-first everywhere and every visit would land on the
  wrong day. The preview shows the weekday and the date of every row, the first and last day, and a few dates of
  the history rows as read, so a misread file shows itself (review). Times are to the minute (seconds must be
  nought). A time the clocks skip or pass twice is refused, not guessed.
- **A status word must be known (review).** No status, or a word for a booked visit, is booked. A word for a
  cancelled or not-attended visit is left out. Any other word (pending, a request, unconfirmed) makes the row
  invalid with the word shown: a visit the salon never accepted must not be reminded.
- **Clients, services and people must exist; nothing is created.** The page gives the order: clients, then the
  price list, then the diary.
- **A client is matched failing closed (review).** By phone (as the client card stores it), then email, then a
  full name that exactly one client has, in either order. A phone or email that is on no card is not tried as a
  name. A phone and an email on two different cards are refused. A phone or email whose card has another first name
  is refused with that card's name shown (a family shares a phone; the daughter's visit is not the mother's).
  Clients merged away, forgotten or not in use are not matched. A name-only match is marked on the preview.
- **A person is matched failing closed (review).** By full name among everybody, leavers included, so a leaver is
  named as such and never found in a colleague; then by first name only if exactly one person, leaver or not, has
  it, and marked. A file with no person column is accepted only when one person takes bookings. The person must do
  the service.
- **The visit keeps the old system's terms.** Its start and its end or length (an end and a length more than a
  minute apart are refused), else the person's own length; its price, else the person's own price. No deposit is
  asked (money already paid in the old system is not recorded here). Status Booked. The client's own link is made
  (ADR 0029), so a reminder lets them change or cancel it as any visit. The first history line says "Imported" and
  who confirmed.
- **The diary is taken as it is.** No rota, notice, advance-limit or step rule: a visit outside the rota is shown
  by the calendar. What is refused is what the database itself refuses: two visits of one person at one time. A
  row that overlaps the diary, an earlier row of the file, or a time held for a waiting client (ADR 0065) is
  refused with its own reason; the one for an earlier row says that a second client during processing time cannot
  be booked yet (review).
- **A second file never brings a visit over twice (review).** A row whose client, service and day match a visit
  brought over before, in any status (since moved or cancelled here), or that is the very visit already in the
  diary, is "already there". A cancelled row whose visit was brought over before and is still booked here is shown
  as "cancelled in the old system since", with a link to the visit; the file does not cancel it.
- **Silent at import.** The visits are booked with a new source, `Import`. The confirmation consumer skips it: the
  old system confirmed these visits. The waitlist consumer still closes the client's waiting entry for the service.
- **Reminders from now on, and the page says when the old system may stop (review).** A reminder of ours goes out
  only when its time is after the import, so a visit within the longest lead (24 hours by default) may miss it.
  The preview and the result give the exact moment until which the old system must keep reminding and how many
  visits it covers. The page also says to stop online booking in the old system before the last file.
- **One write, all or nothing (review).** The visits, their history lines and their events are added, then saved
  once. Events are queued with a new `IEventPublisher.Queue` that adds the outbox row without saving. If the
  database refuses a clash that appeared after the check, the error propagates, the command rolls back and the
  owner is told to check the file again. A test holds one confirm open while a second reaches the clash rule: the
  second leaves no visit, no history line and no event. With the error swallowed, the test fails.
- **Decided again at the confirm**, against the diary, clients, services and people as they are then, through the
  tenant filter. No id of the cached preview is trusted: the database's foreign keys do not check the salon.
- **What is not brought over can be downloaded (review)**: every future row that cannot be read in or was cancelled
  since, with its reason, as a CSV, from the preview and the result. The preview also lists each distinct service,
  person and status word it does not know, with how many rows carry it and the nearest name the salon has (review).
- **Caps:** 2 000 visits from today on per file, 250 000 rows and 20 MB in all.
- **The log** has counts, the salon's id and the confirming user's id; never a file name, a name, a phone or an
  email (review).
- **Who:** owners and managers (`Bookings.Manage`, `Clients.View`, `Clients.Manage`), behind the client import's
  switch, which this import needs first. Linked from the client import's result.

## Not decided here
- Creating clients, services or people from the file. Past visits. Deposits or payments taken in the old system.
  Notes. A column-mapping step. Presets for named systems. Month-first files.
- A second client during processing time (needs the availability engine to read `Service.ProcessingMinutes`).
- Undoing a confirmed file with one button. Cancelling here what was cancelled in the old system, other than by
  hand. Several locations.

## Consequences
- No migration. `AppointmentSource.Import = 3` is a new value of an integer column.
- `IEventPublisher.Queue`, beside `PublishAsync`.
- New: `ImportDates`, `ImportNames`, `AppointmentImport` (Core); `CsvAppointmentImportProvider`;
  `IAppointmentImportService`, `AppointmentImportClassifier`; slice `Appointments/ImportAppointments`.
- The visits file names the third way a visit was booked, "From the old system".
- The client import's page says future visits come over afterwards, from the diary file.
- Imported future visits count as any visit: on the client card, in the reports once they happen, and they block
  forgetting a client until they pass or are cancelled (ADR 0062).

## Rollback
Revert the PR. Visits already brought over stay; they are ordinary bookings with source 3, which the old code reads
as a desk booking everywhere except the visits file, where the word is missing. To take one file's visits out, cancel
them in the diary (the clients are then told of the cancellation).
