# 0082 — Stripe in test mode: the deposit of an online booking

Status: Decided — owner may veto (Tier 2: payments, a new runtime dependency)
Date: 2026-11-20
Ordered by the owner on 2026-11-20: "Stripe in test mode, for the deposit at online booking only."

## Context
A booking has known since ADR 0023 which deposit it asks for, and tells the client "the salon will tell you how to
pay it". The salon then chases the money by its own means and the desk records it. The owner wants to see the other
way working end to end: the client pays the deposit by card the moment they book.

Three things frame it. The salon takes its money on the floor, by its own terminal and in cash, and that must not
change. Stripe Connect per salon is the production design and the owner has parked it. And the owner's Stripe
**test** keys already sit in the user-secrets of `Salon.Web` and in the server's configuration.

## Decision

### One thing, in test mode, behind a flag
- **Only the deposit of an online booking.** Nothing else is charged anywhere. `IPaymentProvider` is untouched and
  `ManualPaymentProvider` stays the one way money taken at the salon is recorded.
- **`Features:StripeDeposits`, off by default everywhere.** With it off the product is what it was: same pages,
  same words, same redirects, and the three new addresses do not exist. The shared test hosts run with it off, so
  every earlier test is the proof, and one test says it in so many words.
- **Test mode only, and the code holds itself to that.** The plugin works only with a test-mode key (`sk_test_…` or
  `rk_test_…`). With a live key it reports itself as not set up and the flow behaves as if the flag were off. A
  session Stripe marks as live is never read as paid.
- **This charges on the platform's own Stripe account.** That is right for a test and wrong for a salon's real
  money: the money would arrive in the platform's account, not the salon's. **Stripe Connect per salon remains the
  production design, it remains parked, and none of it is built here.** This spike must not be given a live key.

### New runtime dependency (said loudly)
**`Stripe.net` 53.2.0** — Stripe's own SDK, Apache-2.0, added through `Directory.Packages.props`. It is referenced
by `Salon.Plugin.Payments.Stripe` and by nothing else; one class (`StripeNetGateway`) uses it. It brings
`System.Configuration.ConfigurationManager` 10.0.0 (with `System.Diagnostics.EventLog` and
`System.Security.Cryptography.ProtectedData`) and `Newtonsoft.Json`, which the solution already pins at 13.0.4.
The SDK's own telemetry header is switched off.

### Where things live
- `Salon.Core.Payments.IOnlineDepositCheckout` — a small contract that names no gateway: is it available, open a
  hosted payment page for an amount, say what became of a session, check a webhook. The host always has one:
  `NoOnlineDepositCheckout` (never available) unless a plugin replaces it. The booking slices know only this.
- `src/Plugins/Salon.Plugin.Payments.Stripe` — the plugin. `IStripeGateway` is the two calls made to Stripe
  (create a Checkout Session, get one); `StripeNetGateway` is the real one. Every test host puts a fake there, so
  **no automated test reaches Stripe's network** and none needs a key.
- `IClientAccountService.RecordOnlineDepositAsync` — the one recorder, used by the client's return and by the webhook.
- `Features/Booking/PayDeposit` (the pay step and its return) and `Features/Payments/StripeWebhook`.

### The flow, flag on and a deposit due
1. The details page says the deposit and that it is paid by card once the booking is made.
2. The SMS code is verified and the visit is booked, exactly as before. The client is then sent to
   `/book/pay/{appointmentId}`, which opens a Stripe Checkout Session (hosted page) for **exactly the deposit the
   booking asks for, in the salon's currency**, and sends them there. A booking that asks for no deposit passes
   straight on to its confirmation.
3. **Paid:** Stripe sends the client to `/book/pay/{id}/return?session_id=…`. Coming back proves nothing. The
   session is fetched from Stripe, server to server, and only if Stripe says it is paid, for this salon and this
   visit, is the deposit written. The client lands on the usual confirmation, which now says "Paid. Card online
   (Stripe)". This works without webhooks.
4. **Went back, closed the page, card refused:** the booking stands with its deposit still due, which is today's
   state (ADR 0023). No cancellation rule is added. The confirmation says it is not paid yet and offers "Pay the
   deposit by card", which is the same step again.
