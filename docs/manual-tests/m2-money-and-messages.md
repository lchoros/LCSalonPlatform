# Manual test: M2 money and messages

For the owner, after PRs #65, #66 and #67 are merged (section L also needs #68, section M #69, section N #72). About 45 minutes. Tick a box when the step behaves as written.
If a step does not, note its number (for example "D4") and what you saw; that is all an agent needs to find it.

Nothing here charges a card or sends a real text or email. Texts and emails land in Mailpit.

## Before you start
1. Merge #65, then #66, then #67. Wait for the green tick on `main` after the last one.
2. In your normal folder: `git pull` on `main`.
3. Build the stylesheet (the pages are unstyled without it):
   `./tools/tailwindcss -i src/Presentation/Salon.Web/Styles/app.css -o src/Presentation/Salon.Web/wwwroot/css/app.css --minify`
4. Start Mailpit: `mailpit`. Inbox: http://localhost:8025
5. Start the app: `dotnet watch --project src/Presentation/Salon.Web`. It applies the four new migrations by itself.
6. Open two browser tabs:
   - Admin: http://studio-20.localhost:5227/admin/appointments
   - Public booking: http://studio-20.localhost:5227/book
7. Logins (password `Studio20!dev`): `owner@studio-20.local` (Owner) and `desk@studio-20.local` (Receptionist).

Good to know while testing:
- A text shows in Mailpit as a message with the subject "SMS to +44…". A real email shows with the client's address.
- Messages after a booking, a move or a cancellation go out within about a minute (the outbox runs every minute).
- When a step says "book online", use a phone number you have not used before in this run, for example
  `07700 900101`, `07700 900102`, and fill in the Email field, for example `test1@example.com`.

## A. Nothing changed for a salon without a policy
- [ ] A1. Sign in as the owner. The menu has a new item "Till" between Calendar and Staff.
- [ ] A2. Settings shows two tabs: "Reminders" and "Deposits and cancellations".
- [ ] A3. On "Deposits and cancellations" all four numbers are 0.
- [ ] A4. In the public tab, pick a service and a time. The details page says nothing about a deposit or cancellation.
      Do not book yet.

## B. Set the rules
- [ ] B1. On "Deposits and cancellations" enter Deposit 20, Free changes 24, Late cancellation fee 50, No-show fee 100.
      Save. You see "The cancellation policy has been saved." and the numbers stay.
- [ ] B2. Enter Free changes 0 with Late cancellation fee 50 and save. It is refused with "A late cancellation fee
      needs a notice period". Put 24 back and save.
- [ ] B3. Enter 150 in any percentage and save. It is refused with "The percentage must be between 0 and 100."
- [ ] B4. Services, open one service, set "Deposit (% of the price)" to 50, save. Open it again: 50 is still there.
- [ ] B5. Clients, open any client, Edit: there is a field "Deposit for this client (% of the price)". Leave it empty.

## C. What the client sees when booking
- [ ] C1. Public tab, start again from /book. Pick a service **other than** the one from B4 and a time **more than
      24 hours** away. The details page shows "A deposit of … secures this visit" with 20% of the price, and a box
      "Changes and cancellation" with three lines (free up to 24 hours, 50%, 100%).
- [ ] C2. Pick the service from B4 instead: the deposit sentence shows 50% of its price.
- [ ] C3. Book the visit from C1 (name, new phone number, email). The code arrives in Mailpit as "SMS to …". Enter it.
- [ ] C4. The confirmation page shows a "Deposit" row with the amount and "The salon will tell you how to pay it."
- [ ] C5. Within a minute Mailpit has two new messages: the confirmation text and an email to the address you typed,
      subject "Your … at Studio 20 is booked". The email has a clickable cancel link.

## D. Deposit at the desk
- [ ] D1. Admin, Calendar, go to the day of the visit from C3 and open it. The card shows a "Deposit" row:
      "£… due, not received" on an amber label, with a link "Deposit details".
- [ ] D2. Open "Deposit details". The amount is filled in. Choose "Bank transfer" and press "Record the deposit".
      You see "The deposit has been recorded.", status "Held", who recorded it and a reference starting `manual:deposit:`.
- [ ] D3. Go back to the visit ("Back to the visit"): the row now says "£… received".
- [ ] D4. Open the client's card (click the name on the deposit page). There is a section "Fees and deposits" with the
      deposit marked "Held" and "Nothing owed".
- [ ] D5. On the deposit page press the browser's Back button to the form and submit it again. It is refused with
      "This visit already has a deposit." Nothing is recorded twice.
- [ ] D6. Trusted client: edit this client, set their deposit to 0, save. Book online again with the **same phone
      number** for another time. The confirmation shows **no** deposit row. Set the field back to empty afterwards.

