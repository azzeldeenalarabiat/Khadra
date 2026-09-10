# Workflows

What the platform actually does, end to end. Each section says who acts, what the
system does, and where the rule lives in code.

---

## 1. Accounts and authentication

### Registering

| Who | How they arrive |
|---|---|
| **Customer** | Self-registers in the phone app |
| **Dealer owner** | Self-registers in the console, then applies for their dealership |
| **Dealer employee** | **Invited** by their owner. Never self-registers |
| **Admin** | **Invited** by another admin. The first one is bootstrapped — see §2 |

Registration creates an account with an **unverified** address. `CanAuthenticate()`
refuses to sign in until the address is proven, so a new account does nothing until
the emailed link is opened.

### Email verification

Registration issues an `EmailVerification` token (24 hours) and emails a link to
`{App:ClientBaseUrl}/verify-email?token=…`. Opening it verifies the address.

`/api/v1/auth/resend-verification` re-issues one. It gates on the *address*, not the
role — which turns out to matter (§2).

### Signing in

`POST /api/v1/auth/login` returns an access token (15 minutes) and a refresh token.
Refresh tokens are **family-tracked**: reuse of a rotated token revokes the whole
family, so a stolen token cannot be replayed alongside the real one.

`CanAuthenticate()` refuses when the account is deleted, suspended, or unverified —
in that order, and a deleted account is reported as bad credentials so the endpoint
does not confirm it exists.

### Password reset

```
POST /api/v1/auth/forgot-password   → always 202, whatever happens
POST /api/v1/auth/reset-password    → consumes the token, sets the password
```

The 202 is uniform **by design**: anything else turns the form into a way to ask the
platform who holds an account. Since the response says nothing, the *reason* is
logged instead — one line per outcome, keyed by correlation id, never containing the
address, the token or the link:

| Event | Meaning |
|---|---|
| `1300` | The submitted value is not an email address |
| `1301` | No account matched |
| `1302` | An account exists but is **soft-deleted** — intended, and otherwise indistinguishable from a typo |
| `1303` | Link issued and handed to the mail transport |
| `1304` | Link issued, transport refused it |

**A reset does not verify the address.** `ResetPasswordHandler` changes the password
and nothing else, so an account that never accepted its invitation still cannot sign
in afterwards. The way out is `resend-verification` → `verify-email` → reset, or the
invitation link itself.

Neither suspension nor an unverified address blocks a reset from being *issued* — a
suspended person still owns their mailbox, and refusing someone who never confirmed
theirs would lock the account permanently. `CanAuthenticate` still refuses the
sign-in, so the link buys a suspended user nothing.

---

## 2. The first administrator

Every admin is invited by an admin, and the last one can never be deactivated. That
leaves one hole: a database that has never had one. `AdminBootstrapper` fills it,
**once**.

```
Admin__Bootstrap__Email / __Phone / __FullName set
  → on startup, and ONLY if the database has never held an administrator
  → create an Admin with a RANDOM, DISCARDED password hash and an unverified address
  → issue an AdminInvitation token (7 days)
  → email {App:ClientBaseUrl}/accept-invitation?token=…
```

The account is **inert**: the hash is over bytes nobody kept, and the address is
unverified, so it cannot be signed into whatever anyone guesses. It becomes usable
only when the person holding the mailbox opens the link and chooses a password —
`AcceptInvitation` verifies the address and sets the password together.

That shape is what makes a configured address safe to ship: on a platform that
already has an admin it is inert, and on one that does not, whoever can set the
configuration already holds the connection string and the signing key.

**It is idempotent, and says why when it declines.** Every branch logs: the address
belongs to someone else, an admin already exists at a different address, the
invitation was already accepted, the account is suspended, or a live invitation is
still outstanding. A stranded invitation (expired, never accepted) is re-issued.

**An invitation stops working once a password exists.** `PasswordChangedAt` is null
on every invited account, so a link that leaked cannot overwrite the password of an
account its owner has already recovered.

Checking the state of all this: [sql/verify-admin.sql](sql/verify-admin.sql).

---

## 3. Dealer registration and approval

### The application

A verified dealer owner submits:

- **The business** — name (locked after approval; it is what the licence was checked
  against), commercial registration number, description
- **Where it is** — a city, a pin on the map, an area, optionally a street
- **When it opens** — one window, applied to every day; per-day hours come later
- **Three documents** — commercial registration, green-plate vehicle registration,
  the owner's ID. JPEG, PNG, WebP or PDF, 8 MB each

