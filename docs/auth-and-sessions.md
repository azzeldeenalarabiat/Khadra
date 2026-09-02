# Authentication and Sessions

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

## BFF session

1. `GET /bff/antiforgery` → antiforgery cookie + `XSRF-TOKEN` cookie; Angular sends the value in `X-XSRF-TOKEN` on every mutation.
2. `POST /bff/login {email, password}` → BFF calls the API, stores tokens in an encrypted Redis ticket, sets the session cookie (Secure, HttpOnly, SameSite=Lax, 8h absolute, 30 min idle).
3. Proxied `/api/**` requests: YARP strips browser `Cookie`/`Authorization`/XSRF headers, attaches the server-held bearer token, refreshing it single-flight one minute before expiry.
4. `POST /bff/logout` revokes the API family and deletes the ticket. `GET /bff/user` returns the session user.
5. Anonymous API routes proxied without a session: register, verify-email, resend-verification, forgot-password, reset-password.

Data Protection keys are persisted in Redis so multiple BFF replicas can read each other's tickets.

## Rate limits (per client IP)

`login` 10 / 15 min · `auth` (register, verify, resend, forgot, reset, change-password) 10 / min · `refresh` 60 / min · global 600 / min. Behind the BFF, configure `KnownProxies` so `X-Forwarded-For` is trusted.
