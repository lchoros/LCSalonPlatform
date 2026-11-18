# System design — Salon Platform (as built at the end of M0)

Read with CLAUDE.md. This file describes what exists; `docs/PLAN.md` says what comes next and `docs/adr/` records why.

## 1. Shape
One ASP.NET Core MVC host (`Salon.Web`) serving every salon from one deployment. A salon is a **tenant**; its public
pages live on `{slug}.{base-domain}` (locally `studio-20.localhost`), the bare base domain serves platform pages only.
Layers follow nopCommerce, slices follow vertical-slice architecture (ADR 0001):

```
Salon.Web (host)          Features/<Module>/<UseCase>/  one use case per folder: command/query, validator, handler,
                          controller, Razor views; Localization/{en-GB,pl-PL}/<Module>.json (one file per module and culture, ADR 0006 amendment); Views/ for the host pages
Salon.Web.Framework       AddSalonFramework, tenant resolution middleware, request localization, pipeline behaviours,
                          plugin loader, widget-zone tag helper, Hangfire wiring, filters (RequireTenant, FeatureGate)
Salon.Services            cross-slice services: TenantLookup, SettingService, LocalizationService, cache, events/outbox,
                          schedule tasks, MoneyFormatter, DemoSeeder, SMTP email
Salon.Data                SalonDbContext, EF configurations, migrations, tenant connection interceptor, RLS helper
Salon.Core                entities, Money, events, CQRS contracts, plugin/payment/SMS contracts, SupportedCultures
Salon.Plugin.*            Payments.Manual (record-only), Sms.Log (log + Mailpit) — Phase 1 stand-ins behind interfaces
```
Dependencies point down only; `Salon.Tests.Architecture` fails the build on a violation, on a slice referencing another
slice, on a tenant-owned entity without `ITenantEntity`, and on any type outside `Salon.Services.Events` touching
`OutboxMessage`.

## 2. Request path
1. `TenantResolutionMiddleware` (ADR 0002): an authenticated user's `tenant_id` claim, else the subdomain slug, looked
   up through the cached `ITenantLookup`; unknown or inactive → 404; bare domain → no tenant.
2. `UseSalonLocalization` (ADR 0006): `?culture=` → culture cookie → the tenant's culture → `Accept-Language` (neutral
   `pl` maps to `pl-PL`) → `en-GB`. Only `en-GB` and `pl-PL` exist.
3. MVC routing to a slice controller (`[Route]` attributes). `[RequireTenant]` turns a tenant-less request into a 404;
   `[FeatureGate("Flag")]` hides a slice while its `Features:<Flag>` setting is off.
4. The controller sends a command or query through `IDispatcher` (ADR 0003). Behaviours, in order: logging →
   FluentValidation (all validators for the request, failures aggregated) → tenant guard (a request needs a tenant
   unless it implements `IPlatformRequest`) → transaction (commands only, `IUnitOfWork` over the scoped DbContext).
5. Razor view next to the controller (`FeatureViewLocationExpander`), text through `@T["Key"]`, Tailwind classes,
   no inline scripts. Polish characters ship as UTF-8 (encoder widened to Latin Extended). A view says what to show and
   holds no code of its own (ADR 0043): numbers come from the model, addresses from the controller, times and dates
   from `Salon.Services.Formatting`, status colours from `Salon.Web.Framework.Ui.Badges`.

## 3. Tenant isolation (ADR 0002)
- Every tenant-owned entity implements `ITenantEntity`; `SalonDbContext` applies a named global filter on
  `TenantId == current tenant` and refuses to save a row that belongs to another tenant (`TenantViolationException`).
- `TenantConnectionInterceptor` runs `set_config('app.tenant_id', …)` when a connection opens; every tenant table has a
  **forced** Postgres row-level-security policy keyed on that setting, so a non-superuser role cannot read across tenants
  even with raw SQL. Startup refuses connection strings that disable the pool reset (`No Reset On Close`, `Multiplexing`).
