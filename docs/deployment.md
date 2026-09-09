# Deploying Khadra

Written after a first deployment to Render failed three times in a row, each on a
different missing setting. The API validates its configuration at startup and
refuses to run when something is missing rather than starting in a state that is
wrong in a way nobody notices — so a first deployment surfaces every gap, one
crash at a time, unless you set them all up front.

This page lists everything. Nothing here is a secret; the secrets are named, not
written down.

## What is actually missing from a built image

`appsettings.json` ships inside the image and carries sensible values for almost
everything: the business rules, the reporting time zone, the document root, the
token lifetimes. Exactly **two** settings have no default anywhere, and each one
stops the process:

| Setting | Why it has no default |
|---|---|
| `ConnectionStrings__DefaultConnection` | There is no sensible database to guess at. |
| `Authentication__Jwt__SigningKey` | A default signing key is a published signing key. At least 32 bytes. |

A third and fourth pass validation and are still wrong for production, which is
worse, because nothing complains:

| Setting | Ships as | Consequence if left |
|---|---|---|
| `App__ClientBaseUrl` | `https://localhost:7243` | Every verification, reset and invitation email links to localhost. The send succeeds. |
| `Email__Provider` | `Logging` | Mail is written to the log and delivered to nobody. |

## Generating the signing key

Generate it on the machine that will hold it, so it never passes through a chat
window, a ticket, or a shell history on a shared box:

```bash
openssl rand -base64 48
```

48 random bytes, base64-encoded, comfortably over the 32-byte minimum. Note what
it protects: access tokens, and — through HKDF — the document-link and
upload-ticket keys. Rotating it invalidates 15-minute access tokens and
sub-15-minute tickets and nothing else, so rotation is cheap. Never reuse the
development value.

## The full environment

Double underscore is the separator: `Authentication__Jwt__SigningKey` sets
`Authentication:Jwt:SigningKey`.

```bash
# Required — the process will not start without these
ConnectionStrings__DefaultConnection="Host=…;Port=5432;Database=khadra;Username=…;Password=…;SSL Mode=Require;Trust Server Certificate=true"
Authentication__Jwt__SigningKey="<openssl rand -base64 48>"

# Required — the process starts without these and behaves wrongly
App__ClientBaseUrl="https://console.example.com"
Email__Provider="Brevo"
Email__ApiKey="<brevo key>"
Email__FromAddress="<a CONFIRMED sender on the Brevo account>"

# Required — see "Who the client is", below
KnownProxies__0="10.0.0.0/8"
KnownProxies__1="172.16.0.0/12"
KnownProxies__2="192.168.0.0/16"
ForwardedHeaders__ForwardLimit="1"

# Only for the very first boot, to invite the first administrator
Admin__Bootstrap__Email="…"
Admin__Bootstrap__FullName="…"
Admin__Bootstrap__Phone="…"
```

Leave `ASPNETCORE_ENVIRONMENT` unset. It defaults to Production, and several
guards — the `KnownProxies` one especially — deliberately do nothing in
Development.

Do **not** set `ASPNETCORE_HTTPS_PORTS` or an `https://` URL. Both hosts call
`UseHttpsRedirection()`; with no HTTPS port known it logs one warning and does
nothing, which is what you want behind a proxy that has already terminated TLS.
Give it one and every request redirects in a loop.

## Supabase: use the session pooler, not the direct connection

This one cost days, and nothing in the error names it.

Supabase offers three connection strings, and the one the dashboard shows first is
the one that cannot work from most hosts:

| | Host | IPv4? |
|---|---|---|
| Direct | `db.<ref>.supabase.co:5432` | **No** — IPv6 only, unless the paid IPv4 add-on is on |
| **Session pooler** | `aws-0-<region>.pooler.supabase.com:5432` | **Yes**, on every tier |
| Transaction pooler | `aws-0-<region>.pooler.supabase.com:6543` | Yes, but no prepared statements |

Resolved for a real free-tier project:

```
db.<ref>.supabase.co            A     (none)
                                AAAA  2406:da18:e5c:b700:…
aws-0-ap-southeast-1.pooler…    A     52.77.146.31, 52.74.252.201
```

