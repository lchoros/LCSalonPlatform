# 0011 — Clients and health data

Status: Decided — owner may veto (Tier 2: GDPR/RODO special-category data)
Date: 2026-11-11

## Context
Every appointment hangs off a client card: who is coming, how to reach them, what they like, and — for colour, lash and
brow work — what they are allergic to and when the last patch test was. Allergies and conditions are *special-category*
data under GDPR art. 9 (RODO in Poland), so they need stronger protection than a name or a phone number, and marketing
consent needs proof: what text the client agreed to and when. The salon owns its data (CLAUDE.md), so the card must be
complete enough to export later; M2 imports Booksy/Versum CSVs into it and the booking flow (M1) looks clients up by
phone from the front desk.

## Options
1. One `clients` table, everything plain. Simple, but a database dump or a mis-scoped query exposes health data.
2. Separate `client_health` table behind its own permission. Cleaner access control, but plain at rest and a join on
   every card; a permission split is premature with four fixed roles.
3. One table; the health field holds cipher text produced by ASP.NET Core Data Protection, decrypted only on the card
   through one service method. Searches, lists, logs and events never touch the column.

## Decision
- **Option 3.** `Client : BaseEntity, ITenantEntity` — `FirstName`, `LastName` (≤ 100 each), `Phone` (E.164-style,
  ≤ 20, normalised before storage and search), `Email` (lower-case, ≤ 254), `Notes` (plain, ≤ 4000),
  `HealthNotesProtected` (cipher text, ≤ 8000; plain text ≤ 4000), `LastPatchTestOn` (`LocalDate?`),
  `MarketingConsent`, `MarketingConsentAt` (`Instant?`), `MarketingConsentWording` (≤ 500), `IsActive`, `CreatedAt`,
  `UpdatedAt`. Table `clients` with forced RLS (ADR 0002), index on `(tenant_id, last_name, first_name)`, unique partial
  indexes on `(tenant_id, phone)` and `(tenant_id, email)` where not null.
- **Health notes are encrypted at rest.** `IFieldEncryptor` (Salon.Services: `Protect`, `Unprotect`, `TryUnprotect`)
  is implemented in Salon.Web.Framework over `IDataProtectionProvider.CreateProtector("Salon.Clients.HealthNotes")`
  (AES-256-CBC + HMACSHA256, authenticated, random IV, so the same text encrypts differently each time). The entity
  stores cipher text; `IClientService.GetCardAsync` is the only path that decrypts. `SearchAsync` projects a summary
  without the column, `ClientCreated(ClientId, TenantId)` carries ids only, the request pipeline logs request *names*
  and the create command redacts itself in `ToString`. The integration tests assert the plain text is absent from the
  row, the outbox payload and the search page.
- **Key ring.** Data Protection keeps its keys in the default ring: the user profile on a developer machine and in the
  test host. Keys are not tenant-specific. Before any deployment (M3, Tier 2) the ring must move to persistent,
  backed-up storage (Postgres via `PersistKeysToDbContext` or a mounted volume) and be protected at rest, otherwise a
  redeploy loses every health note. A card whose cipher text no longer unprotects shows a localized "cannot be read"
  warning instead of failing the page; it never shows the cipher text.
- **Normalisation.** `PhoneNumber.Normalise` strips spaces, dashes, dots and parentheses, maps `00` to `+`, and resolves
  a local number only with a country hint taken from the tenant's currency (`GBP` → UK `0…` → `+44…`; `PLN` → nine
  digits starting 5–8 → `+48…`); anything else needs an explicit `+`. Emails are trimmed and lower-cased. Duplicates
  per tenant by phone or email are refused with `DuplicateClientException` (an `InvalidOperationException` naming the
  field), which the form shows as a localized message next to that field.
- **Consent.** The checkbox label on the form *is* the wording recorded; `SetMarketingConsentAsync(granted, wording)`
  stamps the instant on grant and clears both instant and wording on withdrawal. No consent means no marketing; the
  notification pipeline (M2) reads `MarketingConsent`, nothing else.
- **Access.** All three slices (`Clients/SearchClients`, `Clients/CreateClient`, `Clients/ClientCard`) require a tenant
  and `Clients.View` (ADR 0009): Owner, Manager and Receptionist have it, Staff does not. A finer "health data" permission
  is deferred until roles become configurable (M2).

## Consequences
- Health notes cannot be searched, sorted or reported on in SQL; that is the point. Export (M3) decrypts per card through
  the same service.
- Losing the key ring loses health notes irrecoverably; M3's deployment checklist must include the key store and its
  backup before the first salon goes live.
- Phone and email uniqueness means a family sharing one number needs two cards with distinct contact details or one card
  per number; "merge duplicates" and "household" are later items on the M1 list.
- Tests prove: normalised phone, cipher text ≠ plain text in the row, outbox payload without personal data, duplicate
  phone as a form message, search by name prefix and by digits, no health text in search results, decrypted card,
  consent stamp and clearance, tenant B sees nothing of tenant A (404 on the card), Staff → 403, anonymous → login,
  and `RowLevelSecurityTests` covers the new table automatically.
