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

### Wave 2 baseline at `a9ca9e4` (2026-10-05)

The Wave 2 branch starts at `a9ca9e4`. It differs from `23f9f6a`, where Wave 1's final regression measured every suite,
by one string inside one website test (a placeholder host), so the counts are those:

| Suite | Result |
|---|---|
| Backend: `dotnet test Khadra.slnx`, run alone | 2,570 passed, 40 skipped (PostgreSQL-only) |
| Console: `ng test` | 34 files, 392 tests |
| Website: `ng test` | 35 files, 252 tests |
| App: `flutter analyze` / `flutter test` | no issues / 719 tests |

### Wave 3 baseline at `c014987` (2026-10-06)

The Wave 3 branch `fix/polish-wave3` starts at `c014987`, the commit Wave 2's final regression measured and Staging
runs, so the counts are those:

| Suite | Result |
|---|---|
| Backend: `dotnet test Khadra.slnx`, run alone | 2,706 passed, 43 skipped (PostgreSQL-only); PostgreSQL proofs 62 of 62 |
| Console: `ng test` | 37 files, 461 tests |
| Website: `ng test` | 36 files, 258 tests |
| App: `flutter analyze` / `flutter test` | no issues / 719 tests |

## 2. Contract ledger — what the API serves or accepts

Installed builds: 1.2.0+3 and 1.3.0+4. The tracked minimum (`MobileApp:MinimumSupportedVersion`) is not raised in
this batch (decision D6).

| Wave | Change | Kind | Effect on installed apps |
|---|---|---|---|
| 1 | `handover.code_invalid` from `POST /bookings/{id}/pickup` and `/return` carries `attemptsRemaining` and `maxAttempts` when the wrong guess was counted | additive | none: office-only endpoints |
| 1 | A bare 401 is ProblemDetails with `code` `auth.unauthenticated` (no token) or `auth.session_invalid` (a token was refused), sent as `application/problem+json`. `WWW-Authenticate: Bearer` and the status are unchanged | additive (body) | not breaking: the app refreshes once on any 401 by status and maps only `auth.invalid_refresh_token`; both BFFs end their session on a 401 by status. A customer who still sees a 401 after the single refresh now reads the server's English title ("Your session is no longer valid. Sign in again.") where the framework's bare 401 gave them nothing to show |
| 1 | A 403 for a role the endpoint does not admit is ProblemDetails with `code` `auth.forbidden` (it was empty) | additive (body) | none: the status is unchanged and the app already parses `application/problem+json` |
| 1 | `X-Content-Type-Options: nosniff` on every API answer — added as the response starts, so the exception handler's 500/409/400 answers carry it too | none | none |
| 1 | The console BFF no longer forwards `POST /api/v1/auth/register` anonymously | none | none: the app calls the API directly; the customer website's BFF keeps its own route |
| 1 | The website's dispute page reads the refund's status from the booking's existing `refunds[]` (`disputeTicketId`) | none | none: no API change |
| 2 | Every money amount is serialised at the currency's full scale (`1.500`, never `1.5`); a decided dispute stored before 2026-10-05 is served padded | value | none: the same number in JSON |
| 2 | `dispute.amount_precision` refuses an amount with more decimals than the currency has, from resolve and its preview | new code | none: administrators only |
| 2 | `chargedToDealerEarlier` and `slaState` on the dispute DTO | additive | none: null on the customer's and the office's copies; named-key parsing ignores them |
| 2 | `POST /api/v1/admin/disputes/{id}/resolution-preview` | new endpoint | none: administrators only |
| 2 | `expectedOutcome` on the office's copy of a decided dispute | additive | none: the app never receives an office's copy |
| 2 | After the advisor's review: `ledgerIssues` and `recordedAtNextPass` on the resolution preview, and `anotherDisputeOpen` on `expectedOutcome` | additive | none: administrators' and offices' copies only |
| 2 | `legal.text_unsupported` may carry the reason `nesting` (Markdown nested deeper than the renderer goes) | new value | none: administrators only |
| 2 | A refund an administrator's dispute decision or cancellation ordered raises its notification with the actor `Khadra` (no actor id); push and email have a platform wording | value, wording | the in-app line reads "Khadra: your payment has been refunded"; no new kind, so no unknown-kind line |
| 2 | `GET /api/v1/legal-documents/{terms|privacy}/current`: anonymous, public cache 300 s, weak ETag. The approved scope named `/legal-documents/current`; the advisor's review made it one document per call, before any app reads it | new endpoint | none until 1.4.0 reads it |
| 2 | `legal` block on `/app-config`: each text in force and its page URLs; null when the database cannot be read ("not known", never "nothing published") | additive | none: ignored by named-key parsing |
| 2 | `/api/v1/admin/legal-documents` (list, detail, preview, publish) | new endpoints | none: administrators only |
| 3 | `pickupAvailableFrom` and `returnAvailableFrom` on every booking: the earliest moment the office may record the pickup (the rental start less the frozen turnaround, `Booking.HoldStart`) and the return (the rental start) | additive | none: ignored by named-key parsing |
| 3 | `POST /bookings/{id}/pickup` before `pickupAvailableFrom` and `/return` before the rental start are refused: 409 `booking.pickup_too_early` / `booking.return_too_early`, with `availableFrom`. Checked before the handover code, so an early attempt costs the customer no try | new refusals | none: office-only endpoints. **Code issuance is unchanged** (owner, 2026-10-06, decision A2): `POST /bookings/{id}/handover-code` is still answered on any confirmed booking, because refusing a request installed apps make would be breaking |
| 3 | A customer's copy of a booking (detail, list, `/bookings/next`, create/cancel/non-delivery answers) carries `vehicle.plateNumber: null` until the office approves; list rows gain `approvedAt` | value (same field, null before approval) + additive | installed builds print an empty "Plate:" on a booking awaiting the office until 1.4.0 (owner, decision A3); nothing throws |
| 3 | `POST /dealers` (the office application) and `PUT /dealers/me/profile` refuse a business name that is one of the platform's own: 400 `dealer.business_name_reserved`, on the `businessName` field | new refusal | none: office endpoints only |
| 3 | `disputes[]` on every booking: `{ ticketId, status, openedAt, closedAt }`, oldest first | additive | none: ignored by named-key parsing |
| 3 | The customer's copy of a dispute names "Khadra" for the resolver and the assignee (ids null), and the office's name for an office-opened ticket and office statements (ids null); the customer's booking history carries no id but their own. `openedByUserId`, `authorUserId` and `resolvedByAdminId` become nullable | value | none: no installed build reads these fields |

