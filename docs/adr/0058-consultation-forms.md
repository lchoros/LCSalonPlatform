# 0058 — Consultation forms: what the client said before a colour, kept encrypted on their card

Status: Decided by the owner (2026-11-16: "continue with recommended, I will not veto anything"); Tier 2 because it
is health data and consent
Date: 2026-11-16

## Context
Before a colour or a chemical treatment a salon asks the client about reactions, allergies, their scalp, medication
and pregnancy, and often does a patch test. Today that is a paper sheet in a folder, or nothing. When a client
reacts, the salon's insurer asks for the sheet. The card already has free-text health notes and a patch test date
(ADR 0011), but nothing that says what was asked, what the client answered, and that the client confirmed it.

The answers are health data: special-category data under UK GDPR. The card's health notes are already encrypted
with a field encryptor and shown only on the card, to the logins that may open it.

## Decision
- **One fixed form of six yes-or-no questions**: a reaction before, allergies, the scalp or skin, medication,
  pregnancy or breastfeeding, and colour or chemical work in the last six months. Then a box for what the client
  said, and the day of the patch test if one was done. Every question must be answered: a form with one skipped is
  not kept.
- **The client confirms.** The desk hands the screen over; the client ticks a sentence and types their name. The
  sentence is kept with the record exactly as it was shown, like the marketing consent wording (ADR 0011). The
  post cannot supply its own sentence.
- **The answers exist in the database as cipher text only.** The answers, the details, the patch test day and the
  typed name are one small document, encrypted with the same `IFieldEncryptor` as the health notes. In the clear
  are only whose form it is, when, who took it, what it was for, the consent sentence and the form's version. None
  of those says anything about the client's health.
- **Only the consultation service opens them, on the form's own page.** The card lists a form by its day and
  purpose. Lists, searches, the client file, the daily summary, events and log lines never carry an answer. The
  log has ids only, for a form recorded and for a form opened. The types that hold answers do not print them.
- **Who**: `Clients.View`, the same logins that already read the health notes on the card (owner, manager,
  receptionist). The pages are sent with `no-store`.
- **A record is never edited.** A new consultation is a new record, so what the client confirmed on a day stays
  what they confirmed. The same form sent twice (a second click, a reload) is one record: the form carries its id.
- **A patch test on the form moves the date on the card forward**, never back, so the desk sees the newest test
  without opening a form.
- **A merged card's forms follow it**, like its visits and its account (ADR 0017). No form is taken for an
  inactive card or one merged away; their old forms can still be read.
- **Answers that cannot be opened are said to be unreadable**, as the health notes are, instead of shown as empty.
- **The cipher text column has no length.** How long cipher text is depends on the letters typed; the ceilings
  are on what goes in (2,000 characters of details). A test with the longest form in Polish letters found the
  first, sized column too short.

## Not decided here
- **Staff logins do not reach the form**, because they do not open client cards at all today. In a salon where
  the person doing the colour has their own login, the desk or the owner takes the form. A narrow permission for
  staff is its own, Tier 2, change.
- The client filling the form on their own phone before the visit, from a link in the reminder. It needs a public
  page that writes health data and is not built.
- The salon's own questions, or different forms for different treatments. The form has a version so it can change.
- A real signature drawn on the screen. A typed name with a tick and the time is what is kept.
- Warning at the booking or the checkout when a client has a "yes" or no recent patch test.
- How long forms are kept, and deleting them on a client's request. There is no erasure of a client in the system
  yet; when there is, these records go with the card.
- The owner's privacy notice must say that the salon keeps these answers and why. That is a document, not code.

## Consequences
- Migration `ConsultationRecords`: one new table with the tenant policy. No existing row changes.
- `IConsultationService` in `Salon.Services`; slice `Clients/Consultations`; a list on the client card.
- A client merge moves one more table.
- If the encryption keys are lost, the forms cannot be read, like the health notes (ADR 0011).

## Rollback
Revert the PR and run `Down`, which drops the table. Forms taken in the meantime are lost with it, so after the
first real form is taken, revert the pages only and keep the table.
