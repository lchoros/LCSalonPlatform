# 0075. Public sign-up: a salon and its owner in one form

Status: Decided — owner may veto
Date: 2026-11-19
Issue: until now only the seeder and the test fixtures could create a salon or a login

## Context

Everything a salon needs exists, but there was no door: tenants and logins were created by the demo seeder and the
test helpers only. The pilot plan is a friend running their salon alone, and the first thing they would meet is
"the developer makes your account by hand". The v1.x plan always meant sign-up; the pilot needs it now.

## Decision

- **One form at `/signup` on the bare base domain** (ADR 0002's platform level): salon name, web address, where
  the salon is, the owner's name, email and password. One command (`IPlatformRequest`: it runs before any tenant
  exists), one transaction: the `Tenant` is written, put in scope, and the owner's login is created into it with
  `IUserService` — the same code every later login will use.
- **The slug is the salon's address**, so `TenantSlugRules` (Salon.Core) holds the shape: a clean DNS label of 3
  to 40 characters, normalized to lower case, with the platform's own names (www, api, admin, …) kept back. A
  taken address is told apart politely; two people choosing one at the same moment are settled by the database's
  unique index, and the loser sees the same message.
- **The country choice sets zone, language and currency in one step** (England: Europe/London, en-GB, GBP;
  Poland: Europe/Warsaw, pl-PL, PLN) — the same three values every other screen already reads from the tenant.
- **The done page hands over the salon's own sign-in address** instead of signing the owner in across hosts: the
  auth cookie is host-scoped by design (ADR 0009), and a cross-host token hand-off is a security surface the
  pilot does not need. The owner signs in once, at the salon's address, and lands on getting started (ADR 0068).
- **Behind `Features:SignUp`, off by default**: on in Development and in the test hosts; a deployed instance keeps
  the door shut until the owner opens it. On a salon's subdomain the page does not exist at all.

## Consequences

- The friend creates their salon themselves and is standing in front of the getting-started steps a minute later.
- No email verification yet: phase 1 has no real email, and the flag keeps strangers out; noted as not built,
  wanted before the flag is ever on in public. Rate limiting beyond the platform's lockout policy is also not
  built, for the same reason.
- Staff logins still have no UI (the owner's login is the first ever created outside the seeder); a "team logins"
  admin page is the natural next slice.

## Alternatives considered

- Signing the owner in straight from sign-up via a one-time token on the salon's host: deferred — more moving
  parts for one saved password entry.
- Deriving the slug from the salon name silently: collisions would need suffixes the owner never chose; an
  explicit field with a hint is honest.
- Email verification first: without a real email provider it would verify against Mailpit, which verifies nothing.