# Manual test: M2 money and messages

For the owner, after PRs #65, #66 and #67 are merged (section L also needs #68, section M #69, section N #72, section O #73, section P #74, section Q #75). About 45 minutes. Tick a box when the step behaves as written.
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

## O. Wording of the messages (after PR #73 is merged too)
- [ ] O1. Settings has a fourth tab, "Messages". It lists five messages; each says "Standard wording".
- [ ] O2. Open "Booking confirmation", "Text". Change the wording, press "Preview": the example shows your words with a
      made-up client, a line "Texts needed: …", and a note that nothing is saved yet.
- [ ] O3. Type `{{Price}}` into the message and press "Save": it is refused and lists what the message can use.
- [ ] O4. Remove it and save. The page says "Your own wording". Book online: the confirmation text in Mailpit uses
      your wording.
- [ ] O5. Type a Polish letter such as "ą" and press "Preview": the page warns that a text now holds 70 characters.
- [ ] O6. Open "Verification code" and remove `{{Code}}`: saving is refused.
- [ ] O7. Press "Use the standard wording again": the standard text is back.

## P. Short links (after PR #74 is merged too)
- [ ] P1. Book online. The link at the end of the confirmation text in Mailpit looks like `…/v/` followed by 22
      characters. Opening it shows your visit with the cancel button.
- [ ] P2. On the Messages page the example of the standard booking confirmation says "Texts needed: 1".
- [ ] P3. A cancel link from a booking made before this change (a long one) still opens its visit.

## Q. The salon's catalogue (after PR #75 is merged too)
The demo data changed. Drop and recreate `salon_dev` first (see "Clean up"), then start the app.
- [ ] Q1. `/book` lists your services under Hair, Brows, Eyelashes, Waxing and Beauty, with a search box, squared
      category tiles and "Page 1 of …" at the bottom. Cards have a thin coloured border, one colour per category.
- [ ] Q2. Tap "Brows": only brow services are left. Tap "All".
- [ ] Q3. Search for "keratin": one card "HAIR WOMEN 14: Keratin" with "£70.00 – £85.00" and six options listed.
      Pick one: the next page names it in full.
- [ ] Q4. On the time page the days are small squares.
- [ ] Q5. "HAIR WOMEN 01: Full Colour" shows "Option 1" and "Option 2". These are placeholders: in the admin, open
      each under Services and type the real name in "What makes this one different". Check the durations too; they
      are my estimates.
- [ ] Q6. Admin, Services: the same search, tiles and pages. A variant says which service it is a variant of.

## R. The look of the admin pages (after PR #76 is merged too)
- [ ] R1. Sign in as the owner. Every page has a thin line of colours on top and a short coloured mark under its
      title.
- [ ] R2. Tap each link of the menu in turn. The link of the area you are in is dark, and only that one. On
      "Calendar" the dark one is "Calendar", not "Appointments".
- [ ] R3. Services, "Add a service": "Services" stays dark.
- [ ] R4. Reports: four tiles on top (taken, visits, no-show rate, not paid). On a wide screen the sections under
      them sit in two columns; on a phone in one.
- [ ] R5. Settings: the four tabs look like the category tiles on the booking page; the open one is dark.
- [ ] R6. On a phone nothing scrolls sideways on Services, Clients, Appointments, Calendar, Till, Reports, Staff and
      Settings.

## S. Visits nobody marked (after PR #77 is merged too)
The demo salon books visits for the coming days only, so make an old one first: book a visit for today, then wait
until tomorrow, or use a visit you left unmarked on an earlier day of testing.
- [ ] S1. Appointments: above the day a yellow line says "1 earlier visit has no outcome yet." with "Review them".
      The calendar shows the same line.
- [ ] S2. "Review them": the visit is listed with its day, time, client, service and person, and two buttons.
- [ ] S3. "Client came": the line "The visit is marked as completed." appears and the visit leaves the list.
      Reports, "Completed visits not paid" now lists it with a Checkout link.
- [ ] S4. With another old visit, "Did not come": it becomes a no-show. If the policy has a no-show fee, the fee page
      opens.
- [ ] S5. With nothing left, the list says "Every past visit has an outcome." and the yellow line is gone from
      Appointments.