- Background work uses `ITenantScopeRunner` (fresh DI scope with the tenant set before any connection opens).
- Deliberate exception (ADR 0007, Tier 2): `outbox_messages` is platform-level with a nullable `tenant_id`, because
  the dispatcher reads across tenants; consumers still run inside the message's tenant scope.

## 4. Platform services
| Service | Where | Notes |
| --- | --- | --- |
| `ISettingService` (ADR 0004) | Services/Settings | typed classes (`BookingSettings`), one row per property per tenant, cached per tenant, invalidated on save |
| `IStaticCacheManager` | Services/Caching | `CacheKey` with prefixes; `MemoryCacheManager` keeps a key index for prefix removal |
| `ILocalizationService` (ADR 0006) | Services/Localization | embedded `*.Localization.<culture>.json` per assembly, merged at startup, duplicate or unsupported → startup failure, pl-PL → en-GB → key fallback; counted text reads `Key.One` or `Key.Many` through `LocalizationPlurals.Plural` (ADR 0035); `DayFormatter.ToDayHeading` names the weekday first |
| `IEventPublisher` / `IConsumer<T>` (ADR 0007) | Services/Events | publisher writes an `OutboxMessage` on the handler's DbContext (atomic with the command); `OutboxDispatcher` runs consumers per tenant scope with retry (30 s doubling, 1 h cap), dead-letter after 5 attempts, per-row saves; `OutboxDispatchTask` every minute |
| `IScheduleTask` (ADR 0005) | Services/Tasks | Hangfire recurring jobs, one distributed lock per task, Postgres storage schema `hangfire` |
| Plugins | Web.Framework/Plugins | `IPlugin` + embedded `plugin.json`, validated at startup; `IWidgetPlugin` renders into `<widget-zone>` |
| `MoneyFormatter` | Services/Formatting | native currency → culture symbol (`£45.00`, `45,00 zł`), foreign → `ISO amount` |

## 5. Data
PostgreSQL 17, schema `salon`, snake_case names, UUIDv7 ids, `Instant` (timestamptz) for time, `decimal` + ISO
currency for money. Tables so far: `tenants`, `services` (RLS), `settings` (RLS), `users` (RLS, ADR 0009),
`message_templates` (ADR 0008), `staff_members` and `working_hours` (RLS, ADR 0010; `LocalTime` as `time`, local to the
salon), `clients` (RLS, ADR 0011; health notes as Data Protection cipher text), `appointments` and
`appointment_status_changes` (RLS, ADR 0012; GiST exclusion constraint against overlapping active visits of one staff
member, `client_id` FK to `clients` with Restrict), `client_account_entries` (RLS, ADR 0022 and 0023; append-only fee,
settlement, deposit and visit-payment lines, partial unique indexes for one fee, one deposit and one payment per visit,
one settlement per fee and one return per deposit; `appointments.deposit_amount`, `services.deposit_percent` and `clients.deposit_percent` carry the
deposit rules), `cash_ups` (RLS, ADR 0024; one end-of-day count per salon and day), `locations` (RLS, ADR 0027; the first active
row is the salon's main location), `outbox_messages` (no RLS,
see §3). The demo salon also gets three
staff with weekly hours (Anna Kowalska, Tom Reed, Priya Shah), twelve clients and a few appointments over the coming
working days.
Migrations are additive; rollback notes live in each PR. Local: `salon_dev` (app, migrates and seeds `studio-20` on
start in Development) and `salon_test` (tests, reset by Respawn). The demo salon also gets two logins once ADR 0009 lands:
`owner@studio-20.local` (Owner) and `desk@studio-20.local` (Receptionist), password from `Demo:OwnerPassword` in
`appsettings.Development.json` (a local value, not a secret).

## 6. Slices
- `Catalog/ListServices`, `Catalog/CreateService`, `Catalog/EditService`, `Catalog/ManageCategories` — `/admin/services`,
  `/admin/services/new`, `/admin/services/{id}`, `/admin/services/categories`, behind `Catalog.Manage` (ADR 0009,
  0013); `CreateService` publishes `ServiceCreated`.
