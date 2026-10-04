# Fix & Polish — contract and app-change ledgers

The Fix & Polish batch follows the Staging end-to-end run of 3–5 October 2026 (findings F1–F58). It is built on
`fix/polish-wave1` and later wave branches, cut from `feature/payments-receipts` at `665114c`, the commit every Staging
service runs.

This file keeps three things, from the batch's first commit:

1. the baseline the batch must stay green against;
2. every change to what the API serves or accepts, marked by kind, with its effect on the installed customer apps;
3. every change the next customer-app release (1.4.0) must make.

The rules are CLAUDE.md's "The customer app's contract".

## 1. Baseline at `665114c` (2026-10-05)

| Suite | Result |
|---|---|
| Backend: `dotnet test Khadra.slnx` | 2,562 passed, 40 skipped (PostgreSQL-only). One test, `SmtpEmailSenderReceiptTests.A_relay_that_stalls_after_accepting_gets_one_copy_and_the_send_succeeds`, splits a 4-second budget across two attempts and failed once while three other suites loaded the machine; it passes alone (3 out of 3). Run the backend suite on its own. |
| Console: `ng test` | 30 files, 358 tests |
| Website: `ng test` | 34 files, 241 tests |
| App: `flutter analyze` / `flutter test` | no issues / 719 tests |

## 2. Contract ledger — what the API serves or accepts

Installed builds: 1.2.0+3 and 1.3.0+4. The tracked minimum (`MobileApp:MinimumSupportedVersion`) is not raised in
this batch (decision D6).

| Wave | Change | Kind | Effect on installed apps |
|---|---|---|---|
| 1 | `handover.code_invalid` from `POST /bookings/{id}/pickup` and `/return` carries `attemptsRemaining` and `maxAttempts` when the wrong guess was counted | additive | none: office-only endpoints |
| 1 | A bare 401 is ProblemDetails with `code` `auth.unauthenticated` (no token) or `auth.session_invalid` (a token was refused), sent as `application/problem+json`. `WWW-Authenticate: Bearer` and the status are unchanged | additive (body) | none: the app refreshes once on any 401 by status and maps only `auth.invalid_refresh_token`; both BFFs end their session on a 401 by status |
| 1 | A 403 for a role the endpoint does not admit is ProblemDetails with `code` `auth.forbidden` (it was empty) | additive (body) | none: the status is unchanged and the app already parses `application/problem+json` |
| 1 | `X-Content-Type-Options: nosniff` on every API answer | none | none |
| 1 | The console BFF no longer forwards `POST /api/v1/auth/register` anonymously | none | none: the app calls the API directly; the customer website's BFF keeps its own route |
| 1 | The website's dispute page reads the refund's status from the booking's existing `refunds[]` (`disputeTicketId`) | none | none: no API change |

## 3. App-change ledger — for the 1.4.0 release (Wave 7)

| From | What the app must do |
|---|---|
| F50 | Stop showing the customer the office's and Khadra's shares of a dispute decision (decision 3); show only the customer's own share. |
| F43 (W1-5) | Show what became of the dispute's refund (requested, on its way, refunded on a date, being sent again), read from the booking's `refunds[]` by `disputeTicketId`, as the website now does. |
| F26 (W1-6) | On a booking paid in full, stop saying the deposit "is held until you collect the car"; say it is part of the full amount paid online. |
| F10 (W1-12) | Optional: word `auth.unauthenticated` and `auth.session_invalid` instead of the generic line, if one ever shows after the single refresh. |