5. **Webhook, `POST /webhooks/stripe`,** for the client who pays and never comes back. The signature is verified
   with `Stripe:WebhookSecret` (Stripe's own verifier, five-minute tolerance). A body that does not verify is
   refused with 400 and nothing of it is read. A genuine `checkout.session.completed` only names a session: it is
   then fetched from Stripe and recorded by the same recorder. **Without a webhook secret the address answers 404
   and one warning is logged**; step 3 still confirms payments.

### What goes on the ledger
- One line of the existing kind `DepositPaid`, **held**, for the amount paid, with `ProviderSystemName =
  "Payments.Stripe"`, `ProviderReference =` the Checkout Session id, and no user (nobody at the desk recorded it).
  Fees, the checkout and the client's card read it like any deposit (ADR 0023, ADR 0024).
- **The method says Stripe: a new member `PaymentMethod.OnlineCard`, shown as "Card online (Stripe)".** The enum is
  stored as a number, so there is **no migration**. It is its own line in the till, the cash-up, the reports and the
  money file, and never part of the cash expected in the drawer. **Only a gateway writes it:** it is not in any list
  the desk chooses from, a form that posts it is refused like a method that does not exist, and a line carrying it
  cannot be "corrected" to another method (ADR 0064).
- **Idempotent per Checkout Session id.** The same session reported any number of times (return, reload, webhook,
  webhook again) is one line. The recorder takes the visit's row lock first, so the return and the webhook arriving
  together run one after the other; the unique index "one deposit per visit" (ADR 0023) stays underneath.
- **Only exactly what was asked for is recorded.** Another amount or another currency is not written and is logged
  as an error for a person to look at. So is a payment for a visit that already has a deposit from elsewhere.
- **A deposit paid after the visit was cancelled is still recorded** (with a warning in the log). The salon does
  hold the money, and the desk can only give back what the ledger shows. A cancelled visit is not offered the pay step.

### What Stripe is told, and what the logs say
- Sent to Stripe: the amount in pence or grosze, the currency, one line of text ("Deposit for {service} at
  {salon}"), the two return addresses (they contain the appointment id and nothing else), and our own tenant id and
  appointment id as metadata. **No name, phone or email of the client.** Stripe asks for an email on its own page;
  that is between the client and Stripe.
- Return addresses are built from configuration (`IPublicUrlBuilder`), never from the request's Host header.
- Log lines carry ids, numbers and enums. **Stripe's error messages are never logged or kept**: they can quote the
  end of a key. `StripeOptions` prints only whether a key is set.
- Keys are read through configuration (`Stripe:SecretKey`, `Stripe:WebhookSecret`) and nowhere else. The
  publishable key is not read: the hosted page needs none on our side. No key is in the repository.

### Small print
- Currencies: GBP, PLN and EUR, all a hundred to the unit. Any other is refused rather than guessed.
- An unpaid session closes by itself after 35 minutes (Stripe allows no less than 30).
- A call to Stripe gives up after 15 seconds and is tried once more. If Stripe is away the client goes on to a
  booking that stands with its deposit due.
- The pay step and its return are rate-limited per address (`PublicBooking:DepositPayRateLimit`, 20 an hour).
- The webhook arrives on the platform's address, without a salon. The salon comes from the session Stripe holds
  for our account, and the line is written in a scope of that salon through `ITenantScopeRunner`, like a job. No
  query filter is bypassed.

## Not built
- **Stripe Connect**, onboarding a salon, paying a salon out. Parked by the owner.
- **Refunds through Stripe.** A deposit that goes back is recorded at the desk as before; the money itself is sent
  back by hand in the Stripe dashboard.
- Card on file, charging a no-show fee, paying a bill online.
- A table of sessions. Nothing is stored but the ledger line. So a client who opens two payment pages and pays on
  both is charged twice: the second is not recorded and is logged as an error, to be refunded by hand.
- A screen for any of this in the admin. The switch and the keys are host configuration.
- Texts and emails do not mention the paid deposit.
- The desk's page of a visit's deposit names the method correctly but ends "Recorded by unknown user", because
  nobody at the desk recorded it. A truer sentence is a small change in that slice, left out to keep this one narrow.
- A client who gets a visit another way than the booking form (a freed time from the waiting list, a visit moved)
  is not sent to pay. An online booking's confirmation offers the button whenever its deposit is due.

## Left for the owner
1. **Is this the flow?** If yes, the next step is Connect (the parked design), not a live key here.
2. Should a booking whose deposit is not paid within some time be cancelled? Still not done (ADR 0023, question 1);
   with online payment it becomes a fair rule to have.
3. Before any real client sees this, the privacy notice (ADR 0063) needs a line naming Stripe.
4. The wording "Card online (Stripe)" on the till and the reports.

## Alternatives considered
- **Stripe as an `IPaymentProvider`.** The contract has `SupportsOnlineCharge` for that day. But the client account
  resolves one provider for everything the desk records; a second one would mean choosing a provider per operation
  inside the money services. That is a change to how floor money is recorded, which this order says to leave alone.
- **The Stripe deposit as one of the salon's own methods (ADR 0060)** named "Stripe". No enum change, but it would
  put a row in a list the owner edits, could be renamed or switched off, and could be picked at the desk for cash.
  ADR 0060 already says a real payment account is a plugin, not a row in that list.
- **Believing the return address or the webhook body.** Both name a session; neither is asked whether it was paid.
- **Embedded card fields (Elements).** Card data would pass through our page. The hosted page keeps it off our
  servers and needs no script from us.
- **Webhook only.** Needs a public address and a secret before anything works; the return path works on a laptop.

## Consequences
- A new project in the solution and a new package in the build. No migration.
- Three existing slices changed by a few lines: `EnterDetails` (the sentence about the deposit, and where an
  instant booking goes), `VerifyCode` (where a verified booking goes), `Confirmation` (paid or due). All three ask
  only `IOnlineDepositCheckout.IsAvailable`, which is false unless the flag is on and a test key is set.
- `PaymentMethod` has a sixth member. Code that lists every member must leave it out of what the desk is offered;
  `SalonPaymentMethodRules.IsGatewayOnly` is the one place that says so.
- The E2E fixture can now be extended per feature (`ConfigureHost`), so a flag can be on for one class only.

## Trying it locally
Set `Features:StripeDeposits` to `true` for the Development host, give `Tenancy:PublicPort` the local port so the
return address is right, set a deposit on the policy page, and book. Stripe's test card is 4242 4242 4242 4242. For
the webhook, `stripe listen --forward-to` the local `/webhooks/stripe` prints a signing secret to put in
user-secrets as `Stripe:WebhookSecret`.

## Rollback
- **Switch `Features:StripeDeposits` off.** Immediate; everything is as before.
- Reverting the change removes the plugin, the package and the three addresses. There is no schema to undo. If any
  deposit was recorded by Stripe meanwhile, keep `PaymentMethod.OnlineCard` and its two resource values so those
  lines still read; in test mode there should be none worth keeping.
