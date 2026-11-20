# 0077. Your own password, and the sign-in card

Status: Decided — owner may veto
Date: 2026-11-20
Issue: the team page promises "it is theirs to change", and nothing let a person change it; the owner wants the sign-in to look like the products they respect

## Context

ADR 0076 gave the owner a page to create logins with a starting password "handed over in person; it is theirs to
change" — but only the owner could change it afterwards. Meanwhile the owner's design direction (ADR 0080) names
the Booksy-style centred card as the shape of the sign-in page, with room kept for Google sign-in later.

## Decision

- **`/account/password`, for any signed-in person, about their own login only.** Current password, new password
  twice. The current one proves it is them; the new one must differ and meet the same length rules as everywhere.
- **A wrong current password counts towards the lockout** exactly like a failed sign-in (five, then fifteen minutes,
  ADR 0009): someone at an unlocked screen gets five guesses, not an evening. The handler therefore reports the
  outcome instead of throwing, so the count commits with the transaction. The POST shares the login's rate limit.
- **Every other session ends, this one stays.** The change rotates the security stamp (ADR 0009's machinery); the
  controller re-issues this session's cookie with the fresh stamp, so the device in hand stays signed in.
- **`[RequireSignedIn]`** joins `[RequirePermission]` in the framework: any signed-in user of the request's tenant,
  whatever the role; anonymous is challenged on a salon's host and 404 on the platform domain.
- **The way in is the person's own name** in the admin menu. The page itself sits outside `/admin`, so it shows
  as the client-facing layout with the "Salon admin" way back (ADR 0080's scoping).
- **The sign-in page becomes the centred card** — one card, a line saying who it is for, one strong button; the
  password page wears the same card. No social sign-in is built; the card leaves room for it.

## Consequences

- The promise on the team page is true: a receptionist changes the password they were handed on day one.
- A forgotten password is still the owner's job (the team page) until real email invitations exist.

## Alternatives considered

- Emailing a reset link: needs the invitation/reset token flow, a security surface of its own, deferred with it.
- Keeping the signed-in person signed in everywhere after a change: defeats the point of changing a password that
  may have leaked.