## 3. App-change ledger — for the 1.4.0 release (Wave 7)

| From | What the app must do |
|---|---|
| F50 | Stop showing the customer the office's and Khadra's shares of a dispute decision (decision 3); show only the customer's own share. |
| F43 (W1-5) | Show what became of the dispute's refund (requested, on its way, refunded on a date, being sent again), read from the booking's `refunds[]` by `disputeTicketId`, as the website now does. |
| F26 (W1-6) | On a booking paid in full, stop saying the deposit "is held until you collect the car"; say it is part of the full amount paid online. |
| F10 (W1-12) | Optional: word `auth.unauthenticated` and `auth.session_invalid` in both languages; today an unmapped code shows the server's English title. |
| F7 (W2 G1) | Link to the Terms and the Privacy notice from `/app-config.legal.documents[].pageUrls` (registration and profile), showing no link while a text has none; the consent checkbox comes with Wave 4's consent design. |
| F48 (W2 C6) | Nothing required: a refund Khadra made already reads "Khadra: …" from data. Revisit the Arabic "Khadra" with branding in Wave 5. |
| D4 (W3) | Offer "Show my pickup code" only from `pickupAvailableFrom`, and the return code only from `returnAvailableFrom`, saying when, as the website does; the server keeps issuing codes either way. |
| F65 (W3) | Hide the "Plate" row when `vehicle.plateNumber` is null (a booking the office has not approved); today the label prints with nothing after it. |
| F44 (W3 C3) | Link a decided or withdrawn dispute from the booking, from `disputes[]`, as the website does; today a closed decision is reachable only from a notification. |
