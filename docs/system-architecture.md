# System architecture

What the pieces are, how a request travels through them, and why each boundary is
where it is.

---

## 1. The services

| Service | Project | What it is | Who talks to it |
|---|---|---|---|
| **API** | `Khadra.WebAPI` | The whole business. `/api/v1`, JWT bearer, rate limited, RFC 9457 errors | The BFF, and the mobile app directly |
| **BFF** | `Khadra.Bff` | Browser session, antiforgery, YARP proxy. **Also serves the Angular console** from `wwwroot` | Browsers only |
| **Console** | `Khadra.Dashboard` | Angular 22. Admin screens and the Dealer console | Built into the BFF image |
| **Mobile** | `Khadra.Mobile` | Flutter customer app | Calls the API directly |

The console is **not separately deployable**. A Node stage in the BFF's Dockerfile
builds it into `wwwroot`, and the BFF falls back to `index.html` for client-side
routes. One service, one deploy.

### Backing services

| | Used for | Notes |
|---|---|---|
| **PostgreSQL** (on Supabase) | Everything transactional | Reached through the *session pooler*, not the direct host — see [deployment.md](deployment.md) |
| **Supabase Storage** | Dealer and customer documents | A **private** bucket. No public and no signed URLs |
| **Redis** (Render Key Value) | BFF session tickets **and** the Data Protection key ring | Losing it signs everyone out; it is not a cache |
| **Brevo** | Transactional email over HTTPS | Verification, invitations, password resets |
| **Nominatim** (OpenStreetMap) | Reverse geocoding for the dealer map | Optional; absent, the form asks the owner to type the address |

---

## 2. Two client models, and why they differ

This is the first thing that confuses people.

### The browser: no token ever reaches it

```
browser ──cookie──▶ Khadra.Bff ──bearer──▶ Khadra.WebAPI
                        │
                        └── session ticket in Redis
```

- The session cookie is `__Host-Khadra.Session`: `HttpOnly`, `SecurePolicy.Always`,
  `SameSite=Lax`, and the `__Host-` prefix, which forbids a `Domain` attribute and
  requires `Secure` — so it cannot be set by a subdomain.
- The **ticket** lives in Redis; the cookie is only a key to it. The access token
  the API wants never enters the browser, so no XSS can read it.
- Every state-changing call carries an antiforgery token
  (`__Host-Khadra.Antiforgery`, `SameSite=Strict`), checked before the proxy runs.
- YARP strips the browser's `Cookie`, `Authorization` and XSRF headers on the way
  out and attaches the server-held bearer token instead. **The browser never
  chooses the API identity.**

### The phone: a first-party bearer client

The Flutter app calls `https://khadra.onrender.com` directly with a bearer token
and a refresh token. There is no BFF in the path, because there is no browser to
protect from itself — a native app has no XSS and no CSRF.

This is why the API is **publicly reachable**, and why its rate limiting and
forwarded-header configuration matter as much as the BFF's.

---

## 3. How a request travels

### Console request

```
1. Browser              GET /dealers                     (Angular route, no network)
2. Browser              GET /api/v1/dealers?page=1       cookie + XSRF header
3. Cloudflare           adds CF-Connecting-IP, X-Forwarded-*
4. Render load balancer appends its hop, connects to the container over LOOPBACK
5. Khadra.Bff           UseForwardedHeaders → resolves the visitor, sees https
                        cookie → Redis ticket → access token
                        antiforgery checked, YARP forwards with Bearer
6. Khadra.WebAPI        JWT validated + security stamp checked against the database
                        rate limiter partitions on the resolved client address
                        handler → repository → PostgreSQL
7. back up the chain
```

