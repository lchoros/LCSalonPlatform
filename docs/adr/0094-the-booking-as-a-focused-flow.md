# 0094. The booking as a focused flow

Status: Decided — owner may veto
Date: 2026-11-22
Issue: the owner, with four screenshots of Booksy's booking — "our main focus will be now to perfect the book process for the client … once you get into the booking you do not see the menu or the salon logo"

## Decision (stage 1, built)

- **Inside a booking the page is only the booking.** The time page drops the salon bar; the footer with the privacy
  notice stays (ADR 0063). Its way out is a close button that asks first — "Discard booking?" with "Continue
  booking" and "Yes, discard" — as a `:target` dialog, no script.
- **The month is said once**, above the days; a day tile holds a weekday, a number and a small green bar when the
  day has times, with room around them. The same tall tile on a phone and a desk.
- **Gentler type** in the flow: slate and semibold instead of black and bold.

## Not built yet (stages 2–4, in the owner's order)

2. The summary card under the times (service, price, the time range once a time is tapped, "Staff: no preference"
   with Change) and the total above Continue.
3. The same focused frame on the details and code pages; the service list with a description under the name and
   softer type; `/services` folded into `/book`.
4. **Several services in one booking** ("Add another service"). Today a visit is one service with one person: this
   changes the availability engine (back-to-back times, possibly different people), the visit model, prices,
   reminders and the till. It needs its own design and is the owner's decision.

## Consequences

- A client cannot wander off mid-booking by accident, and cannot lose a half-made booking with one stray tap.
- We take the pattern, not the look: our brand, our tiles, our words.

## Addendum, 2026-11-22: who with is asked, not assumed (stage 3)

The owner on the time page: "it is not clear to select staff during booking; instead of default all it should not be
selected but a mandatory field."

- Where **more than one person** offers the service, the page opens with nobody chosen. The question "Who with" stands
  in a brand-edged box marked "Required", its answers wrapped so all are in sight; no day, no time and no Continue
  show until it is answered. The summary reads "Staff: Not chosen yet".
- **"Anyone available" is an answer like the others** and travels in the address as `staff=any`; the day strip, the
  times and the way back from the details form keep it. Only a person's id goes on to the details step, as before.
