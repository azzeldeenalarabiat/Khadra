# Security decisions

Each of these is a decision with a reason. Several were expensive to learn. If you
are about to undo one, the reasoning is here — read it first, then decide
deliberately rather than by accident.

---

## Sessions: the browser never holds an API token

The console talks to the BFF over a cookie; the BFF holds the session ticket in
Redis and attaches the API bearer token itself. YARP **strips** the browser's
`Cookie`, `Authorization` and XSRF headers on the way out.

So an XSS in the console cannot read an API token, because there is none to read,
and the browser cannot choose the API identity.

The cookies:

| | |
|---|---|
| `__Host-Khadra.Session` | `HttpOnly`, `SecurePolicy.Always`, `SameSite=Lax` |
| `__Host-Khadra.Antiforgery` | `HttpOnly`, `SecurePolicy.Always`, `SameSite=Strict` |

The `__Host-` prefix forbids a `Domain` attribute and requires `Secure`, so a
subdomain cannot set them. **Do not weaken `SecurePolicy`**: antiforgery refuses to
issue a token when `Request.IsHttps` is false, which is what turns a forwarded-header
misconfiguration into a loud 500 rather than a silently insecure cookie.

The phone is different on purpose: a native app has no XSS and no CSRF, so it
carries bearer tokens directly and there is no BFF in its path.

---

## A valid signature is not enough

`OnTokenValidated` re-reads the user on **every request** and fails the token when
the account is missing, not `Active`, or its **security stamp** has changed.

Changing a password, suspending an account, or deactivating an employee rotates the
stamp. Access therefore ends *immediately*, not when a 15-minute token expires.
Spec 4.2 requires exactly this: "their login stops working immediately."

Refresh tokens are **family-tracked**. Using a rotated token revokes the whole
family, so a stolen refresh token cannot be replayed alongside the legitimate one.

---

## Documents are reached through this platform, never by address

The storage bucket is **private**, and the platform mints **no Supabase URL at all**
— not public, and not signed. Signed storage URLs were considered and rejected: they
are a second way in that our authorisation never sees and that we cannot revoke.

There are two ways to earn a private file, and which one applies depends on **how
long the grant lasts**.

**A signed link, where the right is stable for a session.** An administrator
reviewing a dealership, a customer opening their own passport, either party to a
dispute ticket. Authorising once and delivering later costs nothing, because nothing
can change in five minutes that would take the right away.

```
API mints an HMAC-signed link, 5 minutes, on OUR domain
  → the API verifies the signature
  → the API reads the bytes with its own credential
  → streamed back with Cache-Control: no-store, private
```

**A booking-scoped stream, where the right is not stable.** A gallery reading the
licence of the person they are handing a car to (spec 5.1, checklist item 63) may do
so for exactly as long as `Booking.IsLive` — which ends the instant a decision window
closes or the car comes back.

```
GET /api/v1/bookings/{bookingId}/renter-documents/{documentId}
  → dealer staff policy, then membership → this dealership's booking → the booking is LIVE
  → the customer is read OFF THE BOOKING, and the document must be theirs
  → the API reads the bytes with its own credential
  → streamed back with Cache-Control: no-store, private
```

A signed link would have been wrong here twice over. It is a five-minute grant that
outlives the predicate that issued it, so a gallery would keep access after the
booking ended. And its token is `base64url(storageKey)`, which puts
`customers/{customerUserId}/…` into a gallery's browser — a customer identifier the
platform is otherwise careful never to hand them, and the raw storage key that must
not leave the server. **Nothing but a booking id and a document id reaches the
browser**, and both are re-checked against the caller on every request.

Do not "harmonise" the two paths. Checklist items 14 and 63 record why they differ.

Details that matter:

- **A bad or stale signature answers 404, not 403.** A 403 would confirm the
  document exists to someone holding nothing but a guessed key. Same rule on the
  booking-scoped route: a booking that is not this dealership's is `404
  booking.not_found`, and a document id that is not this renter's is `404
  documents.not_found`, so neither confirms that the id names anything real.
- **A closed window is a 409, not a 403.** Once the booking stops being live the
  gallery gets `booking.renter_documents_not_available`, the twin of
  `review.reputation_not_available`. Nothing is wrong with the caller; the window
  they were entitled to has closed, and the console says so rather than reporting a
  broken platform.
- **The renter route is not gated on the dealership being able to trade.** A
  suspended gallery may still record a pickup on a booking approved before the
  suspension, so gating the licence on trading would leave them handing a car to a
  stranger while the platform refused to show them who the stranger is — item 63 in
  a different costume. Membership (owner, or an *active* employee) is the gate.
- **A refused credential is never reported as a missing document.** Letting a 401
  fall through to "not found" would make a revoked key look like a platform that had
  never stored anything — and nobody would go and read the configuration.
- **A missing bucket is distinguished from a missing object.** Supabase answers 404
  for both; conflating them turns a typo in the bucket name into a plausible 404 on
  every tile.
- **The API refuses to start if the bucket is public.** That is the one failure the
  application could never notice by itself, because it never asks for a public URL.

### Keys are a whitelist, shared by every provider

`^[a-z-]+/[0-9a-f-]+/[0-9a-z-]+\.(jpg|jpeg|png|webp|pdf)$` — strictly URI-unreserved
characters, generated by the platform, never taken from an upload.

It matters more against an object store than on a disk: there, a key becomes a **URL
path**, where `?` starts a query, `#` truncates, a leading `/` re-roots the request,
and `..` may be collapsed by an intermediary before the store sees it. One whitelist
answers all of that, and answers it the same way for both providers.

