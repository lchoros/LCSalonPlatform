# 0053 — A sale at the counter without a visit

Status: Decided by the owner (2026-11-16: "go with what is most suitable"); Tier 2 because it changes the money
ledger and the payment plugin's request
Date: 2026-11-16

## Context
A product could only be sold on the bill of a visit (ADR 0037). Somebody who walks in off the street for a bottle
of shampoo has no visit and is nobody's client. The desk either invented a client and a visit, or took the money
outside the system. Both spoil the till and the stock.

Three things assumed a visit or a client: a line of the ledger always had a client, the payment plugin's request
always had an appointment, and the stock service sold "on the bill of" an appointment.

## Decision
- **A counter sale is one `ProductSale` line with no client and no visit**, so the till, the takings, the daily
  summary, the reports and the money file count it with no special case. `ClientAccountEntry.ClientId` becomes
  optional.
- **Only that kind of line may lack a client, and the database enforces it**: a check constraint allows a null
  client only for a product sale without an appointment. Any other line without a client is refused, whatever code
  writes it.
- **Its products are rows of a new table**, `counter_sale_lines`, each pointing at the line of money: product, name,
  units, the price and the cost of the day (ADR 0052). A sale is taken in one go, so a line is paid from the moment
  it exists and never changes. The visit's bill lines are left alone.
- **`PaymentRequest.AppointmentId` and the appointment of `IStockService.SellAsync` become optional.** A gateway
  plugin at M3 must accept a payment that belongs to no visit. The Manual plugin only logs the id.
- **The same form sent twice is one sale.** The form carries the id its sale will have; that id is the id of the
  line of money, so a second click or a reload answers with the sale already recorded, and two requests at the same
  moment cannot both be written. A gateway plugin at M3 must use the same id as its idempotency key.
- **One service owns it**, `ICounterSaleService`: the money through the payment plugin, the line, the product rows
  and every unit off its shelf, all in the caller's transaction or not at all.
- **The page** `admin/payments/sale` (`Payments.Record`: owner, manager, receptionist) works without a script: choose
  up to five products and quantities, "Show the total" (a GET: looking records nothing), then "Record the sale" (a
  POST). It is linked from Products and from the till.
- **In the reports** the units, the best sellers and the profit include counter sales, and a row says how many
  sales were made at the counter. "Visits with a product" still counts visits only.

## Not decided here
- **Giving the money back for a counter sale.** Not built: there is no bill to return it from. Until it is, the
  desk refunds from the till by hand and corrects the shelf with a count. This is the first thing to add.
- Putting a counter sale on a client's card when the buyer is a client. A receipt to print or send. A discount.
  More than five different products on one sale (it is taken as two).

## Consequences
- Migration `CounterSale`: `client_account_entries.client_id` becomes nullable (no row changes), one check
  constraint, one new table with the tenant policy.
- **The migration cannot be rolled back while a counter sale exists.** `Down` makes the column required again and
  fails on a line without a client, on purpose: it does not invent a client for money that has none.
- Code that reads a ledger line's client must expect none. Three places did not: the till, the money file and a
  product return. The compiler found them and each is covered by a test.

## Rollback
Before any counter sale is recorded: revert the PR and run `Down`. After: revert the page so no more are recorded,
and keep the schema.