- `Booking/ListServices` — `/services`, the public price list grouped by category; one tap from the salon home page.
- `Booking/ChooseService`, `Booking/ChooseSlot`, `Booking/EnterDetails`, `Booking/VerifyCode`, `Booking/Confirmation`,
  `Booking/CancelBooking` — the public flow under `/book` (ADR 0014): service → staff or any → day and slot → name and
  phone → 6-digit SMS code → booked, with a signed cancel link (`/book/cancel/{token}`) in the confirmation SMS. Three
  taps to a slot on a phone; server-rendered, no JavaScript needed.
- `Payments/Checkout` also offers the next visit (ADR 0039): for a completed visit, `RebookChoicesQuery` turns the
  salon's `RebookSettings` (weeks) into addresses of the booking form with client, service, person and day filled in.
  `Clients/ClientCard` shows "Book a visit" and "Products bought" (`IClientAccountService.GetPurchasesAsync`).
- `Staff/ListStaff`, `Staff/CreateStaff`, `Staff/EditStaff`, `Staff/EditWorkingHours`, `Staff/TimeOff` — `/admin/staff`,
  `/admin/staff/new`, `/admin/staff/{id}/edit`, `/admin/staff/{id}/hours`, `/admin/staff/{id}/time-off`, all behind
  `Staff.Manage` (ADR 0010, 0015); `CreateStaff` publishes `StaffMemberCreated`, the hours form replaces a person's
  whole weekly rota in one transaction, time off becomes busy intervals for the availability engine.
- `Staff/Commission` — `/admin/staff/commission` behind `Settings.Manage` (owner only, ADR 0059): a services and a
  products percent per active person, both optional. `IBookingService.CompleteAsync` keeps the rates on the visit
  (`Appointment.CommissionRatesKept`); `IReportService` uses them, today's for older visits: `StaffLine.Retail` (the
  products on the bills of the person's visits paid in the period, units kept), `Commission` (`CommissionRules`, to
  the penny) and `AfterCommission`.
- `Staff/ManageCalendarFeed` — `/admin/staff/{id}/calendar-feed` (ADR 0038, `Staff.Manage`): switches a person's
  private calendar address on, replaces it or switches it off (`StaffMember.CalendarFeedToken`).
  `Staff/CalendarFeedFile` — `GET /calendar/{token}.ics`, tenant only and without a sign-in: the person's live and
  past visits (30 days back, a year ahead) written by the pure `CalendarFeedWriter` (RFC 5545), the client as first
  name and initial only, `Cache-Control: private`.
- `Clients/SearchClients`, `Clients/CreateClient`, `Clients/EditClient`, `Clients/ClientCard` — `/admin/clients`,
  `/admin/clients/new`, `/admin/clients/{id}/edit`, `/admin/clients/{id}`, behind `Clients.View` (ADR 0011); the card
  and the edit form are the only read paths that decrypt health notes.
- `Appointments/DayList`, `Appointments/BookForClient` — `/admin/appointments?date=` (one column per bookable staff
  member, status buttons posting to `/admin/appointments/{id}/{confirm|arrived|complete|no-show|cancel}`) and
  `/admin/appointments/new` (client picker, service, staff or any, date, slots from `IAvailabilityService` as small POST
  forms, booked through `IBookingService` as `Source.Admin`), behind `Bookings.Manage` + `Clients.View` because both
  show client names (ADR 0012).
- `Appointments/WithoutOutcome` — `/admin/appointments/without-outcome?page=` (ADR 0034): the visits of earlier days that
  still hold their time (`VisitOutcomeRules`: active status, ended before today began in the salon zone), the latest
  first, with `POST {id}/{came|did-not-come}`. `IVisitOutcomeService` (Services) counts, lists and records through
  `IBookingService`, so a booked visit that "came" gets two history rows and two events in one transaction. The view
  component `VisitsWithoutOutcome` puts the count on the day list and the calendar. Same permissions as the day list.
