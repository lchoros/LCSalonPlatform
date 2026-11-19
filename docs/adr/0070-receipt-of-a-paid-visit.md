# 0070 — The receipt of a paid visit, on screen and by email

Status: Decided — owner may veto; Tier 2 (money shown to a client, and an email to a client)
Date: 2026-11-19

## Context
A client who pays at the desk often asks for a receipt: to claim it back, to keep track of what a colour costs, or
because they paid part by card and part in cash. The checkout records every payment (ADR 0024, 0037, 0057, 0060,
0061, 0064), but there was nothing to hand the client. The desk wrote it by hand or said no.

A receipt in the UK needs no number or VAT lines while the salon is not VAT-registered, and nothing here is a fiscal
document in Poland (a fiscal till prints its own). What the client needs is what they had, what it cost, how it was
paid, and who the salon is.

## Decision
- **One page per paid visit**, `admin/payments/receipt/{appointmentId}`, linked from the bill once it is paid, behind
  the checkout's permissions (`Payments.Record` and `Clients.View`). A visit that is not paid yet, another salon's
  visit and a missing one are all a 404.
- **Read from the bill, never stored.** `IReceiptService` builds it from `IClientAccountService.GetBillAsync`, so a
  receipt says what the till and the reports say, including a method put right later (ADR 0064):
  - the visit at its price, and the products the client kept: units brought back and given the money for are left
    out;
  - how it was paid, in the order the money came in: the deposit, gift vouchers, each part paid earlier, the payment
    at the checkout, then the products per method (by the name the salon's own method had that day). These add up
    to the total; a test holds that.
  - the salon's name, address, phone and email (ADR 0027).
- **Email on request.** One button sends it to the address on the client's card, in the salon's language, as text
  and HTML like every other email. Nothing is sent without the tap. No button when the card has no address or the
  client was forgotten (ADR 0062); a refusal in words when the salon has switched emails to clients off.
- **Printing is the browser's.** The page hides the menus and buttons when printed. The app keeps no script of its
  own, so there is no print button; the page says to use the browser's Print.
- **Logs** carry the visit's id only.

## Consequences
- A client gets a receipt without the desk writing one.
- A receipt can be sent again and again; it is the same each time until the bill changes (a return).

## Not decided here
- Fees from earlier visits paid on the same bill; their own receipt.
- A receipt for a sale at the counter (ADR 0053) or a gift voucher (ADR 0057).
- Sending it by itself when the bill is paid, or a tick for it on the checkout.
- VAT lines and a receipt number, for a salon that is VAT-registered.