Steps 3–5 are load-bearing and were the subject of a production incident; see
[incidents.md](incidents.md#the-console-loaded-and-nobody-could-sign-in).

### Document request

```
Admin opens a licence scan
  → console asks the API for the dealer review payload
  → API returns one SHORT-LIVED, HMAC-SIGNED link per document, on OUR domain
  → browser GETs /api/v1/documents/{token}?expires=&signature=
  → API verifies the signature, then reads the bytes from the private bucket
  → streamed back with Cache-Control: no-store, private
```

### Renter document request

The gallery's right to a renter's licence lasts exactly as long as the booking is
live, so it is re-checked per request instead of frozen into a link:

```
Gallery presses "View driving licence" on a booking
  → browser GETs /api/v1/bookings/{bookingId}/renter-documents/{documentId}
  → API re-runs the whole rule: dealer staff → membership → this dealership's
    booking → the booking is LIVE → the document belongs to that booking's renter
  → API reads the bytes from the private bucket
  → API COMMITS a `Viewed` row to document_access_entries — and if it cannot,
    the bytes are not served
  → streamed back with Cache-Control: no-store, private
```

Pressing "Mark as reviewed" walks the same rule and then writes two rows in one
transaction: the review on the booking, and a `Reviewed` row on the log. It records
that the DEALERSHIP looked — never that Khadra verified anything.

No storage URL exists anywhere in either chain, and the second one puts no storage
key in the browser either. See
[security.md](security.md#documents-are-reached-through-this-platform-never-by-address).

---

## 4. Layering

```
Khadra.Domain          ← no framework references at all
      ↑
Khadra.Application     ← use cases, ports; never touches HttpContext
      ↑
Khadra.Infrastructure  ← EF Core, HTTP clients, the outside world
      ↑
Khadra.WebAPI          ← controllers; no business logic
```

The rules that keep this honest are in
[`.claude/rules/backend/architecture.md`](../.claude/rules/backend/architecture.md).
The ones that matter most day to day:

- **Aggregates** have private setters and static factories. Behaviour methods return
  `Result`/`UnitResult<Error>` for expected failures. `DomainException` is only for
  programming errors.
- **Smart enums** (`Enumeration`), never a C# `enum`. Status, type, role, reason —
  all of them. They persist as their string name.
- **Cross-context references are by `Id` only.** No navigation properties across
  contexts, no EF relationships, no joins. A context that needs another's data asks
  through a port.
- **Handlers return `Result<T, Error>`.** `Error.Kind` maps to an HTTP status in
  `ApiControllerBase.Failure`. Business outcomes are never exceptions.
- **Business numbers come from `IBusinessRulesProvider`**, never a constant. The one
  deliberate exception is the delivery fee, which belongs to the dealership.

### Bounded contexts

`IdentityAccess`, `Dealers`, `Fleet`, `Bookings`, `Disputes`, `Reviews`,
`Notifications`, `Auditing`, `PlatformSettings`, `Payments`. Each has a folder in
Domain and in Application. The status table and the open owner decisions are in
[architecture-bounded-contexts.md](architecture-bounded-contexts.md).

---

## 5. Roles

| Role | Is | May |
|---|---|---|
| `Admin` | The platform owner | Approve/reject dealers, resolve disputes, see every booking, invite other admins |
| `DealerOwner` | The rental office's owner | Everything about their own dealership: profile, fleet, bookings, staff, delivery settings |
| `DealerEmployee` | Staff at one dealership | The day-to-day: bookings, pickups, returns, fleet. Never staff management |
| `Customer` | A renter | Browse, book, pay, raise a dispute, review |

Authorization is **default-deny**: every endpoint requires a valid bearer token
unless explicitly `[AllowAnonymous]`, and anonymous endpoints must be rate limited.

Two policies go further than a role check:

- **`ApprovedDealer`** — being a dealer owner is not enough; the *business* must be
  approved before it may trade. `DealerGateComponent` locks the console to match.
- **`VerifiedEmail`** — the address must be proven.

A valid signature is also not enough on its own: `OnTokenValidated` re-reads the
user on **every request** and fails the token if the account is gone, suspended, or
its **security stamp** has rotated. Changing a password, suspending an account or
deactivating an employee rotates the stamp, so access ends immediately rather than
when the token expires.

---

## 6. Data and time

- **Reporting dates are local to `Asia/Amman`**, never UTC. Everything goes through
  `IReportingCalendar`. A "day" on an admin screen is an Amman day.
- **Rentals are billed in calendar days** — the Amman date difference, minimum one.
  Monday 09:00 to Thursday 11:00 is three days. `RentalDays.Between` is the only
  place that counts, and a booking freezes the count with the two dates it came from.
- **A booking claims the car earlier than the customer collects it.**
  `Booking.HoldStart` is the period start minus a turnaround buffer (120 minutes by
  configuration), padded on the leading edge only. A PostgreSQL `btree_gist`
  exclusion constraint, `bookings_one_hold_per_vehicle`, enforces it in the database
  so the guard, the catalogue and the constraint cannot drift apart.
- **A booking freezes the rules and prices it was made under** (`BookingTerms`,
  `BookingPricing`). A past booking is never re-judged against today's settings.
- **Soft delete everywhere.** `ISoftDeletable`, a global query filter, and
  `DeleteBehavior.Restrict` on every foreign key. Nothing is hard-deleted.

---

## 7. The audit trail

A privileged admin action records an `AuditEntry` through `IAuditTrail.Record(...)`
**inside the handler**, committed by that handler's own `SaveChangesAsync` — so the
record and the action land in one transaction.

It is deliberately **not** fed from domain events: those dispatch *after* commit and
there is no outbox, so an event-fed trail would lose entries exactly when it mattered.

`audit_entries` is append-only, enforced twice: a database trigger and a
`SaveChanges` guard both refuse `UPDATE` and `DELETE`.
