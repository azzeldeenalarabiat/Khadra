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

# Required — see "Who the client is", below. Values are for Render behind
# Cloudflare; an entry may carry a comma-separated list.
KnownProxies__0="::1,127.0.0.0/8"
KnownProxies__1="10.0.0.0/8"
KnownProxies__2="<the 15 ranges from cloudflare.com/ips-v4, comma-separated>"
KnownProxies__3="<the 7 ranges from cloudflare.com/ips-v6, comma-separated>"
ForwardedHeaders__ForwardLimit="3"

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

`ForwardedHeaders__ForwardLimit` is the other half. It counts hops **from the
right**, and it is a **ceiling, not a target**: trust is re-checked at every hop
against the address just consumed, so the walk stops at the first address no
configured range covers however high the number is.

That one sentence invalidates the procedure this page used to give — "raise it by
one and repeat" can never work, because raising it alone does nothing. Measured
against the real chain, with only the loopback hop trusted, limits of 1, 2 and 3
resolved the identical address. **The trust list is what moves the walk; the limit
only permits it.**

### The chain on Render, as captured

```
transport peer      ::1                  Render's router, over loopback
X-Forwarded-For     176.29.3.177,        the visitor
                    172.69.173.136,      a Cloudflare edge
                    10.24.207.134        Render's load balancer
X-Forwarded-Proto   https
Cf-Connecting-Ip    176.29.3.177
```

Three hops, so `ForwardLimit=3`, and every one of them has to be trusted:
loopback, `10.0.0.0/8`, and Cloudflare's published ranges.

**Trusting only `::1` is not enough, even though it makes sign-in start working.**
It fixes the scheme, so the `__Host-` antiforgery cookie can be issued and the 500
goes away — which is exactly what makes it tempting to stop there. But the walk
then resolves `10.24.207.134`, Render's load balancer, for every visitor. The
sign-in cap is keyed on `{address}|{account}`, so a constant address turns it into
a *per-account* bucket shared with the attacker: ten requests a quarter hour aimed
at a named administrator holds that account shut, and the victim cannot move out
of the way. The address-only limits collapse outright, and every session records
the same address, so "Where you are signed in" can no longer tell you anything.

**Do not pad the limit "for safety" either.** When a visitor's own address falls
inside a trusted range — a Cloudflare Worker fetching this origin — the walk does
not stop at them, and one hop too many consumes a value they supplied. Measured:
with a Worker visitor and a prepended entry, 3 resolves the Worker and 4 resolves
the junk. The limit is part of the guarantee.

### Why this is not "trust everything"

The guarantee is checked per request from the transport peer outward, and the only
way to exploit it is to *connect* from a trusted range. Nothing on the internet can
source `::1` or `10.x`, and only Cloudflare can source Cloudflare's ranges.
Cloudflare **appends** the address it accepted the connection from to whatever the
caller sent rather than replacing it, so anything a caller invents lands to the
*left* of their true address and the right-to-left walk reaches the truth first.

### Confirming it

1. Read the startup line: `Forwarded headers trusted from: … , reading 3 hop(s)
   from the right.`
2. Sign in and open **Profile → Where you are signed in**. It must show your own
   address, not a `10.x` or a Cloudflare one.
3. Watch for `CLIENT ADDRESS DISAGREES WITH CLOUDFLARE` in the log. Both services
   cross-check the resolved address against `Cf-Connecting-Ip` — which Cloudflare
   overwrites on ingress, so on a request that really came through Cloudflare the
   two are the same fact reached two independent ways. A disagreement means either
   a Cloudflare range is missing here (they change rarely, but they do change) or
   the request never passed through Cloudflare. That header is cross-checked and
   never believed: trusting it would let the caller name its own partition.
4. Prove the hole is shut: send `X-Forwarded-For: 1.2.3.4` on a sign-in and
   confirm the sessions screen still records your real address.

Refresh the Cloudflare list from `cloudflare.com/ips-v4` and `/ips-v6` when the
warning in step 3 appears. A stale list degrades toward resolving a Cloudflare
edge — never toward believing a caller.

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
