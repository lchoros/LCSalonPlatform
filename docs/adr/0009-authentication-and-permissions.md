# 0009 — Authentication and permissions

Status: Decided — owner may veto (Tier 2: auth/identity)
Date: 2026-11-11

## Context
The admin side (service catalogue, later staff, calendar, settings) must be reachable only by a salon's own people,
each with the rights their role allows: the owner sees money and settings, a receptionist books and takes payments,
a stylist sees their own calendar. Clients never log in (M1 books with an SMS code). CLAUDE.md puts auth/identity in
Tier 2; ADR 0002 already resolves the tenant from an authenticated user's `tenant_id` claim; the CreateService slice
ships behind a feature flag until this exists.

## Options
1. ASP.NET Core Identity with its EF store — complete, but a new runtime package (`Microsoft.AspNetCore.Identity.
   EntityFrameworkCore`), seven tables shaped for a single-tenant app, and a UI that fights the tenant filter.
2. External identity provider — a Phase 1 "no external services" violation and a cost per salon.
3. Cookie authentication from the shared framework, our own tenant-owned `User` entity, and the framework's
   `PasswordHasher<TUser>` (PBKDF2, also in the shared framework, no package) — small, fits rule 4, swappable later.

## Decision
- **Option 3.** `User : BaseEntity, ITenantEntity` — email (unique per tenant, stored lower-case), display name,
  `PasswordHash`, `Role`, `IsActive`, `FailedLoginCount`, `LockedOutUntil`, `SecurityStamp`, `CreatedAt`,
  `LastLoginAt`. Table `users` with forced RLS. Passwords hashed with `PasswordHasher<User>` (default
  PBKDF2-HMAC-SHA512, 100k iterations in .NET 10); plain passwords never logged or stored.
- **Roles and permissions in code.** `Permissions` (Core) is the catalogue of permission names
  (`Catalog.Manage`, `Staff.Manage`, `Bookings.Manage`, `Clients.View`, `Payments.Record`, `Settings.Manage`,
  `Templates.Manage`, `Users.Manage`); `Role` is `Owner | Manager | Receptionist | Staff`; `RolePermissions` maps each
  role to its permissions (Owner: all; Manager: all but Users.Manage and Settings.Manage; Receptionist: Bookings,
  Clients, Payments; Staff: Bookings only). Per-tenant customisation of the map is an M2 setting, not a table today.
- **Sign-in** issues a cookie (`salon.auth`, HttpOnly, Secure, SameSite=Lax, 8 h sliding, no persistent "remember me"
  in M0) carrying `sub` (user id), `tenant_id`, `name`, `role` and one `permission` claim per permission, plus the
  user's `SecurityStamp`. A cookie-validation hook (`OnValidatePrincipal`) rejects the cookie when the user is
  inactive, missing, or the stamp changed (password change, role change, "sign out everywhere"). Five failed
  attempts lock the account for 15 minutes; login responses never reveal whether the email exists. The login
  endpoint is rate-limited per IP (framework rate limiter: 10 attempts per minute).
- **Tenant binding.** Login lives on the salon's subdomain (`/account/login`), so the cookie is scoped to that host.
  `TenantResolutionMiddleware` additionally requires the `tenant_id` claim to equal the host's tenant; a mismatch is
  treated as not signed in (the cookie is ignored and cleared). The platform domain has no login.
- **`[RequirePermission(name)]`** is an authorization filter in Web.Framework: anonymous → redirect to login with
  `returnUrl` (or 401 for non-HTML requests); authenticated without the permission → 403 page. It composes with
  `[RequireTenant]`. The CreateService slice drops `[FeatureGate("CatalogAdmin")]` and the flag in favour of
  `[RequirePermission(Permissions.CatalogManage)]`; `FeatureGate` stays for future flags.
- **Slices**: `Features/Identity/Login` (GET form, POST with anti-forgery, both languages, mobile first) and
  `Features/Identity/Logout` (POST). Admin pages show the signed-in name and a sign-out button in the layout.
- **Seed**: `DemoSeeder` (Development only) adds an Owner `owner@studio-20.local` with the password from
  `Demo:OwnerPassword` (set in `appsettings.Development.json`; it is a local dev value, not a secret) and a
  Receptionist `desk@studio-20.local` with the same password.

## Consequences
- No external dependency and five columns of security state per user; MFA, password reset by email and "remember me"
  are M2 items (reset needs the email pipeline).
- Changing a role or password invalidates existing sessions through the security stamp on the next request.
- Everything about a user is tenant data: the filter and RLS apply, and a platform operator login is deliberately
  absent (platform admin tooling is M3).
- Tests must prove: wrong password and unknown email look the same, lockout after five failures, a cookie from salon
  A is rejected on salon B's host, a Receptionist gets 403 on `Catalog.Manage`, an Owner gets through, a disabled
  user is signed out on the next request, the anonymous admin URL redirects to login.