## E. No-show fee, with the deposit counted
- [ ] E1. Open the visit from C3 again and press "No-show". You land on "Visit fee", not on the day list.
- [ ] E2. The page says the policy sets a no-show fee of 100% and names the full price. Below it: "A deposit of £…
      was taken for this visit and stays with the salon. It counts toward the fee." The amount field shows the price
      **minus** the deposit.
- [ ] E3. Type an amount higher than what is offered and press "Add the fee". It is refused; nothing is added.
- [ ] E4. Put the offered amount back and press "Add the fee to the client's account". Status "Owed", with your name
      and the time under History.
- [ ] E5. The client's card now shows "Owes £…" and the fee in the list.
- [ ] E6. Pick another upcoming visit of a **different** client, mark it "No-show", and on the fee page press "No fee
      this time". You are back on the day list and that client's card shows no fee.
- [ ] E7. On a third no-show (any client), add the fee, then use "Waive the fee" with an empty reason: refused with
      "Say why the fee is waived." Type a reason and waive: status "Waived", the reason and your name in History.

## F. Late cancellation and the online cut-off
- [ ] F1. Book online a visit that starts **less than 24 hours** from now (but more than an hour away). Note: the
      booking itself works as always.
- [ ] F2. In Mailpit open the confirmation text and follow the cancel link. The page warns "this is a late
      cancellation. The salon may charge a fee of £…" (half the price). There is **no** "Change the time instead" button.
- [ ] F3. Change the address from `/book/cancel/…` to `/book/change/…` (same code at the end). The page says the time
      can no longer be changed online and offers no times.
- [ ] F4. Go back and confirm the cancellation. It is cancelled and the page repeats the fee notice. Mailpit gets a
      cancellation text and a cancellation email within a minute.
- [ ] F5. Admin: open that client's card. **No** fee was added by itself. Under Past visits the cancelled visit has
      a small "Fee" link; it opens the fee page with "late cancellation fee of 50%".
- [ ] F6. For the visit from C1 or D6 that is more than 24 hours away: its cancel link shows no warning and still has
      "Change the time instead". Use it to move the visit to another time. Mailpit gets a "new time" text and email.

## G. Checkout
- [ ] G1. Admin, Appointments: book a visit at the desk ("Book") for the client who owes the fee from E4, then go to
      that day and press "Arrived" on it. A dark "Checkout" button appears on that visit.
- [ ] G2. Press "Checkout". The bill shows the price, "Left to pay for this visit", and under "Fees from earlier
      visits" the fee from E4 with a ticked box, plus "Total with all fees".
- [ ] G3. Choose "Cash" and press "Record payment". You see "The payment has been recorded.", the visit status is
      "Completed", "Left to pay" is £0.00 and the page says "This bill is paid."
- [ ] G4. The client's card: "Nothing owed", the fee is "Paid".
- [ ] G5. Deposit comes off the bill: book online a new visit with a deposit, record the deposit (as in D2), mark the
      visit "Arrived", open Checkout. The bill shows the price, a line "Deposit already paid" with a minus, and the
      difference as left to pay. Pay it by "Card at the salon's terminal".
- [ ] G6. Open the checkout of a visit that is only "Booked" (type `/admin/payments/checkout/` and the id from the
      visit's address): it says the client has not arrived yet and offers no payment form.

## H. Till
- [ ] H1. Menu, "Till". Today's totals list each method you used (cash from G3, card from G5, bank transfer from D2
      and G5 if you used it), "Takings" is their sum, and "Cash expected in the drawer" equals the cash line.
- [ ] H2. "Money in and out" lists every entry with time, client, what it was and who recorded it. Check the numbers
      against what you did; they should match to the penny.
- [ ] H3. On any upcoming visit record a deposit by bank transfer, then on the same page "Record the return" in
      cash. Back on the Till the bank transfer total went up and the cash total went **down** by that amount, and the
      list has a red negative line "Deposit returned".
- [ ] H4. Type a counted amount 1.00 lower than expected, add a note, press "Close the day". You see "The day has been
      closed.", a label "Short", the difference and your note.
- [ ] H5. The form is gone. Use the arrow to yesterday and back: the day is still closed with the same numbers.
- [ ] H6. Record one more cash payment today (another checkout). The Till shows an amber note that money was recorded
      after the close and what the drawer should hold now. The count from H4 is unchanged.

## I. Email switch and reminders
- [ ] I1. Settings, Reminders: "Also email clients" is ticked. Untick it and save.
- [ ] I2. Book online with an email address. Mailpit gets the confirmation **text only**, no email to the client.
- [ ] I3. Tick it again and save.
- [ ] I4. Optional, needs patience: book online a visit that starts 2 to 3 hours from now, between 10:00 and 19:00.
      Within about ten minutes after the "2 hours before" moment Mailpit gets a reminder text and a reminder email.
      Exactly one of each, however long you wait.

