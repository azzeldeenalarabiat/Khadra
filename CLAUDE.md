# Khadra

Car rental marketplace for Jordan: customers rent from licensed (green-plate) rental offices. Roles: Admin (platform owner), Dealer Owner, Dealer Employee, Customer. Spec: `docs/Car_Rental_System_v3.0.docx` (v3.0 "Confirmed Business Rules"). One .NET 10 backend serves an Angular 22 business dashboard (through a BFF) and a Flutter customer app (bearer tokens, not in this repo). Modular monolith with DDD bounded contexts; see `docs/architecture-bounded-contexts.md`.

## Commands

- Infra: `docker compose up -d` (postgres:18 on 5432, redis:8 on 6379, mailpit UI on http://localhost:8025)
- Build: `dotnet build Khadra.slnx`  ·  Test: `dotnet test Khadra.slnx`
- Add migration: `dotnet ef migrations add <Name> --project Khadra.Infrastructure --startup-project Khadra.WebAPI --output-dir Persistence/Migrations`
- Apply migrations: `dotnet ef database update --project Khadra.Infrastructure --startup-project Khadra.WebAPI` (Development auto-applies when `Database:AutoMigrate` is true)
- Run API: `dotnet run --project Khadra.WebAPI --launch-profile https` → https://localhost:7012 (Scalar UI at `/scalar/v1`)
- Run BFF: `dotnet run --project Khadra.Bff --launch-profile https` → https://localhost:7243 (needs Redis)
- Dashboard: `npm start` in `Khadra.Dashboard/` (ng serve on 4200, proxies `/bff` and `/api` to the BFF); production build `npm run build -- --configuration production`
- Dev secrets: `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<conn>" --project Khadra.WebAPI`, `dotnet user-secrets set "Authentication:Jwt:SigningKey" "<32+ bytes>" --project Khadra.WebAPI`, `dotnet user-secrets set "BffSecurity:RedisConnection" "localhost:6379" --project Khadra.Bff`

## Architecture map (dependency direction: Domain <- Application <- Infrastructure <- WebAPI)

- `Khadra.Domain` — shared kernel (`Common/`: `Id`, `Entity`, `AggregateRoot`, `ValueObject`, `Enumeration`, `Error`, `Money`, `Percentage`, `GeoPoint`, `DateRange`) and one folder per bounded context: `IdentityAccess/`, `Dealers/`, `Fleet/`, `Bookings/`, `Disputes/`, `Reviews/`, `PlatformSettings/`. `Payments/` is NOT built (blocked on owner decisions). Repository interfaces live next to their aggregate. See `docs/architecture-bounded-contexts.md` for the status table and the open owner decisions.
- `Khadra.Application` — CQRS: `<Context>/<UseCase>/<UseCase>Command.cs` (+ validator) and `<UseCase>Handler.cs` using `ICommand<T>`/`IQuery<T>` (MediatR). Ports in `Common/Ports/`. Behaviors: logging, FluentValidation.
- `Khadra.Infrastructure` — `KhadraDbContext`, `Persistence/Configurations/<Context>/`, migrations, repositories, `UnitOfWork` (dispatches domain events after commit), BCrypt/JWT/opaque tokens, MailKit email, strongly-typed options.
- `Khadra.WebAPI` — controllers under `/api/v1`, JWT bearer with security-stamp check, policies, rate limiting, ProblemDetails, OpenAPI.
- `Khadra.Bff` — cookie session (`__Host-Khadra.Session`) + Redis ticket store + antiforgery + YARP proxy to the API. The browser never sees API tokens.
- `Khadra.Tests` — xunit: `Domain/`, `Application/` (NSubstitute), `Persistence/` (SQLite in-memory), `Security/` (WebApplicationFactory).
- `Khadra.Dashboard/` — Angular 22 admin console. All 22 screens implemented from `docs/design/` (the exported Claude Design project, which is the source of truth for the look). Sample data in `src/app/core/data/` is shaped like the API responses that will replace it.

## Backend conventions (MUST) — details in `.claude/rules/backend/architecture.md`

- Aggregates: private setters, static factories, behaviour methods returning `Result`/`UnitResult<Error>` (CSharpFunctionalExtensions) for expected failures; `DomainException` only for programming errors.
- Status/type fields are smart enums (`Enumeration`), never plain C# `enum`. Email, phone, name, money, geo, date ranges are value objects.
- Cross-context references by `Id` only. No navigation properties or EF relationships across contexts.
- Soft delete via `ISoftDeletable`; `DeleteBehavior.Restrict` on every FK; never hard-delete.
- Handlers return `Result<T, Error>` / `UnitResult<Error>`; `Error.Kind` maps to HTTP status in `ApiControllerBase.Failure`. Do not throw for business outcomes. Handlers call `IUnitOfWork.SaveChangesAsync` explicitly and never touch `HttpContext` (use `ClientInfo` / `ICurrentActor`).
- Business numbers (commission %, deposit %, no-show hours, delivery fee, penalties, cancellation window, SLA) come ONLY from `IBusinessRulesProvider` (configuration section `BusinessRules` today, admin-editable aggregate later). Never a constant.
- A booking FREEZES the rules and the price it was made under (`BookingTerms`, `BookingPricing`). Never judge a past booking against current settings.
- Penalties are ASSESSED, never charged. Spec 3.3: with no dispute ticket, no penalty is applied at all. Money moves only through an Admin resolving a ticket.
- REST: plural nouns, `/api/v1`, RFC 9457 ProblemDetails with `code` + `traceId`, 201 + Location, 202 for accepted async work, 204 for no body, pagination with `PagedResult<T>`. Anonymous endpoints must be rate limited.
- Style: primary constructors, file-scoped namespaces, `sealed` by default, `internal` for infrastructure implementations. Every use case ships with tests.

## Frontend conventions (MUST)

Follow `.claude/rules/frontend/angular-dashboard.md`: standalone + OnPush, `.component.ts` + `.component.html` only (no per-component styles and no `style=` attributes), all calls through the BFF, logical CSS properties for RTL, currency code on every money value. The Admin console deliberately does NOT use PrimeNG: it is built to the bespoke Nocturne design system, and that exception is recorded in the rules file.

## Forbidden actions

- Never hand-edit an existing file under `Persistence/Migrations/`; add a new migration.
- Never commit secrets. `appsettings.Local.json`, `.env*` and `.keys/` are gitignored; tracked `appsettings*.json` hold empty placeholders.
- Never touch the Payments context (money movement, commission, refunds) without explicit owner approval.
- Never `git push` without explicit owner approval; never force-push; never `dotnet ef database drop` or `docker compose down -v`.
- Never bypass the soft-delete query filter without an explicit, commented reason.

## Open business decisions (spec §2.2)

Dealer non-delivery penalty tier (flat 25% / 50% / tiered), quick-cancellation processing fee, insurance and mileage/fuel policy defaults, minimum renter age, IDP requirement for foreigners. Ask the owner before coding anything that depends on these.
