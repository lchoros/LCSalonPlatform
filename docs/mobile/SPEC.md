# Mobile app spec — the staff appointment portal

Decided direction: ADR 0081 (owner, 2026-11-20). This spec is the working detail; the ADR is the record.

## Audience

Salon staff first: the owner, managers and the team who live on their phones between clients. A **client app is
explicitly out of scope for now** — clients book through the public web pages, which already work on a phone.

## Stages

### Stage 0 — PWA (done with this spec)

- `wwwroot/manifest.webmanifest` + an SVG icon make the admin portal installable: an icon on the home screen, a
  standalone window opening on the diary (`/admin/appointments`).
- No service worker: the diary must never show a stale day from a cache. Offline is not a goal.
- The `<link rel="manifest">` and theme-colour meta in the layout are wired separately (the orchestrator's branch).
- **Follow-up:** PNG icons (192×192 and 512×512, plus maskable variants) — Safari ignores SVG manifest icons, so
  iOS "add to home screen" needs the PNGs (or an `apple-touch-icon`) before the install experience is right there.

### Stage 1 — the MAUI shell app (Tier 2 scaffold; planned here, started by the owner)

Screens and behaviours:

- **WebView shell over the portal.** One screen hosting the admin web app; the web stays the single place screens
  are built and fixed. External links open in the system browser, the portal stays in the shell.
- **Native login hand-off.** Sign in once in the shell; the app keeps the session alive (secure storage for the
  cookie/token, re-authentication only when the server says so). No password ever stored.
- **Push notification for a new or a cancelled booking**, deep-linking to that day of the diary. Tapping "New
  booking 14:00" opens the shell on the diary scrolled to that day.

### Stage 2 — native screens (waits for the Tier 2 `/api/v1` decision)

Candidates, in order: the **diary** (the day at a glance, the everyday screen), then **checkout**. Each native
screen replaces its WebView page only when it is clearly better in the hand, never just to be native.

## Non-goals

- No marketplace, no discovery, no cross-salon anything — the app is one salon's own portal.
- No online payments in the app.
- No client-facing app (for now; the owner decides if ever).
- No online selling.

## Constraints (the platform's rules apply in the app)

- Every string in **en-GB and pl-PL**, through the same resource keys the web uses.
- Times shown in **the salon's zone**, stored as `Instant` — the phone's zone never silently re-labels the diary.
- **No client data in logs or in push payload bodies.** A push says "New booking 14:00" or "Cancelled: 14:00
  tomorrow" — never a client name, phone number or service that could identify a person. The detail lives behind
  the deep link, after sign-in.
- Push tokens are tenant-scoped and removed on sign-out.

## Open questions for the owner

- **Push provider:** start with Firebase Cloud Messaging for Android + APNs for iOS directly, no third-party
  aggregator (OneSignal etc.) — fewer hands on the tokens, two free services. Confirm.
- When to buy the store accounts (Apple $99/year, Google Play $25 once) — needed before Stage 1 ships, not before
  it is built.
- "Continue with Google" sign-in: recorded as a later Tier 2 decision (ADR 0075/0076 stand: email + password only).
- Staff profile photos: later, with the owner's go-ahead.