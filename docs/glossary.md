# Glossary (EN → PL)

| Term | Polish | Meaning in code |
| --- | --- | --- |
| Salon / Tenant | Salon | `Tenant` — one business account |
| Location | Lokalizacja | `Location` — a physical salon address with phone and email; the first active one is the salon's main location (ADR 0027) |
| Staff member | Pracownik / Specjalista | `StaffMember` — bookable person |
| Service | Usługa | `Service` — e.g. "Women's cut & blow-dry" |
| Processing time | Czas działania (np. farby) | Gap inside a service when the staff member is free |
| Buffer | Przerwa techniczna | Time after a service before the next can start. The screen calls it "clean-up time" (ADR 0035); the code keeps `Buffer` |
| Counted text | Tekst z liczbą | A resource key with two values, `Key.One` and `Key.Many`, read through `Plural(key, count, …)` (ADR 0035) |
| Appointment | Wizyta | `Appointment` — one client visit, one or more items |
| Deposit | Zadatek | Amount asked for at booking (`Appointment.DepositAmount`) and recorded on the client account when received; kept by the salon and counted toward the fee when the client misses the visit or cancels late (zadatek, not zaliczka — different legal meaning in PL) |
| No-show | Nieobecność / Nie przyszedł | Client did not attend |
| Late cancellation | Późne odwołanie | Cancelled inside the policy window |
| Notice period | Termin bezpłatnego odwołania | Hours before a visit until which a change or cancellation is free (`CancellationPolicySettings.FreeChangeHours`) |
| Client account | Rozliczenia klienta | `ClientAccountEntry` lines: fees a client owes and what settled them (payment, waiver); never edited |
| Waiver | Umorzenie | The salon lets a fee go, with a reason |
| Checkout | Rozliczenie wizyty | The bill of a visit: price less the deposit held, plus open fees the client pays now; one recorded payment |
| Till / cash-up | Kasa / zamknięcie dnia | A day's recorded money per method and the end-of-day count of the cash drawer (`CashUp`), closed once |
| Takings | Utarg | Everything that came in on a day less everything that went back out, all methods |
| Utilisation | Obłożenie | Booked time as a share of working time (rota less time off) in a period; unknown without working time |
| Variant | Wariant | A service that shares a title with siblings (`Service.VariantGroup`) and differs by its label (`VariantLabel`): "Keratin · Long hair". Clients see one entry with a price range |
| Product | Produkt | `Product` — something on the salon's shelf: sold to clients (it has a price) or used at the chair ("salon use only") (ADR 0036) |
| Stock movement | Ruch magazynowy | `StockMovement` — one line of a shelf's history: a delivery, used in the salon, written off, or a count; never edited |
| On the shelf | Stan (na półce) | `Product.StockOnHand` — whole units according to the lines; never below zero |
| Stock count | Inwentaryzacja | The number a shelf really holds, typed by a manager; the line is the difference from the record |
| Low stock | Niski stan | At or under the product's warning number (`LowStockAt`), zero included |
| Bill line (product) | Pozycja rachunku (produkt) | `VisitProductLine` — a product rung up on a visit's bill with the name and price it had then; a draft until paid, fixed afterwards (ADR 0037) |
| Product sale | Sprzedaż produktów | `ClientAccountEntryKind.ProductSale` — the money taken for the products on a bill, apart from the visit's payment; "Products sold" in the till and the reports |
| Found at a sale | Znaleziono przy sprzedaży | The count line a sale adds when the record held fewer units than the client took home |
| Placeholder | Znacznik | `{{Name}}` in a message's wording, replaced when the message is sent; each message offers its own list |
| SMS segment | Część SMS-a | One billed text: 160 characters in the basic alphabet, 70 once a letter such as "ą" is used |
| Visit without an outcome | Wizyta bez wyniku | A visit of an earlier day that is still booked, confirmed or arrived: nobody recorded whether the client came (ADR 0034) |
| No-show rate | Odsetek nieobecności | No-shows over visits that had an outcome (completed plus no-shows) |
| Patch test | Test uczuleniowy | Allergy test before colour/lash/brow tint |
| Consultation form | Formularz konsultacyjny | Signed client questionnaire |
| Cash-up | Raport kasowy / Zamknięcie dnia | End-of-day takings check |
| Gift voucher | Bon podarunkowy | Prepaid voucher |
| Rota | Grafik | Staff working schedule |
| Calendar address | Adres kalendarza | A staff member's private address (`/calendar/{token}.ics`) that a calendar app subscribes to; shows their visits one way, the client by first name and initial (ADR 0038) |
