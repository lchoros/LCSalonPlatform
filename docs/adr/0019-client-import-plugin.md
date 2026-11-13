# 0019 — Client import plugin (Booksy / Versum CSV with preview)

Status: Decided — owner may veto
Date: 2026-11-12

## Context
A salon leaving Booksy or Versum arrives with a client list it does not want to retype. Both products export clients as
CSV, with different column names, separators and encodings (Booksy: UTF-8 with a BOM, comma-separated, English or Polish
headers; Versum: Polish headers, often semicolon-separated and saved as Windows-1250 from Excel). The salon owns its data
(CLAUDE.md), so the import must be predictable: the owner sees what will happen before anything is written, duplicates are
not created, and nothing sensitive is written by accident. CLAUDE.md rule 8 and the plugin rule of ADR 0001 apply:
import is a compile-time plugin behind `IPlugin`, and the slice never depends on the plugin assembly.

## Options
1. Parse in the slice with a CSV NuGet package (CsvHelper). Fast to write, but adds a runtime dependency (Tier 2) and ties
   the slice to one format.
2. One generic "map your columns" wizard. Flexible, but a long multi-step UI for a job done once per salon.
3. A plugin with one parser per source product, detected from the header line, feeding a product-neutral row model that
   a cross-slice service classifies against the existing clients; the slice shows a preview and commits on confirm.

## Decision
**Option 3.**

### Contracts (Salon.Core/Import)
- `ImportedClientRow(SourceRow, FirstName, LastName, Phone, Email, Notes, MarketingConsent, Problems)` — one client as it
  appears in the file, text as typed (trimmed), with per-row `ImportProblem`s found while parsing (`MissingFirstName`,
  `MissingLastName`, `WrongColumnCount`).
- `IClientImportParser` — one source product: `Format` ("Booksy", "Versum"), `Match(header)` (how many columns of the
  header it recognises, 0 = not this product) and `Parse(header, records)` over already-split string records. Parsers are
  pure and take no stream, so they are testable without files.
- `IClientImportProvider : IPlugin` — the plugin itself: `Parse(bytes, fileName)` decodes, splits, caps and picks the
  parser with the best `Match`; returns `ClientImportParseResult(Format, Rows, Error)` where `Error` is a file-level
  `ClientImportError` (`Empty`, `TooLarge`, `TooManyRows`, `UnknownFormat`, `NotText`).
- `ClientImportLimits`: 5 000 data rows and 2 MiB per file. Both the controller and the provider enforce them.

### Plugin `Salon.Plugin.Import` (SystemName `Import.Csv`, group `Import`)
- `CsvDecoder`: UTF-8/UTF-16 BOM wins; otherwise strict UTF-8; if that fails the bytes are read as Windows-1250 (the
  Excel default on a Polish PC). A file with NUL bytes is refused as `NotText`.
- `CsvReader`: a small RFC 4180 reader (quoted fields, `""` escapes, newlines inside quotes, CRLF or LF). The separator
  is detected from the header line (`;`, `,` or tab, whichever occurs most outside quotes). Blank lines are skipped.
- Header matching is case-, accent- and punctuation-insensitive (`Numer telefonu` = `numer_telefonu` = `NUMER TELEFONU`).
- `BooksyCsvParser` — **assumed** Booksy Biz customer export / import template columns. Recognised: first name
  (`First Name`, `Imię`), last name (`Last Name`, `Nazwisko`), one full-name column when the two are absent (`Name`,
  `Customer`, `Client`, `Imię i nazwisko`, `Klient`: split at the first space), phone (`Phone`, `Phone Number`, `Mobile`,
  `Cell Phone`, `Numer telefonu`, `Telefon`), email (`Email`, `E-mail`, `Email Address`, `Adres e-mail`), notes (`Notes`,
  `Note`, `Customer notes`, `Notatki`, `Notatka`), consent (`Marketing Consent`, `Marketing`, `Accepts Marketing`,
  `Email Marketing`, `SMS Marketing`, `Zgoda marketingowa`). Signature columns that tip detection towards Booksy:
  `Phone Number`, `Cell Phone`, `Customer notes`, `Accepts Marketing`, `Birthday`, `Zip Code`.
- `VersumCsvParser` — **assumed** Versum "Eksport klientów" columns. Recognised: `Imię`/`First name`, `Nazwisko`/
  `Last name`, phone (`Telefon`, `Telefon komórkowy`, `Nr telefonu`, `Telefon 1`, `Phone`), email (`E-mail`, `Email`,
  `Adres e-mail`), notes (`Opis`, `Uwagi`, `Komentarz`, `Notatki`, `Description`), consent (`Zgoda marketingowa`,
  `Zgoda na marketing`, `Zgoda SMS`, `Zgoda e-mail`, `Marketing consent`, `Marketing`). Signature columns: `Telefon
  komórkowy`, `Opis`, `Zgoda na marketing`, `Data urodzenia`, `Źródło`, `Ostatnia wizyta`, `Liczba wizyt`.
