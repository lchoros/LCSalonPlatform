# 0030 — The salon edits the wording of its messages on a page, with a preview and the cost in texts

Status: Decided — owner may veto
Date: 2026-11-13

## Context
ADR 0008 gave every salon its own wording for each message: a `message_templates` row overrides the embedded
default for a key, channel and culture. `IMessageTemplateService` could save and remove such a row since M0, and the
`Templates.Manage` permission has existed since ADR 0009, but there was no page. A salon could not change a word of
what its clients read. Every competitor lets the owner do that, and "does it sound like us" is one of the first
things a salon checks.

## Decision
- **One page under Settings.** `/admin/settings/messages` lists the five messages (confirmation, reminder,
  cancelled, new time, verification code) with their channels and whether the salon uses its own wording.
  `/admin/settings/messages/{key}/{sms|email}` edits one. Behind `Templates.Manage`: Owner and Manager.
- **The salon's language.** Messages go out in the tenant's culture, so that is the wording the page reads and
  writes, whatever language the person at the screen has chosen.
- **Placeholders are listed and checked.** The page names each placeholder the message offers and what it stands
  for. A placeholder the message does not offer is refused next to the field. `MessageTemplateRules` in Core is the
  one definition; the service repeats the check.
- **Some placeholders must stay.** `MessageTemplateDefinition.RequiredTokenNames`: the verification text must keep
  `{{Code}}`, otherwise a client could never finish a booking. Refused by the page and by the service.
- **Lengths.** Subject 200, text 480, email 4000 characters before the placeholders are filled in.
- **A preview with made-up details, without saving.** The page shows what a client would receive: the salon's own
  name and links, a visit tomorrow at ten, a link as long as a real one. "Preview" is a second submit button with
  its own address (`formaction`), so it works without script. What is shown but not saved is marked as such.
- **The cost of a text is shown.** `SmsLengthRules` counts the texts the example needs (GSM 03.38: 160 or 153 each;
  70 or 67 each once one character is outside the basic alphabet) and says so when a letter such as "ą" or an emoji
  is the reason. An estimate, because real names and services differ in length.
- **Back to the standard wording** deletes the salon's row; the standard text is shown next to the button first.
- **A manager gets a Settings link** that leads to this page only, and the Settings tabs show each role only the
  pages it may open.

## Consequences
- **Found while building:** the standard confirmation text with a client's link is about 170 characters, which is
  two texts, not one. The link alone is about 75 characters because the token has 43. Before real SMS (M3) either the
  standard wording gets shorter or the link does (a short code for links in texts). Not changed here; it is a cost
  decision for the owner.
- Only the tenant's culture can be edited. A salon that writes to clients in two languages is not a case we have.
- No history of earlier wordings and no "send me a test message"; the preview is the check.
- The email footer with the salon's address (ADR 0027) is added after the wording and is not part of the preview.
- No migration: the table exists since ADR 0008.

## Alternatives considered
- **A rich text editor for emails.** Needs script and an HTML sanitiser; plain text rendered to safe HTML
  (ADR 0025) is enough for a confirmation.
- **Live preview and a live character counter.** Needs script on a settings page that otherwise has none; a button
  does the same in one tap.
- **Letting unknown placeholders through as text.** A typo such as `{{ClientName}}` would reach every client as an
  empty gap. Refusing it is kinder.

## Rollback
Revert the PR: the page and the manager's link go; saved wordings stay in `message_templates` and keep applying.
The two new service checks (length, required placeholders) go with it.
