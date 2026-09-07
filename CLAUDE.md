# Khadra

Car rental marketplace for Jordan: customers rent from licensed (green-plate) rental offices. Roles: Admin (platform owner), Dealer Owner, Dealer Employee, Customer. Spec: `docs/Car_Rental_System_v3.1.docx` (v3.1 "Tech Stack Finalized"; v3.0 is kept for history — the two differ only in the header, every numbered section is identical). v3.1 names this stack: ASP.NET Core + PostgreSQL + EF Core. One .NET 10 backend serves an Angular 22 business dashboard (through a BFF) and a Flutter customer app (bearer tokens, not in this repo). Modular monolith with DDD bounded contexts; see `docs/architecture-bounded-contexts.md`.

## Commands

- Infra: `docker compose up -d` (postgres:18 on 5432, redis:8 on 6379, mailpit UI on http://localhost:8025)
- Build: `dotnet build Khadra.slnx`  ·  Test: `dotnet test Khadra.slnx`
- Add migration: `dotnet ef migrations add <Name> --project Khadra.Infrastructure --startup-project Khadra.WebAPI --output-dir Persistence/Migrations`
- Apply migrations: `dotnet ef database update --project Khadra.Infrastructure --startup-project Khadra.WebAPI` (Development auto-applies when `Database:AutoMigrate` is true)
- Run API: `dotnet run --project Khadra.WebAPI --launch-profile https` → https://localhost:7012 (Scalar UI at `/scalar/v1`)
- Run BFF: `dotnet run --project Khadra.Bff --launch-profile https` → https://localhost:7243 (needs Redis)
- Dashboard: `npm start` in `Khadra.Dashboard/` (ng serve on 4200, proxies `/bff` and `/api` to the BFF); production build `npm run build -- --configuration production`
- Dev secrets: `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<conn>" --project Khadra.WebAPI`, `dotnet user-secrets set "Authentication:Jwt:SigningKey" "<32+ bytes>" --project Khadra.WebAPI`, `dotnet user-secrets set "BffSecurity:RedisConnection" "localhost:6379" --project Khadra.Bff`
- First administrator: `dotnet user-secrets set "Admin:Bootstrap:Email" "<a mailbox you can open>" --project Khadra.WebAPI` (plus `:Phone` and `:FullName`). On startup, and ONLY on a database that has never held an administrator, the API emails that address an invitation; the account does nothing until the link is accepted. There is no bootstrap password, and no other way to create the first one.
- Real email: `Email:Provider` selects the transport — `Resend` or `Brevo` (HTTPS APIs, port 443), `Smtp` (Gmail, Brevo, any relay, port 587) or `Logging` (writes to the log, delivers nothing). Credentials are secrets and live ONLY in user-secrets or the environment; tracked appsettings hold placeholders. **Prefer an HTTPS transport:** a network that blocks outbound 587 fails in the worst way — the TCP handshake succeeds, the SMTP greeting never arrives, and every message stalls until it times out. `Email:TimeoutSeconds` (default 15) is the TOTAL wait a caller may suffer, and `Email:MaxAttempts` (default 3) splits it into that many tries, so retries never lengthen the wait.
  - Resend: `dotnet user-secrets set "Email:ApiKey" "re_..." --project Khadra.WebAPI`. `Email:FromAddress` must be `onboarding@resend.dev` (delivers only to the account owner, no domain setup) or a sender on a domain verified at resend.com/domains.
  - Brevo over HTTPS (any recipient, no domain needed — only a confirmed sender): `Provider` `Brevo`, `dotnet user-secrets set "Email:ApiKey" "xkeysib-..." --project Khadra.WebAPI`, and `Email:FromAddress` set to the address confirmed under *Senders, Domains & Dedicated IPs*. The API key (`xkeysib-`) is NOT the SMTP key (`xsmtpsib-`); each is refused by the other, and the startup probe says which you have.
  - Gmail: `Provider` `Smtp`, Host `smtp.gmail.com`, Port 587, `UseStartTls` true, plus `Email:Username` and an `Email:Password` that is a **Google App Password** (myaccount.google.com/apppasswords) — the account password is refused with `535-5.7.8 BadCredentials`.
  - Brevo: `Provider` `Smtp`, Host `smtp-relay.brevo.com`, Port 587, `Email:Username` = your Brevo login, `Email:Password` = an SMTP key. No code change; it is the same sender.
  - The API probes the transport on every start and logs `Email ready…` or `EMAIL WILL NOT BE DELIVERED…` with the provider’s own reason. Check that line before debugging anything else about email.
  - `Email ready…` proves the transport ANSWERED, not that it is fast enough to use. `Email:TimeoutSeconds` is split across `Email:MaxAttempts`, so the default 15/3 gives each attempt five seconds — and a server that takes longer than that just to send its SMTP greeting fails every send while the startup probe still reports ready. The dev mailpit did exactly this until `MP_SMTP_DISABLE_RDNS` was set in `docker-compose.yml`: a reverse-DNS lookup with nowhere to go delayed its greeting by eight seconds. If mail is not arriving and the probe says ready, time the greeting before suspecting anything else.

## Architecture map (dependency direction: Domain <- Application <- Infrastructure <- WebAPI)

- `Khadra.Domain` — shared kernel (`Common/`: `Id`, `Entity`, `AggregateRoot`, `ValueObject`, `Enumeration`, `Error`, `Money`, `Percentage`, `GeoPoint`, `DateRange`) and one folder per bounded context: `IdentityAccess/`, `Auditing/`, `Dealers/`, `Fleet/`, `Bookings/`, `Disputes/`, `Reviews/`, `PlatformSettings/`. `Payments/` is NOT built (blocked on owner decisions). Repository interfaces live next to their aggregate. See `docs/architecture-bounded-contexts.md` for the status table and the open owner decisions.
- `Khadra.Application` — CQRS: `<Context>/<UseCase>/<UseCase>Command.cs` (+ validator) and `<UseCase>Handler.cs` using `ICommand<T>`/`IQuery<T>` (MediatR). Ports in `Common/Ports/`. Behaviors: logging, FluentValidation.
- `Khadra.Infrastructure` — `KhadraDbContext`, `Persistence/Configurations/<Context>/`, migrations, repositories, `UnitOfWork` (dispatches domain events after commit), BCrypt/JWT/opaque tokens, MailKit email, strongly-typed options.
- `Khadra.WebAPI` — controllers under `/api/v1`, JWT bearer with security-stamp check, policies, rate limiting, ProblemDetails, OpenAPI.
- `Khadra.Bff` — cookie session (`__Host-Khadra.Session`) + Redis ticket store + antiforgery + YARP proxy to the API. The browser never sees API tokens.
- `Khadra.Tests` — xunit: `Domain/`, `Application/` (NSubstitute), `Persistence/` (SQLite in-memory), `Security/` (WebApplicationFactory).
- `Khadra.Dashboard/` — Angular 22 console for both sides of the platform. Admin screens under `features/*`; the dealer console under `features/dealer/` and `features/fleet/` behind `/dealer/*` (`DealerGateComponent` locks it while the dealership cannot trade). Both are implemented from `docs/design/` (`Admin Console` and `Dealer Console.dc.html`, the exported Claude Design project, which is the source of truth for the look).
- **The console holds no sample data.** Every screen reads the live API through the BFF or says plainly that it is not built. `features/not-built/` is that screen for the Admin (customers, payments, payouts, finance, reviews, the lookups, admin users, platform settings, notifications, security); `features/dealer/not-live.component.ts` is its dealer counterpart (reviews, notifications). Each names what it will show and which context is missing, so nobody has to guess whether a figure is real. `core/data/` now holds structure only: `nav.data.ts` (sidebar and breadcrumbs, with badges naming a live count rather than carrying a number) and `dashboard.data.ts` (presenter view-model types). When you build a context, replace its `notBuilt` route with a real component and delete its entry from `SCREENS` there. Never reintroduce a fixture to fill a screen.
- **The database holds no sample data either.** There was a `DevelopmentSeeder` that invented administrators, galleries, customers, cars, bookings and disputes so the console had something to show; it was deleted on 2026-09-05. A fabricated administrator can approve a real gallery, its password was a working credential committed to source, and it counted towards the guard that stops the last real administrator being deactivated. Every account and every gallery now arrives the way a real one does — through the screens. The first administrator is the single exception, and it arrives as an emailed invitation (see `AdminBootstrapper`), never as a row with a password. Never add a seeder back.

## Backend conventions (MUST) — details in `.claude/rules/backend/architecture.md`

- Aggregates: private setters, static factories, behaviour methods returning `Result`/`UnitResult<Error>` (CSharpFunctionalExtensions) for expected failures; `DomainException` only for programming errors.
- Status/type fields are smart enums (`Enumeration`), never plain C# `enum`. Email, phone, name, money, geo, date ranges are value objects.
- Cross-context references by `Id` only. No navigation properties or EF relationships across contexts.
- Soft delete via `ISoftDeletable`; `DeleteBehavior.Restrict` on every FK; never hard-delete.
- Handlers return `Result<T, Error>` / `UnitResult<Error>`; `Error.Kind` maps to HTTP status in `ApiControllerBase.Failure`. Do not throw for business outcomes. Handlers call `IUnitOfWork.SaveChangesAsync` explicitly and never touch `HttpContext` (use `ClientInfo` / `ICurrentActor`).
- Business numbers (commission %, deposit %, no-show hours, penalties, cancellation window, SLA, turnaround minutes) come ONLY from `IBusinessRulesProvider` (configuration section `BusinessRules` today, admin-editable aggregate later). Never a constant.
- **The delivery fee is NOT one of them.** It was, and the owner moved it (2026-09-06) onto the dealership that performs the delivery: it lives on `DeliverySettings` beside the radius, each gallery sets its own from `/dealer/delivery`, and there is no platform-wide figure any more. That matches where the money already went — `BookingPricing` leaves the fee out of the deposit base, so the platform takes no commission on it, and puts it in `BalanceDue` for the driver to collect in cash. A booking still freezes the fee it was made under, so a gallery raising its price never re-prices an existing booking.
- A booking FREEZES the rules and the price it was made under (`BookingTerms`, `BookingPricing`). Never judge a past booking against current settings.
- **Rentals are billed in CALENDAR days** (owner, 2026-09-07): the Amman date difference, minimum one, so Mon 09:00 to Thu 11:00 is three days. `RentalDays.Between(DateOnly, DateOnly)` is the only place that counts, and `BookingPricing` freezes the count with the two dates it came from. `DateRange` has no day count and must not grow one — it is the shared kernel's instant interval and has no time zone. Convert through `IReportingCalendar` before pricing. A screen NEVER recomputes days from the two instants; it renders the server's figure.
- **A booking claims the car earlier than the customer collects it.** `Booking.HoldStart` is the period start minus `BookingTerms.TurnaroundBuffer` (`BusinessRules:TurnaroundMinutes`, 120), padded on the LEADING edge only, and zero for an extension. `bookings_one_hold_per_vehicle` (btree_gist EXCLUDE) enforces it in the database. Anything asking "is this car free" goes through `BookingHolds` so the guard, the catalogue and the constraint cannot drift apart.
- Audit trail: a privileged admin action records an `AuditEntry` through `IAuditTrail.Record(...)` in the handler, committed by that handler's own `SaveChangesAsync` so the record and the action land in ONE transaction. Never fed from domain events (they dispatch after commit, with no outbox). `audit_entries` is append-only: a database trigger and a `SaveChanges` guard both refuse UPDATE and DELETE. Two readers serve it: `IAuditFeedReader` for the dashboard's fixed glance, `IAuditLogReader` for the filtered, paged audit screen, whose order is (occurred_at DESC, id DESC) because a non-total order lets a page boundary drop an entry.
- Read models for admin screens are per-context reader ports (`Khadra.Application/<Context>/ReadModels/`), implemented in `Khadra.Infrastructure/Reporting/` and composed by one handler. Never call the readers concurrently: they share the scoped `DbContext`.
- Reporting dates are LOCAL to `AdminDashboard:ReportingTimeZone` (Asia/Amman), never UTC. Use `IReportingCalendar`.
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

## Deferred work that must not be forgotten

`docs/pre-launch-checklist.md` lists what is knowingly deferred while this is a development system
and must be closed before real users, real bookings or real money. Add to it whenever you leave
something for later; an item comes off only by being fixed.

## Open business decisions (spec §2.2)

Dealer non-delivery penalty tier (flat 25% / 50% / tiered), quick-cancellation processing fee, insurance and mileage/fuel policy defaults, IDP requirement for foreigners. Ask the owner before coding anything that depends on these.

**Dealer console defaults awaiting a decision (2026-09-03):** a suspended dealer may still record a pickup on an already-approved booking (default: allowed; returns are always allowed); the business name is locked after approval (default: locked) while location and operating hours stay editable; the customer's contact details are never shown to the dealer (the console makes no promise about it); staff management (`/dealers/me/employees`) requires an approved, trading dealer, so the owner of a suspended dealer cannot deactivate an employee (default kept). Ask the owner before changing any of these.

**Minimum renter age: settled at 21** by the owner and enforced (`BusinessRules:MinimumRenterAge`, `RenterAgePolicy`). The spec still says "value pending Section 2 decision" in §5.1 and lists it as open in §2.2 — the document has not caught up with the decision. The code is right; the spec needs a revision.