- Both parsers need a name column (first+last or full name) and at least one of phone/email in the header; otherwise
  `Match` is 0 and the file is `UnknownFormat`. Consent is true for `yes`, `true`, `1`, `tak`, `y`, `x`, `✓`.
- Everything else in either export (birthday, gender, address, tags, visit counts, last visit, balance, loyalty points)
  is ignored on purpose. **Visit history is not imported** (appointments need services and staff that do not exist yet) and
  **health data is never imported**: a Booksy/Versum export has no dedicated allergy column, and importing free text into
  the encrypted field would silently turn ordinary notes into special-category data (ADR 0011). Free-text notes go to the
  plain `Notes` field, truncated to its 4 000 characters.
- The header names above are assumptions from the products' public import templates and user-shared exports; they are
  the first thing to adjust when a real pilot file arrives. The alias tables are the only place to touch.

### Service `ClientImportService` (Salon.Services/Clients/Import)
- `PreviewAsync(parsed)` classifies every row for the current tenant as **New**, **Duplicate** or **Invalid**:
  - Invalid: a parser problem, a phone `PhoneNumber.TryNormalise` refuses with the tenant's currency hint
    (`InvalidPhone`), an email `EmailAddress.TryNormalise` refuses (`InvalidEmail`), a name over the column length.
  - Duplicate: the normalised phone or email already belongs to a client of the tenant (`DuplicatePhone`,
    `DuplicateEmail`); a row with neither phone nor email whose first+last name already exists (case-insensitive,
    `DuplicateName`); or a row that repeats a phone/email/name of an earlier row in the same file (`DuplicateInFile`).
  - The existing phones, emails and names are loaded once per preview (one salon has thousands of clients at most), so a
    5 000-row file costs three queries, not 10 000.
- `CommitAsync(preview, consentWording)` creates the **New** rows only, each through `IClientService.CreateAsync` (the
  same normalisation, duplicate check and `ClientCreated` event as the form), inside the command's transaction (ADR 0003).
  A `DuplicateClientException` raised meanwhile (a client added between preview and confirm) is counted as a duplicate,
  not an error. With `MarketingConsent` the recorded wording is the localized "Imported from {file} ({format})" so the
  card shows where the consent claim came from; the instant is the import time.
- Idempotent by construction: running the same file again classifies every row as a duplicate and creates nothing.

### Slice `Features/Clients/ImportClients` at `/admin/clients/import`
- `[RequireTenant]`, `[RequirePermission(Permissions.ClientsManage)]`, `[FeatureGate(nameof(FeaturesOptions.ClientImport))]`.
  `Clients.Manage` is a new permission: part of `Permissions.All`, so Owner and Manager hold it; Receptionist and Staff do
  not. The flag defaults to off in `appsettings.json` and on in Development and the test hosts.
- Step 1 upload: one file input, anti-forgery, `.csv`/`.txt`; the request limit is 8 MiB so an oversized file still gets
  the localized "too large" message rather than a bare 413. Step 2 preview: counts, the first 50 rows with status and
  problem text, the rest summarised, and a Confirm button. Step 3 result: counts and a link to the client list.
- The parsed preview lives in `IMemoryCache` for 15 minutes under a random 256-bit token together with the tenant id; the
  Confirm form posts only the token. A token of another tenant, an unknown token or an expired one is a 404 for the
  other tenant and a redirect to step 1 with "expired" otherwise. Rows are never re-posted from the browser.
- No JavaScript: three plain forms, Tailwind, 390 px first.

## Consequences
- Adding a third source (Fresha, Treatwell, a hand-made spreadsheet) is one parser class plus fixtures; nothing else moves.
- The alias tables encode guesses about third-party exports; a file that does not match is refused as `UnknownFormat`
  with the header shown, not imported wrongly. Expect to extend them after the first pilot.
- Rows without a last name are invalid rather than guessed (the client card requires both names); the preview says so
  and the owner can fix the file. Appointment history, balances and health notes stay behind in the old system.
- The preview cache is per process; a restart between preview and confirm sends the owner back to step 1 (15 minutes,
  one form, acceptable). If the host is ever scaled out (M3+) the cache moves to a distributed store behind the same token.
- Tests: RFC 4180 reader, decoder fallbacks, both parsers on fixtures (Booksy UTF-8 BOM, Versum Windows-1250 with Polish
  letters, a broken file), a FsCheck round trip (generated rows → CSV → parser → same rows), caps, the pure classifier,
  the three steps over HTTP (counts, normalised phones, idempotent second run, cross-tenant token, Receptionist 403, flag
  off 404) and one Playwright run at 390 px with screenshots and axe.
