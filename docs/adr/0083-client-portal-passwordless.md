# 0083. "My bookings": a client signs in with a text message code, no password

Status: Decided — owner may veto (Tier 2: this is a sign-in)
Date: 2026-11-20
Issue: owner's order of 2026-11-20 — a customer login without passwords

## Context

A client can change or cancel a visit only from the link in a text message they still have (ADR 0014, ADR 0021,
ADR 0029). Lose the message and the only way is to ring the salon. The owner asked for a door on the salon's
public site: type your mobile number, get a code, see what you have booked. ADR 0009 said "clients never log in";
this changes that sentence for one narrow purpose and nothing else: no password, no account, no profile.

## Decision

- **Three pages and a sign-out on the salon's host**, behind `Features:ClientPortal` (off by default):
  `/my-bookings/sign-in` takes the number (`ClientPortal/RequestCode`), `/my-bookings/code/{id}` the six digits
  (`ClientPortal/EnterCode`), `/my-bookings` lists the visits ahead (`ClientPortal/ListVisits`),
  `POST /my-bookings/sign-out` ends it (`ClientPortal/SignOut`). The rules live in `IClientPortalService`
  (Salon.Services); the slices only hand over. Nothing personal is in any address: the id is the verification row's.
- **The code is the booking's code.** `PhoneVerificationRules` makes, hashes and checks it (six digits, per-row
  salt, SHA-256, constant-time compare, ten minutes, five attempts); nothing about codes is written a second time.
  A sign-in is a `PhoneVerification` row with a third purpose, `ClientPortal`, next to `Booking` and `Waitlist`
  (ADR 0055), so the retention task (ADR 0017) and a client's erasure (ADR 0062) already cover it, and the
  three-codes-an-hour limit per number counts bookings and sign-ins together. Each service reads only rows of its
  own purpose: a booking code cannot sign in, a sign-in code cannot book. The text is the existing `Booking.Code`
  template. The salon's "no code needed to book" setting does not apply here: this door always asks for the code.
- **The page tells nobody who is a client.** Asking for a code does the same work for every readable number: one
  row, one rendered text, one redirect to the code page, which says "if this number is on a booking with us, we've
  texted it a code". Only when a card carries the number is the text handed to the SMS provider. The row of an
  unknown number is hashed from a value that is not six digits (`GenerateUnmatchableSecret`), so no code can ever
  prove it, while its wrong attempts are counted like any other's. A wrong, expired, used-up or already-used code
  gets one sentence. The hourly limit refuses known and unknown numbers alike, in the booking form's words.
- **The same per-address limits as the booking flow**: the sign-in post is under `sms-code` and the code post under
  `code-verify` (ADR 0014), the same policies and the same counters. Both posts carry an anti-forgery token.
- **The session is a cookie of its own, `salon.client`, and not an ASP.NET authentication scheme.** It never
  becomes `HttpContext.User`, so nothing that admits staff (`[RequirePermission]`, the tenant middleware, the admin
  shell) can read a client as staff, and the staff cookie `salon.auth` is never read as a client. It is sealed with
  Data Protection under its own purpose and carries the tenant id, the client card's id, the instant the code was
  entered and the instant last seen: no number, no name. HttpOnly, Secure, SameSite=Lax, no Domain (host-only),
  Path `/my-bookings` (the browser sends it nowhere else), no expiry date (it goes when the browser closes).
  Thirty idle minutes or twelve hours since the code end it; both are checked on the server against `IClock`.
- **Two checks keep a cookie inside its salon**: the tenant id inside the sealed value must be the host's, and the
  card is then read through the tenant filter, where another salon's card does not exist.
- **The session hangs on the card and ends when the card changes.** Each page view reads the card again: gone,
  erased, merged away, without a number, or edited since the code was entered (`Client.UpdatedAt`) means the
  session is over and the client proves the number again. So a number the desk corrects stops opening the old
  session at once, without the number ever being in the cookie.
- **What is shown**: visits that still hold their time and have not started, soonest first, at this salon only:
  service, person, day and time, and the links to the change and cancel pages that already exist
  (`/book/change/{token}`, `/book/cancel/{token}`). No price, no deposit, no note, no health data, no past visit,
  no name. A visit without a link (booked at the desk before ADR 0029) is listed with "contact the salon".
  The page is `no-store`.
- **"Every card with the number"** is how the query is written, although today a number is on one card per salon
  (unique index, ADR 0011). The day a family may share a number, their visits are listed with no change here.
- **`phone_verifications.service_id` becomes optional**, because a sign-in books nothing. A check constraint keeps
  it required for bookings and waitlist requests (`service_id IS NOT NULL OR purpose = 3`), so those rows are
  guarded as before. Booking and waitlist code reads the service through `PhoneVerificationRules.ServiceOf`.
- **The gate is an authorization filter** (`[ClientPortalGate]`): flag off, or no salon on the host, is a 404
  before the anti-forgery check, so a post to a switched-off page does not answer 400 and give the page away.
- **Logs** carry ids, outcomes and counts. A test reads every line the host writes during a sign-in and finds no
  number, code or name.

## Consequences

- A client who lost the message can still change or cancel, and the salon's phone rings less.
- One more text message per sign-in, paid by the salon; the limits cap it like a booking's.
- A number typed by a stranger is kept in `phone_verifications` for a day, as the booking form already does.
- Signing out removes the cookie from that browser. A copy of the cookie taken elsewhere stays good until thirty
  idle minutes or twelve hours pass: there is no server-side list of sessions. Accepted for what the page shows;
  a list is the next step if the page ever shows more.
- The cookie needs the Data Protection key ring to survive restarts and to be shared between instances, like the
  staff cookie and the health notes already do.

## Not closed here (the owner should know before the flag goes on in public)

- **Timing with a real SMS provider.** Both paths do the same database work, but a known number also waits for the
  provider. With the Phase 1 log provider the difference is nothing; with a real one it can be measured. ADR 0014
  already names the cure (a queued send); it should be in place before the flag is on with a real provider.
- **The per-address limits behind a proxy** count the proxy's address unless forwarded headers are configured; that
  is the booking flow's situation too, and a deployment matter.
- **A family sharing one number** cannot have two cards today, so there is nothing to show for the second person.

## Rollback

Switch `Features:ClientPortal` off: every address is a 404 and the link is gone; cookies already issued open
nothing. The schema change is compatible with the code before it (bookings and waitlist requests still write a
service; the column merely allows null for a purpose that code never reads). To take the migration back as well:
with the flag off, wait a day for the retention task to clear the sign-in rows, then run the migration's `Down`
(it deletes any that remain and makes the column required again).

## Alternatives considered

- A second ASP.NET cookie scheme: it works, but puts a client principal one mistake away from `HttpContext.User`
  and its expiry runs on the system clock; a sealed value read only by the portal's own pages is smaller and
  cannot be confused with a staff login.
- A table of its own for sign-in codes: a second copy of the retention, erasure and rate-limit rules, and two
  limits per number instead of one.
- The number in the cookie, to notice a changed number exactly: the brief keeps personal data out of the cookie;
  "the card changed" is coarser and errs on the side of asking again.
- An email link instead of a code: salons reach clients by text, and many cards have no email.