All three are **required**, and completeness is checked *before* anything is written,
so a missing document does not leave two orphaned files in storage.

The application starts at `PendingReview`.

### The review

An admin opens the dealer, sees the details and the map pin, and opens each document
through a short-lived signed link (§7). Three outcomes:

| Outcome | Effect |
|---|---|
| **Approved** | The dealership may trade. It can list cars and take bookings |
| **ClarificationNeeded** | Sent back with a note. The owner edits and resubmits |
| **Rejected** | With a reason. Recorded, and the owner is told |

Every outcome writes an `AuditEntry` in the same transaction and notifies the owner.

Later, an admin may **suspend** an approved dealership, or **reactivate** it.
A suspended dealer keeps its data and its history.

### What "approved" gates

`ApprovedDealer` and `ApprovedDealerStaff` policies. The console's
`DealerGateComponent` locks the whole dealer area to match, so a dealer whose
business cannot trade sees an explanation rather than broken screens.

**Awaiting an owner decision** (defaults in force, see [../CLAUDE.md](../CLAUDE.md)):
a suspended dealer may still record a pickup on an already-approved booking; returns
are always allowed.

---

## 4. Location, cities and the map

### Cities are real data, not a hard-coded list

Cities live in the database and are created by an administrator through the console.
There is **no seeder** — a fabricated city is exactly the kind of invented data the
standing rule forbids. Until an admin creates them, the dropdown is empty and the
form says so.

A city may carry a centre point. When it has one, the map opens there; when it does
not, the form says *"The platform has not pinned this city yet, so the map cannot
start there"* and asks the owner to place the pin.

> Production currently has four cities — Amman, Al-Salt, Irbid, Zarqa — and **none
> has a centre pinned**, so the second path is the one owners meet today. Pinning
> each city's centre is a small administrative task that would open the map at
> roughly the right place instead of asking every owner to find it.

### The map

Leaflet over OpenStreetMap tiles, in `Khadra.Dashboard/src/app/shared/map/`. The
owner can:

- click the map, or drag the pin, to set the location
- use **"Use my current location"** (browser geolocation)
- type latitude and longitude directly
- clear the pin

Attribution is displayed, as OpenStreetMap's licence requires.

### Reverse geocoding

Moving the pin asks the API — never Nominatim directly from the browser — for a
suggested **area** and **street**:

```
GET /api/v1/dealers/address-suggestions?latitude=&longitude=&language=
```

The API proxies to Nominatim server-side. This is deliberate: it keeps the
User-Agent policy in one place, lets the platform rate-limit and cache, and does not
expose the browser's IP to a third party.

It is a **form assistant only**. The suggestion fills two text boxes the owner can
overwrite; it never writes to a record on its own, and the address that is stored is
whatever the owner submitted.

**Geocoding is optional.** With `Geocoding:Provider` unset, suggestions answer
`503 geocoding.not_configured` and the form asks the owner to type the address. The
map, the pin and the coordinates all still work.

---

## 5. Vehicles and the fleet

A dealership lists vehicles. Each carries make, model, year, plate, transmission
(`Automatic`/`Manual`), fuel type (`Petrol`/`Diesel`/`Hybrid`/`Electric`), fuel
policy (`FullToFull`/`SameToSame`), seats, daily price, and photos.

| Status | Means |
|---|---|
| `Draft` | Being prepared. Not bookable, not visible |
| `Active` | Listed and bookable |
| `Hidden` | Temporarily off the catalogue, keeps its history |
| `Maintenance` | Off the road |

Photos go through the presigned upload flow (§7). A vehicle is never hard-deleted.

**Availability** is answered in one place — `BookingHolds` — so the booking guard,
the public catalogue and the database exclusion constraint cannot disagree.

---

## 6. Employees

A dealer **owner** invites staff by email. The invitation creates an inert account
(random discarded hash, unverified address) and emails a 7-day link. Accepting
proves the mailbox and sets the first password, in one step.

An owner may grant or withdraw **report access** per employee, and deactivate an
employee — which rotates their security stamp, so **every session they hold ends
immediately**, not when their token expires.

Staff management requires an approved, trading dealership. That is a default
awaiting an owner decision: the owner of a *suspended* dealer currently cannot
deactivate an employee.

