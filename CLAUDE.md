# Khadra

Car rental marketplace for Jordan: customers rent from licensed (green-plate) rental offices. Roles: Admin (platform owner), Dealer Owner, Dealer Employee, Customer. Spec: `docs/Car_Rental_System_v3.1.docx` (v3.1 "Tech Stack Finalized"; v3.0 is kept for history — the two differ only in the header, every numbered section is identical). v3.1 names this stack: ASP.NET Core + PostgreSQL + EF Core. One .NET 10 backend serves an Angular 22 business dashboard (through a BFF) and a Flutter customer app (bearer tokens) in `Khadra.Mobile/`. Modular monolith with DDD bounded contexts; see `docs/architecture-bounded-contexts.md`.

**The customer app lives in THIS repository.** It used to have its own — `khadra-mobile` on GitHub — and that line said so. The app moved here and the sentence did not, which is how a deployment review found a stale instruction pointing at a repository last pushed on 2026-09-07. `Khadra.Mobile/` is the canonical working copy (owner, 2026-09-20); `khadra-mobile` is retired and must not be pushed to. Whether the app eventually splits back out is a separate decision, to be taken when production is stable rather than inside a release.

## Commands

- Infra: `docker compose up -d` (postgres:18 on 5432, redis:8 on 6379, mailpit UI on http://localhost:8025)
- Build: `dotnet build Khadra.slnx`  ·  Test: `dotnet test Khadra.slnx`
- Add migration: `dotnet ef migrations add <Name> --project Khadra.Infrastructure --startup-project Khadra.WebAPI --output-dir Persistence/Migrations`
- Apply migrations: `dotnet ef database update --project Khadra.Infrastructure --startup-project Khadra.WebAPI` (Development auto-applies when `Database:AutoMigrate` is true)
- Run API: `dotnet run --project Khadra.WebAPI --launch-profile https` → https://localhost:7012 (Scalar UI at `/scalar/v1`)
- Run BFF: `dotnet run --project Khadra.Bff --launch-profile https` → https://localhost:7243 (needs Redis)
- Customer website (dev): the API on its own ports, the customer BFF (`dotnet run --project Khadra.Bff --launch-profile customer-web`, https://localhost:7244, `BffSecurity__ApiBaseUrl` and the `webapi` cluster address pointed at that API), then `npx ng serve` in `Khadra.Web/` (4400; proxies `/bff` and `/api` to the customer BFF; set `KHADRA_API_URL` to the API's http address for the renderer). Open http://localhost:4400. Tests: `npx ng test --watch=false`; production build `npx ng build`, served by `node dist/khadra-web/server/server.mjs` with `KHADRA_API_URL`, `KHADRA_PUBLIC_BASE_URL`, `KHADRA_EDGE_SECRET`.
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

- `Khadra.Domain` — shared kernel (`Common/`: `Id`, `Entity`, `AggregateRoot`, `ValueObject`, `Enumeration`, `Error`, `Money`, `Percentage`, `GeoPoint`, `DateRange`) and one folder per bounded context: `IdentityAccess/`, `Auditing/`, `Dealers/`, `Fleet/`, `Bookings/`, `Disputes/`, `Reviews/`, `PlatformSettings/`, `Shortlist/`. `Payments/` was built on 2026-09-08 with the owner's explicit approval: `Payment` (one checkout attempt) with `Refund` children, plus `ProviderEventReceipt` outside the aggregate. **No provider is configured**, so every checkout is refused with `payments.provider_unavailable` and the startup log says `PAYMENTS ARE NOT ACCEPTED` on every boot — see pre-launch item 76 for what closing that needs. Repository interfaces live next to their aggregate. See `docs/architecture-bounded-contexts.md` for the status table and the open owner decisions.
- `Khadra.Application` — CQRS: `<Context>/<UseCase>/<UseCase>Command.cs` (+ validator) and `<UseCase>Handler.cs` using `ICommand<T>`/`IQuery<T>` (MediatR). Ports in `Common/Ports/`. Behaviors: logging, FluentValidation.
- `Khadra.Infrastructure` — `KhadraDbContext`, `Persistence/Configurations/<Context>/`, migrations, repositories, `UnitOfWork` (dispatches domain events after commit), BCrypt/JWT/opaque tokens, MailKit email, strongly-typed options.
- `Khadra.WebAPI` — controllers under `/api/v1`, JWT bearer with security-stamp check, policies, rate limiting, ProblemDetails, OpenAPI.
- `Khadra.Bff` — cookie session (`__Host-Khadra.Session`) + Redis ticket store + antiforgery + YARP proxy to the API. The browser never sees API tokens.
- `Khadra.Tests` — xunit: `Domain/`, `Application/` (NSubstitute), `Persistence/` (SQLite in-memory), `Security/` (WebApplicationFactory).
- `Khadra.Dashboard/` — Angular 22 console for both sides of the platform. Admin screens under `features/*`; the dealer console under `features/dealer/` and `features/fleet/` behind `/dealer/*` (`DealerGateComponent` locks it while the dealership cannot trade). Both are implemented from `docs/design/` (`Admin Console` and `Dealer Console.dc.html`, the exported Claude Design project, which is the source of truth for the look).
- `Khadra.Web/` — the public CUSTOMER WEBSITE (2026-09-23), a separate Angular 22 workspace with server-side rendering. Public pages (`/{ar|en}`, `/cars`, `/cars/{words}-{id}`, `/dealers`, `/dealers/{words}-{id}`) render on the server for search engines; account pages (sign-in, bookings, saved, notifications, profile, documents) render in the browser only and carry `noindex`. The language is the first URL segment; `/` redirects to `/en` only when the browser clearly prefers English, otherwise `/ar`. It sits BEHIND a second deployment of `Khadra.Bff` (`BffSecurity:Deployment` = `customer-web`, see `Khadra.Bff/Deployments/`), which owns the host, the session and `/api` and proxies every other path to the renderer; the renderer calls the API directly, anonymously, for public data only. Its look is the customer design handoff (`khadra_theme.dart`), not the console's tokens. Same rules as the console: no sample data, no business number in a template, every figure from the API or `/app-config`.
- `Khadra.Bff` deployments: each is a file in `Khadra.Bff/Deployments/` (route table, allowed roles, cookie names, frontend). `console` keeps every name it had (cache prefix, Data Protection application name, cookies), so its sessions survive; `customer-web` has its own, so the two cannot read each other's sessions on one Redis. A BFF refuses, at sign-in and on every request, a role its deployment does not serve (`bff.role_not_allowed`).
- **The console holds no sample data.** Every screen reads the live API through the BFF or says plainly that it is not built. `features/not-built/` is that screen for the Admin (customers, payments, payouts, finance, reviews, the lookups, admin users, platform settings, notifications, security); `features/dealer/not-live.component.ts` is its dealer counterpart (reviews, notifications). Each names what it will show and which context is missing, so nobody has to guess whether a figure is real. `core/data/` now holds structure only: `nav.data.ts` (sidebar and breadcrumbs, with badges naming a live count rather than carrying a number) and `dashboard.data.ts` (presenter view-model types). When you build a context, replace its `notBuilt` route with a real component and delete its entry from `SCREENS` there. Never reintroduce a fixture to fill a screen.
- **The database holds no sample data either.** There was a `DevelopmentSeeder` that invented administrators, galleries, customers, cars, bookings and disputes so the console had something to show; it was deleted on 2026-09-05. A fabricated administrator can approve a real gallery, its password was a working credential committed to source, and it counted towards the guard that stops the last real administrator being deactivated. Every account and every gallery now arrives the way a real one does — through the screens. The first administrator is the single exception, and it arrives as an emailed invitation (see `AdminBootstrapper`), never as a row with a password. Never add a seeder back.

## Backend conventions (MUST) — details in `.claude/rules/backend/architecture.md`

- Aggregates: private setters, static factories, behaviour methods returning `Result`/`UnitResult<Error>` (CSharpFunctionalExtensions) for expected failures; `DomainException` only for programming errors.
- Status/type fields are smart enums (`Enumeration`), never plain C# `enum`. Email, phone, name, money, geo, date ranges are value objects.
- Cross-context references by `Id` only. No navigation properties or EF relationships across contexts.
- Soft delete via `ISoftDeletable`; no FK cascades in the database ever (every constraint restricts); never hard-delete an aggregate. In the model that means `DeleteBehavior.Restrict` for references and `DeleteBehavior.ClientCascade` for a child collection the aggregate removes from, where EF deletes the orphan instead of throwing "the association … has been severed". See `.claude/rules/backend/architecture.md`.
- Handlers return `Result<T, Error>` / `UnitResult<Error>`; `Error.Kind` maps to HTTP status in `ApiControllerBase.Failure`. Do not throw for business outcomes. Handlers call `IUnitOfWork.SaveChangesAsync` explicitly and never touch `HttpContext` (use `ClientInfo` / `ICurrentActor`).
- Business numbers (commission %, deposit %, no-show hours, penalties, cancellation window, SLA, turnaround minutes, shortlist cap) come ONLY from `IBusinessRulesProvider` (configuration section `BusinessRules` today, admin-editable aggregate later). Never a constant.
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

## The customer app's contract (MUST) — settled by the owner, 2026-09-22

Every response and request the customer app reads is a contract with every build already installed on a phone. An installed build cannot be patched, only refused, so:

- **Any breaking mobile/API contract change ships with a raised minimum supported app version.** Breaking means anything an installed build would misread or fail on: a field removed or renamed, a type or shape changed (a string becoming `{ text, language }` was the first), a code or enum value it keys on renamed, an endpoint removed, moved or given a new meaning, a status it relies on changed, or a request it sends refused where it used to be accepted. An added optional field or a new endpoint is not breaking. Prefer that additive shape — the old field kept beside the new one — whenever it is honest; when it is not, this rule applies in full.
- **In the repository, one change set carries all three:** the app reading the new contract, with `Khadra.Mobile/pubspec.yaml` raised to a new MAJOR.MINOR.PATCH (the `+build` number alone does not count: the comparison ignores it); the API serving it; and `MobileApp:MinimumSupportedVersion` in `Khadra.WebAPI/appsettings.json` raised to that version.
- **In production, the compatible mobile build is published BEFORE the server minimum is raised.** Publish the build, confirm it works against the API that is live, and only then deploy the API carrying the raised minimum. Raised first, the minimum refuses every customer with nothing to update to.
- `MobileAppMinimumVersionTests` fails if a tracked settings file sets the minimum above the app's own version; the publishing order has no test, and is this rule. The mechanism — `X-Khadra-App-Version`, `426 app.update_required`, `mobileApp` in `/app-config`, the app's update screen, and the version order in `docs/contracts/app-version-vectors.json` — is in `docs/contracts/README.md`.

## Forbidden actions

- Never ship a change the installed customer app cannot read without the raised minimum and the publish-first order above.
- Never hand-edit an existing file under `Persistence/Migrations/`; add a new migration.
- Never commit secrets. `appsettings.Local.json`, `.env*` and `.keys/` are gitignored; tracked `appsettings*.json` hold empty placeholders.
- Never touch the Payments context (money movement, commission, refunds) without explicit owner approval. It exists now; the rule did not lapse when it was built.
- **Never register a payment provider that simulates success.** One that captured and confirmed would be indistinguishable, in every table and on every screen, from a real payment: bookings would read Confirmed, galleries would prepare cars, and nobody could tell which rentals had money behind them. It is the cash path pre-launch item 2 already forbids, in a different costume. Tests substitute `IPaymentProvider` at the handler boundary and need nothing registered.
  - **`SANDBOX` is the one recorded exception, approved by the owner on 2026-09-21**, so the booking and payment lifecycle can be clicked through end to end before a merchant account exists. It is an exception, not a repeal, and what makes it allowable is three mechanisms rather than an intention — each proven by `SandboxPaymentGuardTests`: `Program.cs` refuses to start a Production host on it; `PaymentsStartupCheck` refuses to start whenever this database's payments and this process's provider are different kinds of money, **in both directions**, so a database that has taken sandbox payments stays on the sandbox for good and cannot later be served by `None` or by a real provider; and every row it writes carries `SANDBOX` in `payments.provider` for as long as the row exists, which is the single marker `PaymentDto.isSandbox` and the boot log both read. It is published too: `/app-config` carries `payments.mode` (`None`/`Sandbox`/`Live`) and the two consoles (admin and dealer) show a standing banner on `Sandbox` and on nothing else. **The customer app shows none** (owner, 2026-09-23): the staging app is marked by its launcher name, "Khadra TEST", and its own package id, never inside the UI; production can never report `Sandbox`, so a customer never has anything to be told. **A second exception does not get to cite the first**: what made this one allowable was the guards, not the reason. See pre-launch items 76 and 123, and delete the whole thing the day a real adapter lands.
- Never `git push` without explicit owner approval; never force-push; never `dotnet ef database drop` or `docker compose down -v`.
- Never bypass the soft-delete query filter without an explicit, commented reason.

## Deferred work that must not be forgotten

`docs/pre-launch-checklist.md` lists what is knowingly deferred while this is a development system
and must be closed before real users, real bookings or real money. Add to it whenever you leave
something for later; an item comes off only by being fixed.

## Open business decisions (spec §2.2)

Dealer non-delivery penalty tier (flat 25% / 50% / tiered), quick-cancellation processing fee, insurance and mileage/fuel policy defaults, IDP requirement for foreigners. Ask the owner before coding anything that depends on these.

**Dealer console defaults awaiting a decision (2026-09-03):** a suspended dealer may still record a pickup on an already-approved booking (default: allowed; returns are always allowed); the business name is locked after approval (default: locked) while location and operating hours stay editable; the customer's contact details are never shown to the dealer (the console makes no promise about it); staff management (`/dealers/me/employees`) requires an approved, trading dealer, so the owner of a suspended dealer cannot deactivate an employee (default kept). Ask the owner before changing any of these.

**Shortlist, settled by the owner (2026-09-11):** favourites are a `Shortlist` context. Account-only,
with no device-local list. The cap is **100** (`BusinessRules:MaxShortlistEntries`). A car that stops
being bookable **keeps its row** and is shown as "Currently unavailable / غير متاحة حاليًا" — still
named, with its gallery, and with no way to start a booking from it — **never auto-removed**, and
**never with a reason**: hidden, in maintenance, suspended and deleted are answered identically
everywhere else and this screen is not the exception. The name is read live and past the soft-delete
filter, deliberately, so that deletion does not become the one reason a customer can tell apart. See
pre-launch items 87-89 and `IShortlistReader`.

**A gallery may not approve a booking late (2026-09-11).** An approval must leave the customer the
WHOLE frozen `BookingTerms.PaymentWindow` before the rental starts, so `DecisionDeadline` is capped at
`Period.Start - PaymentWindow` rather than at the rental start. Putting the rule in that one column is
what keeps the availability predicate, the settlement sweep, the DTO flags and the console countdown
correct without any of them being touched. `Approve` restates the invariant, which is NOT redundant
for rows created before the change. Two consequences: `MinimumBookingLeadTimeMinutes` must STRICTLY
exceed `PaymentWindowHours` (validated at startup; 240 against 120 today, and the extra two hours are
both halves settled by the owner on 2026-09-11), and an approval now emails the customer, after the
commit, with failures logged and swallowed, because a mail server cannot be allowed to undo a
decision a gallery has made.

**The customer app's visual source of truth is the design handoff (2026-09-11)**, not
`Khadra.Dashboard/src/styles/_tokens.scss`. `KhadraColors`, `Radii`, `Shadows` and `KhadraTheme` in
`Khadra.Mobile/lib/core/theme/khadra_theme.dart` carry it, and NOTHING in that app names a colour or
a radius outside that file — no `Color(0x...)`, no `BorderRadius.circular` in a screen. Keep it that
way: it is what made adopting the handoff a change to a token list rather than a sweep through thirty
screens. Latin is Manrope, Arabic is Noto Kufi Arabic, and the fallback ORDER is load-bearing because
Kufi carries Latin too. The handoff is authoritative for LOOK ONLY: its BOOKING FLOW artboard shows
pay-before-approval and a simulated declined payment, and this platform has ruled out both.

**The customer app opens on Get Started, and guest browsing is a CHOICE (2026-09-12).**
A fresh install, cleared app data and a deliberate sign-out all land on `/welcome`,
which offers exactly three things: browse as a guest, sign in, create an account. The
gate is a device-local flag in ordinary preferences (`khadra.entry_chosen`, owned by
`EntryChoice`) and NOT a fourth `SessionStatus` — signed out is signed out whether or
not a choice was made, and folding a stored preference into the credential state
machine would put it in front of `restore()` and the router's refresh listener. It is
consulted at `/` ONLY: public routes render before the session resolves so that
`/verify-email?token=…` survives a cold start, and a gate across every route would
land that on a welcome screen with the single-use link unspent. Sign-out clears the
flag, so the next launch shows the same screen it was left on. An expiry or a
suspension does not: that customer has an account and chose long ago.

Two things the owner settled on 2026-09-12 when asked. **The three account tabs are
not redirected.** Bookings, Alerts and Profile each open and show one shared
`AccountRequired` panel offering both ways in; the data is already behind
authentication where it counts (the providers never call the API without a session,
and every one of those endpoints is refused server-side), while a redirect would leave
the tab shell — the bottom bar disappears — and Profile is where the language switch
lives, so gating it would strand an Arabic speaker who has not signed in. The hard
redirect stays for routes that ACT on an account: documents, edit profile, change
password, sessions, `/book`, `/bookings/*`, `/disputes/*`. **And sign-out
returns to Get Started**, not to the catalogue.

**Saved cars gained a TAB on 2026-09-20** (owner), so the bar is Home, Bookings,
Alerts, Saved, Profile — five destinations, and `bottom_nav_test.dart` measures that
they arrive whole at 375 and 360 in both languages. The tab is an ADDITION, settled
again on 2026-09-21: the entry in My Account stays exactly where it was and behaves
exactly as it did, pushing its own screen with a back arrow to Profile. So one screen,
two ways in, and they differ where it matters — `/saved` is a tab, absent from the
hard-redirect set, showing the shared `AccountRequired` panel to a guest the way the
other four tabs do; `/profile/saved` is a step taken from inside an account and keeps
its guard. `ShortlistScreen.asTab` is which. **Android Back on a tab** returns to Home
from the other four, and on Home asks once before it will close the app (`AppShell`, a
two-second window). Predictive back is off for the tabs as a consequence and untouched
everywhere else.

`khadra.session_owned` is what makes "must not restore a stale session" true rather
than hoped for — see `docs/auth-and-sessions.md`. It replaces `khadra.install_marker`.

**Minimum renter age: settled at 21** by the owner and enforced (`BusinessRules:MinimumRenterAge`, `RenterAgePolicy`). The spec still says "value pending Section 2 decision" in §5.1 and lists it as open in §2.2 — the document has not caught up with the decision. The code is right; the spec needs a revision.
