# 0051 — "Your data": the salon takes everything away in one place, the client list included

Status: Decided by the owner (2026-11-16: "go with what is most suitable"); Tier 2 because it is personal data
Date: 2026-11-16

## Context
The product's promise is: no marketplace, no commission, flat price, **the salon owns its data**. The systems a
salon leaves to come here make leaving hard: the client list is the hardest thing to get out, and the marketplace
keeps the client for itself. Here the money, the visits and the stock were already files (ADR 0042, ADR 0047). The
client list was not, and nothing told the owner that the files exist.

## Decision
- **A page, "Your data", under Settings** (`admin/settings/your-data`, owner only). It says in plain words that the
  data is the salon's, how much there is, and links every file: the client list, the visits and the money of each
  year since the salon's first visit or payment, and the stock list. This is the promise made visible, and what the
  product does that the competition does not.
- **The client list as a file** (`admin/clients/export.csv`), written like the other files: first name, last name,
  phone, email, notes, marketing consent and the day it was given, client since, completed visits, last visit, next
  visit, active. Cards merged into another are left out: they are pointers, not clients.
- **Owner only.** `Settings.Manage` together with `Clients.Manage`. A manager can see and change clients one by one;
  taking the whole list away is the owner's. The list of clients with their numbers is what a salon is worth.
- **Health notes are in no file.** They are special-category data, stay encrypted on the client's card and are not
  read by this slice at all (ADR 0011). The page says so.
- **Each download of the client list is written to the log** with who took it and how many rows, never with what is
  in them.
- **The headings are ones the client import reads** (ADR 0019), so the file of one salon is the client list of
  another, or of the same salon after a mistake. The import now drops the apostrophe a spreadsheet file puts in
  front of a phone number that starts with a plus; any other apostrophe stays. A test proves the round trip.
- **Consent is carried as it is.** The file says who agreed to marketing and when. What the salon does with the
  file afterwards is the salon's responsibility as the controller of the data; the page says the file holds
  personal data and is theirs to keep safe.

## Not decided here
- A record of downloads the owner can read on a page (today it is the server log). One archive with every file. A
  scheduled copy by email or to the salon's own cloud folder. Deleting a salon's data on request when it leaves.
- A client's own copy of their data (a subject access request) as a button on the client's card.

## Consequences
- No migration. Two new slices: `Settings/YourData` and `Clients/ExportClients`.
- A file on somebody's laptop is outside the platform's control. It holds names, phone numbers, email addresses and
  notes.
- A manager cannot take the client list. If a salon wants that, it is a permission to add, not a default.

## Rollback
Revert the PR. Nothing is stored by it.