---

## 7. Documents and secure storage

### What is stored

Dealer licence documents, customer identity papers and driving licences, dispute
evidence, vehicle photos, dealership branding.

Validation lives in the Application layer, above the storage port: **8 MB** maximum,
and only `image/jpeg`, `image/png`, `image/webp`, `application/pdf`. The stored
extension comes from a fixed list — never from the uploaded filename.

### Where

`Documents:Provider` selects the store:

| | |
|---|---|
| `Local` | Files on this machine. Development and tests only |
| `Supabase` | A **private** Supabase Storage bucket. Production |

Production **refuses to start** on `Local`, because Local is the default and a
forgotten variable would otherwise accept every upload and delete the lot on the
next deploy.

### How a document is read

The bucket is private and **no Supabase URL is ever minted** — not public, not
signed. There are two routes in, chosen by how long the reader's right lasts.

**A signed link, for a right that is stable for a session** — an admin reviewing a
dealership, a customer opening their own paperwork, either party to a dispute:

```
API mints an HMAC-signed link, 5 minutes, on OUR domain
  → GET /api/v1/documents/{token}?expires=&signature=
  → API verifies the signature (a bad one is reported as 404, not 403)
  → API reads the bytes from the bucket with its own credential
  → streamed back with Cache-Control: no-store, private
```

**A booking-scoped stream, for a right that can end at any moment** — the gallery
checking the licence of the person they are handing a car to (spec 5.1):

```
GET /api/v1/bookings/{bookingId}/renter-documents          → what is on file
GET /api/v1/bookings/{bookingId}/renter-documents/{id}     → the bytes
  → dealer-staff policy
  → membership: the owner, or an ACTIVE employee
  → the booking is this dealership's, else 404 booking.not_found
  → the booking is LIVE, else 409 booking.renter_documents_not_available
  → the renter is read OFF the booking; a document that is not theirs is 404
  → streamed back with Cache-Control: no-store, private
```

The whole check runs again on every byte-serving request, so access ends the instant
the booking stops being live rather than when a link happens to expire. Nothing but
two ids reaches the browser: no storage key, no signature, no customer id.

