# 0056 — A cancelled visit's time is offered to the first client waiting

Status: Decided by the owner (2026-11-16: "continue with recommended, I will not veto anything"); Tier 2 because it
sends messages to clients on its own
Date: 2026-11-16

## Context
The waitlist (ADR 0041) is worked by the desk, and since ADR 0055 clients can put themselves on it. When a visit is
cancelled its time is free again, but somebody has to notice, open the list and call. In a busy salon nobody does,
and the chair stays empty. A marketplace fills that chair with a stranger and takes a commission. Here the salon
fills it from its own waiting clients.

## Decision
- **When a visit is cancelled, by the client or by the salon, the first client waiting for that time gets one
  text**: which service, when, and a link to the booking page of that service on that day.
- **"Waiting for that time" means** the same service, a day inside the days they gave, and the person they asked
  for if they named one (`WaitlistRules.Wants`). The one who asked first comes first, as on the desk's list.
- **Nothing is held.** Whoever books first has the time: the waiting client through the link, somebody else on the
  booking pages, or the desk. Booking it closes their entry as before (ADR 0041). Holding a time for one person
  needs a clock and a second message when it runs out; that is a larger feature and is not built.
- **One person, not the whole list.** A text to everybody waiting sells one time to many people and costs many
  texts. The next cancellation goes to the next in line.
- **Not more than once a day to the same person** (`WaitlistEntry.LastToldAt`, `WaitlistRules.CanBeTold`), so two
  cancellations in an afternoon are not two texts. Then it is the next person's turn.
- **Only a time that can really be booked.** The text is sent only if that time with that person is on the booking
  page at that moment: far enough ahead for the salon's notice period and not taken again.
- **Not at night.** Inside the salon's quiet hours (ADR 0020) nothing is sent, and it is not sent later either: by
  morning the time may be gone. The desk has the list.
- **Not to the client who cancelled**, even if they are also waiting for that service.
- **Off until the owner switches it on**, on the waitlist settings page beside the public form's switch
  (`BookingSettings.TellWaitlistWhenFreed`). Every text costs the salon money.
- **The wording is a message template** (`Waitlist.TimeFreed`, text only), so the owner edits it with the others
  (ADR 0030).
- **Marked before it is sent**, as reminders are (ADR 0020): at worst a text is missed, never repeated. The desk's
  list shows the day a client was last texted.
- **A consumer of its own** of the cancellation event, so the outbox retries it alone and the message to the client
  who cancelled does not depend on it (ADR 0025).

## Not decided here
- Holding the freed time for the person told, and passing it on when they do not take it.
- The same by email. Telling the waitlist about a time freed by moving a visit, or by a change of the rota.
- A text when the desk books somebody else into the time before the waiting client looked.

## Consequences
- Migration `WaitlistToldAt`: one nullable column on `waitlist_entries`.
- One more message template in both languages; two tests that list the templates were extended.
- A waiting client may tap the link and find the time gone. The text says "before it goes" for that reason.

## Rollback
Switch it off on the settings page: nothing more is sent. To remove it: revert the PR and run `Down`, which drops
the column.
