# 0006 — Localization from embedded JSON resources, request culture from the tenant

Status: Decided — owner may veto
Date: 2026-11-11

## Context
CLAUDE.md rule 6: every piece of UI text goes through `ILocalizationService` resource keys with both `en-GB` and
`pl-PL` values. The platform serves British and Polish salons from one deployment, so the culture is a per-request
decision, and the salon's own language (ADR 0002: `Tenant.Culture`) must win over the server's locale. Resource files
need to be checked for parity in CI, must not need a database to render the first page, and must be extensible by
plugins without letting a plugin silently rewrite host text.

## Decision
- **Flat JSON, embedded.** Each assembly that owns UI text ships `Localization/<culture>.json` as an
  `<EmbeddedResource>` (manifest name `Salon.Web.Localization.en-GB.json`), a flat object of `"Area.Key": "text"`.
  Keys are `PascalCase` segments joined by dots (`Home.BookingOpensSoon`). `EmbeddedJsonResourceSource` reads every
  `*.Localization.<culture>.json` of one assembly and rejects a culture outside `SupportedCultures.All`, two resources
  for the same culture in one assembly, a key repeated inside one file, and invalid JSON.
- **One in-memory table, validated at startup.** `LocalizationService` (singleton) merges all `IResourceSource`s once
  into a `FrozenDictionary` per culture; `UseSalonLocalization()` reads `ILocalizationService.Cultures` while the host
  starts, so any packaging mistake stops the app instead of failing requests. An unsupported culture from *any* source
  (embedded or otherwise) is rejected there too. Lookup falls back requested culture → `en-GB` → the key itself, and
  logs one warning per missing `(culture, key)` pair so a gap is visible without flooding the log.
- **No silent overrides.** Two sources defining the same key for the same culture throw `InvalidOperationException`
  at load, naming both sources and the key. Salon-specific wording (a tenant renaming "Appointment" to "Visit") is a
  later, per-tenant database layer on top of this one; it is not the plugin mechanism.
- **Request culture order.** `UseSalonLocalization()` runs after `UseSalonTenancy()` and picks, in order:
  `?culture=` query string, the ASP.NET culture cookie, the tenant's `Culture` (`TenantCultureProvider`), then
  `Accept-Language`. Unsupported values are skipped and the default is `en-GB`. ASP.NET's parent fallback only walks
  upwards (`pl-PL` → `pl`), so `SupportedAcceptLanguageProvider` maps neutral browser tags through
  `SupportedCultures.Match` (`pl` → `pl-PL`) and tries up to ten header values. Views read strings through an injected
  `T` (`@T["Home.Title"]`) declared in the project-root `_ViewImports.cshtml`, so `Views/`, `Features/` and `Areas/`
  all see it.
- **UTF-8 output.** The HTML encoder allows Basic Latin, Latin-1 Supplement, Latin Extended-A/B and General
  Punctuation, so Polish text is served as characters rather than numeric entities.
- **Parity in CI.** `Salon.Tests.Architecture` loads the host's resources and asserts that `en-GB` and `pl-PL` have
  identical key sets and matching `{n}` placeholders, no empty values, well-formed keys, that every key used by a
  `.cshtml` anywhere in the host exists, and that no host view carries the brand or booking copy as literal text.

## Consequences
- Adding text means adding the same key to two JSON files; CI fails on a missing translation or a mistyped key instead
  of a reviewer spotting English on a Polish salon's page.
- No database round-trip or cache invalidation for static text; a deploy is the only way to change it, which is right
  for phase 1. Per-tenant overrides, pluralisation and ICU message formats are deferred to when M1 screens need them.
- `string.Format` with the current culture covers the M0 needs; dates and money still format through NodaTime and
  `Money` in the location's zone and currency, not through the localization service.
- `TenantCultureProvider` looks the tenant up by id, a second (cached) lookup after the middleware's slug lookup on the
  first request per tenant. A later refactor can stash the `TenantInfo` on the request instead.
- Plugins get their own resource files and the same parity rules once they carry UI text; today none do.

## Amendment 2026-11-12 — one resource file per module and culture

**Why.** Every slice appended its keys to the end of the two flat files, so every pair of parallel branches conflicted
on `en-GB.json` and `pl-PL.json` even when the slices had nothing in common. The merge conflicts were mechanical but
cost a review round each time.

**Decision.**
- The host keeps `src/Presentation/Salon.Web/Localization/<culture>/<Module>.json`, e.g. `Localization/en-GB/Catalog.json`
  and `Localization/pl-PL/Catalog.json`. The module is the first segment of the key (`Catalog.CreateService.Title` lives
  in `Catalog.json`); a key without a dot goes to `Common.json`. A slice edits only its module's two files; a new module
  adds both files.
- The csproj embeds `Localization\**\*.json` with
  `LogicalName="$(RootNamespace).Localization.$([System.String]::Copy('%(RecursiveDir)').Replace('\', '.').Replace('/', '.'))%(Filename)%(Extension)"`.
  MSBuild expands `%(RecursiveDir)` before it evaluates the property function, so the culture folder (`en-GB\` on
  Windows, `en-GB/` on Linux) becomes a dot segment and the manifest name is `Salon.Web.Localization.en-GB.Catalog.json`.
  No flat-file fallback was needed.
- `EmbeddedJsonResourceSource` accepts several resources per culture and merges them: `…Localization.<culture>.<Module>.json`
  and the legacy `…Localization.<culture>.json` (plugins with little text may keep one file per culture). A key present in
  two files of the same culture is a startup error naming both files and the key. `ReadFiles()` exposes the parsed files
  so tests can check parity file by file. `ILocalizationService`, `LocalizationService` and the `T` view helper are
  unchanged; no slice or view changed.
- Architecture tests (`LocalizationResourceTests`) additionally assert: every module file exists for both cultures (the
  message names the missing `Localization/<culture>/<Module>.json`), every key sits in the file named after its first
  segment, module names are one PascalCase segment, manifest names carry no path separator, and no flat culture file is
  embedded in the host.
- `tools/split-localization.ps1` (PowerShell 7) splits a flat `<culture>.json` into module files preserving key order,
  merges into existing module files (new keys are appended, existing keys are kept; `-Overwrite` takes the flat file's
  text; differences are reported either way), deletes the flat file unless `-KeepFlat`, and is a no-op when no flat file
  exists. It is the fix when a branch from before the split brings a flat file back: `pwsh tools/split-localization.ps1`,
  then commit.

**Consequences.**
- Two slices in different modules no longer touch the same resource files; two slices in the same module still can, and
  Git merges line-level additions inside one module file without a conflict unless both append at the very end.
- Key order inside a module file is the order of first appearance in the old flat file and then append order; the files
  are not alphabetised, so a rebase diff stays small.
- The CLAUDE.md rule 6 wording does not name file paths, so it is unchanged.
