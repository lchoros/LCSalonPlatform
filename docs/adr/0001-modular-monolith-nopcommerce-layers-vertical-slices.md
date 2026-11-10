# 0001 — Modular monolith: nopCommerce layers outside, vertical slices inside

Status: Accepted (owner)
Date: 2026-11-10

## Context
One developer plus AI agents must build and run a multi-tenant salon SaaS. The owner knows nopCommerce well
and wants its proven ideas (layers, plugins, events, settings, localization, scheduled tasks), but nopCommerce's
large service classes make parallel agent work collide.

## Options
1. Microservices — too much to operate for one person.
2. Classic nopCommerce layering only — familiar, but big shared services become merge hot-spots.
3. nopCommerce layering + vertical slices per use case inside Salon.Web/Features.

## Decision
Option 3. Layers Core → Data → Services → Web.Framework → Web, plus Plugins. Use cases live as slices
(`Features/<Module>/<UseCase>/`) that never reference each other; modules talk through domain events or
narrow services. Providers (payments, SMS, import, calendar sync) are compile-time plugins.

## Consequences
- Agents can work on different slices in parallel with few conflicts.
- Architecture tests (NetArchTest) enforce the rules in CI.
- One container to deploy; can split a module out later if it ever needs to scale alone.
