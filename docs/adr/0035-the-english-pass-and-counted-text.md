# 0035 — The English pass and counted text

Status: Decided — owner may veto
Date: 2026-11-15

## Context
The owner asked to focus on England and on the English wording. Every English string, the standard messages and the
main screens were read as a British client and as the desk of a salon would read them. Most of the text held up. What
did not:

- **The salon's own front page said "Online booking opens here in M1."** Booking has been open for a long time; the
  page had not been touched since the first week. The platform page still said "M0 walking skeleton".
- **Counts of one were wrong.** "1 attempts left", "Free up to 1 hours before the visit", "1 services",
  "All 1 appointments". One test even expected "1 attempts left".
- **Hints written for Poland or for a programmer.** "+48" as the example country code, "a letter such as ą or ł",
  "Enter a date as YYYY-MM-DD" under a date picker that shows 16/11/2026, "Enter a time as HH:MM".
- **Words the desk does not use.** "Buffer", "ranges", "stylist" in a salon that also does nails and lashes.
- **A day without its weekday.** The day list said "16 November 2026". A salon plans by "Monday".
- **Emails began the first sentence in lower case** after "Hi Olivia,", which is right in Polish and wrong in English.

## Decision
- **Counted text has two values** (`LocalizationPlurals.Plural`): `Key.One` for exactly one and `Key.Many` for every
  other count, zero included. The count is `{0}` in both; the caller's arguments follow. English needs no more. A
  language with more forms writes its `.Many` value so that it reads right with any number ("Usługi: {0}"). No plural
  library, no rule table per language: two keys cover English, which is where the care goes now, and a count-neutral
  sentence covers Polish. Architecture tests keep the pairs complete and check every `Plural(...)` call in the host.
- **A count that is not the first argument is written count-neutral** instead ("rows: {2}"), as "Visits: {0}" already
  was. Zero gets its own sentence where "All 0 appointments" would read badly ("Noah Reed has no appointments to
  move.").
- **The salon's front page invites a booking.** "Book an appointment" leads to `/book`, "Services and prices" is the
  second button, and "Where to find us" follows. The build notes are gone from both front pages.
- **"Buffer" is "clean-up time" on the screen.** The code, the database column and the glossary keep `Buffer`. "Staff
  member" replaces "stylist". "Processing time" stays: salons and their suppliers use it.
- **The weekday leads the day** on the day list, the calendar, the appointment card, the till and the day rows of the
  report (`DayFormatter.ToDayHeading`: "Monday 16 November 2026"). On a phone the year may wrap to a second line.
- **Dates and times in error messages follow the screen**: "Choose a valid date.", "Enter a time such as 09:30."
- **The standard messages.** The confirmation text ends "Change or cancel:" before its link, because the link does
  both and a client who can move a visit is one who does not miss it. Email bodies start their first sentence with
  a capital. A salon that wrote its own wording keeps it.
- **One wording per idea.** "Anyone available" at the desk as on the booking page; "two decimal places" everywhere.
- **Polish values** change only where a key was split in two or its meaning changed. No Polish pass (owner's focus).

## Consequences
- 28 expected texts in 19 test files were changed to the new wording, one of them from "1 attempts left" to
  "1 attempt left". No test was removed or loosened. Three phrases ("YYYY-MM-DD", "HH:MM", "stylist") and the build
  note are now refused in en-GB by an architecture test.
- The length of the confirmation text grows by two characters. It stays one text for ordinary names.
- A count-neutral Polish `.Many` reads a little stiff. A real Polish plural rule is a later decision, with the
  Polish pass.
- Left alone on purpose: "Checkout", "Till", "No-show" (British salon software uses all three); the platform
  tagline, which names the UK and Poland and is the owner's positioning; "No free times in the next {0} days",
  which is never one day.

## Rollback
Revert the PR. No migration, no setting, no stored data depends on any of it.
