## Summary
<!-- One or two sentences: what changes for a salon owner, receptionist or client -->

Closes #

## Tier
- [ ] tier:0 — auto (docs/tests/copy/refactor/slice bug fix)
- [ ] tier:1 — auto, guarded (new slice behind flag, additive migration)
- [ ] tier:2 — owner only (payments, auth, tenancy, destructive migration, GDPR, infra, deps, CI)

## Plan and decisions
- Plan comment: <!-- link -->
- ADRs: <!-- docs/adr/NNNN-... or "none" -->

## Slices touched
- `Features/<Module>/<UseCase>`

## Tests
- [ ] Failing tests committed before the fix/feature
- [ ] Unit · [ ] Integration · [ ] Tenant isolation · [ ] E2E (mobile 390px) · [ ] Property tests (if availability/booking)

## Screenshots
<!-- Playwright mobile + desktop -->

## Migration and rollback
<!-- Additive? Expand/contract step? How to roll back -->

## Checklist
- [ ] en-GB and pl-PL strings
- [ ] No hard-coded settings, no inline scripts
- [ ] < 600 changed lines
