# 0063 — The salon's privacy notice, where clients give their details

Status: Decided by the owner's standing word (2026-11-16: "continue with recommended, I will not veto anything";
2026-11-18: "please continue"); Tier 2 because it is what clients are told about their personal data
Date: 2026-11-18

## Context
The public booking form asks a stranger for their name, phone number and email, and said nothing about what
happens to them. The waitlist form had one sentence. A salon in England has to tell people, at the moment it
collects their details, who is collecting them, what for, who sees them, how long they are kept and what the
person can ask for. ADR 0062 gave the salon the means to honour two of those requests (a copy, and deletion); a
client has to be told they exist.

A salon owner is not going to write this text. Most will use whatever the software gives them, so what the
software gives them has to be true about what the software does.

This is how the system behaves, not legal advice. The settings page says so to the owner.

## Decision
- **Every salon has a notice at `/privacy`**, readable without signing in, on the salon's own address.
- **The standard notice is built from facts**: the salon's name, address, phone number and email from "Salon
  details" (ADR 0027), and what this system does with client details. It says what is kept (contact details,
  visits and payments, notes including health information the client gives), what for, who sees it (the salon's
  staff, and the companies that run the system and deliver messages on the salon's behalf), that nothing is sold
  or listed on a marketplace, how long, what the client can ask for (a copy, a correction, deletion, no more
  offers), that the salon answers within a month, and where to complain. The complaint body is in the text of each
  language: the Information Commissioner's Office in English, the President of the Personal Data Protection
  Office in Polish.
- **Each sentence is true of the system as built.** "If you ask us to delete them, we do. We then keep only the
  record that a visit took place and was paid for, without your name" is ADR 0062. "Offers only if you said yes"
  is the marketing consent of ADR 0011. A change to what the system does with client data must change the
  standard text and move `PrivacyNoticeRules.StandardTextDate`.
- **The owner may put their own text in its place** (`/admin/settings/privacy`, owner only): plain text, a blank
  line between paragraphs, up to 20,000 characters, shown as text and never as markup. The salon's name and how
  to reach it stay above and below it. The owner's text is kept while the standard notice is shown, so switching
  back and forth loses nothing.
- **"Last updated"** is the day the owner last saved the page, or the day the standard text was last changed.
- **Linked where details are given**: at the bottom of every page a client sees, beside the booking form's
  button with one sentence saying what the typed details are used for, and beside the waitlist form.
- **The settings page says plainly** that the standard notice is a starting text and not legal advice, and to use
  own text if something is different in the salon.

## Not decided here
- **A lawful basis named in legal terms** (contract, legal obligation, consent). The standard text says the same
  things in plain words. A salon whose adviser wants the terms uses its own text.
- **Retention in years.** The standard text does not name a number, because the system does not delete on a timer
  (ADR 0062) and the right number is the salon's decision.
- A record of which wording a client saw when they booked. The marketing consent keeps its own wording (ADR 0011);
  the notice is information, not consent.
- A cookie notice: the public pages set only the cookies they need to work.
- Terms of booking as a separate page; the cancellation terms are shown on the booking form (ADR 0022).
- The platform's own privacy notice and the data processing agreement between the platform and a salon: those are
  the owner's documents, for the sign-up of M3.

## Consequences
- No migration: three settings (`PrivacyNoticeSettings`).
- `IPrivacyNoticeService` in `Salon.Services`; slices `Booking/ShowPrivacyNotice` and
  `Settings/EditPrivacyNotice`; a link in the layout's footer for visitors on a salon's address.
- Whoever changes what the system does with client data owns the standard text too.

## Rollback
Revert the PR. The settings rows are ignored by the old code.