Reasoning in
[security.md](security.md#documents-are-reached-through-this-platform-never-by-address).

### The presigned upload ticket

For large or client-driven uploads the API issues a ticket that commits to a storage
key *before* the bytes exist. **A ticket is spent once**: a second write at the same
key is refused (`409 upload.already_stored`), so evidence cannot be swapped after it
has been read.

---

## 8. The booking lifecycle

```
                 ┌──────────┐
                 │ Requested│  customer asks
                 └────┬─────┘
        dealer rejects│ dealer approves        no answer in time
        ┌─────────────┼──────────────────┐         │
        ▼             ▼                  │         ▼
   ┌────────┐   ┌──────────┐             │    ┌─────────┐
   │Rejected│   │ Approved │             │    │ Expired │
   └────────┘   └────┬─────┘             │    └─────────┘
                     │ deposit paid       │
                     ▼                    │
               ┌───────────┐              │ customer cancels, any point before pickup
               │ Confirmed │              │         │
               └────┬──────┘              │         ▼
                    │ collected           │   ┌───────────┐
                    ▼                     └──▶│ Cancelled │
              ┌───────────┐                   └───────────┘
              │ PickedUp  │
              └────┬──────┘   never collected
                   │ returned      └──────▶ ┌────────┐
                   ▼                        │ NoShow │
             ┌──────────┐                   └────────┘
             │ Returned │
             └────┬─────┘
                  ▼
            ┌───────────┐
            │ Completed │
            └───────────┘
```

Statuses in code: `Requested`, `Approved`, `Rejected`, `Confirmed`, `PickedUp`,
`Returned`, `Completed`, `Cancelled`, `Expired`, `NoShow`.

### The rules that shape it

- **The car is held from before the rental starts.** `HoldStart` = period start
  minus the turnaround buffer, so a car returned at 11:00 is not re-let at 11:05.
  Enforced by a database exclusion constraint, not just application code.
- **Calendar days, Amman time.** Minimum one day. Frozen on the booking with the two
  dates it was computed from.
- **The booking freezes its terms.** Deposit percentage, commission, cancellation
  window, penalties, delivery fee — all captured at booking time. A dealership
  raising its delivery fee never re-prices an existing booking.
- **The delivery fee belongs to the dealership**, not the platform. It is excluded
  from the deposit base (so the platform takes no commission on it) and included in
  the balance the driver collects in cash.
- **A minimum lead time** stops a car being booked for one minute from now, which
  would collapse every downstream window at once.
- **Rejection and cancellation carry reasons** — smart enums, not free text:
  `VehicleUnavailable`, `DatesConflict`, `OutsideDeliveryRadius`,
  `CustomerVerificationIncomplete`, `Other`; and for the customer,
  `PlansChanged`, `FoundBetterPrice`, `TravelCancelled`, `BookedByMistake`, `Other`.
- **Every transition records who did it** — `BookingParty`: `Customer`, `Dealer`,
  `Admin`, `System`, `Unattributed`.

A background service (`BookingSettlementService`) expires unanswered requests and
unpaid approvals on a timer. Every rule it applies belongs to the aggregate; the
service only decides how often to ask.

---

## 9. Payments and the deposit

**No money moves. This is deliberate and enforced.**

`Payments:Provider` is `None`, and there is deliberately **no other value this build
understands**. `UnconfiguredPaymentProvider` refuses every checkout with
`payments.provider_unavailable`, the customer is told so on their own booking, and
the boot log says `PAYMENTS ARE NOT ACCEPTED` on every start.

Nobody may add a simulated provider. One that captured and confirmed would be
indistinguishable — in every table and on every screen — from a real payment:
bookings would read `Confirmed`, dealerships would prepare cars, and nobody could
tell which rentals had money behind them.

The domain is built and waiting: `Payment` (one checkout attempt) with `Refund`
children, and `ProviderEventReceipt` outside the aggregate for webhook idempotency.
Closing this needs a merchant account — pre-launch item 76.

**Penalties are assessed, never charged.** With no dispute ticket, no penalty is
applied at all. Money moves only through an admin resolving a ticket.

---

## 10. Disputes

Either party raises a ticket against a booking, with evidence (photos or PDFs
through the upload ticket flow).

| Status | Means |
|---|---|
| `Open` | Raised, awaiting the platform |
| `UnderReview` | An admin has picked it up |
| `Resolved` | An admin decided; any penalty is applied here |
| `Withdrawn` | The raiser dropped it |

This is the **only** path by which money moves between the parties. The evidence
window is why upload tickets are single-use: the raiser must not be able to replace
a photo after the other party and the admin have seen it.

---

## 11. Reviews

After a completed booking, the customer reviews the dealership and the dealership
reviews the customer. Neither side sees the other's review until both are in, or the
window closes — so one cannot answer the other.

`Review.Revise` exists and is guarded twice (the edit window *and* the reveal) but is
currently unreachable: no endpoint calls it. Recorded so the second guard is not
mistaken for dead code and removed.

---

## 12. Notifications

In-app notifications, written in the same transaction as the thing they describe.
Kinds include `BookingRequested`, `BookingApproved`, `BookingRejected`,
`BookingConfirmed`, `BookingPickedUp`, `BookingReturned`,
`BookingCancelledByCustomer`, `BookingNonDeliveryReported`, `DealerApproved`,
`DealerRejected`, `DealerClarificationRequested`, `DealerSuspended`,
`DealerReactivated`, `ReportAccessGranted`.

**Push notifications do not exist yet** — pre-launch item 73.

---

## 13. Localization: Arabic and English, RTL

Both surfaces are fully bilingual, and Arabic is a first-class language rather than
a translation layer bolted on.

| | Console | Mobile |
|---|---|---|
| Dictionaries | `core/i18n/en.ts`, `ar.ts` | `l10n/app_en.arb`, `app_ar.arb` |
| Switching | Header toggle, persisted | In-app, persisted |
| Direction | `dir` on the document root | `Directionality` |

**Layout is written in logical CSS properties** — `margin-inline-start`, not
`margin-left`; `padding-inline-end`, not `padding-right`. That is what makes RTL a
direction change rather than a second stylesheet. It is a hard rule in
[`.claude/rules/frontend/angular-dashboard.md`](../.claude/rules/frontend/angular-dashboard.md).

A test asserts the two dictionaries have the same keys, so a screen cannot ship with
an English string and no Arabic one — and it currently passes, so nothing is
untranslated.

Money always carries its currency code. Dates and numbers go through
`format.service.ts`, which respects the active locale.
