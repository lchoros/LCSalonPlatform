# 0032 — A real-sized catalogue: variants, shared list components and the look

Status: Decided — owner may veto
Date: 2026-11-14

## Context
The demo salon had six made-up services. The pilot salon's price list has 47, fifteen of them with a price range
("Keratin £70 – £85") that hides variants, in five categories. On that list the booking page was one long scroll with
no way to find anything, the day tiles were taller than wide, and the owner asked for three things: the real
services, filters, search and pages built as components that other lists reuse, and a lighter, more modern look
(white cards with a thin two-colour border, squared tiles).

## Decision
- **A variant is a service.** Two optional columns on `services`: `variant_group`, the title shared with sibling
  variants, and `variant_label`, what tells this one apart. Both or neither. `Name` stays the full name
  ("Keratin · Long hair"), so the calendar, the till, reports and every message name the service without knowing
  about variants. Price, duration, staff prices and the deposit rule are per variant because they are per service.
- **Entries, not rows.** `ServiceCatalogue` (Core, pure) turns services into list entries: a service on its own, or
  the variants of one category that share a title, at the place of the first. It also filters by category and by a
  search in which every word must occur. Clients and the admin list use the same code.
- **Paging is one type.** `Paged<T>` and `Pages.Cut` in Core: a page number that does not exist is moved to the
  nearest that does, so no address is an error.
- **Three shared partials, no script:** `_SearchBox` (a GET form), `_FilterTiles` (links) and `_Pager` (links),
  with their models and `ListUrl` in `Salon.Web.Framework/Lists`. A search or a page is an address: it can be
  shared, and the back button works. An address never carries an empty parameter or `page=1`.
- **The booking page** shows search, category tiles and pages once a salon has more than eight services; a salon
  with a handful keeps the plain page. Twelve entries a page. Variants are folded under their title (`<details>`)
  with the price range and open when a search found them.
- **The admin list** gets the same search, tiles and pager (24 a page) and names the title a variant belongs to. The
  two service forms get the two variant fields.
- **The look.** One vocabulary in `app.css`: a surface takes one of five accents (`.accent-1` to `.accent-5`), which
  set a two-colour border painted behind a white padding box and a soft glow of the same colour. Categories take
  the accents in order, so a tile and its cards match. Chosen states stay dark for contrast. Day tiles are 56 px
  squares. No image, no inline style, no script.
- **The demo catalogue is the pilot salon's price list** (`Studio20Catalogue`), with its street address. Two things
  the price list does not say are filled in for the salon to correct: every duration is an estimate, and a price
  range without named variants (all but Keratin) is two options, "Option 1" and "Option 2", at its lowest and
  highest price. The phone number stays one that reaches nobody, because the repository is public.

## Consequences
- An existing demo database keeps its old services: the seeder never re-seeds a catalogue. Drop and recreate
  `salon_dev` to get the new one.
- Only the public booking flow and the shared list components have the new look. The admin pages keep their plain
  style until a restyle of their own.
- The price shown for a variant is its list price; staff prices (ADR 0018) still apply at the next step.
- Filtering and paging happen in memory. A salon has tens of services; if one ever has thousands, `ServiceCatalogue`
  is the one place to change.
- Additive migration `ServiceVariants`: two nullable columns.

## Alternatives considered
- **A `service_variants` child table.** Every screen that names a service would have to learn about it, and price,
  duration, staff prices and deposits would all need a second home. A variant has everything a service has.
- **Composing the name from title and label on every read.** Dozens of queries project `Name`; one wrong place and a
  message says "Long hair" with no service.
- **Client-side filtering with script.** Fast on a long page, but the list would no longer be an address and the
  page would not work without script.
- **A component library for the look.** A new runtime dependency for five accents and three components.

## Rollback
`Down` drops the two columns; variants become ordinary services again and keep their full names. Reverting the PR
restores the old demo data for new databases only.
