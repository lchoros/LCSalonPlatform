# 0076. Team logins: who can sign in, managed by the owner

Status: Decided — owner may veto
Date: 2026-11-19
Issue: sign-up (ADR 0075) creates the owner's login; nobody could create the second

## Context

Roles, permissions, lockout and the sign-in flow have existed since ADR 0009, but logins themselves were created
only by the seeder and the test helpers. A real salon's receptionist had no way to get one. The user service
already knew how to create, re-password, re-role and deactivate a login; what was missing was the owner's page.

## Decision

- **`/admin/settings/team`**, a settings tab, owner only (`Settings.Manage`): every login — who, email, role,
  on/off, last sign-in — with "you" marked on the owner's own row. People who take bookings but never sign in stay
  on the Staff pages; the two lists are deliberately separate (no User↔StaffMember link yet).
- **Add a login** (`Identity/CreateLogin`): name, email, role, and a **starting password the owner types and hands
  over in person** — phase 1 has no real email, so there are no invitation emails; the hint says the password is
  the person's to change. A duplicate email, on or off, is refused and points at the edit page instead.
- **Change a login** (`Identity/EditLogin`): role, an optional new password, on/off. Every applied change rotates
  the security stamp, so that person is signed out everywhere (ADR 0009's machinery, unchanged).
- **The salon always keeps at least one active owner login.** The last one can be neither demoted nor turned off:
  the form hides the options and the handler refuses anyway — nobody locks a salon out of itself, including the
  owner by accident. `IUserService` gained `ReactivateAsync` (the email stays unique even for a turned-off login,
  so re-enabling must exist) and `ListAsync`.

## Consequences

- The pilot friend can give their receptionist a login the day they start, and take it away the day they leave —
  the row stays, turned off, with its history of sign-ins.
- A turned-off login fails sign-in exactly like a wrong password: nothing reveals which emails exist.
- Password hand-over is physical; good enough for one salon, revisited when invitations ride on real email at M3.

## Alternatives considered

- Email invitations with a set-your-own-password link: needs real email (M3), and the token flow is a security
  surface the pilot does not need yet.
- Deleting a login instead of turning it off: history (who took which payment) references users; off is honest.
- Linking logins to staff members now: real, but a modelling decision of its own; the page ships without it.