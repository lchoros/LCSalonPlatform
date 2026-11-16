# 0055 — "Tell me when a time comes up": the waitlist on the public booking pages

Status: Decided by the owner (2026-11-16: "go with what is most suitable"; named as next in line in run 12); Tier 2
because it takes personal data from the public and changes the text message check
Date: 2026-11-16

## Context
The desk has a waitlist (ADR 0041): clients who want a visit the diary has no room for, the one who asked first on
top. Only the desk could put somebody on it. A person who opens the booking pages and finds no time that suits
simply leaves, and the salon never knows. A marketplace would show that person another salon. This product has no
marketplace: the salon should keep its own demand and fill its own freed times.

A public form that writes to the salon's list is also a way to put anybody's number on it. The booking flow already
proves a number with a text message code (ADR 0014).

## Decision
- **Off until the owner switches it on**, at `admin/settings/waitlist` (`Settings.Manage`):
  `BookingSettings.PublicWaitlist`. While it is off every address of the form is a 404 and nothing is offered.
- **Under the times of every service** the booking pages say "No time that suits you? Ask us to tell you when one
  comes up", with the person the visitor had chosen carried along.
- **The form** takes a first and last name, a mobile number, optionally an email, optionally the days between which
  the visitor could come, and optionally a remark such as "mornings only". No marketing tick: joining the list is
  not consent to marketing, and none is recorded.
- **The number is proven before anything reaches the salon.** The same code, lifetime, attempts and hourly limit
  per number as a booking; the limit counts both together. Only then is the client found by the number or created,
  and put on the desk's list through `IWaitlistService`, as if the desk had done it.
- **A code sent for one purpose is useless for the other.** `PhoneVerification.Purpose` says what the row is for.
  The booking's verify step does not find a waitlist row and the waitlist's does not find a booking row; tests prove
  both directions.
- **A card the salon already has is not rewritten.** A known number gets its entry on the existing card; the name
  typed in the form is not copied over it.
- **One entry per client and service**, as ADR 0041 says: asking again keeps the place and takes the new days and
  remark. The right code sent twice is still "on the list".
- **Nobody is messaged automatically.** The desk works the list from the top and gets in touch. An entry that came
  from the form is marked "Asked online" on the desk's list. A booking for that service closes the entry by itself,
  as before.
- **The rows of the check are cleaned up** by the same job and after the same time as a booking's.

## Not decided here
- A message to the waiting client when a time is freed, and holding the freed time for them. Taking oneself off the
  list by a link. Showing the visitor their place in the queue.
- Offering the form only when a day has no times at all.

## Consequences
- Migration `PublicWaitlist`: four additive columns on `phone_verifications` (`purpose` with a default of booking,
  two days, a remark). No row changes.
- For a waitlist row the booking's `start_at` column holds the moment of asking; there is no time to book.
- A new public form takes names and numbers of people who are not clients yet. They become client cards only after
  the number is proven. The page says what the details are used for and that the salon takes them off on request.

## Rollback
Switch it off on the settings page: the form is gone at once and the entries already on the list stay with the
desk. To remove it: revert the PR and run `Down`, which drops the four columns.
