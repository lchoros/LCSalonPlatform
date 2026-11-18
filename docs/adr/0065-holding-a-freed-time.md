# 0065 — A freed time is kept for the waiting client who was told about it

Status: Decided by the owner's standing word (2026-11-16: "continue with recommended, I will not veto anything";
2026-11-18: "please continue"); Tier 2 because it books a visit on the strength of a link, sends messages on its
own, and adds a tenant-owned table
Date: 2026-11-18

## Context
Since ADR 0056 the first client waiting gets a text when a visit is cancelled. Nothing was held: the text said
"book it before it goes", the link led to the ordinary booking pages (service, time, details, a code by text), and
anybody else could take the time while the client was reading. A text that often ends in "sorry, gone" teaches
clients to ignore the next one. ADR 0056 named holding the time, and passing it on, as not built.

A marketplace fills an empty chair with a stranger. Here the salon fills it from its own waiting clients, and the
client who waited longest gets a fair chance at it.

## Decision
- **The freed time is kept for the client who was told.** A row in `waitlist_offers` says: this time, with this
  person, for this waiting client, until this moment. While it stands and its minutes have not run out, the time is
  busy for everybody else: availability (ADR 0012) counts it like a visit. It is off the public booking pages and
  the desk's booking form alike, so there is one rule and no way round it.
- **For how long is the owner's number**, in minutes, on the waitlist settings page beside the switch for the text
  (`BookingSettings.HoldFreedTimeMinutes`). 30 to begin with; 0 keeps nothing and sends the text of ADR 0056
  unchanged; at most 240. A hold never outlives what it holds: it ends at the last moment the time can still be
  booked online (its start less the salon's notice).
- **The text says until when, and its link books with one tap.** A second template (`Waitlist.TimeHeld`) with a
  short address, `/w/{token}`. The page shows the service, the person, the time, the price and until when it is
  kept, and one button. No name to type and no code: the link went to the client's own phone, which is the same
  proof a visit's link has always been (ADR 0014). The token is 16 random bytes, like a visit's (ADR 0031).
- **Taking it is a booking like any other.** `IBookingService.BookAsync` with the same rules; the visit's
  confirmation goes out, the deposit rules apply, and the booking closes the client's place on the waitlist through
  the event that always did (ADR 0041).
- **Not taken, it goes to the next in line.** A task runs every minute (`Waitlist.PassOnHeldTimes`): an offer whose
  minutes ran out, or whose client stopped waiting (booked another time, or was taken off the list), is ended and
  the same time is offered to the next client by the same rules as the first (ADR 0056: asked for it, not told in
  the last day, not at night, still bookable). When nobody is left the time is simply on the booking pages. The
  hold itself ends by the clock, not by the task: a task that runs late keeps nobody from booking.
- **Never back to whoever gave it up.** The offer remembers the cancelled visit; its client is skipped at every
  pass.
- **The desk sees it and can let it go.** The waitlist shows "kept for this client: when, with whom, until when"
  with a button that ends the offer at once. Nobody else is told then: the desk decided.
- **One time, one client; one client, one time.** Two unique indexes on standing offers: per person and start, and
  per waitlist entry. On top of that each step is one statement that either wins or finds the work done:
  - a client is marked as told with `UPDATE … WHERE last_told_at` is still old, so two cancellations at the same
    moment text one waiting client once (this also closes a gap ADR 0056 had);
  - an offer is taken with `UPDATE … WHERE ended_at IS NULL AND held_until > now`, so five taps book one visit and
    the other four are shown that visit;
  - the clock ends an offer with `UPDATE … WHERE ended_at IS NULL`, so a tap in the same instant either has the
    time or is told it is no longer kept.
  Two race tests let the requests go at the same moment. With the guards taken out they fail every time.
- **A time that cannot be booked after all** (the desk wrote a visit straight into the diary, the rota changed) is
  said to be gone, in words, with a link to the other times. The client stays on the waitlist.
- **Every outcome is kept**: taken, not taken, let go by the desk, gone. An offer is never deleted while the
  waitlist request stands.

## Not decided here
- The same by email; a reminder shortly before the hold runs out.
- Offering a time freed by moving a visit or by a change of the rota (ADR 0056 left these out too).
- A mark on the diary where a time is kept. The desk reads it on the waitlist; the booking form simply does not
  offer the time.
- Telling the client who came too late that the time went to somebody else.

## Consequences
- **Migration `WaitlistOffers`**: one new table with row level security (ADR 0003), two partial unique indexes and
  a unique index on the token. Offers go with their waitlist request (`ON DELETE CASCADE`), so forgetting a client
  (ADR 0062) removes them; the copy of a client's data lists them under the request.
- `IWaitlistOfferService` gains `FindAsync`, `TakeAsync`, `ReleaseAsync`, `PassOnDueAsync`; `WaitlistHoldRules`
  (pure); slices `Booking/TakeHeldTime` and `Appointments/ReleaseHeldTime`; `WaitlistItem.Held`.
- `AvailabilityService` reads standing offers as busy time. One more indexed query per availability request.
- One more message template in both languages; the three tests that list the templates exactly were extended,
  as for ADR 0056.
- The tests of ADR 0056 now say in their set-up that the salon keeps nothing (0 minutes), because the starting
  value is 30. No assertion in them changed.
- A scheduled task that runs every minute for every salon: one indexed query each.
- The privacy notice does not change: nothing new is done with a client's details. The text is the one the
  client asked for when they joined the waitlist, whose form says the salon will call or text that number.

## Rollback
Set the minutes to 0 on the settings page: texts go out as in ADR 0056 and nothing is kept; offers that stand run
out by themselves. To remove it: revert the PR and run `Down`, which drops the table. No other table changes.
