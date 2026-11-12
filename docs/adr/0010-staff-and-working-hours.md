# 0010 — Staff members and weekly working hours

Status: Decided — owner may veto
Date: 2026-11-11

## Context
The availability engine (M1) needs to know who works in a salon and when. Booksy and Versum both model this as a
person with a weekly rota plus exceptions; salons think of it the same way ("Anna does Tuesday to Saturday, ten till
six"). This is the first M1 slice, so it also sets the shape later Staff slices (time off, breaks, roles, staff-specific
prices) extend. CLAUDE.md rules 4 (tenant-owned data behind the filter and RLS), 5 (times in the location's zone) and
6/7 (resource keys, typed settings) apply; ADR 0002 gives the tenant its `TimeZoneId`, ADR 0009 the `Staff.Manage`
permission.

## Options
1. Store working hours as a JSON blob on the staff row — one column, but no index, no constraint against overlaps and
   awkward queries for "who works on Tuesday".
2. One row per staff member per day with a fixed pair of columns (start, end, break start, break end) — simple, but a
   second shift or a third range needs a schema change.
3. One row per range (`WorkingHours`: staff, day, start, end), several rows per day for split shifts, overlap refused
   by validation and a unique index — a few more rows, every question is a plain query, and exceptions later reuse the
   same range shape.

## Decision
Option 3.
- **`StaffMember : BaseEntity, ITenantEntity`** (`Salon.Core.Staff`): `DisplayName` (≤ 100), optional `Email`
  (≤ 254, lower-case) and `Phone` (≤ 30), optional `UserId` linking to a login of the same salon — a plain column, not a
  foreign key, so a person can exist before or without an account and an account can go without touching the rota —
  `Colour` (`#RRGGBB`, default `#78716C`, used by the calendar later), `IsActive`, `IsBookable`, `SortOrder`,
  `CreatedAt`. Table `staff_members`, forced RLS.
- **`WorkingHours : BaseEntity, ITenantEntity`**: `StaffMemberId` (FK → `staff_members`, cascade), `Day`
  (`IsoDayOfWeek`, stored as int), `Start` and `End` (`LocalTime`, Postgres `time`), `SortOrder`. `End > Start` on the
  same day (no overnight shifts in M1), both on a 5-minute grid, at most two ranges per day, no overlap between the
  ranges of one person on one day; a day without rows is a day off. Table `working_hours`, forced RLS, unique index on
  `(tenant_id, staff_member_id, day, start)`.
- **Times are local to the salon.** A rota says "09:00", not an instant; the availability engine combines it with the
  tenant's `TimeZoneId` per date, so the rota survives daylight-saving changes unchanged. Exceptions (time off, a
  different Saturday), breaks and roles are the next slice and reuse `TimeRange`.
- **Rules live in Core, messages in the slice.** `WorkingHoursRules` (step, end after start, overlap detection, max
  ranges, parsing what `<input type="time">` posts) and `WorkingHoursSummary` (consecutive days with identical ranges
  collapse to "Mon–Fri 09:00–17:00") are pure and unit-tested without a database; the `EditWorkingHours` validator maps
  each failure to a resource key in both cultures. Slices may not reference each other (rule 3), so anything two Staff
  slices need sits in Core.
- **Slices** under `admin/staff`, all `[RequireTenant]` + `[RequirePermission(Permissions.StaffManage)]` (Owner and
  Manager): `ListStaff` (GET `admin/staff`), `CreateStaff` (GET/POST `admin/staff/new`, publishes `StaffMemberCreated`
  through the outbox), `EditWorkingHours` (GET/POST `admin/staff/{id}/hours`: seven days, up to two ranges each, a
  "day off" checkbox; the command replaces every `WorkingHours` row of that person inside the pipeline's transaction).
  Day names come from resource keys (`Common.Day.*`, `Common.DayShort.*`), not `CultureInfo`, so the parity test covers
  them. A minimal admin nav (`Layout.Nav.*`) appears in the layout for signed-in users.
- **Seed.** `DemoSeeder` gives Studio 20 three staff (Anna Kowalska Tue–Sat 10:00–18:00; Tom Reed Mon–Fri 09:00–13:00
  and 14:00–17:00; Priya Shah Mon/Wed/Fri 09:00–15:00) when it creates the salon, and adds the same three to an existing
  demo salon that has no staff yet; the catalogue and logins of an existing salon are never touched.

## Consequences
- Every question the engine asks ("Tom's ranges on Wednesday") is one indexed query; no JSON parsing, no fixed column
  pairs.
- A deleted staff member takes their rota with them (cascade); appointments will reference the staff id with
  `Restrict`, so deletion stops being possible once a person has history — deactivation is the normal path.
- Two ranges per day is a form limit, not a schema limit; raising it later is a constant and a view change.
- `UserId` without a foreign key means a dangling id is possible if a user row is deleted; users are deactivated, not
  deleted (ADR 0009), and the link is informational until staff self-service arrives.
- The hours screen posts seven fixed days and the server ignores ranges of a day marked off, so it works without
  JavaScript; progressive enhancement (disable inputs on "day off") can come with the first shared JS module.
