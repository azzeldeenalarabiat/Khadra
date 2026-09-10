# Production

Topology, configuration, deployment order and recovery.

**No secret appears in this file.** Every credential is named, never written down.
Real values live only in Render's environment and, for development, in user-secrets.

> **What is live right now.** The API and the BFF are deployed and healthy. The
> schema is applied. Documents are on the **private** Supabase bucket
> `khadra-documents`, confirmed reachable and private by the boot probe. Brevo is
> sending. The forwarded-header chain is trusted end to end. An administrator exists
> and has accepted their invitation, and four cities are configured — Amman,
> Al-Salt, Irbid, Zarqa, none with a centre point yet.
>
> The one thing still missing is a **merchant account**: no payment can be taken.
> See [README.md](README.md#current-state) for the full breakdown.

For what each individual setting *does* and what breaks without it, see
[deployment.md](deployment.md). This page is about the system as a whole.

---

## 1. What is deployed where

| | Runs on | Address | Notes |
|---|---|---|---|
| **API** (`Khadra.WebAPI`) | Render web service, Docker | `https://khadra.onrender.com` | Public. The phone app calls it directly |
| **BFF + console** (`Khadra.Bff`) | Render web service, Docker | the `khadra-bff` service URL | Serves the Angular console and proxies to the API |
| **PostgreSQL** | **Supabase** | session pooler host | *Not* on Render |
| **Redis** | Render Key Value | private | BFF sessions + Data Protection key ring |
| **Document storage** | **Supabase Storage** | private bucket | No public URL, ever |
| **Email** | **Brevo** | HTTPS API | Not SMTP — see §6 |
| **Reverse geocoding** | Nominatim (OpenStreetMap) | public | Optional |
| **Edge** | **Cloudflare**, in front of Render | — | Render fronts every service with it |

`render.yaml` declares **only** the BFF and the Key Value instance. The API service
and the database already exist and are managed by hand — the Blueprint deliberately
does not touch them.

### How they relate

```
        Cloudflare edge
              │
     ┌────────┴────────┐
     ▼                 ▼
  khadra-bff       khadra (API)  ◀──── Flutter app (bearer tokens)
     │                 │
     │  YARP + bearer  │
     └────────────────▶│
                       ├──▶ Supabase PostgreSQL   (session pooler)
                       ├──▶ Supabase Storage      (private bucket)
                       ├──▶ Brevo                 (HTTPS)
                       └──▶ Nominatim             (optional)
     │
     └──▶ Render Key Value (Redis): session tickets + key ring
```

**The BFF→API hop matters.** Prefer the private address (`http://khadra:8080/`) so
the API sees this container and honours the `X-Forwarded-For` the BFF sends. Over
the public URL the API sees Render's shared outbound address for the entire console
population, collapsing per-address rate limits into one bucket.

> **Render free web services cannot *receive* private network traffic.** If the API
> is on a free plan the private address will not work and you must use the public
> URL, with the collapse above as the cost. Do not "fix" that by trusting Render's
> egress ranges — every tenant in the region shares them.

---

## 2. Environment — the API (`khadra`)

Double underscore is the separator: `Authentication__Jwt__SigningKey` sets
`Authentication:Jwt:SigningKey`. **Enter bare values in the dashboard.** Quotes in
the examples below are shell syntax; a dashboard field keeps them, and
`"Brevo"` with quotes matches no transport (this has happened — see
[incidents.md](incidents.md)).

### Will not start without these

| Variable | What it is |
|---|---|
| `ConnectionStrings__DefaultConnection` | Supabase **session pooler**, not the direct host (§4) |
| `Authentication__Jwt__SigningKey` | ≥ 32 bytes. Generate with `openssl rand -base64 48` on the machine that will hold it |

### Will not start with these wrong

| Variable | Value |
|---|---|
| `Email__Provider` | `Brevo` |
| `Documents__Provider` | `Supabase` |

Both default to something safe-for-development and disastrous-for-production
(`Logging` writes mail to the log; `Local` puts documents on a disk that is deleted
every deploy), so **Production refuses to boot on either default**.

### Required for correct behaviour

| Variable | Value |
|---|---|
| `Email__ApiKey` | Brevo **HTTP API** key, begins `xkeysib-` |
| `Email__FromAddress` | An address **confirmed** in Brevo |
| `App__ClientBaseUrl` | The **console** (BFF) address, no trailing slash. Every emailed link is a page in that console |
| `Documents__Supabase__Url` | `https://<project-ref>.supabase.co` |
| `Documents__Supabase__Bucket` | The private bucket name |
| `Documents__Supabase__ServiceKey` | A project **secret** key. Bypasses RLS — see §5 |
| `KnownProxies__0..3` | The forwarded-header trust list (§7) |
| `ForwardedHeaders__ForwardLimit` | `3` (§7) |

### Optional

| Variable | Effect if unset |
|---|---|
| `Geocoding__Provider` / `Geocoding__UserAgent` | Address suggestions answer `503`; the form asks the owner to type it. **Set both or neither** — the provider without a User-Agent stops startup |
| `Admin__Bootstrap__Email` / `__Phone` / `__FullName` | Only for the very first boot. Remove afterwards |

Leave `ASPNETCORE_ENVIRONMENT` **unset** — it defaults to Production, which is what
arms the guards above.

Do **not** set `ASPNETCORE_HTTPS_PORTS` or an `https://` URL. Behind a proxy that
has already terminated TLS, `UseHttpsRedirection()` with no HTTPS port logs one
warning and does nothing, which is correct. Give it one and every request redirects
in a loop.

---

## 3. Environment — the BFF (`khadra-bff`)

Declared in `render.yaml`, so a Blueprint sync sets most of it.

| Variable | Value |
|---|---|
| `BffSecurity__ApiBaseUrl` | Where the API is (§1) |
| `ReverseProxy__Clusters__webapi__Destinations__primary__Address` | The same value |
| `BffSecurity__RedisConnection` | Wired automatically from the Key Value instance |
| `KnownProxies__0..3` | Same trust list as the API |
| `ForwardedHeaders__ForwardLimit` | `3` |

The BFF stores no documents and sends no mail, so it needs neither set of
credentials.

---

## 4. Supabase — PostgreSQL

**Use the session pooler.** This cost days once and nothing in the error names it.

| | Host | IPv4? |
|---|---|---|
| Direct | `db.<ref>.supabase.co:5432` | **No** — IPv6 only on the free tier |
| **Session pooler** | `aws-0-<region>.pooler.supabase.com:5432` | **Yes**, every tier |
| Transaction pooler | same host, `:6543` | Yes, but **no prepared statements** |

Render has no IPv6 outbound, so the direct host fails at DNS with an error that says
nothing about addressing: the app starts, `/health/live` answers 200, and every
request touching data returns 500.

Not the transaction pooler either — Npgsql uses prepared statements by default and
EF migrations rely on them.

Two details that catch people:

- The username carries the project ref: `postgres.<ref>`, not `postgres`.
- The region in the pooler host is the **database's** region, not the app's.

URL form is accepted and converted internally, and `SSL Mode=Require` is added.

### Schema

Render runs `preDeployCommand` only on paid instances; on a free one it is accepted
in the dashboard and silently never runs. So the schema is applied from **Supabase's
SQL editor** instead:

| File | Use |
|---|---|
| [sql/khadra-schema.sql](sql/khadra-schema.sql) | A database that does not exist yet. Idempotent |
| [sql/2026-09-10-dealer-address.sql](sql/2026-09-10-dealer-address.sql) | The address columns, for a database that already exists |
| [sql/supabase-lockdown.sql](sql/supabase-lockdown.sql) | Revokes PostgREST access from `anon`/`authenticated` |
| [sql/verify-admin.sql](sql/verify-admin.sql) | Read-only: is there an administrator, and can they sign in? |

`Database__AutoMigrate` is honoured **only in Development**, by design — nobody
migrates production by flipping a flag.

---

## 5. Supabase — Storage

Create the bucket **before** deploying. The API will not create it: a bucket created
by accident is a bucket whose visibility nobody chose.

Dashboard → **Storage** → **New bucket**:

| | |
|---|---|
| Name | e.g. `khadra-documents` |
| **Public** | **OFF** — this is the entire design |
| File size limit | `8 MB`, matching `Documents:MaximumSizeBytes` |
| Allowed MIME types | `image/jpeg, image/png, image/webp, application/pdf` |

No RLS policies are needed: a private bucket refuses the anon and authenticated keys
outright, and a secret key bypasses RLS by design.

**The API checks all of this at every boot** and refuses to start if the bucket is
missing, the credential is refused, or **the bucket is public**. That last check
exists because it is the one failure the application could never notice by itself —
it never asks for a public URL, so a bucket flipped public in the dashboard would
look, from inside, exactly like a working private one.

### Narrow the project, not the credential

A secret key bypasses RLS across the **whole project**, including PostgreSQL. Khadra
uses no PostgREST, so:

- **Disable the Data API** (Settings → API), or remove `public` from the exposed
  schemas. After that the key reaches Storage and an empty Auth, and nothing else.
- Create **one key dedicated to this API** so it can be revoked on its own.

Supabase is retiring the legacy `service_role` JWT in favour of `sb_secret_…` keys.
Both work: the API sends `apikey` always and adds `Authorization: Bearer` only when
the key is a JWT, because a secret key is *rejected* if sent as a bearer token.

---

## 6. Brevo — email

Use the **HTTPS API**, not the SMTP relay. A network that blocks outbound 587 fails
in the worst possible way: the TCP handshake succeeds, the SMTP greeting never
arrives, and every message stalls until it times out.

| Setting | Value |
|---|---|
| `Email__Provider` | `Brevo` |
| `Email__ApiKey` | The key from *Settings → API keys*, beginning `xkeysib-` |
| `Email__FromAddress` | An address **confirmed** under *Senders, Domains & Dedicated IPs* |

The `xkeysib-` API key is **not** the `xsmtpsib-` SMTP key. Each is refused by the
other, and the startup probe says which you have.

**Read the boot line before debugging anything else about email**, category
`Khadra.Email`:

```
Email ready. Brevo accepted the API key over HTTPS. Sending as …, a confirmed sender…
EMAIL WILL NOT BE DELIVERED. …the provider's own reason…
```

An unconfirmed sender is the commonest way a correct key still delivers nothing —
Brevo only says so at send time, one message at a time, so the probe says it once at
startup instead.

---

## 7. Cloudflare and forwarded headers

Render fronts every service with Cloudflare and connects to the container over
**loopback**. A real production request looks like this:

```
transport peer      ::1                  Render's router, over loopback
X-Forwarded-For     176.29.3.177,        the visitor
                    172.69.173.136,      a Cloudflare edge
                    10.24.207.134        Render's load balancer
X-Forwarded-Proto   https
Cf-Connecting-Ip    176.29.3.177
```

Three hops, so **`ForwardedHeaders__ForwardLimit=3`**, and every hop must be trusted:

| Variable | Contents |
|---|---|
| `KnownProxies__0` | `::1,127.0.0.0/8` — the platform router |
| `KnownProxies__1` | `10.0.0.0/8,172.16.0.0/12,192.168.0.0/16` — Render's private network |
| `KnownProxies__2` | Cloudflare's IPv4 ranges, comma-separated, from `cloudflare.com/ips-v4` |
| `KnownProxies__3` | Cloudflare's IPv6 ranges, from `cloudflare.com/ips-v6` |

One entry may carry a comma-separated list, so this is five variables rather than
twenty-five.

Three things worth understanding, each learned expensively:

1. **`ForwardLimit` is a ceiling, not a target.** Trust is re-checked at each hop
   against the address just consumed, so the walk stops at the first untrusted
   address however high the number is. Raising it alone does nothing.
2. **Trusting only `::1` is not enough.** It fixes the scheme — which makes sign-in
   start working, so it is tempting to stop there — but resolves Render's load
   balancer as the client for every visitor. The sign-in cap is keyed on
   `{address}|{account}`, so a constant address turns it into a per-account bucket
   shared with an attacker.
3. **This is not "trust everything".** The guarantee is checked per request from the
   transport peer outward, and the only way in is to *connect* from a trusted range.
   Nothing on the internet can source `::1` or `10.x`, and only Cloudflare can
   source Cloudflare's ranges. Cloudflare **appends** rather than replaces, so
   anything a caller invents lands to the left of their true address and the
   right-to-left walk reaches the truth first.

Both services cross-check the resolved address against `Cf-Connecting-Ip` and warn
on disagreement — which catches a stale Cloudflare list and a request that never
traversed Cloudflare, the only two ways this configuration fails.

Refresh the ranges when that warning appears. A stale list degrades toward resolving
a Cloudflare edge, never toward believing a caller.

---

## 8. Deployment

### Images

Both build from the **repository root** — .NET restore needs `global.json`,
`Directory.Build.props`, `Directory.Packages.props` and `.editorconfig`, which live
there rather than beside a project:

```bash
docker build -f Khadra.WebAPI/Dockerfile -t khadra-webapi .
docker build -f Khadra.Bff/Dockerfile    -t khadra-bff    .
```

The BFF image contains the console: a Node stage builds it into `wwwroot`. Build off
the server — Angular needs Node and roughly 2 GB.

Both images are Debian-based (`aspnet:10.0-noble`) rather than Alpine on purpose:
`AdminDashboard:ReportingTimeZone` is validated at startup with
`TryFindSystemTimeZoneById`, and Alpine ships no tzdata, so `Asia/Amman` would fail
with a message that reads like a typo.

### Order

Ordering matters. Both guards below refuse to start rather than run wrongly, so
getting this backwards means a failed deploy, not a silent problem.

1. **Schema** — run any outstanding SQL in Supabase. Additive, nullable columns are
   safe to apply *before* the new build: EF names its columns explicitly, so the
   running older API neither sees nor touches them.
2. **Storage** — create the bucket, private, with its limits.
3. **Environment** — set the variables. Bare values, no quotes.
4. **Deploy the API** (`khadra`). Watch the boot log for the three lines that matter:
   `Email ready…`, `Document storage. …bucket is private.`, and
   `Forwarded headers trusted from: …, reading 3 hop(s) from the right.`
5. **Deploy the BFF** (`khadra-bff`), if the console changed.

Reversed, the new API would query columns that do not exist yet.

### Verifying afterwards

| Check | How |
|---|---|
| API alive | `GET /health/live` → 200 |
| Database reachable | `GET /health/ready` → 200 |
| Console loads | Open the BFF URL; the sign-in page should be styled |
| Sign-in works | `GET /bff/antiforgery` must be **200**, not 500 |
| Client address correct | Sign in, open **Profile → Where you are signed in**. It must show *your* address, not a `10.x` or a Cloudflare one |
| Mail works | The boot line, then a real password reset |
| Documents work | Submit a dealer application and open a document as an admin |

---

## 9. Recovery

### The console loads but nobody can sign in

`/bff/antiforgery` returning 500 means the request is not seen as HTTPS, so the
`__Host-` antiforgery cookie cannot be issued. Almost always the forwarded-header
trust list (§7). The BFF logs the transport peer and every forwarding header it
received; read that before changing anything.

### No email is arriving

Read the boot line first (§6). If it says `Email ready`, the transport answered — so
the problem is downstream: an unconfirmed sender, or the address genuinely has no
account. The password-reset diagnostics (events `1300`–`1304`) say which, without
logging the address.

### Documents 404 for the administrator

If *every* document 404s, suspect the bucket name rather than the documents. The API
distinguishes a missing object from a missing bucket and says which; a credential
problem is never reported as a missing document.

### The API will not start

It refuses on purpose in a handful of cases, each with a message naming the setting:
`Email:Provider` selecting the Logging transport, `Documents:Provider` keeping files
on the container, an unrecognised value for either, `KnownProxies` empty, a missing
or public storage bucket, a bad `Geocoding:UserAgent`, or an invalid
`Admin:Bootstrap` phone.

### Everyone was signed out at once

The Redis instance restarted. **Free Key Value instances do not persist to disk** —
Render's own documentation: "whenever an instance restarts, all of its data is
lost." It holds the session tickets *and* the Data Protection key ring, so losing it
signs everyone out. They sign in again; nothing else is lost. This is the reason to
move to a paid plan, not capacity.

### Something leaked into the log

If mail ever ran on the `Logging` transport, every message — **including its link** —
was written to the application log, and those links are working credentials until
their token expires: 60 minutes for a reset, 24 hours for verification, **7 days for
an invitation**. Search the retention window, then consume anything still live with
the statement in [sql/verify-admin.sql](sql/verify-admin.sql). Do it *after* fixing
the transport, or the replacement is written straight back into the same log.

---

## 10. Backups

`pg_dump` covers the database. It does **not** cover documents: licence scans and
identity papers live in Supabase Storage and are not in the database. They belong in
the same backup plan, because they are identity papers and losing them is not a
performance event.
