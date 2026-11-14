# 0031 — Short links in messages, so a confirmation is one text

Status: Decided — owner may veto (Tier 2: changes the secret in the client's own link)
Date: 2026-11-14

## Context
ADR 0030 added a count of the texts a message costs, and the first thing it showed was that the standard
confirmation costs two. The wording is about 95 characters; the link was about 75: the salon's address, the path
`/book/cancel/` and a token of 43 characters (32 random bytes, ADR 0014). One text holds 160. Every confirmation,
reminder link and "new time" message would be billed twice once texts cost money (M3), for nothing the client reads.

## Decision
- **The token is 16 random bytes: 22 characters.** 128 bits from the system's cryptographic generator. That is the
  strength of a random UUID and of the session and reset tokens of most web applications; it cannot be guessed or
  searched. 32 bytes bought nothing a client or an attacker could notice, and cost 21 characters in every text.
- **Messages carry `/v/{token}`** ("v" for visit) instead of `/book/cancel/{token}`: ten characters less.
  `IPublicUrlBuilder.CancelBookingUrl` builds it for texts and emails alike.
- **The short address only forwards** to `/book/cancel/{token}`, the page where the visit is changed or cancelled
  and where an unknown or used link is explained. The page and its address do not change, so links on the
  confirmation page and bookmarks keep working. The target is built from the escaped token, so it is always a path
  on the same site.
- **Old links keep working.** `PublicBookingToken.LooksValid` accepts both lengths; appointments keep the token they
  have. No migration and no rewrite of stored tokens.
- **Result:** the standard English confirmation with its link is about 140 characters. A test builds it with a
  production-length address and a longer service name and asserts one text.

## Consequences
- A Polish text is still two texts whenever the wording contains a Polish letter, because such a text holds 70
  characters. The salon sees that on the messages page (ADR 0030) and can write without diacritics if it wants to.
- A very long salon, service or staff name can still push a text over 160; the messages page shows the count for an
  example, not for every client.
- The token is still the only credential for one visit, as before. It is never logged and only sent to the client.
- A link opened from a text makes one extra request (the forward).

## Alternatives considered
- **A separate short code per visit** (a new column, for example 10 characters). More to store, index and migrate
  for a result two characters shorter than this, with a weaker secret.
- **A link-shortening service or a shared short domain.** A third party in the path of a link that is a credential,
  and one more thing to run. Not before there is a reason.
- **Shorter standard wording.** Saves a few characters and reads worse; the link was the waste, not the sentence.
- **Keep 32 bytes and only shorten the path.** Saves ten characters; the text stays at two.

## Rollback
Revert the PR: new bookings get 43-character tokens again and messages carry `/book/cancel/…`. Short tokens already
given stop passing `LooksValid`, so their links would show "unknown link"; revert only together with a guard that
keeps accepting 22 characters, or before real clients have received short links.