- [ ] S6. A visit of today that is over is not on the list: it is on today's day list.

## T. The English wording (after PR #78 is merged too)
Read these as a client and as the desk would. Tell me any sentence that sounds wrong to you: you know your clients.
- [ ] T1. Open `http://studio-20.localhost:5227/` signed out. The page says "Book your appointment online at any
      time." with "Book an appointment" first, "Services and prices" second and "Where to find us" below. Nothing
      about "M1".
- [ ] T2. "Book an appointment" opens "Choose a service" in one tap.
- [ ] T3. Book a visit and type a wrong code four times. The messages count down "4 attempts left", "3", "2" and
      then "You have 1 attempt left."
- [ ] T4. Settings, Deposits and cancellations: set the free hours to 1 and save. On the booking page, step 3 says
      "Free up to 1 hour before the visit." Set it back afterwards.
- [ ] T5. The confirmation text in Mailpit ends "Change or cancel:" and the link. The email starts
      "Hi Olivia," and then "Your …" with a capital.
- [ ] T6. Appointments, Calendar and Till name the weekday: "Monday 16 November 2026".
- [ ] T7. Services, edit a service: the field is "Clean-up time after the service (minutes)" and the list shows
      "10 min clean-up". Nowhere does it say "buffer" or "stylist".
- [ ] T8. Services, Categories: a category with one service says "1 service".
- [ ] T9. Staff, working hours: make two shifts overlap. The message is "The two shifts on this day overlap."
- [ ] T10. Settings, Messages, a text: the note under the length says an emoji or a curly apostrophe makes each
      text hold 70 characters.

## U. Products and stock (after PR #79 is merged too)
An existing `salon_dev` gets seven demo products on the next start, because it has none.
- [ ] U1. The menu has "Products". The list shows seven products, each with a number "on the shelf"; two are
      marked "Low".
- [ ] U2. "Low stock (2)" shows only those two. Search "colour" finds the colour tube. "All" brings the list back.
- [ ] U3. "Add product": name "Hairspray 300 ml", price 9.50, warn at 2, on the shelf today 6. Save. The product's
      page shows 6 and one line "Stock count +6" with your name.
- [ ] U4. "A delivery arrived": 12, note "Invoice 1042". The page says "12 units added. The shelf now holds 18."
- [ ] U5. "Used in the salon": 1. The shelf holds 17.
- [ ] U6. "Used in the salon": 50. Nothing changes and the red line says the record shows only 17.
- [ ] U7. "Count the shelf": 15. The page says the count differed by 2 units and the shelf holds 15.
- [ ] U8. "Write off": 1 without a reason is refused; with "Broken" it is recorded and the reason is in the history.
- [ ] U9. "Edit": untick "The salon still uses this product". The list no longer shows it; "No longer used (1)"
      does.
- [ ] U10. Sign in as `desk@studio-20.local`: the list and the product page open, "A delivery arrived" and "Used in
      the salon" are there, "Count the shelf", "Write off", "Edit" and "Add product" are not.

## V. Selling a product on a visit's bill (after PR #80 is merged too)
- [ ] V1. Mark a visit of today as arrived and open its Checkout. Below the bill there is "Add a product" with the
      products that have a price. "Colour tube" is not in the list.
- [ ] V2. Choose "Shampoo 250 ml", 2, "Add to the bill". The bill shows "Shampoo 250 ml × 2", £25.00, and "Left to
      pay" is the visit plus £25.00. Products still shows the same number on the shampoo's shelf.
- [ ] V3. Add the shampoo once more: the line says "× 3", not a second line. "Take off" removes it; add 2 again.
- [ ] V4. "Record payment" by card. The visit is completed, the line says "Paid: Card at the salon's terminal" and
      has no "Take off".
- [ ] V5. Till: the day lists "Visit paid" and "Products sold" as two lines; the takings are their sum.
- [ ] V6. Products, the shampoo: two fewer on the shelf and the newest line of the history is "Sold −2".
- [ ] V7. Back on the same Checkout, add a "Hair oil 100 ml" and pay cash: a paid visit can still sell a bottle.
- [ ] V8. Count a product's shelf down to 0, then sell one of it on a bill and pay. The history shows "Found at a
      sale +1" and then "Sold −1"; the shelf is 0, not below.
