# 0036 — Products and stock

Status: Decided — owner may veto
Date: 2026-11-15

## Context
A salon keeps two kinds of things on its shelves: bottles it sells to clients and tubes it uses up at the chair.
The pilot salon keeps count of both in the system it uses today, so moving here would lose that. Nothing in the
platform knew about products. The owner chose this as the next piece for the English salon.

Selling a product takes money, and money is Tier 2. So this decision covers the shelf only: what the salon stocks,
how many are there, and who changed that. The sale at the checkout is a later, separate change that builds on it.

## Decision
- **A product** (`Product`, table `products`): a name unique in the salon, an optional brand and barcode or supplier
  code, an optional price to clients, an optional warning number, and whether the salon still uses it. No price
  means "salon use only". A product is never deleted; one no longer used leaves the list and keeps its history.
- **Whole units.** A shelf is counted in bottles and tubes. Part-used tubes are not tracked: nobody at a basin
  weighs colour, and a number nobody keeps right is worse than no number.
- **Every change is a line** (`StockMovement`, table `stock_movements`), append-only like a line on a client's
  account (ADR 0022). Four kinds: a delivery, used in the salon, written off (with a reason), and a count. A count
  is the number the shelf really holds; its line is the difference. Each line carries who, when, an optional note
  and what the shelf held afterwards.
- **A running total on the product** (`StockOnHand`), changed only together with a line, by one service
  (`IStockService`). The total is what the list reads; the lines are the proof. An integration test and a property
  test keep "total = sum of the lines" true.
- **Never below zero.** Taking more than the record holds is refused with "count the shelf first". The record can be
  wrong, and the honest way to put it right is a count that shows in the history, not a negative shelf. A check
  constraint is the backstop.
- **Two people at once.** The product row carries Postgres' `xmin` as its version. The slower of two simultaneous
  changes is refused and told the shelf's number as it now is; nothing is lost or counted twice.
- **Low stock** is "at or under the warning number", zero included. A product without a number never warns; one no
  longer used is never low. The list marks low products and has a filter for them. No email, no notice elsewhere
  yet.
- **Who may do what**, with the permissions that exist (no change to roles):
  - See the shelves, record a delivery, record a use: everybody who works the diary (`Bookings.Manage`). The
    person who empties the tube is the one who can say so.
  - Add or change a product, count a shelf, write units off: owner and manager (`Catalog.Manage`). These put the
    books right without anything to show for it.
- **Four slices** under `Features/Products`: the list, the product form (add and change are one use case: "keep a
  product's details"), the product page, and recording stock. They link by address. The kind of a stock change as
  it is written in an address lives in `Salon.Web.Framework`, because one slice builds those addresses and another
  answers them.
- **A new product may start with a count** on the form, so a salon can type its shelf in one pass.
- **Demo data**: seven products with generic names, no real brands (the repository is public), two of them low.

## Not decided here
- **Selling at the checkout** (Tier 2): a product on a visit's bill, the money on the ledger and in the till, the
  unit off the shelf as "sold". `IStockService` is where that will take stock from.
- Suppliers, purchase orders, cost prices and stock value.
- Linking products to services ("a full head colour uses two tubes").
- Importing the product list from the salon's current system.
- A notice or an email when something runs low.

## Consequences
- One additive migration, `ProductsAndStock`: two new tables with row-level security like every tenant table.
- A ninth link in the admin menu, "Products", for everybody who works the diary.
- The list reads all of a salon's products and filters in memory, as the services list does. Fine for hundreds.
- A count made in the same moment as another change is refused, not merged. The desk counts again.

## Rollback
Revert the PR and run the migration's `Down`: it drops both tables. Nothing else refers to them.
