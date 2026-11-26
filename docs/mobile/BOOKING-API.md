# The booking API for the client app (`/api/v1/booking`)

Decision record: ADR 0097. The shapes are the records in `src/Libraries/Salon.Api.Contracts/Booking/BookingContracts.cs`;
this page says how they are used. Three lanes build against it at once: the API in `Salon.Web`, the app's logic in
`Salon.Mobile.Core`, and the MAUI app.

## Ground rules

- **The salon is the host**, as on the web: `https://{salon host}/api/v1/booking/...`. No tenant id in any address or
  body. The bare platform domain answers 404.
- **Anonymous.** These are the same things the public web pages do; nothing here needs a sign-in. The per-address rate
  limits of the web flow apply to `start` and `verify` (429 with `ApiProblem`).
- **JSON**, camelCase, `application/json`. Dates `yyyy-MM-dd` and clock times `HH:mm` are in the salon's zone; instants
  are ISO-8601 UTC. The app never converts with the phone's zone.
- **The server decides**, the app shows. Prices, durations, who is free, whether Continue is allowed: all come back in
  the answers. The app holds only what the client chose (`VisitPartRequest` list, the day, the tapped time).
- **Refusals** are `ApiProblem` with status 400 (`validation`), 404 (`notFound`), 409 (`slotTaken`), 429 (`tooManyCodes`).
- Nothing personal is logged; phone and email never appear in an address.

## Endpoints

| Method and path | Body | Answer | What the web equivalent is |
|---|---|---|---|
| `GET /services` | — | `ServiceList` | `/book` (all services, no paging; the app filters and searches on the phone) |
| `POST /availability` | `AvailabilityRequest` | `Availability` | `/book/{service}?staff=&also=&with=&date=&from=&start=` |
| `GET /terms` | — | `BookingTerms` | the notices on the details page |
| `POST /start` | `StartBookingRequest` | `StartBookingResponse` | the details form's POST |
| `POST /verify/{verificationId}` | `VerifyRequest` | `VerifyResponse` | the code form's POST |
| `GET /visits/{appointmentId}` | — | `BookedVisit` | `/book/done/{id}` |

## The flow in the app

1. **Services.** `GET /services` once. Show the list (name, description, price with "from", duration under the price),
   a search box and filter chips: All, Popular (when any `isPopular`), then the category names. Options of one
   service (`variantGroup`) sit under one row that opens.
2. **Time.** `POST /availability` with one part and no staff. Show the month, the days (`hasTimes` false = struck
   through), Morning / Afternoon / Evening, the times, a card per part (name, price, its own time, "Staff: … [Choose]"),
   the total, and Continue (grey until `canContinue`). Every tap — a day, an arrow (`earlierFrom` / `laterFrom`), a
   time, a person — is the same call again with the new choice; the answer replaces the screen's state.
   A part with `needsStaffChoice` shows "Not chosen yet · Required". Its staff sheet lists `staff` (already narrowed to
   who is free at the chosen time) plus "Anyone available".
3. **Add another service** when `canAddService`: back to the service list in "add" mode (what is already in the visit
   is not offered), then availability again with the longer list of parts, the day and time kept.
4. **Details.** `GET /terms`, then the form: first name, last name, mobile number, email (optional), the marketing
   tick with `consentWording`, a link to `privacyUrl`. `POST /start`.
5. **Code.** When `verificationId` comes back: six digits, `POST /verify/{id}`. `wrongCode` shows `attemptsLeft`;
   `slotTaken`, `expired`, `tooManyAttempts` lead back to the time screen.
6. **Done.** `GET /visits/{id}`: what was booked, when, who with, and "Manage this booking" opening `manageUrl` in the
   browser.

## Not in this version

Signing in, a client's list of visits, moving or cancelling inside the app (the manage link does it on the web),
payments, push notifications, the staff diary (ADR 0081 stays as it is for that).