- `Products/*` — `/admin/products` (ADR 0036): `ListProducts` (`?show=low|not-in-use&q=&page=`, shared list
  components), `SaveProduct` (`new`, `{id}/edit`; `Catalog.Manage`), `ProductCard` (`{id}?page=`: the shelf, four
  forms, the history) and `RecordStock` (`POST {id}/stock/{received|used|written-off|counted}`). `Product` carries the
  running total `StockOnHand`; every change is an append-only `StockMovement` written with it by `IStockService`
  (Services), which refuses a shelf below zero and, through the row version `xmin`, the slower of two simultaneous
  changes. `StockRules` and `ProductRules` (Core) are pure. List, page, deliveries and uses need `Bookings.Manage`;
  counting and writing off need `Catalog.Manage` too.
- `Payments/BillProducts` — `POST /admin/payments/checkout/{appointmentId}/products` and `.../products/{lineId}/remove`
  (ADR 0037): products on a visit's bill. `VisitProductLine` copies name and price when rung up; unpaid lines can be
  changed and removed, paid ones point at their `ProductSale` account line. `IClientAccountService.PayBillAsync`
  settles the visit (`VisitPayment`) and the products (`ProductSale`) with one method and, in the same transaction,
  `IStockService.SellAsync` takes each product off its shelf (`Sold`, with the visit), counting the shelf up first
  when the record reads short. The checkout page shows the lines and the form; same permissions as the checkout.
  `POST .../products/{lineId}/return` (ADR 0040) gives the money back for units of a paid line: a `ProductReturn`
  account line that points at the sale, `ReturnedQuantity` on the bill line and, when the unit can be sold again,
  `IStockService.ReturnAsync` (`Returned`). The payment plugin records it through `RecordRefundAsync`.
- `Appointments/Waitlist`, `Appointments/AddToWaitlist` — `/admin/appointments/waitlist?page=` and `.../waitlist/new?clientId=`
  (ADR 0041): clients waiting for a visit, oldest first, with "Book" (the booking form filled in) and
  `POST {id}/remove`. `IWaitlistService` (Services) lists, counts, adds (one waiting entry per client and service)
  and closes; `WaitlistBookedConsumer` closes a wish when `AppointmentBooked` arrives through the outbox. The view
  component `WaitlistLink` puts the count on the day list and the calendar. Same permissions as the day list.
- `Appointments/Calendar` — `/admin/appointments/calendar?view=day|week&date=&staff=` (ADR 0016): a CSS-grid timetable
  computed by the pure `CalendarLayout` in Services, an appointment card at `calendar/{id}` with the day list's status
  buttons and a move form posting to `calendar/{id}/move` (`IBookingService.MoveAsync`, re-checked through the engine,
  publishes `AppointmentMoved`). Same permissions as the day list. FullCalendar drag-and-drop is deferred until the
  library is vendored.
- `Catalog/StaffOverrides` — `/admin/services/{id}/staff` behind `Catalog.Manage` (ADR 0018): per person "offers",
  duration and price for one service. `IServiceOfferingResolver` (Services) is the single read path; the availability
  engine, `BookAsync`, the public price list ("from") and the public flow all quote through it.
- `Clients/MergeClients` — `/admin/clients/{id}/merge` behind `Clients.View` + `Clients.Merge` (ADR 0017): pick the card
  to keep, confirm, appointments move in one transaction and the duplicate stays as an inactive tombstone. The card
  (`Clients/ClientCard`) shows visit history.
- `Products/ImportProducts` — `/admin/products/import` behind `Catalog.Manage` (ADR 0045): upload a CSV file, check
  a preview kept under a server-side token (`TokenStore<T>`), confirm. `IProductImportProvider` (in
  `Salon.Plugin.Import`) finds the columns by their headings and reads the numbers (`ImportNumbers`);
  `IProductImportService` classifies each row as new, already there or invalid with the product form's rules and adds
  the new ones with their opening count as a line of the stock history. A product the salon has is never changed.
- `Products/ExportProducts` — `GET /admin/products/stock.csv` behind `Catalog.Manage` (ADR 0047): every product with
  what is on the shelf today as a CSV file, under the headings the import reads, so the file of one salon is the
  product list of another. Written by `SalonControllerBase.CsvDownload` like the money file.
