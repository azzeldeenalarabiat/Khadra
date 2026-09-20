# Authentication and Sessions

The reference: token lifetimes, storage, and the session contract.

For the surrounding narrative see [workflows.md §1](workflows.md#1-accounts-and-authentication)
(registration, verification, reset, invitations) and
[security.md](security.md#sessions-the-browser-never-holds-an-api-token) (why the
browser holds no token, and what the security stamp is for).

## Two clients, one contract

- **Flutter customer app** calls `Khadra.WebAPI` directly with bearer tokens and stores the refresh token in secure device storage.
- **Angular dashboard** (Admin, Dealer Owner, Employee) talks only to `Khadra.Bff`. The BFF keeps the API tokens in an encrypted Redis ticket and gives the browser a `__Host-Khadra.Session` cookie. The browser never sees an access or refresh token.

## Tokens

| Token | Lifetime (config) | Storage |
|---|---|---|
| Access JWT (HS256) | `Authentication:Jwt:AccessTokenMinutes` = 15 | client memory / BFF ticket |
| Refresh token (256 random bits) | `Authentication:Policy:RefreshTokenDays` = 14, family ceiling `RefreshFamilyDays` = 30 | SHA-256 hash in `refresh_tokens` |
| Email verification link | `EmailVerificationHours` = 24 | SHA-256 hash in `verification_tokens` |
| Password reset link | `PasswordResetMinutes` = 60 | SHA-256 hash in `verification_tokens` |

JWT claims: `sub`, `email`, `name`, `role`, `email_verified`, `khadra:security_stamp`, `khadra:must_change_password`, `jti`, `iat`. On every request the API re-checks that the user exists, is active and still carries the same security stamp, so password changes, suspensions and deletions invalidate access tokens immediately.

## Flows

**Register** → `POST /api/v1/auth/register` creates a `Customer` (unverified) and emails a verification link (`{App:ClientBaseUrl}/verify-email?token=…`). The password is hashed with BCrypt (enhanced mode, work factor 12).

**Verify email** → `POST /api/v1/auth/verify-email {token}`; single-use, idempotent for already-verified users.

**Login** → `POST /api/v1/auth/login`. Unknown email, wrong password and deleted accounts all return 401 `auth.invalid_credentials` (constant-time via a dummy hash). Suspended → 403 `auth.account_suspended`; unverified → 403 `auth.email_not_verified`. Success starts a new refresh-token family.

**Refresh** → `POST /api/v1/auth/refresh`. The presented token is consumed and replaced inside the same family (same absolute deadline). Presenting an already-consumed token is treated as replay: the entire family is revoked. Concurrent refreshes of the same token are detected with the PostgreSQL `xmin` concurrency token.

**Logout** → `POST /api/v1/auth/logout {refreshToken, allDevices}` revokes the family (or all families).

**Forgot / reset password** → always 202; the reset link is single-use; resetting rotates the security stamp and revokes every session, then sends a "password changed" notice.

**Change password** (authenticated) → verifies the current password, revokes all sessions, returns a fresh token pair.

## The customer app's entry flow

Three states decide what a launch shows, and they are deliberately kept apart.

**Is there a session?** `SessionStatus` — `unknown` while the cold-start rotation is
in flight, then `signedOut` or `signedIn`. About credentials and nothing else.

**Has this device said how it wants to be used?** A device-local flag in ordinary
preferences, `khadra.entry_chosen`, owned by `EntryChoice` in `lib/core/providers.dart`.
Set by Get Started's *Browse as a guest*, and by a successful sign-in or
registration. Cleared by a deliberate sign-out and by nothing else — an expiry or a
suspension is not a choice, and that customer has long since made theirs. It is in
preferences rather than the secure store because "the app's data was cleared" is
exactly what must bring Get Started back, and because iOS keeps Keychain items when
an app is deleted.

**Are these tokens ours?** `khadra.session_owned`, in preferences, read by
`SessionStore.sessionIsOwned`. See below.

The flag is consulted at ONE place: `/`, the app's entry point, inside the router's
redirect. Never globally — public routes render before the session resolves precisely
so that `/verify-email?token=…` survives a cold start, and a gate across every route
would land that on a welcome screen with the link unspent. A destination parked on
`/?next=…` travels onto `/welcome` and from there onto the sign-in form. One known
bypass, accepted: on the web the browser's URL is the initial location, so a typed
`/search` never passes through `/`.

Signing out goes to `/welcome`, not to the catalogue: somebody signing out is leaving
the device or switching accounts, and Get Started's *Sign in* is the switch-account
path.

### Ownership: why a sign-out sticks

Every secure-store call in `SessionStore` is bounded at five seconds and swallows its
own failure, because a store that never answers must not leave a customer on a splash
screen for ever. The cost is that `clear()` can report success having deleted nothing
— and a cold start would then rotate a live refresh token and sign somebody back into
the account they had just left. Requirement: *after logout the app must not restore a
stale session.*

`khadra.session_owned` closes it, written where failures are visible and ordered so
every crash window fails safe:

- `saveRefreshToken` sets it **after** the token is written. A crash between the two
  leaves a valid token nobody will read — one extra sign-in.
- `clear()` removes it **before** deleting anything. A crash, failure or timeout
  leaves an orphan nobody will read — no cost at all.
- `readRefreshToken` / `readRefreshExpiry` return null when it is absent, without
  touching the store.
- `discardDisownedTokens()` at startup deletes what the app does not own, because on
  an iOS reinstall that entry is somebody else's credential on a device that may have
  changed hands.
- `disownSession()` covers the opposite drift: Android Auto Backup restores
  preferences to a new device while the Keystore key does not travel, so the marker
  can outlive the token it claims. It fires **only on a definite absence** —
  `readRefreshTokenOutcome()` reports whether the store answered at all, because a
  timeout or a locked keystore reads as null everywhere else and giving up the claim
  on one of those would be permanent: the next launch deletes what this install no
  longer owns. Same rule as `refresh()`: only a verdict ends a session.

A device with **no preferences at all** is a different answer from one whose marker is
absent: it cannot tell, so it trusts the secure store. Answering "not ours" there
would mean no session ever survived a cold start.

This replaced `khadra.install_marker`, which answered only the reinstall half. One
marker, one meaning. Covered by `test/session_store_test.dart`.

## BFF session

1. `GET /bff/antiforgery` → antiforgery cookie + `XSRF-TOKEN` cookie; Angular sends the value in `X-XSRF-TOKEN` on every mutation.
2. `POST /bff/login {email, password}` → BFF calls the API, stores tokens in an encrypted Redis ticket, sets the session cookie (Secure, HttpOnly, SameSite=Lax, 8h absolute, 30 min idle).
3. Proxied `/api/**` requests: YARP strips browser `Cookie`/`Authorization`/XSRF headers, attaches the server-held bearer token, refreshing it single-flight one minute before expiry.
4. `POST /bff/logout` revokes the API family and deletes the ticket. `GET /bff/user` returns the session user.
5. Anonymous API routes proxied without a session: register, verify-email, resend-verification, forgot-password, reset-password.

Data Protection keys are persisted in Redis so multiple BFF replicas can read each other's tickets.

## Rate limits (per client IP)

`login` 10 / 15 min · `auth` (register, verify, resend, forgot, reset, change-password) 10 / min · `refresh` 60 / min · global 600 / min. Behind the BFF, configure `KnownProxies` so `X-Forwarded-For` is trusted.
