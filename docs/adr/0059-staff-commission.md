# 0059 — Commission per person, and what each person's work leaves the salon

Status: Decided by the owner (2026-11-16: "continue with recommended, I will not veto anything"); Tier 1 (a new
screen and an additive migration). It touches what people are paid, so it is listed in the digest for the owner.
Date: 2026-11-16

## Context
Most salons in England pay the people who do the work a commission: a percent of the services they did, and often
a smaller percent of the products they sold. The owner works it out at the end of the month from the diary and the
till, by hand. The report already showed each person's completed visits, what they were worth and how busy the
person was, but not what they cost or what they left the salon. Booksy and Versum leave this to a spreadsheet or an
extra product. A salon that sees its real profit per person is the point of this platform.

## Decision
- **Two optional rates per person**: a percent of services and a percent of products, 0 to 100 with two decimals.
  Empty means the person is not paid that way. The database refuses a rate outside 0 to 100.
- **Set on one page for the whole team** (`/admin/staff/commission`), not in each person's edit form: the owner sets
  pay in one sitting, and pay stays apart from contact details.
- **Only the owner sets the rates** (`Settings.Manage`). A manager reads the report, so sees the commission due, but
  cannot change any rate, their own included.
- **Services**: the rate applies to the booked price of the person's completed visits in the period, the same
  figure the report already showed as what their work was worth.
- **Products**: the rate applies to the products on the bills of the person's visits, paid in the period, at the
  price sold, for the units the client kept. A product brought back earns nothing. Counter sales have no person and
  earn no commission.
- **Commission is to the penny, half a penny up**, on each total.
- **The report's staff section adds, per person**: products sold on their visits, the commission with the rates it
  used ("Commission (40% services, 10% products)") and what is left after it. A person without a rate shows no
  commission, and the section looks as before.

## Not decided here
- **Rates of the day.** The report uses today's rates for any period, as the working hours already use today's
  rota. A raise in March changes what February's report says. Keeping the rate on each visit when it is completed
  would fix that; it is a column on the visit and its own change.
- Commission on what was paid rather than on the booked price (a discount or a voucher). Tiers ("50% above £3,000
  a month"), a fixed wage or rent a chair, deductions for product used in the service, holiday pay.
- A file of commission for the payroll, a payslip, or a person seeing their own figures.
- Who sold a counter sale.

## Consequences
- Migration `StaffCommission`: two nullable columns on `staff_members` with check constraints. No row changes.
- `CommissionRules` in Core; `StaffLine` gains `Retail`, the rates, `Commission` and `AfterCommission`, set only when
  there is something to show, so existing figures and tests are unchanged.
- New slice `Staff/Commission`; a link on the staff list for the owner.

## Rollback
Revert the PR and run `Down`, which drops the two columns and their checks. Rates entered in the meantime are lost.