The direct host publishes **no A record at all** on the free tier. Render, Fly and
most container platforms have no IPv6 outbound, so the connection fails at DNS —
and surfaces as a name-resolution error that says nothing about addressing. The
application starts, `/health/live` answers 200, and every request touching data
returns 500.

**Use the session pooler.** Not the transaction pooler on 6543: it does not support
prepared statements, which Npgsql uses by default and EF migrations rely on.

Two details that catch people:

- The username carries the project ref — `postgres.<ref>`, not `postgres`.
- The region in the pooler host is the DATABASE's region, not the app's. A database
  in Singapore is `ap-southeast-1` however far away the service runs.

```bash
ConnectionStrings__DefaultConnection=postgresql://postgres.<ref>:<password>@aws-0-<region>.pooler.supabase.com:5432/postgres
```

URL form is fine — the application converts it, and adds `SSL Mode=Require`, which
Supabase needs.

## Who the client is

`KnownProxies` is a **security input**, not a formality.
`ForwardedHeadersMiddleware` only checks who sent `X-Forwarded-For` when it has
something to check against, so an empty list does not mean "trust nobody", it
means "trust anybody" — and the header is what every rate limit partitions on,
including the 10-per-15-minutes cap that is the only brute-force protection on
password sign-in. The API refuses to start rather than run that way.

`ForwardedHeaders__ForwardLimit` is the other half, and it is the half that is
easy to get wrong quietly. It counts hops **from the right**. One is correct when
the only thing in front is a proxy this deployment owns. On a managed platform it
usually is not: Render, for one, fronts a service with Cloudflare *and* its own
load balancer, and puts the real visitor **first** in the header. Read one hop
from the right there and you resolve a piece of the platform's infrastructure —
the same address for every visitor on earth. That is not a spoofing hole, but it
collapses every limit into one bucket, and ten failed sign-ins by anybody lock
the whole platform out for fifteen minutes.

**Measure it; do not assume it.** Neither Render nor Cloudflare publishes a fixed
inbound address, so there is no number to look up.

1. Read the startup line: `Rate limiting will identify clients by
   X-Forwarded-For, trusted only from: … , reading N hop(s) from the right.`
2. Sign in from an address you know, and open **Profile → Where you are signed
   in**. That screen shows the address the API recorded.
3. Your own address means the count is right. A platform address means raise
   `ForwardedHeaders__ForwardLimit` by one and repeat.
4. Then prove the hole is shut: send `X-Forwarded-For: 1.2.3.4` from outside on a
   sign-in and confirm the sessions screen still records your real address.

## Migrations

`Database__AutoMigrate` is honoured **only in Development**, by design — nobody
can migrate production by flipping a flag. The schema is applied as its own step
before the new version serves traffic.

Ordering is not optional: `AdminBootstrapper` queries `users` during API startup
without guarding for a missing table, so an API that starts before the schema
exists crash-loops.

## Uploaded documents outlive the container

`Documents__RootPath` defaults to `App_Data/documents` **inside the container**.
On a platform with an ephemeral filesystem — Render included — that directory is
gone on the next deploy, taking every gallery's commercial registration and every
customer's licence and passport with it.

Attach persistent storage and point `Documents__RootPath` at it. Two things
follow from the image running as the non-root `app` user: the mount must be
writable by that user, and these files belong in the backup plan beside
`pg_dump`, because they are identity papers and they are not in the database.

## Images

Both build from the **repository root**, because .NET restore needs `global.json`,
`Directory.Build.props`, `Directory.Packages.props` and `.editorconfig`, and those
live there rather than beside a project:

```bash
docker build -f Khadra.WebAPI/Dockerfile -t khadra-webapi .
docker build -f Khadra.Bff/Dockerfile    -t khadra-bff    .
```

The BFF image contains the admin console: it is built in a Node stage and copied
into `wwwroot`, because `Khadra.Bff` serves it and falls back to `index.html` for
client-side routes. There is no separate console service to deploy.

Both images are Debian-based (`aspnet:10.0-noble`) rather than Alpine on purpose:
`AdminDashboard:ReportingTimeZone` is validated at startup with
`TryFindSystemTimeZoneById`, and Alpine ships no tzdata, so `Asia/Amman` would
fail with a message that reads like a typo.

Build off the server. Angular needs Node and roughly 2 GB; a small instance will
run out of memory mid-build.