### An upload ticket is spent once

A presigned ticket commits to a key before the bytes exist. A second write at that
key is **refused** (`409 upload.already_stored`).

Without that, the person who raised a dispute could replace the evidence after the
other party and an administrator had read it — same key, same record, different
bytes. The 409 is also the right answer for a client retrying after a lost response:
the bytes are there, go to the confirmation step.

---

## Account enumeration

The endpoints that could be used to ask "does this person have an account" answer
uniformly:

| | |
|---|---|
| `forgot-password` | Always 202 |
| `resend-verification` | Always 202 |

Because the response says nothing, the **reason is logged** instead — where an
operator can read it and a caller cannot. The address, the token and the link are
never in those lines, and tests assert their absence rather than trusting the next
person to remember.

The trade-off that is *not* hidden: a send that was **attempted and failed** is
reported to the caller. That leaks a little, but only while the mail path is broken,
and the alternative is telling someone a link is on its way when the relay just
refused it. Recorded on the pre-launch checklist beside the other enumeration items.

A residual channel remains and is recorded: the no-account path is one database
round trip while the account path is several plus an email, so response time
distinguishes them. Queuing the send removes it.

---

## Forwarded headers are a security input

`X-Forwarded-For` decides the rate-limit partition, including the
ten-per-fifteen-minutes cap that is the only brute-force protection on password
sign-in. Whoever may set it picks their own bucket.

The subtlety that made this a live vulnerability once:
`ForwardedHeadersMiddleware` only checks the sender when it has something to check
against — `KnownNetworks.Count > 0 || KnownProxies.Count > 0` — so **both lists empty
does not mean "trust nobody", it means "trust anybody"**. The API now refuses to
start rather than run that way.

`ForwardLimit` must equal the hop count **exactly**. Padding it is not "safe": when a
visitor's own address falls inside a trusted range — a Cloudflare Worker fetching the
origin — one hop too many consumes a value the visitor supplied.

The full reasoning and the current values are in
[production.md](production.md#7-cloudflare-and-forwarded-headers).

---

## Configuration that fails loudly

A running system that is quietly wrong is worse than one that will not start. These
all refuse at boot rather than at the first request that needs them:

| Refusal | Why |
|---|---|
| `KnownProxies` empty outside Development | Silently means "trust anybody" |
| `Email:Provider` = `Logging` in Production | Writes every message to the log and delivers nothing |
| An unrecognised `Email:Provider` | The fallback was the Logging transport, silently |
| `Documents:Provider` = `Local` in Production | Files deleted on the next deploy |
| An unrecognised `Documents:Provider` | Same silent-fallback trap |
| Storage bucket missing, refused, or **public** | Discovering it at the first upload is far too late |
| `Geocoding:Provider` without a `UserAgent` | Nominatim blocks unidentified callers |
| `Documents:Supabase:Url` not https, unless loopback | Identity documents do not cross a network in the clear |
| An invalid `Admin:Bootstrap` phone or name | Would leave a platform with no administrator and nobody aware |

Two of these — the mail transport and the document store — exist because the silent
fallback *actually happened*. See [incidents.md](incidents.md).

---

## Money

**No payment provider is configured, and no value of `Payments:Provider` simulates
success.** `UnconfiguredPaymentProvider` refuses every checkout, and the boot log
says `PAYMENTS ARE NOT ACCEPTED` on every start.

A simulated provider would be indistinguishable, in every table and on every screen,
from a real one: bookings would read `Confirmed`, dealerships would prepare cars,
and nobody could tell which rentals had money behind them.

**Penalties are assessed, never charged.** With no dispute ticket, no penalty is
applied at all. Money moves only through an administrator resolving a ticket.

---

## The audit trail cannot be edited

`audit_entries` is append-only, enforced **twice**: a database trigger and a
`SaveChanges` guard both refuse `UPDATE` and `DELETE`.

Entries are written by the handler inside its own transaction, never from domain
events — those dispatch after commit with no outbox, so an event-fed trail would
drop entries exactly when something went wrong.

---

## Secrets

- Nothing secret is committed. `appsettings.Local.json`, `.env*` and `.keys/` are
  gitignored; tracked `appsettings*.json` hold empty placeholders.
- Real values live in user-secrets (development) or the platform environment
  (production).
- The Supabase storage key is redacted from HTTP client logging, and no code path
  prints it.
- The JWT signing key protects access tokens and, through HKDF, the document-link
  and upload-ticket keys. Rotating it invalidates 15-minute tokens and sub-15-minute
  tickets and nothing else, so rotation is cheap. Generate it on the machine that
  will hold it, so it never passes through a chat window or a shell history.

---

## Consciously accepted, and recorded

These are not oversights. Each is written down in
[pre-launch-checklist.md](pre-launch-checklist.md) with what closing it needs:

- **Documents are not yet backed up** alongside `pg_dump`.
- **Free Redis does not persist**, so a restart signs everyone out.
- **IP-partitioned rate limiting** cannot see a botnet and punishes an office behind
  one NAT. Per-account lockout is the control that actually protects one account.
- **A crash between committing an upload and delivering its email** can strand a
  token; the recovery is manual.
- **Vehicle photos and branding** are served through the API from private storage.
  Spec 7 permits public URLs for car images; a separate public bucket is a later
  decision.
