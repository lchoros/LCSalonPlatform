# 0096. A person for each service of a visit

Status: Decided — owner may veto
Date: 2026-11-25
Issue: the owner on the phone booking page, the evening it went live — "you can pick staff for each service, not the
same person for all"; "you pick one service in full, then you are able to add the next service"; and, on the page
itself: no footer when selecting a time, the chosen person a full purple circle, the staff line inside the service's
box with a small photo, Continue always visible and grey, "Add another service" always visible and pale, and a
message when either is tapped too early.

## Context

ADR 0095 made a visit of several services one person's work from start to end. That is the simple case, and it
shuts out the common one: a cut with one person and a colour with another. Nothing in the data stood in the way —
each service of a visit was already an appointment of its own with its own `staff_member_id`.

## Decision

- **Each service has its own answer to "who with"**: a person who offers it, or "anyone available". The staff line
  is inside the service's card, under a thin line, with the person's small face and a button that opens that
  service's own sheet. In the address the first service keeps `staff=`; the further ones ride in `with=`, one answer
  for each service in `also=`, in order (an id, `any`, or `-` where nothing is said yet).
- **A service added to the visit takes the answer of the one before it** when that still fits (the same person, if
  they offer it; "anyone" after "anyone"), so most clients answer once. Where the person before does not offer it
  and there is a choice, the line says "Not chosen yet · Required" and Continue opens its sheet.
- **The times of a visit come from a plan** (`IAvailabilityService.GetVisitPlanAsync`): each part names its service
  and its person or nobody. A start is offered when every part can be filled one after the other. Where the parts
  left open allow one person for all of them, that is taken first; otherwise the person of the part before is tried
  first, so the visit changes hands only where it has to. A part that follows another need not start on a step of
  the clock (`AvailabilityCalculator.IsFree`). One person for every part still goes through the plain engine.
- **Booking**: `BookVisitRequest.StaffMemberIds` names who does each part; `BookVisitAsync` checks the same plan and
  writes the appointments in one batch, as before. The pending row keeps the answers in
  `phone_verifications.extra_staff_ids` (empty id = anyone); "anyone" is settled by the plan when the code is right.
- **One service in full, then the next**: "Add another service" works once a time is tapped and every "who with" is
  answered. Until then it is in sight but pale, and Continue is in sight but grey; a tap on either shows what is
  still missing over the top of the page ("Choose a time first.", "Choose who with first.", or both).
- **On the page**: the chosen face in a sheet is a full circle of the brand, the others white with its edge; the time
  page has no footer.

## Consequences

- Additive migration `VisitStaff`: `phone_verifications.extra_staff_ids` (`uuid[]`, default empty). Rollback drops it;
  pending bookings then book with anyone for their further services.
- **The footer and the privacy notice** (ADR 0063 said every public page links to it). The time page collects nothing
  about the client and now has no footer; the link stays on the service list before it and on the details page that
  asks for the client's name and number, beside the form. The test that pinned "every page" now says exactly this.
  This touches a privacy decision, so the PR is marked for the owner.
- A visit that changes hands needs both people free one after the other, so it has fewer times than either alone.
- Messages name the first part's person (`StaffName`); the services are all named, as before.
- Still not built: moving a visit of several services online; the desk's booking form for several services; a gap
  between the parts of a visit (the client waits) — parts always follow at once.
- Not covered by a test: the plan across a change of clocks, and visits of three or four parts with mixed answers
  (two parts are covered).

## Alternatives considered

- **Ask "who with" again for every added service**: two more taps for the client who wants the same person.
- **Let the engine place the second service by itself with a wait between** (what Booksy does): the owner called it
  out — the client should choose the time, and the parts should follow one another.
- **Chaining the step-aligned slot lists of each service**: a 20-minute first service never lines up with a second.

## Addendum, the same night: what the owner changed after trying it

- **Nobody is picked for an added service.** ("second service should not set auto staff.") The rule above that an
  added service takes the answer of the one before it is withdrawn: its line says "Not chosen yet · Required" like
  the first. Where only one person offers a service there is still nothing to ask.
- **Continue stays grey until a time is tapped and every "who with" is answered.** Tapped too early it does not open
  a sheet any more: with no time it says so at the top of the page; with a time and an answer owed it points at the
  staff line that waits, which lights up gently for a moment.
- **With a time tapped, a service's sheet offers only the people free then.** The visit is planned once more for each
  person in that place, the other answers as they are; a note says whose list this is and that another time shows
  others. Cost: one plan per person per service on each page with a tapped time — a few dozen small queries for a
  salon of three; to be made one pass if a salon with many people feels it.
- **The time page is one screen on a phone**: the days and times hold at the top, the total and Continue at the
  bottom, and the service cards scroll in a space of their own between them, so nothing slides under the button.
  From `sm` the page flows as before with Continue stuck to the bottom over a solid strip.

## Addendum, 2026-11-26

- **The time is chosen again after a service is added** ("do not want to add time automatically for second service,
  user has to select it again"). The way to the service list carries who with and the day, not the time: the visit is
  a different length now, and the client taps its time afresh. Each card shows its length until then.
- **"Add another service" tapped too early has its own words**: "Please complete this service first: choose a time."
  (or who with, or both), apart from Continue's "Choose a time first."
- **Only what a tap changes goes pale** (`data-change` on the page): a day changes the times; an arrow the days and
  the times; a time or a person changes neither, and the line of times stays where the finger left it.
