# 0088. Staff profile photos: faces in the picker and on the staff pages

Status: Decided — owner may veto
Date: 2026-11-20
Issue: the owner, 2026-11-20 — "the way Booksy shows faces"; ADR 0080 lists staff photos as a planned slice

## Context

A client choosing who to book with reads names only. Booksy leans on faces: the same person is easier to pick, and
a salon looks like its people. ADR 0080 left room for avatars and named this slice as "new ground" because nothing
in the product took a binary file from a user or served one back. Three facts shaped it: the pilot host deploys by
syncing the published folder (a file the deploy did not ship is deleted), no image library is among our
dependencies, and a photo of a person is personal data.

## Decision

- **The picture lives in the database**, in its own tenant-owned table `staff_photos` (`StaffPhoto`: staff member,
  content type, bytes, `UpdatedAt`), one row per person (unique index), with the forced `tenant_isolation` policy
  every tenant table carries (ADR 0002). The bytes are never on the `staff_members` row and no list query touches
  them: lists ask `IStaffPhotoService.VersionsAsync` for "who has a picture, and which version", two small columns.
- **Nothing resizes or re-encodes the picture.** It is stored as uploaded and CSS crops it to a circle
  (`object-cover`). The price of that choice is the cap: **512 KB**. The booking page shows every face of the team
  at once, on a phone, so each one has to be small on the wire; with no way to shrink a picture on the server the
  cap is the only control we have.
- **A file is a picture when its first bytes say so**: JPEG (`FF D8 FF`), PNG (the eight-byte signature) or WebP
  (`RIFF….WEBP`). The file name and the browser's content type are never read; the stored content type is the one
  the bytes gave. SVG and GIF are refused (an SVG can carry script). `StaffPhotoRules` in `Salon.Core` holds the
  rule; the refusal is a localized sentence on the staff edit page.
- **Upload and removal are their own slice**, `Staff/SetPhoto` (`POST /admin/staff/{id}/photo` and
  `…/photo/remove`), behind `Staff.Manage` and anti-forgery. Its form sits on the staff edit page through the
  `StaffPhotoForm` view component (a slice may not reference another), and it answers by redirecting back there
  with a confirmation or the reason (`SetProblem`).
- **Serving is a public GET**, `Staff/ShowPhoto` at `/staff-photo/{staffMemberId}?v={version}` on the salon's
  host. It reads through the tenant filter, so another salon's id is a 404. An anonymous visitor is given the
  picture of an **active** staff member only; a signed-in user of the salon also sees inactive people (the staff
  list shows them). The response carries the stored content type, `X-Content-Type-Options: nosniff` and
  `Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; sandbox`, so a file that passed the
  signature check but hides something else is still only ever treated as an image.
- **Caching is keyed by the version.** The version is `UpdatedAt` in Unix milliseconds — a timestamp and not a
  counter, so removing a picture and uploading another can never reuse an address a browser already holds. A
  request whose `v` is the current version is answered `public, max-age=31536000, immutable`; any other (`v`
  missing or stale) gets the current picture with `no-cache` and an ETag (so asking again costs a `304`). A
  signed-in user's copy is `private` instead of `public` (an answer to a request that carried the sign-in cookie
  may carry a renewed one), and a picture of an inactive person is `private, no-cache`.
- **One avatar everywhere**: the `<avatar>` tag helper renders the photo as a circle or, when there is none, the
  person's first letter on `bg-brand-soft text-brand` (the look of `.service-dot`). It is on the admin staff list,
  the staff edit page and inside each person pill of the public "who with" picker. The picture is decorative next
  to the visible name, so its `alt` is empty and the letter is `aria-hidden`.

## Personal data (GDPR)

A staff photo is personal data of the staff member, kept on the salon's instruction for one purpose: showing who a
client books with.

- **Removed means deleted**: "Remove photo" deletes the row; there is no soft delete and no history of pictures.
  Replacing a picture overwrites the bytes.
- **It goes with the person**: the foreign key to `staff_members` cascades, so deleting a staff member deletes the
  picture in the same statement.
- **Only the picture is kept**: no file name (it can say who took the photo or where), no EXIF handling — the bytes
  are stored as uploaded, so a salon should not upload a picture whose embedded location matters. Stripping
  metadata needs an image library and belongs with resizing (below).
- **Logs carry ids and sizes only**, never a file name.
- **What cannot be recalled**: a picture a visitor's browser cached under its versioned address stays in that
  browser. Removing the photo or deactivating the person stops the address answering at once, for everyone else.

## Consequences

- A photo taken with a phone camera is usually 2–8 MB and will be refused; the message says the limit and suggests
  a cropped copy or a screenshot. This is the weak point of "no resize" and the first thing to revisit: shrinking
  in the browser before upload (a canvas in a module script, no dependency) or an image library on the server
  (a new runtime dependency, Tier 2).
- The database grows by at most 512 KB per staff member; a salon has tens of people. Backups carry the pictures,
  which is the point: nothing lives outside the database to lose on a deploy.
- Additive migration `StaffPhotos` (one new table, its indexes and policy). Rollback: `Down` drops the policy and
  the table; the pictures are lost and nothing else refers to them.
- The diary columns and the client-facing team page named in ADR 0080 are not part of this slice; they can use the
  same tag helper and `IStaffPhotoService.VersionsAsync` when their lanes are free.

## Alternatives considered

- **Files under `wwwroot`**: the simplest to serve, and deleted by the next deploy on the pilot host; also outside
  the database backup and outside row-level security.
- **Object storage (S3-compatible)**: right at scale, but a new dependency, credentials and a bill before the
  first salon has uploaded a face.
- **The bytes on the staff member row**: one table fewer, and every staff query would have to remember not to
  select the column.
- **Trusting the browser's content type or the extension**: both are whatever the sender says.
- **A short cache with revalidation instead of immutable addresses**: every booking page view would ask again for
  every face; the versioned address makes the second visit free.