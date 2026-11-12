# 0014 — Public booking page and SMS code verification

Status: Decided — owner may veto (Tier 2: the SMS code is the client's only authentication)
Date: 2026-11-12

## Context
M1's gate: "a test client books on a phone in under 60 seconds". The engine (ADR 0012), staff (ADR 0010) and client
cards (ADR 0011) exist; what is missing is the page a client uses. Clients never log in (ADR 0009), so something else
must prove that the person booking owns the phone number the salon will later call or text, stop a bot from filling
the diary with made-up names, and let the client cancel from the confirmation text without an account. The flow must
work on a 390 px phone, in both languages, without JavaScript, and in a handful of taps.

## Options
1. No verification: anyone types a name and a number and the slot is gone. Fastest, and the reason salons hate
   no-shows from fake bookings on free widgets.
2. Email magic link. Slower on a phone (switch to the mail app), and salons reach clients by SMS anyway.
3. A six-digit code sent by SMS to the number typed, checked before the booking is written; a secret token in the
   confirmation text for cancelling. Standard on every booking app the clients already use.

## Decision
- **Flow and routes** (all anonymous, `[RequireTenant]`, on the salon's host): `GET /book` lists active services as
  tappable cards (`Booking/ChooseService`); `GET /book/{serviceId}?date&staff` shows a 14-day date strip bounded by
  `BookingSettings.MaxAdvanceDays`, a person picker with "Anyone available" first, and the free times of the chosen day
  from `IAvailabilityService` (one call for the whole strip, so the default day is the first with a free time)
  (`Booking/ChooseSlot`); `GET|POST /book/{serviceId}/details?start&staff` collects first name, last name, mobile,
  optional email and the marketing tick (`Booking/EnterDetails`); `GET|POST /book/verify/{verificationId}` takes the
  code (`Booking/VerifyCode`); `GET /book/done/{appointmentId}` confirms (`Booking/Confirmation`);
  `GET|POST /book/cancel/{token}` cancels (`Booking/CancelBooking`). Every link in the picker is a plain anchor and
  every state change a form POST with an anti-forgery token: no script is needed anywhere.
- **Pending booking in the verification row.** `PhoneVerification : BaseEntity, ITenantEntity` (table
  `phone_verifications`, forced RLS) carries the normalised phone, the code's salt and hash, `ExpiresAt` (10 minutes),
  `Attempts`, `VerifiedAt`, `AppointmentId` once booked, and the whole pending booking (service, staff or null for
  "anyone", start, names, email, consent flag and the consent wording shown). The verify page therefore needs no session
  or cookie: the row id in the URL is the only state, and the row only exists for the salon whose host it is served on.
- **The code.** Six digits from `RandomNumberGenerator` ("000000"–"999999"); `CodeSalt` is 16 random bytes (base64);
  `CodeHash` is base64 SHA-256 of `"{salt}:{code}"`; the check is `CryptographicOperations.FixedTimeEquals`. Five wrong
  codes kill the row for good, the sixth and later attempts are refused without being checked; an expired row is
  refused likewise. All of this is pure (`PhoneVerificationRules`, Core) and unit-tested. The code lives only in the
  request that generated it and in the SMS; `PublicBookingService` logs the verification id and the provider name,
  never the code. The Phase 1 `LogSmsProvider` writes the message text to the log and to Mailpit — that is its purpose
  and acceptable in Development only; a real provider (M3) receives the text and logs nothing.
- **Repeat protection.** A right code books, stamps `AppointmentId` on the row and any later POST with the same id lands
  on the same confirmation instead of booking twice. A row verified whose slot was gone (lost a race) is spent; the
  client is sent back to the time picker.
- **Anti-abuse.** Three codes per phone number per hour, counted from `phone_verifications` rows (`PhoneVerificationRules.MaxCodesPerPhone`,
  a domain rule); ten details POSTs per address per hour (`sms-code`) and twenty code attempts per address per hour
  (`code-verify`) through the framework rate limiter, configured under `PublicBooking:*` (Web.Framework) so tests can
  lift them on the shared host and prove the 429 on a host with the defaults. Every POST carries an anti-forgery token.
- **Client record.** On verification the client is found by normalised phone (`IClientService.FindByPhoneAsync`, new) or
  created with the names, phone, email and — when ticked — the consent flag, instant and the exact wording shown
  (`Booking.Public.ConsentWording` in the request culture with the salon's name, ADR 0011). An existing card keeps its
  names; a tick on an existing card without consent grants it. An email that already belongs to another card is left
  off the new card (the phone decides who the client is; a shared family address must not block a booking).
- **Booking.** Through `IBookingService.BookAsync` with `AppointmentSource.Online`. For "anyone" the person is chosen at
  booking time from the engine's answer for that start (fewest bookings that day, ADR 0012), so the client sees one list
  and the salon balances the diary. `BookAsync` now sets `Appointment.PublicToken` for online bookings: 32 random bytes
  as base64url (`PublicBookingToken`), unique index, the credential of the cancel link. Admin bookings have no token
  and no public page.
- **Confirmation SMS.** `AppointmentBookedSmsConsumer : IConsumer<AppointmentBooked>` (Services/Notifications) runs from
  the outbox in the tenant's scope, loads names through the filter, renders `Booking.Confirmation` (SMS) in the salon's
  culture with `AppointmentStart` formatted in the salon's zone ("Tue 18 Nov, 10:00" / "wt. 18 lis, 10:00") and
  `CancelUrl` from `IPublicUrlBuilder`, and sends through `ISmsSender`. Skipped for admin bookings (M2 notifications),
  clients without a phone, and appointments no longer active. Delivery is at-least-once (ADR 0007): a crash between the
  send and the outbox update can repeat the text; acceptable for a confirmation.
- **Absolute links without a request.** `IPublicUrlBuilder` (Services contract) is implemented in Web.Framework
  (`PublicUrlBuilder`) from `Tenancy:BaseDomains[0]`, the tenant slug and `Tenancy:PublicScheme` (default https; http
  when unset in Development) plus an optional `Tenancy:PublicPort` for a local Kestrel. Links never come from the
  request's Host header, so a spoofed header cannot steer a client elsewhere. Registered like the password hasher:
  interface in Services, implementation in the framework.
- **Template key.** `Booking.Code` (SMS only, tokens `SalonName`, `Code`) joins the catalogue with both default texts.
  The embedded defaults file requires all three fields per key, so the key also carries an email subject and body;
  they are not sent by anything today.
- **Settings honoured.** `BookingSettings.RequireSmsVerification` off books at once from the details form (the salon
  trusts its clients, or a test wants speed); `MaxAdvanceDays` bounds the strip; the engine applies notice and step.
- **Cancel by token.** `GET /book/cancel/{token}` shows the appointment and a confirm button; `POST` cancels as
  `CancelledByClient` through the booking service (frees the slot, writes history, publishes the event). An unknown,
  malformed or other salon's token, an appointment already cancelled or finished, or one that has already started each
  get a localized message on the same page (200): the client followed a link from their own SMS and deserves an
  explanation, not a 404. The confirmation page is addressed by the appointment id (UUIDv7, 74 random bits) and shows
  the cancel link, so the id is a weaker credential for the same action; both are unguessable, neither is ever listed.

## Consequences
- A client books in four screens: service → time → details → code. A returning client types the same four fields; a
  cookie that prefills them is a later, additive convenience.
- The salon pays for one SMS per booking attempt plus one confirmation; the per-phone and per-address limits cap the
  damage of a scripted attacker to a few texts an hour. Real-provider cost controls are an M3 topic.
- `phone_verifications` grows one row per attempt and keeps the client's typed details for a while; a retention clean-up
  task (delete rows older than a day) is a later additive change, and the table is covered by RLS like every other.
- The code SMS is sent inside the command's transaction: if the provider throws, no row is kept and the client sees an
  error; if the row fails to commit after the send, the client holds a code nobody can use. Both are acceptable at
  Phase 1; a queued send is the M3 alternative when a real provider's latency matters.
- The staff picker and date strip are links, not radio inputs: no script is needed and one tap changes the view. On a
  phone they scroll sideways inside their own row, so the page itself never scrolls horizontally.
- Tests prove: hashing and attempt rules (unit), URL formatting (unit), token sets (unit), and over HTTP the full flow,
  the code read from a recording SMS sender, five wrong codes, the normalised client with consent wording, the outbox
  event and the confirmation text with the cancel link, cancel and reuse, tenant B against tenant A's token, the
  no-verification path, the 429, and a Playwright run on a 390 px viewport timed under 60 seconds.