- Product costs (ADR 0049): `Product.CostPrice` is kept by `Products/SaveProduct`, shown with the margin by
  `Products/ProductCard` (`ProductRules.Margin`) and as the worth of the shelves by `Products/ListProducts`
  (`ProductRules.Shelf`), read by the import and written by the stock file. The controllers take costs off the page
  for anybody without `Catalog.Manage`.
  When a bill is paid, the cost of the day is kept on each product line (`VisitProductLine.UnitCost`, ADR 0052), and
  the reports show the profit on products from it (`ProductSaleRules.Profit`).
- `Settings/DailySummary` — `/admin/settings/daily-summary` behind `Settings.Manage` (ADR 0050): a switch and a time
  on the salon's clock (`DailySummarySettings`), with what today's email would say. `IDailySummaryService` writes
  the day's numbers from `IReportService` (no client named) and sends them to the active owner logins;
  `DailySummaryTask` runs every five minutes, and a row in `daily_summary_deliveries`, unique per salon and day,
  is claimed before anything is sent.
- `Settings/YourData` — `/admin/settings/your-data` behind `Settings.Manage` (ADR 0051): the promise "the salon owns
  its data" as a page. Counts of clients, visits, money lines and products, and a link to every file: the client
  list, the visits and the money of each year, the stock list. The files belong to their own slices.
- `Clients/ExportClients` — `GET /admin/clients/export.csv` behind `Settings.Manage` and `Clients.Manage`, so the
  owner only (ADR 0051): every client card with contact details, notes, consent and visit counts, merged pointers
  left out. Health notes are never read. Each download is logged with who and how many rows. The client import
  reads the file back.
- `Payments/PartPayment` — `POST /admin/payments/checkout/{appointmentId}/part` behind `Payments.Record` and
  `Clients.View` (ADR 0061): a part of the visit's price paid one way before the rest is paid another.
  `IClientAccountService.PayPartAsync` writes a `PartPayment` line (kind 13) that is less than what is left;
  `VisitBill.Outstanding` counts it like a deposit held, and `PayBillAsync` settles the visit with one
  `VisitPayment` for the rest. Both take a row lock on the visit first, so two requests cannot overpay it.
- The salon's own payment methods (ADR 0060): `Settings/PaymentMethods` at `/admin/settings/payment-methods` behind
  `Settings.Manage` adds, renames and stops using `SalonPaymentMethod` rows and switches the built-in methods on and
  off (`PaymentMethodSettings`; cash is always on). `IPaymentMethodService.OptionsAsync` is the one list every form
  offers, rendered by the `<payment-method-options />` tag helper in `Salon.Web.Framework`; `ResolveAsync` checks a
  posted `PaymentChoice` in `ClientAccountService`, `CounterSaleService` and `GiftVoucherService` before a line is
  written. A line paid by an own method has `Method = Other`, `CustomMethodId` and `MethodName` (a check constraint
  keeps the three together). `TillRules.TotalsByMethod` gives each own method its own line in the till and reports.
- `Clients/Consultations` — `/admin/clients/{clientId}/consultations/new` and `.../consultations/{id}` behind
  `Clients.View` (ADR 0058): the consultation form before a colour and one kept record. `IConsultationService`
  keeps the answers, the details, the patch test day and the name the client typed as one document encrypted with
  `IFieldEncryptor` (`consultation_records.answers_protected`); only it opens them, and only for the record's own
  page. The card (`Clients/ClientCard`) lists a form by day and purpose. The pages are `no-store`; the log has ids
  only. A patch test on a form moves `Client.LastPatchTestOn` forward; `IClientService.MergeAsync` moves the forms.
- `Clients/ImportClients` — `/admin/clients/import` behind `Clients.Manage` and `Features:ClientImport` (ADR 0019):
  upload a Booksy or Versum CSV, preview (new / duplicate / invalid), confirm. Parsing lives in the plugin
  `Salon.Plugin.Import`; the preview is kept server-side under a random token bound to the salon.
