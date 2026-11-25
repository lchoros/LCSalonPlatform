# 0098. Messages: tags to tap, a live example, and a count of texts

Status: Decided — owner may veto
Date: 2026-11-25
Issue: the owner, on the messages page — "can we have something simple for [message templates] so I have seen like tags available for admin staff to drop, not sure how it should be exactly"

## Context

The edit page of a message (ADR 0030) listed its placeholders as code (`{{ClientFirstName}}`) with a description,
and the person had to type them into the box by hand, double curly brackets and all. The example of what a client
receives only changed after pressing Preview, which sent the form to the server. Someone who has never seen a
placeholder had to learn the syntax before writing a word.

## Decision

- **Tags as chips with plain names.** Above the boxes, each placeholder the message allows is a chip in the page's
  language ("Client's first name", "Service", "Link to change or cancel" …). A tap puts the tag where the cursor was
  in the box last used (the subject or the message of an email, the text of an SMS), with a space where it would
  otherwise touch a word, and the cursor waits after it. The chip keeps the focus off itself, so a phone's keyboard
  stays up. The existing `Settings.Messages.Token.*` keys now hold these plain names.
- **The box keeps the real syntax.** What is saved and what the validator checks do not change; one line explains
  that the coloured words in the example are filled in for each client. No rich-text editor.
- **A live example under each box.** The server writes it on load (and after Preview or a refused save); a small ES
  module (`wwwroot/js/message-tags.js`) rewrites it on every key. Example values are coloured; a tag the message does
  not offer is red with the words "unknown tag". Unknown means exactly what saving refuses
  (`MessageTemplateRules.NotAllowed`): a misspelt name or one belonging to another message. The pieces come from a
  pure `MessagePreviewRenderer` in Salon.Core; the script reads the server's own placeholder pattern from the page,
  so both find the same tags.
- **The examples live in Salon.Services** (`MessagePreviewSamples`, pure): the salon's own name and links, a visit
  tomorrow at ten in the salon's format, kept until two hours before, a code of the real length. `HeldUntil` had no
  example before and came out empty.
- **The SMS count is taken on the example**, after the values are filled in, and says it is an estimate. The script
  counts with the same GSM-7 table as `SmsLengthRules` (160, or 153 each; one character outside the table, a Polish
  letter included, makes it 70, or 67 each); unit tests pin every two-place sign and every Polish letter.
- **Putting back the standard text asks first**, with the no-script `<details class="confirm">` of ADR 0092, and a
  link beside the "Your own wording" badge leads to it.
- **Without script** the chips are disabled buttons that read as a list of each name and how to type it, the hint
  says to type the tag, and the Preview button is the way to a new example. The script switches the chips on and
  hides the Preview button.

## Consequences

- Nothing saved changes shape; the overrides, the validation, the permissions and the list page are as they were.
- The browser and the server could drift in two places, the GSM table and the line-break rules. The table is
  copied with a pointer to its source, and the E2E walk checks that the count the browser showed before saving is
  the count the server shows after a reload.
- A box not used yet takes a tapped tag at its end.

## Alternatives considered

- A rich-text box that shows tags as coloured pills inside the text: a contenteditable editor to keep in step with
  plain text, much more script than the house allows (no new JS framework), and worse on a phone keyboard.
- Drag and drop from chip to box: touch dragging into a textarea needs its own pointer handling. A tap does the same
  job; it can come later if salons ask for it.
- Asking the server for the example on every key (htmx): a round trip per key, and no example while offline. The
  pattern and the sample values are on the page already.