- Where **one person** offers the service there is nothing to ask and the page is as it was.
- A person in the address who is not bookable (or does not offer the service) is no answer: the question again.
- A day asked for in the address (the waiting list's "a time came up" message links to one) is kept through the answer.
- The booking itself is unchanged: this is a rule of the page, not of the engine. A hand-written address to the
  details step without a person still books with whoever is free, as "anyone" always did.
- Cost: one more tap for a client who does not mind who. Taken on purpose, so the choice is seen. The change-a-booking
  page keeps its own picker, where the person the visit is with is already the answer.

## Addendum, 2026-11-22: one page lists the services (stage 4)

The owner: "do we need to list services separate for customers? or just when book is enough?" — and, asked whether to
fold the two together, "yes merge".

- **`/book` is the price list.** `/services` answers with a redirect to it (302, so it can be taken back), and the
  home page, the "no access" page and the privacy notice point at `/book`. The home page has one button, "Book an
  appointment"; "Services and prices" is gone.
- **Nothing the price list said is lost.** `/book` already showed name, description, duration and price; it now also
  says "from £40.00" where the people who offer a service charge differently (ADR 0018), which only `/services` did.
- **The address block** ("Where to find us") was on `/services` and is not wanted on the booking steps: it stays on
  the home page, the confirmation and the messages.
- Cost: a salon's printed or shared `/services` links land on step 1 of a booking, under the heading "Choose a
  service", not on a page called "Services". If a salon wants a plain price list back, the redirect is one line.

## Addendum, 2026-11-25: the time page on a phone, after the owner's screen (stage 5)

The owner sent the phone screen they want ("on mobile please copy one to one") and said where it may differ.

- **The top is one line on a phone**: the month as the title, the two arrows ("we can still keep left right"), the
  way out. "Choose a time" stays as the page's heading for a screen reader. On a desk the title stands above and the
  month sits between its arrows. One set of elements placed by a grid, so nothing is in the page twice.
- **No steps on a phone** ("on mobile we do not need to see steps, on desktop yes"): the step line and its three
  bars are hidden under `sm` on the service, time and details pages.
- **Day tiles are taller on a phone** (80 px by 64 px; 64 by 56 from `sm`).
- **Morning · Afternoon · Evening** as one segmented control, in words instead of clock ranges, and the times of the
  chosen part in **a single line that slides**. From `sm` every part shows under its heading in the grid, as before.
  A small script (`booking-slots.js`) brings the chosen day and time back into sight after a tap, because each tap is
  a new page and a line starts at its beginning; without script the page works, the chosen one may need a slide.
- **Who with is a sheet over the page**, not a row on it ("launching some light modal where you can select staff").
  It is still asked, not assumed (stage 3): while nobody is chosen the sheet is simply open and the days and times
  wait behind it, with "Back to services" as the way out. Afterwards the card says "Staff: … " with **Change**, which
  opens the sheet again; its cross closes it. No script (`:target`).
- **The card** holds the service, its price, the time and the staff line; "Change service" is a quiet link under the
  name; **"+ Add another service"** is a line of its own under the card; the total shows the length beneath it.
- **Changing the day or the month does not move the screen**: the top line and the card hold still and the days and
  times go pale and come back (a view transition; browsers without it navigate as before).

Where this differs from the screen sent, on purpose: the staff question still has to be answered first (the owner's
own rule of three days earlier; one line to change if "No preference" should be the default again), the arrows stay,
and only parts of the day that have a free time get a tab.

Not done yet: the wide layout with the order in a column at the right (the owner's third picture).

## Addendum, 2026-11-25 (evening): staff by its button, no reload, a card per service (stage 6)

The owner tried stage 5 on a phone the same evening.

- **The staff sheet does not open by itself** ("on Booksy you can see the change staff button, so we want that, not a
  modal from the start"). The page opens with the days and the times of anyone who could do the visit; the staff card
  says "Not chosen yet · Required" with a **Choose** button. It is still asked, not assumed: with a time tapped and
  nobody chosen, **Continue opens the sheet** instead of going on, and the answer keeps the day and the time (a time
  that person does not have quietly drops out).
- **The sheet is the whole screen in white, coming up gently, with the people side by side** under their faces. Solid
  white, not a veil: text showing faintly through fails the contrast check.
- **A tap on a day, an arrow, a time or a person no longer loads the page** ("the loading bar at the top is showing
  and the entire screen jumps up and down"). `booking-slots.js` fetches the same address the link points to, puts
  the content it gets into the page, and writes the address without adding a step to the history. While it is on its
  way the days and times go pale, and what arrives comes up from pale. The server decides everything as before;
  without script, or when the fetch fails, the link is simply followed.
- **A grey card for each service** with its own time ("09:45 – 10:30", then "10:30 – 10:50") and a cross at its
  corner that takes it out; who with has a card of its own.
- **"Add another service" comes after a time is chosen** ("I would rather require select time first, then being able
  to add more services" — Booksy puts the second service half an hour later by itself). Until then the page says so.
  The day and the time ride through the service list and back.

Asked for and not built yet: **a person chosen for each service** (now one person does the whole visit, ADR 0095).
That changes how the times of a visit are found and what a booking stores; it is the next step, and the staff line
moves into each service's card with it.

### The service list, the same evening

The owner on the list of services: "smaller text, more padding top and bottom … Booksy has a small grey description,
not fully visible, so something to read it in full when clicking … and they show the time small and grey just under
the price."

- Rows have more room above and below; the name and the price are 14 px.
- How long a service takes moved from under the name to **under the price**, small and grey.
- What the salon wrote about a service shows as **two grey lines under the row with "More"**; a tap opens all of it in
  the same white sheet as "who with", with the price and length, **"← Back to the services"** and a Book button. It is
  a link of its own, outside the row's link, so a tap on the row still books and a tap on the text reads.
- "Book" is on the row on a phone too (it was only shown from `sm`).
- Options of one service (variants) show no description yet.

### No category names over the list (2026-11-25, late)

The owner: "can we remove category names when listing services? … filter yes, but no need to show the category name
on each." The list is one list in the order of the categories; their names are on the filter tiles only. Asked
alongside and not built: a "popular services" filter — it needs a rule for what counts as popular (see the PR).

### "Popular" is the owner's pick; the steps return on a phone (2026-11-26)

- **Popular.** Offered two rules — the most booked in ninety days, or a tick on each service — the owner chose the
  tick: "Show under Popular" on a service's form in the admin (`services.is_popular`, additive migration
  `ServicePopular`). The booking page shows a "Popular" tile beside the categories as soon as one service is ticked
  (`/book?category=popular`); it narrows the list across categories. Nothing is counted or guessed.
- **Steps on a phone.** "Can we add steps back on mobile, but only after selecting": the three bars show on the time
  and details pages on a phone too; the service list, where nothing is chosen yet, stays without them. The line
  "Step 2 of 3" stays for a desk.