- `Settings/SalonDetails` — `/admin/settings/salon-details` behind `Settings.Manage` (ADR 0027): address, phone and
  email of the salon's main location through `ILocationService`. The `SalonContact` view component shows them as
  "Where to find us" on the public service list, details step, confirmation, cancel and change pages (nothing while
  there is no address), and `AppointmentNotifier` ends every booking email with them.
- `Settings/MessageTemplates` — `/admin/settings/messages` and `/{key}/{sms|email}` behind `Templates.Manage`
  (ADR 0030): the salon's own wording of each client message in the tenant's culture through
  `IMessageTemplateService`, placeholders checked by the pure `MessageTemplateRules`, a preview with made-up details
  that is not saved, and the number of texts it costs (`SmsLengthRules`). The Settings tabs show each role only the
  pages it may open; a manager's Settings link leads here.
- `Settings/Notifications` — `/admin/settings/notifications` behind `Settings.Manage` (ADR 0020): reminders on/off, two
  lead times, quiet hours (`NotificationSettings`).
- `Settings/CancellationPolicy` — `/admin/settings/cancellation-policy` behind `Settings.Manage` (ADR 0022): notice
  period in hours and the late-cancellation and no-show fee as a percentage of the booked price
  (`CancellationPolicySettings`; all zero by default, which is "no policy"). Both settings pages share `_SettingsNav`.
- `Settings/NextVisit` — `/admin/settings/next-visit` behind `Settings.Manage` (ADR 0039 amendment): the three
  numbers of weeks the checkout offers for the next visit (`RebookSettings`), 0 leaves one out, three zeros switch
  the offer off; the page shows what the desk will see through `RebookRules.Choices`.
- `Payments/VisitFee` — `/admin/payments/fees/{appointmentId}` behind `Payments.Record` + `Clients.View` (ADR 0022): the
  fee the policy allows for a missed or late-cancelled visit (pure `CancellationPolicy` in Core), a form to add it
  (amount can be lowered), "client paid" (through `IPaymentProvider`, Manual: record only) and "waive" (reason
  required), with who did what. `IClientAccountService` (Services) owns the ledger; the day list redirects here after
  a no-show or late cancellation, the client card lists the fees and what is owed, and the calendar card of a later
  visit says when the client owes something.
- `Payments/VisitDeposit` — `/admin/payments/deposits/{appointmentId}` behind `Payments.Record` + `Clients.View`
  (ADR 0023): the deposit a booking asks for (pure `DepositRules` in Core: client, else service, else salon; snapshot
  on the appointment), a form to record it received and one to record it returned, both through `IPaymentProvider`
  (Manual: record only). The calendar card shows what is due or received; a deposit the salon holds is taken off the
  fee the fee page offers.
- `Payments/Checkout` — `/admin/payments/checkout/{appointmentId}` behind `Payments.Record` + `Clients.View`
  (ADR 0024): the bill of a visit the client turned up for (pure `TillRules` in Core: price less the deposit held),
  the client's open fees ticked onto it, one payment through `IPaymentProvider` (Manual: record only). Paying an
  arrived visit completes it in the same transaction. Linked from the day list, the calendar card and the client's
  history.
- `Payments/CashUp` — `/admin/payments/cash-up?date=` behind the same permissions (ADR 0024): `ICashUpService` adds up
  the day's ledger lines that carry a method (in the salon's zone), per method, with the cash the drawer should hold;
  the day is closed once with the cash counted (`CashUp`), and money recorded later is flagged. "Till" in the nav.
- `Payments/CounterSale` — `/admin/payments/sale` behind `Payments.Record` (ADR 0053): a product sold to somebody who
  walks in, without a visit. Choose products, see the total (GET), record (POST). `ICounterSaleService` records one
  `ProductSale` line with no client and no visit, a `CounterSaleLine` per product with the price and cost of the
  day, and takes the units off the shelves, in one transaction. A check constraint lets only that kind of ledger
  line lack a client. The till, the reports and the files count it like any product sale.
