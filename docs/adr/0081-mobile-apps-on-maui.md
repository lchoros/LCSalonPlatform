# 0081. Mobile apps for the team, staged: PWA, then a .NET MAUI shell, then native screens

Status: Decided by the owner (2026-11-20)
Date: 2026-11-20
Issue: the owner wants the team's diary in their pocket as a real app on iOS and Android — an appointment portal,
explicitly not a marketplace

## Context

The product is a web application at the pilot host; the admin diary already works on a phone's browser, and ADR 0080
gives mobile its shell (the bottom bar and the drawer). But staff live on their phones, and "open the browser, find
the tab" loses to an icon on the home screen with a push notification when a booking lands or is cancelled. Booksy's
staff app is a large part of why salons stay with it.

One developer maintains everything here. Whatever the mobile answer is, it must reuse what exists — the C# contracts
in `Salon.Core`, the web screens that already work — and never fork the product into a second codebase with its own
truth. There is no marketplace and no online selling in it: the app is the salon's own portal, nothing else.

## Decision (the owner's, 2026-11-20)

**.NET MAUI, in three stages, each shippable on its own:**

- **Stage 0 — PWA.** The web portal becomes installable: a web app manifest and an icon, so "add to home screen"
  gives staff an icon and a standalone window today, with zero new runtime. No service worker yet — the diary must
  never show stale truth from a cache.
- **Stage 1 — a MAUI shell app.** One MAUI project hosting the portal in a WebView, plus the two things a wrapped
  site cannot do: a native login hand-off (sign in once, stay signed in) and **native push notifications** for a new
  or cancelled booking, deep-linking to that day of the diary. The screens stay the web's — one place to fix a bug.
- **Stage 2 — native diary screens** (diary first, checkout as the candidate after) against a future `/api/v1` JSON
  API with token auth. That API touches auth and tenancy, so it is **Tier 2 and decided separately**; Stage 2 waits
  for it.

**Why MAUI and not the others:** one language and one repo — the app cites `Salon.Core` contracts instead of
re-declaring them; the solo maintainer stays in the C#/.NET toolchain he already runs, with no second ecosystem to
patch and no bridge layer to debug.

**Where it lives:** Stage 1 onwards is `src/Mobile/` in this repository, with its own CI job so a mobile break never
blocks a web merge. The MAUI workload is a new runtime dependency, so scaffolding the project is itself **Tier 2** —
this ADR plans it; the owner starts it.

**Store prerequisites (owner purchases):** an Apple Developer membership ($99/year) and a Google Play developer
account ($25 once). iOS builds need macOS: CI uses GitHub's free macOS runners, which this public repository gets at
no cost.

**Recorded for later, decided by the owner when they come up:** room for "Continue with Google" sign-in (Tier 2 —
ADR 0075/0076 stand until then: email and password only), staff profile photos, and nothing sold online ever.

## Consequences

- Stage 0 is a manifest and an icon in `Salon.Web` — Tier 1, done now alongside this ADR.
- Stage 1 begins only when the owner installs the MAUI workload and approves the Tier 2 scaffold; push needs Firebase
  Cloud Messaging for Android and APNs for iOS (the owner's question to settle: those two directly, no third-party
  aggregator to start with).
- A push payload never carries client data — "New booking 14:00", never a name (the spec in `docs/mobile/SPEC.md`
  holds the line).
- Stage 2 is parked behind the Tier 2 `/api/v1` decision; nothing in Stages 0–1 presumes its shape.

## Alternatives considered

- **Flutter:** excellent native UI, but a second language (Dart), a second toolchain and hand-written mirrors of
  every `Salon.Core` contract — the worst trade for a solo C# maintainer.
- **React Native:** the JavaScript ecosystem this project deliberately keeps small (no bundler, no framework — this
  file's rule 8); same contract-mirroring cost as Flutter plus a bridge to debug.
- **PWA-only, no store app:** Stage 0 is exactly this, kept — but iOS web push is still second-class (install-first,
  no reliable deep links into a standalone PWA), and salons look for their tools in the stores. The PWA is the floor,
  not the ceiling.