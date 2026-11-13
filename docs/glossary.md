# Glossary (EN → PL)

| Term | Polish | Meaning in code |
| --- | --- | --- |
| Salon / Tenant | Salon | `Tenant` — one business account |
| Location | Lokalizacja | `Location` — a physical salon address |
| Staff member | Pracownik / Specjalista | `StaffMember` — bookable person |
| Service | Usługa | `Service` — e.g. "Women's cut & blow-dry" |
| Processing time | Czas działania (np. farby) | Gap inside a service when the stylist is free |
| Buffer | Przerwa techniczna | Time after a service before the next can start |
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
| Patch test | Test uczuleniowy | Allergy test before colour/lash/brow tint |
| Consultation form | Formularz konsultacyjny | Signed client questionnaire |
| Cash-up | Raport kasowy / Zamknięcie dnia | End-of-day takings check |
| Gift voucher | Bon podarunkowy | Prepaid voucher |
| Rota | Grafik | Staff working schedule |
