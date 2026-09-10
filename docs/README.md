# Khadra — documentation

Khadra is a car rental marketplace for Jordan. Customers rent from **licensed
green-plate rental offices**; the platform verifies each office's licence before it
may list a car, takes a card deposit at booking, and holds the record of what was
agreed.

This folder is the whole written record of the system. Start here.

---

## Read in this order

**Your first hour**

| | |
|---|---|
| [system-architecture.md](system-architecture.md) | What the pieces are, how a request travels, and why the shape is what it is |
| [local-development.md](local-development.md) | Get it running on your machine |
| [workflows.md](workflows.md) | What the platform actually does, end to end, per role |

**When you start changing things**

| | |
|---|---|
| [../CLAUDE.md](../CLAUDE.md) | The house rules. Not optional, and several were learned the hard way |
| [architecture-bounded-contexts.md](architecture-bounded-contexts.md) | The context map and what is built in each |
| [auth-and-sessions.md](auth-and-sessions.md) | Token lifetimes and the session contract, in reference form |
| [testing.md](testing.md) | How this is tested, and what the suites currently say |
| [security.md](security.md) | The security decisions, each with its reasoning |

**When you touch production**

| | |
|---|---|
| [production.md](production.md) | Topology, services, environment, deployment order, recovery |
| [deployment.md](deployment.md) | The per-setting reference: what each variable is and what breaks without it |
| [sql/README.md](sql/README.md) | Applying the schema and checking the administrator |

**Before you promise anything to a user**

| | |
|---|---|
| [pre-launch-checklist.md](pre-launch-checklist.md) | Everything knowingly deferred. An item comes off only by being fixed |
| [incidents.md](incidents.md) | Bugs that reached production, and what actually caused them |
| [spec-amendments.md](spec-amendments.md) | Where the code deliberately differs from the spec, and who decided |

**The specification**

`Car_Rental_System_v3.1.docx` is the product spec. `v3.0` is kept for history; the
two differ only in the header. Where the code and the spec disagree,
[spec-amendments.md](spec-amendments.md) says which is right and why.

---

## The shape of it, in one picture

```
   Customer's phone                 Dealer / Admin browser
   (Flutter, bearer tokens)         (Angular console)
            │                                 │
            │                                 ▼
            │                       ┌───────────────────┐
            │                       │   Khadra.Bff      │  cookie session, antiforgery,
            │                       │  (+ the console)  │  YARP proxy. The browser never
            │                       └─────────┬─────────┘  sees an API token.
            │                                 │
            ▼                                 ▼
   ┌──────────────────────────────────────────────────────┐
   │                    Khadra.WebAPI                     │
   │   /api/v1 · JWT bearer · rate limits · ProblemDetails│
   └───┬──────────┬───────────┬───────────┬───────────┬───┘
       │          │           │           │           │
   PostgreSQL  Supabase    Brevo      Nominatim     Redis
   (Supabase)  Storage     (email)   (geocoding)  (BFF sessions)
```

Four things are worth knowing immediately, because they explain most of the design:

1. **The browser never holds an API token.** The console talks to the BFF, which
   holds the session server-side in Redis and proxies to the API. The phone is
   different: it is a first-party client and carries bearer tokens directly.
2. **The platform holds identity documents.** Licence scans, commercial
   registrations, passports. That single fact drives the storage design, the
   signed-link scheme, and several of the refusals in [security.md](security.md).
3. **No screen invents data.** Every figure a user sees came from the database
   through an API built for that screen. There is no seeder and no fixture — see
   the standing rule in [../CLAUDE.md](../CLAUDE.md).
4. **Money does not move yet.** There is no merchant account, so every checkout is
   refused with `payments.provider_unavailable` and the boot log says
   `PAYMENTS ARE NOT ACCEPTED`. This is deliberate; see
   [workflows.md](workflows.md#payments-and-the-deposit).

---

## Repository layout

```
Khadra.Domain          aggregates, value objects, smart enums, repository interfaces
Khadra.Application     use cases (CQRS + MediatR), ports, validators, behaviours
Khadra.Infrastructure  EF Core, migrations, repositories, email, storage, geocoding
Khadra.WebAPI          /api/v1 controllers, auth, rate limiting, ProblemDetails
Khadra.Bff             cookie session + Redis + antiforgery + YARP; serves the console
Khadra.Dashboard       Angular 22 console (Admin and Dealer)
Khadra.Mobile          Flutter customer app
Khadra.Tests           xunit: Domain, Application, Persistence, Security
docs/                  this folder
docs/sql/              schema and operational SQL
```

Dependency direction is one way: `Domain ← Application ← Infrastructure ← WebAPI`.
The Domain references no framework at all.

---

## Current state

Three categories, kept apart on purpose — "it works on `main`" and "it is running in
production" are different claims, and conflating them is how a deploy gets skipped.

### ✅ Live in production

| | |
|---|---|
| **API** | `https://khadra.onrender.com` — healthy, database reachable |
| **BFF + console** | Deployed on Render, behind Cloudflare |
| **Database** | Supabase PostgreSQL, schema applied, reached through the session pooler |
| **Document storage** | **Private** Supabase bucket `khadra-documents`. The boot probe confirms it is reachable *and* private on every start |
| **Email** | Brevo over HTTPS, confirmed sender |
| **Forwarded headers** | Full Cloudflare → Render → loopback chain trusted, `ForwardLimit=3` |
| **Administrator** | Bootstrapped; the invitation was accepted |
| **Cities** | 4 — Amman, Al-Salt, Irbid, Zarqa. All active; **none has a centre pinned yet**, so the map asks the owner to place the pin rather than opening at the city |

### 🔧 Built and tested, not yet exercised in production

| | |
|---|---|
| Dealer registration → approval | The whole flow, including map, address and the three documents |
| Booking lifecycle | Every transition except the ones that need a payment |
| Disputes, reviews, fleet, employees, notifications | Built; awaiting real traffic |
| Reverse geocoding | Optional. Set `Geocoding__Provider` + `__UserAgent` to enable; unset, the form asks the owner to type the address |

### ⛔ Remaining pre-launch work

| | |
|---|---|
| **Payments** | **No merchant account.** Every checkout is refused with `payments.provider_unavailable` and the boot log says `PAYMENTS ARE NOT ACCEPTED`. Nothing simulates success, deliberately — pre-launch item 76 |
| Everything else | [pre-launch-checklist.md](pre-launch-checklist.md). An item comes off only by being fixed |

### Tests

| Backend | Dashboard | Mobile |
|---|---|---|
| **1093** | **44** | **53** |

Two backend tests need PostgreSQL running; see [testing.md](testing.md).

### Not built at all

Payouts, finance reporting, admin-editable platform settings. The console shows a
"not built" screen for these, naming what it will show and what is missing — better
than a screen of zeros nobody can tell apart from real data.
