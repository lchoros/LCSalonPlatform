# 0008 — Message templates with tokens

Status: Decided — owner may veto
Date: 2026-11-11

## Context
Every salon sends the same handful of texts (booking confirmation, reminder, cancellation) by SMS and email, in the
salon's language, with its own wording. Booksy users complain that they cannot change those texts. M1's booking flow
needs a template service to exist first; M2 adds the admin screen that edits templates. CLAUDE.md rule 6 (all UI text
through resource keys, both cultures), rule 4 (tenant-owned data behind the filter and RLS) and rule 7 (no hard-coded
settings) apply.

## Options
1. Hard-code the texts in the notification code with `string.Format` — fastest, but salons cannot change them and
   positional `{0}` placeholders are unreadable for a receptionist.
2. A full template language (Scriban/Liquid) — a new runtime dependency (Tier 2) and more power than a salon needs;
   conditionals and loops in SMS text are a support burden.
3. Named tokens (`{{ClientFirstName}}`) over plain text, with platform defaults embedded as resources per culture and
   an optional per-tenant override row.

## Decision
- **Tokens, not a language.** `MessageTemplateRenderer` (Salon.Core, no dependencies) replaces `{{Name}}`
  placeholders from a `MessageTokens` set; names are case-insensitive, `\{{Name}}` escapes, malformed placeholders stay
  as written, unknown tokens render empty and are reported in `RenderedMessage.MissingTokens` so callers can log them.
  Values are already-formatted strings: the caller formats dates and money for the tenant's culture (ADR 0006),
  the renderer never formats.
- **Catalogue of keys.** `MessageTemplateCatalog` (Core) lists every template key with its channel(s) and the token
  names it may use (`Booking.Confirmation`, `Booking.Reminder`, `Booking.Cancelled` × Sms, Email). Validation of an
  edited template (M2) checks against this list; an unknown key is a programming error, not data.
- **Defaults embedded, overrides in a table.** Platform defaults live in `Salon.Services` as embedded
  `MessageTemplates/{culture}.json` (same packaging and parity rule as localization resources: both cultures, same keys,
  same token set per field). A tenant override is a row in `message_templates` (`ITenantEntity`, forced RLS, unique on
  tenant + key + channel + culture) holding `Subject` (email only) and `Body`. `IMessageTemplateService.GetAsync(key,
  channel, culture)` returns the tenant row when present and active, else the embedded default for that culture, else
  the `en-GB` default; `RenderAsync` combines it with tokens. Overrides are cached per tenant under the
  `Salon.messagetemplates.{tenantId}` prefix and dropped on save (ADR 0004 pattern).
- **No sending here.** Rendering returns text; the notification pipeline (M1) hands it to `ISmsSender` /
  `IEmailSender`.

## Consequences
- Adding a template key means a Core catalogue entry plus both default texts; an architecture test fails the build
  when a culture file lacks a key or the token sets differ between cultures.
- SMS defaults are kept under 160 characters with typical token values; the renderer does not enforce length (the
  sender will, in M1).
- Rendering is pure and fast (one regex pass); templates with hundreds of tokens are not a goal.
- Per-tenant editing of defaults is an M2 screen; until then only the seed and tests write override rows.
- Drafts of the default texts and of the renderer were produced by the owner's local model (see `docs/local-models.md`)
  and reviewed here.
