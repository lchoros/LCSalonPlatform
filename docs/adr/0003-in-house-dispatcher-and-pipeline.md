# 0003 — In-house request dispatcher and pipeline behaviours

Status: Decided — owner may veto
Date: 2026-11-11

## Context
Every vertical slice needs the same cross-cutting steps around its handler: logging, input validation, the tenant
guard from ADR 0002 and one database transaction per command. MediatR is the usual answer, but it moved to a
commercial licence in 2025 and we would use only `IRequestHandler` plus `IPipelineBehavior`; notifications,
streams and the source generator are not needed because events already go through `IEventPublisher` and the outbox.

## Options
1. MediatR — familiar, but a licence to track and a runtime dependency (Tier 2) for two interfaces.
2. No dispatcher; controllers call handlers directly — every slice repeats validation, guard and transaction code.
3. Small in-house dispatcher (~60 lines) with open-generic behaviours registered through `IServiceCollection`.

## Decision
Option 3.
- Contracts live in `Salon.Core.Cqrs` so handlers stay free of ASP.NET: `IRequest<T>`, `ICommand<T>`, `IQuery<T>`,
  `IRequestHandler<TRequest, T>`, `IPipelineBehavior<TRequest, T>`, `IDispatcher`, and the marker `IPlatformRequest`.
- `Salon.Web.Framework.Pipeline.Dispatcher` resolves the single handler from the scope and wraps it in the registered
  behaviours in registration order, through one cached generic wrapper per request type.
- Fixed order: `LoggingBehavior` → `ValidationBehavior` (all FluentValidation validators; throws
  `ValidationException` with every failure) → `TenantGuardBehavior` (throws `TenantViolationException` unless the
  request is `IPlatformRequest`) → `TransactionBehavior` (commands only; joins an open transaction) → handler.
- `TransactionBehavior` talks to `IUnitOfWork`; the default `EfUnitOfWork` is a three-line adapter over
  `SalonDbContext.Database`, so the behaviour is unit-tested without Postgres.
- `AddSalonPipeline(params Assembly[])` registers everything and scans handlers and validators;
  `AddSalonFramework(configuration, typeof(Program).Assembly)` calls it for the host.

## Consequences
- Slices inject `IDispatcher` and never resolve handlers or open transactions themselves.
- Every request needs a tenant by default; platform-level use cases opt out explicitly and are easy to grep for.
- No notifications or streaming requests; if a slice ever needs them, extend this dispatcher rather than add a package.
