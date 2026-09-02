---
paths:
  - "Khadra.Domain/**/*.cs"
  - "Khadra.Application/**/*.cs"
  - "Khadra.Infrastructure/**/*.cs"
  - "Khadra.WebAPI/**/*.cs"
  - "Khadra.Bff/**/*.cs"
---

# Backend Architecture Rules (DDD + CQRS + REST)

## Layering (dependency direction: Domain <- Application <- Infrastructure <- WebAPI)
- `Khadra.Domain`: aggregates, entities, value objects, smart enums, domain events, domain services/policies, repository interfaces. No framework references.
- `Khadra.Application`: use cases as CQRS commands/queries + handlers (MediatR), DTOs, validators, ports (interfaces implemented by Infrastructure), pipeline behaviors.
- `Khadra.Infrastructure`: EF Core `KhadraDbContext`, entity configurations, migrations, repositories, unit of work, security (BCrypt, JWT, opaque tokens), email, options.
- `Khadra.WebAPI`: controllers, authentication/authorization policies, rate limiting, ProblemDetails, OpenAPI. No business logic.
- `Khadra.Bff`: browser session (cookie + Redis), CSRF, YARP proxy. No domain logic.

## Bounded contexts
- One folder per context in Domain and Application: `IdentityAccess`, `Dealers`, `Fleet`, `Booking`, `Payments`, `Disputes`, `Reviews`, `PlatformSettings`.
- Cross-context references are by `Id` only. Never add navigation properties or EF relationships across contexts.
- A context never mutates another context's aggregate; it sends a command or reacts to a domain event.

## Domain (MUST)
- Aggregates inherit `AggregateRoot`; entities inherit `Entity`; value objects inherit `ValueObject`; use `Id` (not raw `Guid`) for identities.
- No public setters. State changes go through intention-revealing methods that enforce invariants and raise domain events.
- Status/type fields are smart enums (sealed class inheriting `Enumeration`), never plain C# `enum`.
- Email, phone, money, geo-coordinates, names, date ranges are value objects.
- Aggregates return `Result` (CSharpFunctionalExtensions) for expected rule violations; throw `DomainException` only for programming errors / impossible states.
- Soft delete: aggregates that users can "delete" implement `ISoftDeletable`. Never hard-delete.
- Business numbers (commission %, deposit %, no-show hours, delivery fee, penalties, cancellation window, SLA) are NEVER constants. They flow in through `IBusinessRulesProvider` / policy parameters.

## Application (MUST)
- One folder per use case: `<UseCase>Command.cs` or `<UseCase>Query.cs` plus `<UseCase>Handler.cs` implementing `ICommand<T>` / `IQuery<T>` (MediatR-backed).
- Handlers return `Result` / `Result<T>` for expected failures (not-found, conflict, forbidden, validation). Use `AppError.*` helpers so the API maps them to the right status code. Do not throw for business outcomes.
- Handlers call `IUnitOfWork.SaveChangesAsync` explicitly. Multi-aggregate writes use `IUnitOfWork.ExecuteInTransactionAsync`.
- Validation lives in FluentValidation validators next to the request; the `ValidationBehavior` runs them.
- Handlers never touch `HttpContext`. Client facts (IP, user agent, current user) arrive via `ClientInfo` / `ICurrentActor`.

## Infrastructure (MUST)
- snake_case naming (`UseSnakeCaseNamingConvention`). One `IEntityTypeConfiguration<T>` per aggregate/entity under `Persistence/Configurations/<Context>/`.
- `DeleteBehavior.Restrict` on every FK. Smart enums persisted via `HasConversion` to their string name. Value objects via `OwnsOne` or converters.
- Add migrations with `dotnet ef migrations add <Name> --project Khadra.Infrastructure --startup-project Khadra.WebAPI`. Never hand-edit an existing migration.
- Secrets only from user-secrets / env vars / gitignored `appsettings.Local.json`. Tracked appsettings contain empty placeholders.

## REST (MUST)
- Routes: `/api/v1/<plural-resource>`; controllers inherit `ApiControllerBase` and map results with `FromResult`.
- Status codes: 200 OK, 201 Created (+ `Location`), 202 Accepted (async work such as email), 204 No Content, 400 validation / bad token, 401 unauthenticated or bad credentials, 403 forbidden / account state, 404, 409 conflict, 429 rate limited.
- Errors are RFC 9457 ProblemDetails with `traceId` and a stable `code` (e.g. `auth.invalid_credentials`). No stack traces, no internal messages.
- Lists are paginated (`?page=&pageSize=`) and return `PagedResult<T>`.
- Every mutation endpoint on the BFF side requires antiforgery; every API endpoint requires authentication unless explicitly `[AllowAnonymous]` and rate limited.

## Style
- Primary constructors, file-scoped namespaces, `sealed` by default, `readonly record struct` for small value types, `internal` for infrastructure implementations.
- Every new use case ships with domain and handler tests in `Khadra.Tests`.