- `Payments/ReturnCounterSale` — `/admin/payments/sales?date=` behind `Payments.Record` (ADR 0054): the counter
  sales of a day, newest first, and `POST .../{saleId}/lines/{lineId}/return`, which gives the money back at the
  price sold as a `ProductReturn` line with no client that points at the sale. `ICounterSaleService.ReturnAsync`
  claims the units in one statement, so two tills cannot return the same unit.
- Gift vouchers (ADR 0057): `Payments/SellVoucher` at `/admin/payments/vouchers/new`, `Payments/Vouchers` at
  `/admin/payments/vouchers` (the list with what the salon owes, a voucher's page, `POST .../{id}/refund`) and
  `Payments/RedeemVoucher` (`POST /admin/payments/checkout/{appointmentId}/voucher`), all behind `Payments.Record`.
  `IGiftVoucherService` records a sale as a `VoucherSale` line with no client, a use as a `VoucherRedeemed` line
  with no method on the client's account, and a refund as `VoucherRefund`. `VisitBill.VoucherApplied` counts toward
  the visit like a deposit held. What is left on a voucher is claimed in one statement.
- `Reports/ExportMoney` — `GET /admin/reports/money.csv?from=&to=` (ADR 0042), same permissions as the report: every
  line of the period that moved money (`IReportService.GetMoneyLinesAsync`, the till's own rule) as a CSV written by
  the pure `Csv` (Core) in the reader's language, list separator and decimal sign, UTF-8 with a byte order mark;
  typed text that starts like a formula is made plain. `Cache-Control: no-store`.
- `Reports/ExportVisits` — `GET /admin/reports/visits.csv?from=&to=` (ADR 0047), same permissions as the report: one
  row per visit that starts in the period, whatever became of it (`IReportService.GetVisitLinesAsync`), written like
  the money file. Client names, no phone numbers, no notes.
- `Reports/Overview` — `/admin/reports?period=` or `?from=&to=` behind `Reports.View` + `Clients.View` (ADR 0026), read
  only: `IReportService` reads the period's ledger lines and visits and adds them up with `TillRules` and the pure
  `ReportRules` in Core (periods, rates, working minutes). Takings per method, kind and day (each day links to its
  till), visits by outcome, fees, completed visits not paid (linked to their checkout), top services, and per person
  revenue and utilisation from the rota less time off. Owner and Manager only. "Reports" in the nav. When a
  product was sold or returned in the period: products sold, given back, units, share of takings and best sellers
  (`RetailReport`, ADR 0044). Under the headline tiles, the days that have happened set against the same
  days one period earlier: total taken, completed visits, no-shows, the change in words (`ReportComparison`,
  `ReportRules.Compare`, ADR 0046). In the takings, each day of the period as a bar (ADR 0048): plain SVG shapes
  laid out by the pure `BarChart` (Core) and written by `TakingsCharts` beside the view; no script, no chart
  library. Periods of 2 to 62 days (`ReportRules.ChartDays`).
- `Booking/JoinWaitlist` — `/book/{serviceId}/waitlist`, `/book/waitlist/verify/{id}`, `/book/waitlist/done`
  (ADR 0055): a visitor who finds no time leaves a name and a number, proves the number with the booking's SMS code
  and is put on the desk's waitlist (ADR 0041) by `IPublicWaitlistService`. `PhoneVerification.Purpose` keeps a
  waitlist code from making a booking and the other way round. Only while `BookingSettings.PublicWaitlist` is on;
  the owner's switch is `Settings/PublicWaitlist` at `/admin/settings/waitlist`.
- Freed times (ADR 0056): `WaitlistTimeFreedConsumer` hears `AppointmentCancelled` and `IWaitlistOfferService` sends
  one `Waitlist.TimeFreed` text with a booking link to the first client waiting for that service, day and person
  (`WaitlistRules.Wants`, `CanBeTold`), when `BookingSettings.TellWaitlistWhenFreed` is on, outside quiet hours and
  only if the time is on the booking page. Nothing is held; the desk's list shows who was texted.
- `Booking/Reschedule` — `/book/change/{token}` (ADR 0021): the client's own link moves the visit through
  `IBookingService.MoveAsync`; inside the policy's notice period it offers no times (ADR 0022), while
  `Booking/CancelBooking` still cancels and states the late fee first. The details page states the policy before the
  client books.

Background work (ADR 0005 schedule tasks, all per salon through `ITenantScopeRunner`): `Outbox.Dispatch` (ADR 0007),
`Booking.CleanupVerifications` (hourly, ADR 0017) and `Notifications.SendReminders` (every five minutes, ADR 0020).
Reminders are computed from the appointment when due and claimed by a unique `appointment_reminders` row before the SMS
is handed over, so a client is never texted twice for one moment.

Lists (ADR 0032): `Paged<T>` and `Pages.Cut` in Core cut any list into pages; `ServiceCatalogue` turns services into
entries (a service, or the variants that share `Service.VariantGroup`) and applies the category filter and the search.
The shared partials `_SearchBox`, `_FilterTiles` and `_Pager` (models and `ListUrl` in `Salon.Web.Framework/Lists`)
render them as GET forms and links, so a filtered page is an address. The booking page and the admin services list
use them. The look is one vocabulary in `Styles/app.css`: five accents (`.accent-1` to `.accent-5`) give cards and
tiles a two-colour border and a soft glow. The admin pages use the same vocabulary one size down (ADR 0033):
`btn-compact`, `field-compact`, `page-title`, `panel`, `list-card`, `empty-state`, `stat`; the menu marks the area
of the page with `nav-link` and `aria-current="true"`. A view takes its classes from `app.css` and does not carry
utility strings of its own for buttons, fields, notices or panels.

Client messages (ADR 0025): each booking message has one rule (`AppointmentBookedNotice`, `AppointmentCancelledNotice`,
`AppointmentMovedNotice` in `Salon.Services/Notifications`) and one consumer per channel, SMS and email, so the outbox
retries a failed channel alone: `outbox_messages.completed_consumers` lists the consumers that are done with an event,
the dispatcher gives every consumer its turn per attempt and skips the finished ones on a retry. `AppointmentNotifier`
renders the template for the channel and hands it to `ISmsSender` or `IEmailSender`; emails are the plain template
text rendered to safe HTML (`EmailBodies`). `NotificationSettings.EmailEnabled` switches the email channel per salon. Every booking
carries a public token, so a booking made at the desk is confirmed like an online one and its client can change or
cancel through the same link (ADR 0029); `NotificationSettings.ConfirmDeskBookings` switches the desk confirmation off. Messages carry the short
address `/v/{token}` (ADR 0031), which forwards to `/book/cancel/{token}`; the token is 16 random bytes (22
characters), and 43-character tokens from before keep working.

## 7. Testing
Unit (xUnit + Shouldly + NSubstitute, FsCheck properties for availability and reminder rules), Architecture (NetArchTest
+ resource parity per module file + outbox isolation + the E2E wait guard), Integration (`WebApplicationFactory` against
local Postgres, Respawn reset, tenant-scope helpers, RLS proven as a non-superuser role), E2E (Playwright, 390 px and
1280 px, axe, screenshots). CI runs all four on `ubuntu-24.04` with a Postgres 17 service container, plus gitleaks on
PRs and CodeQL on `main` (findings fail the job).

E2E rules that keep the suite stable: wait for navigation with `page.ReachAsync(...)`
(`tests/Salon.Tests.E2E/Infrastructure/PageNavigationExtensions.cs`), never Playwright's `WaitForURLAsync`, which can
miss the load event on a cold process (an architecture test enforces this); click the link or button itself, because CI
pages are unstyled; assert whole phrases, never three or four letters that random tokens can contain. To watch a run in
a visible browser: `pwsh scripts/watch-e2e.ps1 -Filter <name>`.

## 8. Not yet built
M1 remainder: several locations per salon (one exists, ADR 0027), FullCalendar drag-and-drop with live updates. M2 leftovers: payment reversals, split
payments, tips, discounts and retail products at the checkout. Reports:
export, charts, comparison with the period before. M3:
payments beyond the record-only provider, real SMS/email, deployment.