## J. The receptionist
- [ ] J1. Sign out, sign in as `desk@studio-20.local`. The menu has Clients, Appointments, Calendar and Till, but no
      Settings and no Services.
- [ ] J2. The receptionist can open a fee page, a deposit page, a checkout and the Till, and record on each.
- [ ] J3. Type `/admin/settings/cancellation-policy` in the address bar: you are sent to an "access denied" page.

## K. Polish and phone
- [ ] K1. Add `?culture=pl-PL` to any admin address. Check "Kasa", a fee page ("Opłata za wizytę"), a deposit page
      ("Zadatek za wizytę") and the policy page ("Zadatki i odwołania"). No English sentences are left on them.
      Amounts use a comma (10,00). Add `?culture=en-GB` to switch back.
- [ ] K2. In the browser's developer tools choose a phone width (390 px). Walk through one checkout and the Till:
      nothing scrolls sideways and every button is easy to hit with a thumb.

## L. Reports (after PR #68 is merged too)
Sign out and in again first: the "Reports" link needs a permission your old session does not have.
- [ ] L1. As the owner the menu has "Reports" between Till and Staff. It opens on "This month".
- [ ] L2. "Total taken" and the lines under "By payment method" equal what the Till showed in section H for the same
      day. Open "Day by day" and tap today: you land on today's Till with the same takings.
- [ ] L3. Under "Visits" the numbers match what you did: the no-shows from section E, the cancellation from F.
      "No-show rate" is no-shows divided by completed plus no-shows.
- [ ] L4. Mark a visit "Arrived", then "Complete" **without** the checkout. Reports lists it under "Completed visits
      not paid" with the price. Tap "Checkout", pay it, go back to Reports: it is gone and the takings went up.
- [ ] L5. "Top services" and "Staff" list what was completed. Each person shows "Booked … of … working hours".
- [ ] L6. Tap "Last month", then "Other dates" with a first day after the last day: the page says the dates cannot be
      used and shows this month.
- [ ] L7. Sign in as `desk@studio-20.local`: no "Reports" in the menu, and `/admin/reports` typed by hand goes to
      "access denied".
- [ ] L8. `/admin/reports?culture=pl-PL`: "Raporty", "Utarg", "Obłożenie"; no English left.

## M. Salon address (after PR #69 is merged too)
- [ ] M1. Settings has a third tab, "Salon details". The demo salon already has "20 Example Street, London, N1 9GU"
      and the phone number 020 7946 0020.
- [ ] M2. Clear the address and save: "Enter the address." appears next to the field. Type "ask at the desk" as the
      phone number and save: it is refused with an example. Nothing was changed.
- [ ] M3. Enter your own address, phone number and email and save. You see "The salon details have been saved."
- [ ] M4. Public tab, `/book`: under the services there is "Where to find us" with the address on one line,
      "Open in maps", "Call …" and the email address. "Open in maps" opens a map of the address in a new tab.
- [ ] M5. Book online with an email address. The details page and the confirmation page show the same block. The
      confirmation **email** in Mailpit ends with the salon's name, the address and the phone number. The **text**
      does not mention the address.
- [ ] M6. Open the cancel link from the text: the page offers the phone number to call.
- [ ] M7. As `desk@studio-20.local` there is no Settings menu, and `/admin/settings/salon-details` goes to
      "access denied".

## N. Desk booking confirmation (after PR #72 is merged too)
- [ ] N1. Settings, Reminders: there is a ticked box "Confirm bookings made at the desk".
- [ ] N2. Admin, Appointments, "Book": book a visit for a client who has a phone number and an email address. Within a
      minute Mailpit has a confirmation text and a confirmation email for that client, each with a cancel link.
- [ ] N3. Open the cancel link from that text: it shows the visit and offers "Change the time instead". Move the visit
      to another time. The day list in the admin shows the new time.
- [ ] N4. Untick the box and save. Book another visit at the desk: no confirmation arrives. Book one online: its
      confirmation still arrives. Tick the box again.

## Clean up
The demo data is yours to keep or reset. To start again from a clean demo salon, drop and recreate `salon_dev`; the
app seeds it on the next start. Set the four policy numbers back to 0 if you want the salon without a policy.

## What these steps do not cover (the automated tests do)
- Two people pressing the same button at the same moment (the database refuses the second).
- A mail server that is down: the email is retried and the text is not repeated. Not testable by hand here, because
  in Phase 1 the text itself is delivered to Mailpit.
- Another salon never seeing this salon's money.