- [ ] V9. Reports, this week: "What it was for" names "Products sold".

## W. A calendar address for staff (after PR #81 is merged too)
- [ ] W1. Staff: each person has a "Calendar" button. The page says the address is switched off and what a visit
      will show: the service, the client's first name and initial, the salon's address.
- [ ] W2. "Switch the address on": a private address appears, ending in `.ics`.
- [ ] W3. Open the address in a private browser window (not signed in): a file downloads or shows text starting
      `BEGIN:VCALENDAR`, with a `SUMMARY` line per visit such as "Cut & blow-dry · Sophie S.". No surname, no phone.
- [ ] W4. Paste the address into Google Calendar on a computer ("Other calendars", "From URL"). The visits show
      after Google has read it; that can take a while.
- [ ] W5. "Make a new address": the old address now answers "not found", the new one works.
- [ ] W6. "Switch the address off": the address answers "not found".

## X. After the visit (after PR #82 is merged too)
- [ ] X1. Pay a visit at the Checkout. Below the bill: "Book the next visit" with "In 4 weeks", "In 6 weeks" and
      "In 8 weeks", each with its date.
- [ ] X2. Tap "In 6 weeks": the booking form opens with the client, the same service, the same person and that day.
      Pick a time and book.
- [ ] X3. A visit that is only booked or arrived does not offer the next visit.
- [ ] X4. Open the client's card: "Book a visit" is under the name and opens the booking form for that client.
- [ ] X5. After a product was sold to the client (section V), the card has "Products bought" with the product, how
      many, the date and the amount. Tapping a row opens that bill.

## Y. Returning a product (after PR #83 is merged too)
- [ ] Y1. Open the Checkout of a visit with a paid product (section V). Under the paid line: "Return this product".
- [ ] Y2. Open it, leave 1, choose "Cash", leave "It can be sold again" ticked, "Record the return". The line says
      "Returned: 1". Products shows one more on that shelf and "Returned by a client" in its history.
- [ ] Y3. Return another with the box unticked: the money is recorded, the shelf does not change.
- [ ] Y4. Try to return more than the client still has: nothing changes and the red line says so.
- [ ] Y5. Till: the day lists "Products returned" as money out; "cash expected" is lower by what you gave back.
- [ ] Y6. A product that is on the bill but not paid has "Take off", not "Return this product".

## Z. The waitlist (after PR #84 is merged too)
- [ ] Z1. Appointments and Calendar have a "Waitlist" button. The list is empty and says how to add somebody.
- [ ] Z2. Open a client's card, "Add to the waitlist": pick a service, leave the days empty, note "mornings only",
      save. The list shows the client with their phone number, the service, "Anyone available" and the note.
- [ ] Z3. The "Waitlist" button on Appointments now carries a 1.
- [ ] Z4. Add the same client for the same service again with a first day: the message says the details were
      updated and the place is kept; the list still has one row.
- [ ] Z5. "Book" on the row opens the booking form with the client, the service and the day. Book a time. Within a
      minute the client is gone from the waitlist.
- [ ] Z6. Add another client and "Take off the list": they are gone at once.
- [ ] Z7. Appointments, "Book", choose a client, a service and a day with no free times: "Put on the waitlist" is
      offered under the message.

## AA. The money lines as a file (after PR #85 is merged too)
- [ ] AA1. Reports, a period with payments: beside "Takings" there is "Download for a spreadsheet".
- [ ] AA2. Tap it: a file named `money-<first day>-to-<last day>.csv` downloads.
- [ ] AA3. Open it in Excel or Google Sheets: one row per payment with date, time, client, what it was for, the
      service, how it was paid, the amount and who recorded it. A deposit or a product returned is a negative amount.
- [ ] AA4. Add up the "Amount" column: it equals "Total taken" on the report for the same period.
- [ ] AA5. A period without any money has no download button.

## AB. The weeks for the next visit (after PR #87 is merged too)
- [ ] AB1. Settings has a "Next visit" tab. It shows 4, 6 and 8, and under them "In 4 weeks", "In 6 weeks",
      "In 8 weeks".
