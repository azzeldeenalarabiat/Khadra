# Local development

Getting Khadra running on your machine, and the handful of things that will
otherwise waste an afternoon.

---

## Prerequisites

| | Version | Notes |
|---|---|---|
| .NET SDK | **10.0.400** | Pinned in `global.json`. A different patch will refuse to build |
| Node.js | 20+ | For the Angular console |
| Docker Desktop | current | Postgres, Redis, Mailpit |
| Flutter | 3.44 / Dart 3.12 | Only if you are touching the mobile app |

---

## 1. Infrastructure

```bash
docker compose up -d
```

That gives you:

| | Where |
|---|---|
| PostgreSQL 18 | `localhost:5432` |
| Redis 8 | `localhost:6379` |
| Mailpit (catches all mail) | http://localhost:8025 |

Mailpit is where every email goes in development. Open it in a browser — you will
need it constantly, because verification, invitations and password resets all arrive
as links you have to click.

> `MP_SMTP_DISABLE_RDNS` is set in `docker-compose.yml` on purpose. Without it,
> Mailpit does a reverse-DNS lookup with nowhere to go, delays its SMTP greeting by
> eight seconds, and every send times out while the startup probe still says
> "Email ready". If mail stops arriving locally, check that first.

---

## 2. Secrets

Nothing secret is ever committed. Development values live in **user-secrets**.

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Port=5432;Database=khadra;Username=<user>;Password=<password>" \
  --project Khadra.WebAPI

dotnet user-secrets set "Authentication:Jwt:SigningKey" "$(openssl rand -base64 48)" \
  --project Khadra.WebAPI

dotnet user-secrets set "BffSecurity:RedisConnection" "localhost:6379" \
  --project Khadra.Bff
```

The user and password are whatever `docker-compose.yml` sets for Postgres.

`appsettings.Local.json` also works and is gitignored — but note it is re-added
*after* the host's own settings in `Program.cs`, so it outranks environment
variables set by a test host. That has surprised people.

---

## 3. Database

Development auto-applies migrations when `Database:AutoMigrate` is true. To do it
by hand:

```bash
dotnet ef database update --project Khadra.Infrastructure --startup-project Khadra.WebAPI
```

Adding one:

```bash
dotnet ef migrations add <Name> --project Khadra.Infrastructure \
  --startup-project Khadra.WebAPI --output-dir Persistence/Migrations
```

**Never hand-edit an existing migration.** Add a new one.

---

## 4. Run it

```bash
# API      → https://localhost:7012   (Scalar UI at /scalar/v1)
dotnet run --project Khadra.WebAPI --launch-profile https

# BFF      → https://localhost:7243   (needs Redis)
dotnet run --project Khadra.Bff --launch-profile https

# Console  → http://localhost:4200    (proxies /bff and /api to the BFF)
cd Khadra.Dashboard && npm start
```

For the mobile app:

```bash
cd Khadra.Mobile
flutter run -d chrome          # or a device
```

---

## 5. Getting an administrator

There is no seeded admin and no default password. The first one arrives as an
emailed invitation:

```bash
dotnet user-secrets set "Admin:Bootstrap:Email"    "you@example.com"  --project Khadra.WebAPI
dotnet user-secrets set "Admin:Bootstrap:Phone"    "0791234567"       --project Khadra.WebAPI
dotnet user-secrets set "Admin:Bootstrap:FullName" "Your Name"        --project Khadra.WebAPI
```

Restart the API, open **Mailpit**, click the invitation, choose a password.

Notes:

- It runs **only** on a database that has never held an administrator.
- The phone must be a valid Jordanian mobile (`07xxxxxxxx`); an invalid one stops
  startup with a message saying so.
- If nothing happens, the log says why — the address belongs to someone else, an
  admin already exists, the invitation is still live, and so on.

Then create the cities you need through the console. There is no seeder; the
dropdown is empty until an administrator adds them.

---

## 6. Tests

```bash
dotnet test Khadra.slnx                       # backend
cd Khadra.Dashboard && npx ng test --watch=false   # console
cd Khadra.Mobile   && flutter test                 # mobile
```

Some backend tests boot a real host and touch PostgreSQL. **If Docker is not
running you will see two failures** in `ForwardedHeaderTrustTests` and
`MailTransportConfigurationTests`, both `Failed to connect to 127.0.0.1:5432`. Start
Docker and they go green — see [testing.md](testing.md).

---

## 7. Optional: the things production has

None of these are needed to develop, and all of them degrade honestly.

### Real email

Development uses Mailpit. To send for real, set `Email:Provider` to `Brevo` (HTTPS,
port 443 — preferred) or `Smtp`. The API probes the transport on every start and
logs `Email ready…` or `EMAIL WILL NOT BE DELIVERED…` with the provider's own
reason. **Check that line before debugging anything else about email.**

Brevo's HTTP API key (`xkeysib-`) is *not* its SMTP key (`xsmtpsib-`); each is
refused by the other, and the probe says which you have.

### Reverse geocoding

Unset, address suggestions answer `503 geocoding.not_configured` and the form asks
the owner to type the address. To enable it you must set **both**:

```bash
Geocoding__Provider=Nominatim
Geocoding__UserAgent="Khadra/1.0 (+https://your-host; you@example.com)"
```

Setting the provider without a User-Agent **stops startup** — Nominatim blocks
unidentified callers, and a block is indistinguishable from the feature never
having worked.

### Document storage

Development uses `Local` (files under `App_Data/documents`). Production uses a
private Supabase bucket. See [production.md](production.md).

---

## 8. Conventions you will trip over

Read [../CLAUDE.md](../CLAUDE.md) properly, but these four catch everyone:

1. **No static data on a screen, ever.** Every figure comes from the database
   through an API built for that screen. No sample rows, no placeholder numbers, no
   business constant written as a literal. A screen with nothing behind it says so.
2. **Smart enums, not C# `enum`.** Status, type, role, reason.
3. **Business numbers come from `IBusinessRulesProvider`**, never a constant.
4. **Every value-object property mapped with `ToJson()` must be configured
   explicitly.** EF includes a property by convention only when it has a setter, and
   ours are get-only — so anything left to convention is silently dropped from the
   JSON with no error. This is how several frozen booking fields were lost once. A
   SQLite round-trip test must assert every field.

And on the frontend: standalone components, `OnPush`, `.component.ts` +
`.component.html` only (no per-component styles, no `style=` attributes), all calls
through the BFF, logical CSS properties for RTL, a currency code on every money
value.
