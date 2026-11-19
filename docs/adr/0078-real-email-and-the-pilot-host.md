# 0078. Real email through an authenticated relay, and the pilot host

Status: Decided by the owner
Date: 2026-11-19
Issue: the owner put the platform on a host and wanted real email the same evening

## Context

Phase 1 said "no deployment, no real email" — defaults, not laws. On 2026-11-19 the owner personally chose a .NET
host (MonsterASP), created a hosted PostgreSQL database there, handed over the publish profile and said deploy. The
merged `main` went up the same evening: migrations applied to the hosted database, the app served over HTTPS once
the owner enabled the certificate, and the studio-20 demo salon seeded itself on first start. Hosted PostgreSQL 18
runs the same schema the local 17 does; the row-level security wall (ADR 0002) held even against the database's own
login — without `app.tenant_id` set, tenant rows are invisible.

What could not stay as it was: the SMTP sender spoke only plain, credential-less SMTP (built for the local Mailpit),
and the fake-SMS provider mirrored every "text" to `sms@mailpit.local` — through a real relay that address bounces,
one bounce per text, into the owner's inbox.

## Decision

- **`EmailOptions` gains `Username`, `Password` and `UseStartTls`.** Empty username means the local Mailpit exactly
  as before; a set one means STARTTLS and a login — the submission-port convention every real relay speaks. No new
  dependency: MailKit already does both.
- **The pilot relay is the owner's Gmail with an app password**, configured only in the server's
  `appsettings.Production.json` (never in the repo; the repo's gitleaks scan stands guard). Gmail's ~500/day cap is
  plenty for one salon; a domain address replaces it when the custom domain arrives.
- **The fake-SMS mirror address becomes `Sms:MirrorAddress`** (default: the Mailpit mailbox). On the pilot host it
  points at the owner's own inbox: until a real SMS provider exists (M3), the owner reads every text a client would
  have received. The provider still sends nothing to a phone.

## Consequences

- Receipts and reminder emails now genuinely arrive; "SMS" arrives as email to the owner, labelled with the phone
  number it would have gone to.
- The platform page is public at the host's address, but no salon page is reachable yet: salons live on subdomains
  and the host's free domain has no wildcard DNS. A custom domain with `*.domain` is the next owner purchase.
- Sign-up stays off (ADR 0075) until that domain exists; the demo salon was seeded server-side instead.

## Alternatives considered

- A transactional provider (Postmark/SES) now: the M3 plan, and still right for scale — but it needs an account,
  domain verification and DNS records the pilot does not have yet. Gmail works tonight with zero setup.
- Keeping the SMS mirror pointed at Mailpit and silencing the bounces: hides exactly the messages the pilot most
  needs eyes on.