- [ ] AB2. Type 60 in the third field and save: "Weeks must be between 0 and 52." under that field; nothing saved.
- [ ] AB3. Set 5, 0 and 10 and save: the page confirms and shows "In 5 weeks" and "In 10 weeks".
- [ ] AB4. Take a payment for a visit: the paid bill offers the next visit in 5 and in 10 weeks, nothing else.
- [ ] AB5. Set all three to 0 and save: the page says no next visit is offered, and a paid bill has no such step.
      Set them back to 4, 6 and 8.

## AC. Products in the reports (after PR #88 is merged too)
- [ ] AC1. Reports for a period in which no product was sold: there is no "Products sold" section.
- [ ] AC2. Sell two of one product and one of another on a visit's bill and take the payment. Reports, "This month":
      "Products sold" shows what was sold, 3 units, 1 visit with a product, and both products under "Best sellers",
      the one that brought in more first.
- [ ] AC3. "Sold" equals "Products sold" under "What it was for" in the takings.
- [ ] AC4. Return one unit from the paid bill. The section now has "Given back for returns" in red, and "Products
      after returns" is lower by that amount. "Units sold" is unchanged: what was sold stays sold.
- [ ] AC5. "Share of takings" is products after returns divided by "Total taken".

## AD. Importing products from a file (after PR #89 is merged too)
- [ ] AD1. Products has "Import from a file" beside "Add a product". The page shows what a file looks like.
- [ ] AD2. In a spreadsheet make five rows under the headings `Name,Brand,Code,Price,Stock,Low stock at`: three good
      ones, one with the name of a product you already have, one with "twelve" as its price. Save as CSV.
- [ ] AD3. Upload it. The preview says 3 new, 1 already exists, 1 cannot be imported, with the reason under each
      row that is left out. Open Products in another tab: nothing was added yet.
- [ ] AD4. "Import 3 new products". The result says 3 products added and how many units were counted onto the
      shelves.
- [ ] AD5. The three products are on the list with their prices and units. A product's history has one line:
      "Imported from <your file>".
- [ ] AD6. The product you already had kept its price and its stock.
- [ ] AD7. Upload the same file again: nothing new to import, and no button to confirm.

## AE. The report against the period before (after PR #90 is merged too)
- [ ] AE1. Reports, "This month", in a salon with payments this month: under the four tiles there is "Against the
      period before" with the days compared, e.g. the 1st to today against the 1st to the same day last month.
- [ ] AE2. The table has total taken, completed visits and no-shows, each with now, before and the change in words.
- [ ] AE3. With nothing in the month before, the change reads "Nothing before", not a percentage.
- [ ] AE4. "Last month" compares the whole month with the whole month before it.
- [ ] AE5. Dates wholly in the future: the table is not shown.

## AF. Visits and the stock list as files (after the branch `agent/m3-24-more-files` is merged too)
- [ ] AF1. Reports, "This month": the Visits section has "Download the visits". The file opens in Excel with one row
      per visit: date, time, client, service, with whom, minutes, status, booked online or at the salon, price.
- [ ] AF2. The number of rows is the "Visits" total on the report. Cancelled visits and no-shows are rows too.
- [ ] AF3. A period without a visit has no button.
- [ ] AF4. Products has "Download the stock list" beside "Import from a file". The file has every product with what
      is on the shelf today; a product you stopped using says "No" under "In use".
- [ ] AF5. Sign in as the receptionist: Products shows the shelves and no download button.
- [ ] AF6. Upload the downloaded stock file under "Import from a file": every row is "already exists" and nothing
      changes.

## AG. Takings each day as bars (after the branch `agent/m3-25-report-chart` is merged too)
- [ ] AG1. Reports, "This month", in a salon with payments this month: the Takings section has "Takings each day",
      one bar per day of the month. Days without money have no bar but keep their place.
- [ ] AG2. Under the picture: the first and last day, and "Best day: ..., £...". The tallest bar is that day.
- [ ] AG3. Point at a bar with a mouse: it says its day and amount.
- [ ] AG4. Return a deposit on a day without other money: that day's bar hangs red under the line.
- [ ] AG5. "Other dates" with one day, or with more than 62 days: no chart, the figures are still there.
- [ ] AG6. On a phone the picture is as wide as the card and the page does not scroll sideways.

