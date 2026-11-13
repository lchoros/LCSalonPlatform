# 0027 — Locations start as one: the salon's address and contact

Status: Decided — owner may veto
Date: 2026-11-13

## Context
The glossary has had `Location`, "a physical salon address", since M0, and "locations" is the last unbuilt line of M1.
Until now a salon had a name and nothing else: a client who booked online was never told where to go or whom to call,
on the page or in the confirmation. The pilot salon has one address. A salon with several sites needs much more (staff
per site, a site on each visit, a site picker in the public flow), and none of that can be judged before a salon with
two sites exists.

## Decision
- **A `Location` entity now, one per salon in the UI.** Table `locations` (tenant-owned, RLS): optional name of the
  place, two address lines, town, postcode, optional phone and email, sort order, active flag. The first active row by
  sort order is the salon's **main location**. `ILocationService` reads it and creates or replaces it; nothing in the
  UI adds a second row yet.
- **Owner only.** `/admin/settings/salon-details` behind `Settings.Manage`, a third tab under Settings. Address, town
  and postcode are required; the rest is optional.
- **Clients see it where they decide and where they look back.** A view component, `SalonContact`, renders "Where to
  find us" with the address, an "Open in maps" link, a `tel:` link and a `mailto:` link on the service list, the
  details step, the confirmation, and the cancel and change pages. It renders nothing while there is no address. The
  slot step is left alone: it is about times and is already the longest page on a phone.
- **Emails end with it.** `AppointmentNotifier` appends the place's name (or the salon's), the address and the phone
  number to every booking email. Texts are not touched: an address would push most of them into a second SMS segment.
- **The address is written the way the country writes it.** One line, postcode after the town in the United Kingdom
  and before it in Poland (`LocationRules.OneLine`, chosen by the tenant's culture).
- **A phone number is kept as typed.** It must be digits with the usual separators and an optional leading plus, 6 to
  15 digits; only its dialable form goes into the `tel:` link.
- **The maps link is a plain link** to a Google Maps search for the address. No script, no embedded map, no key; the
  address is the only thing in it and nothing is requested until the client taps.
- **Times stay in the tenant's zone** (ADR 0002). CLAUDE.md rule 5 speaks of "the location's zone"; with one location
  per salon and one zone per market the two are the same, and a zone per location can be added with the second site.

## Consequences
- The demo salon gets a made-up address and a phone number from the range Ofcom reserves for drama.
- A salon that has not entered an address looks and behaves exactly as before; nothing is required.
- An owner who edited email templates sees the footer after their text; there is no switch for it.
- Several locations per salon are a later feature on this table: `staff_members.location_id`, a snapshot
  `appointments.location_id`, a location step in the public flow, and reports per location. All additive.
- Additive migration `Locations`: one table with RLS and two indexes.

## Alternatives considered
- **A typed settings class (`SalonContactSettings`).** No migration, but the address would have to move into a table
  the day a second site appears, and the glossary already names the entity.
- **Full multi-location now.** Staff, visits, availability and the public flow would all change for a case no pilot
  salon has; the wrong guess would be expensive to undo.
- **Tokens in the templates (`{{SalonAddress}}`).** Leaves a blank line for a salon without an address and makes
  every default template longer; a footer is what a reader expects at the end of an email.
- **An embedded map.** Needs a third-party script and a key, and breaks the CSP rule; a link does the job.

## Rollback
`Down` drops `locations`. Reverting the PR removes the settings tab, the block on the booking pages and the email
footer; no other table refers to a location.