## AH. What a product costs (after the branch `agent/m3-26-cost-prices` is merged too; it has a migration)
- [ ] AH1. As the owner, Products: "On the shelves" shows two sums and says how many products have no cost yet.
- [ ] AH2. Open a product for sale, Edit, type "What it costs you" (less than the price), save. The product's page
      says "Costs you £... You keep £... of each one sold (...%)."
- [ ] AH3. Back on the list the sum "At what it cost you" went up by the cost times the units on that shelf.
- [ ] AH4. Type a cost above the price: the page says each one is sold for less than it costs.
- [ ] AH5. Sign in as the receptionist: the list has no "On the shelves" panel and the product's page no cost.
- [ ] AH6. "Download the stock list" has a Cost column. Import a file with a Cost column: the preview shows
      "costs £..." and the new products keep it.

## AI. The daily summary by email (after the branch `agent/m3-27-daily-summary` is merged too; it has a migration)
- [ ] AI1. Settings has a "Daily summary" tab. It is off, the time is 20:00, and the page shows what today's email
      would say now: takings, visits, tomorrow's bookings, a link to the report. No client is named.
- [ ] AI2. Tick "Send the daily summary", set the time to two minutes ago, save.
- [ ] AI3. Within five minutes Mailpit (http://localhost:8025) has one email per owner login with the same numbers
      as the page showed. The link opens the report of today after a sign-in.
- [ ] AI4. Wait ten more minutes: no second email for today.
- [ ] AI5. Clear the time and save: "Enter a time such as 20:00." and nothing is saved.
- [ ] AI6. Sign in as a manager: Settings has no "Daily summary" tab.
- [ ] AI7. Untick the box and save: nothing is sent the next day.

## AJ. Your data (after the branch `agent/m3-28-your-data` is merged too)
- [ ] AJ1. As the owner, Settings has a "Your data" tab. It says the data belongs to the salon, how many clients,
      visits and products there are, and has a row per year with "Visits" and "Money".
- [ ] AJ2. "Download the client list": the file opens in Excel with a row per client: name, phone, email, notes,
      marketing consent, client since, completed visits, last and next visit.
- [ ] AJ3. A client with health notes on their card: the notes are not in the file.
- [ ] AJ4. Sign in as a manager: Settings has no "Your data" tab, and `/admin/clients/export.csv` is refused.
- [ ] AJ5. The "Visits" and "Money" buttons of a year download that year's files.
- [ ] AJ6. With `Features:ClientImport` on, upload the downloaded client file under Clients, "Import": every row is
      "already exists".

## AK. Profit on products (after the branch `agent/m3-29-product-profit` is merged too; it has a migration)
- [ ] AK1. Give a product for sale a cost (Products, Edit, "What it costs you"). Sell two on a visit's bill and pay.
- [ ] AK2. Reports, this month: "Products sold" has "Profit on products" with an amount and a share, and the
      product in the best sellers says "Profit: £...". It is twice the price less the cost.
- [ ] AK3. Change the product's cost. The profit of the sale already made does not move.
- [ ] AK4. Sell a product that has no cost: the report says how many units were sold with no cost on record.
- [ ] AK5. Return one of the two units: the profit halves.

## AL. A sale at the counter (after the branch `agent/m3-30-counter-sale` is merged too; it has a migration)
- [ ] AL1. Products has "Sell at the counter"; so has the till. As the receptionist you get in; as staff you do not.
- [ ] AL2. Choose a product and 2, "Show the total": the lines and the total are shown. Nothing is recorded yet:
      the till of today has no new line.
- [ ] AL3. Choose how it was paid, "Record the sale": "Sold for £... (Cash)." The form is empty again and the shelf
      in the list of products is two lower.
- [ ] AL4. The till of today has the money as "Counter sale", in the total of its method.
- [ ] AL5. Reports, this month: "Products sold" counts the units and has "Sales at the counter, without a visit".
- [ ] AL6. A quantity of 500, or no product at all: the page says why and nothing is recorded.

## AM. Giving the money back for a counter sale (after the branch `agent/m3-31-counter-sale-return` is merged too; it has a migration)
- [ ] AM1. Sell two of a product at the counter. The page has "Sales taken at the counter, and returns".
- [ ] AM2. That page lists today's counter sales, newest first: time, how it was paid, who took it, the lines.
- [ ] AM3. "Return this product" on a line, 1, "Record the return": "The return has been recorded: £... given
      back." The sale says "Given back: £..." and the line "Returned: 1".
- [ ] AM4. The shelf is one higher again. With the box unticked it stays where it was.
- [ ] AM5. The till of today has the money going out as "Counter sale", "Products returned".
- [ ] AM6. Try to return more than is left: the page says why and nothing is recorded.
- [ ] AM7. The arrow to yesterday shows yesterday's counter sales, or says there were none.

## AN. The waitlist on the booking pages (after the branch `agent/m3-32-public-waitlist` is merged too; it has a migration)
- [ ] AN1. Open the public booking pages and choose a service: under the times there is no offer of a waitlist.
- [ ] AN2. As the owner, Settings, "Waitlist": tick the box and save.
- [ ] AN3. The booking pages now say "No time that suits you? Ask us to tell you when one comes up" under the times.
- [ ] AN4. Fill in a name and a mobile number you have not used, a remark, "Send code". The code is in the log and
      in Mailpit. Enter it: "You are on the list".
- [ ] AN5. As the owner, Appointments, Waitlist: the person is on the list with the remark, marked "Asked online",
      and is a client card now.
- [ ] AN6. Ask again with the same number and another remark: still one entry, with the new remark.
- [ ] AN7. A wrong code says how many tries are left; after five the page offers to start again.
- [ ] AN8. Untick the box in Settings: the offer is gone and the form's address is "not found".

## AO. A freed time is offered to the first person waiting (after the branch `agent/m3-34-waitlist-message` is merged too; it has a migration)
- [ ] AO1. Settings, "Waitlist": tick "Text the first person waiting when a visit is cancelled" and save.
- [ ] AO2. Put a client with a mobile number on the waitlist for a service. Book another client for that service a
      few days ahead, then cancel that visit, during the day.
- [ ] AO3. Within a minute the log and Mailpit have a text to the waiting client: the service, the time, and a link.
      The link opens the booking page of that service on that day, with the time on it.
- [ ] AO4. The waitlist shows "Texted about a freed time on ..." under that client.
- [ ] AO5. Cancel a second visit the same day: that client gets no second text; the next person waiting does.
- [ ] AO6. Settings, "Messages": "A time has come up" is there and its wording can be changed.
- [ ] AO7. Untick the box: a cancellation sends nothing to the waitlist.

## AP. Gift vouchers (after the branch `agent/m3-35-gift-vouchers` is merged too; it has a migration)
- [ ] AP1. The till has "Gift vouchers". The page says what is still to be used: £0.00 to begin with.
- [ ] AP2. "Sell a gift voucher": 50, cash, a name under "Who is it for?". The next page shows the code, £50.00 and
      "Use by" a year from today. The till of today has "Gift voucher sold" £50.00.
- [ ] AP3. Mark a client as arrived for a visit that costs less than £50. At the checkout, "Use a gift voucher",
      type the code (lower case and without the dash also works). The bill shows "Gift voucher −£...", nothing is
      left to pay and the visit is completed. The message says what is left on the voucher.
- [ ] AP4. The voucher's page lists that visit and the amount left. The list shows the smaller amount still to be
      used.
- [ ] AP5. Use the same voucher on a visit that costs more than what is left: the rest is paid by card or cash as
      usual, for the difference only.
- [ ] AP6. Reports, this month: the takings have the £50 once, on the day the voucher was sold. The visit the
      voucher paid for is not under "Completed visits not paid".
- [ ] AP7. Sell another voucher and, on its page, "Give the money back": it says so, and the code is refused at a
      checkout afterwards. A voucher that was used has no such button.
- [ ] AP8. A wrong code at the checkout: "There is no voucher with this code."

## Clean up
The demo data is yours to keep or reset. To start again from a clean demo salon, drop and recreate `salon_dev`; the
app seeds it on the next start. Set the four policy numbers back to 0 if you want the salon without a policy.

## What these steps do not cover (the automated tests do)
- Two people pressing the same button at the same moment (the database refuses the second).
- A mail server that is down: the email is retried and the text is not repeated. Not testable by hand here, because
  in Phase 1 the text itself is delivered to Mailpit.
- Another salon never seeing this salon's money.
