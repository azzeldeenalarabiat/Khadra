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

### Wave 5 final regression on `fix/polish-wave5` (2026-10-08)

Wave 5 starts at `e698beb`, the end of Wave 4, and carries F88, F90 and F92 and pre-launch items 175, 222 and 27 (the
temporary push trace deleted, which takes three trace-only tests out of the app's suite). Measured on its final commit:

| Suite | Result |
|---|---|
| Backend: `dotnet test Khadra.slnx`, run alone | 3,015 passed, 57 skipped (PostgreSQL-only) |
| Console: `ng test` · `npm run i18n:check` | 45 files, 572 tests · 0 to fix |
| Website: `ng test` | 52 files, 357 tests |
| App: `flutter analyze` / `flutter test` | no issues / 773 tests |

### Wave 6 final regression on `fix/polish-wave6` (2026-10-08)

Wave 6 starts at `4fb2f86`, the end of Wave 5, and carries pre-launch items 1, 14, 19, 33, 36, 47, 50, 51, 52, 54, 81,
103, 104, 105, 106, 108, 113, 119, 120, 121, 125, 129, 155, 174, 176, 184, 197, 223 and 236, the console and website
menus' keyboard behaviour and the early-tap fix under 222. The app is not touched (its items are Wave 7's). Measured on
its final working tree:

| Suite | Result |
|---|---|
| Backend: `dotnet test Khadra.slnx`, run alone, with `KHADRA_TEST_POSTGRES` set | 3,167 passed, 0 skipped (the PostgreSQL proofs run) |
| Console: `ng test` · `npm run i18n:check` · production build | 51 files, 607 tests · 0 to fix · clean |
| Website: `ng test` · production build | 57 files, 373 tests · clean |
| App | not run: no file under `Khadra.Mobile/` changed |

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
| 3 | `disputeWindowEndsAt` on every booking: when its dispute window closes, or null | additive | none: ignored by named-key parsing |
| 3 | A dated `GET /vehicles` lists only cars that can be collected (office open at both local times) or delivered (eligible, office delivers); rows gain `selfPickupAvailable` | behavioural + additive | fewer results, all bookable; the shape is unchanged |
| 3 | `paymentOption` reads `FullUpfront` on a booking a full payment confirms (it read `DepositOnly` on every booking); bookings already paid keep what they had, with no backfill | value, field unread | none: no build has ever read it |
| 3 | `bookings.earliestDecisionDeadline` on `GET /dealers/me/dashboard`: when the first waiting request expires unanswered, null when none waits | additive | none: office endpoint |
| 3 | `GET /dealers/me/activity` and the dashboard's `recentActivity` list every status change on the office's bookings, not only the office's own; entries gain `actorParty`, and `actorUserId` and `actorName` are null on every change the office did not make. `?actor=me` still returns only the caller's own office changes | behavioural + additive | none: office endpoints |
| 3 | Eight office notification kinds, in the console and by email: `DisputeOpened`, `DisputeResolved`, `BookingCompleted`, `BookingMarkedNoShow`, `BookingExpiredUnpaid`, `BookingCancelledByAdmin`, `SettlementRecorded`, `SettlementVoided`. The settlement sweep no longer raises `BookingReturned` by "A customer" for a completion; rows already stored stay, and the console words them as completions | new values, office only | none: an office's notifications reach no installed app |
| 3 | `YourDisputeOpened` (push and email) when a customer opens a dispute: its subject is the BOOKING and its `dueAt` the ticket's SLA deadline. A dispute the office opens tells the customer through the existing `YourDisputeUpdated` | new value | installed builds show their generic line in the list and the server's own push text, and a tap opens the booking (its subject), which links the dispute |
| 3 | The website reports its language on every switch as well as at sign-in, and the console at sign-in and on every switch, through the existing `PUT /auth/me/language` | none | none |
| 3 | The checkout's return address carries the language the checkout was opened in (`{ReturnUrlBase}/{ar|en}/bookings/{id}`): the request's `Accept-Language`, else the customer's stored language, else English. The sandbox's `checkoutUrl` gains `?lang=`, whitelisted on its page | value | none: the app's WebView reads nothing back from either address |
| 4 | Provider-event receipts may read `Duplicate`, `AssumedDuplicate`, `AmountMismatch`, `SecondCapture` or `OtherAttempt`, and carry `captureReference`; the administrator's payment page gains `incidents[]`; `POST /admin/payments/{id}/incidents/{id}/handled` (204; 404 `payments.incident_not_found`; 409 `payments.incident_already_handled`); the attention queue gains `CaptureIncidentOpen` rows and the finance panel `openCaptureIncidentsCount` | new values, new endpoint, additive | none: administrators only |
| 4 | The sandbox's capture events carry `captureReference` (`sbxcap_…`), editable on its page | sandbox only | none |
| 4 | Refunds queue rows gain `refusalCount`, `nextAttemptAt` and `needsAPerson`; the attention queue's `RefundFailed` row counts only refunds refused at least `Payments:RefundRefusalsBeforeAlert` times | additive, behavioural | none: administrators only. A customer's refund fields keep their meaning (`failedAt` is the latest refusal; no new status) |
| 4 | `OfficeBalanceDto` gains `heldCount`/`held` and `blockedCount`/`blocked` (`notYetDue` kept); payables lists take `scope=NothingDue`, and `Open` leaves out net-zero payables nothing holds back; a manual hold on a net-zero payable is refused, 409 `payables.nothing_to_hold` | additive, behavioural, new refusal | none: administrators and offices only |
| 4 | A payment receipt states "Booking status after this payment" (a `statusAfterPayment` line, and `booking.statusAfterPayment` in the snapshot); statements and refund receipts are unchanged, and no issued document changes | document content | none: every client renders sections generically and reads neither key; the shared fixture is regenerated |
| 4 | Expiry notifications (`YourBookingExpired`, and `BookingExpiredUnpaid` to the office) are also raised when a lapse is settled on load and saved by another command — a capture seconds after the payment deadline above all | value (who is told, when) | installed builds already word `YourBookingExpired` |
| 4 | `register`, `register-dealer-owner` and `accept-invitation` take optional `acceptedLegalVersions` and `legalLanguage` (W4-8); the registration answers gain `consentsRecorded`, and `accept-invitation` answers `{ message, consentsRecorded }` | additive | none: an installed build sends neither and is never refused for it (row below) |
| 4 | New refusals on those three: 409 `legal.version_not_current` (a text not in force), 400 `legal.consent_required` (a text in force not accepted — website and console callers only) and 400 `legal.consent_language_required` | new refusals | none: a request that declares an app version is not required to carry consent (pre-launch item 238) |
| 4 | `GET`/`POST /api/v1/auth/me/legal-consents` (the person's record and what is pending; accept the texts in force); `pendingConsents` on `/auth/me`, empty for an administrator | new endpoints, additive | none until 1.4.0 reads them |
| 4 | A signed-in request from somebody with a text pending is refused 403 `legal.consent_pending` (`pending[]`, no-store) — except sign-out, `GET /auth/me`, the two consent endpoints and `PUT /auth/me/language`. Never judged: anonymous endpoints, administrators, and a request that declares an app version; both BFFs now strip `X-Khadra-App-Version` | new refusal | none: every installed build from 1.1.0 declares its version (`docs/contracts/README.md`) |
| 4 | `YourDocumentRejected` (W4-9; push and email): no subject and no reference, and its words never carry the reason; the email opens `{site}/profile/documents` | new value | installed builds show their generic line and the server's push text, and a tap opens nothing until 1.4.0 routes it |
| 4 | A document Khadra rejected counts as not filed: `/customers/me/documents` lists its type in `missing` with `isComplete: false` (the file stays listed, `Rejected`, with its `reviewNote`), and `booking.documents_incomplete` gains `missingDocumentTypes` and `rejectedDocumentTypes` | behavioural, additive | installed builds already show a rejected file with its note, and their incomplete-documents path; they do not name the rejected types until 1.4.0 |
| 4 | `rejectedByPlatform` on the office's renter documents (`GET /bookings/{id}/renter-documents`) | additive | none: office endpoint |
| 4 | `GET /api/v1/admin/customers/{id}/documents/{documentId}` (the file, its view recorded first) and `POST …/reject` (`{ reason, uploadedAt }`; 409 `documents.changed_since_viewed` and `documents.not_viewed`, 404 `documents.not_found`); `CustomerDocumentRejected` in the audit vocabulary | new endpoints | none: administrators only |
| 4 | The handover's fuel level reads as a whole percentage on the website and in the console (W4-10) | none | none: no API change |
| 5 | The staff invitation asks for a name, an email and a phone on single lines, checked for shape before sending (F88); a renter's document on file reads "Uploaded" on the admin profile, as on the website (F90); the website's drawer and account menu scroll inside a short screen, so My account and Sign out stay reachable (F92) | none | none: no API change |
| 5 | `GET /api/v1/admin/dashboard/activity`: each entry carries `actorUserId`, null when nobody acted, and the console words that actor itself (pre-launch item 175) | additive | none: administrators only |
| 5 | The customer BFF adds a per-page nonce to `script-src` on the renderer's pages and sends it to the renderer in `X-Khadra-Csp-Nonce`, so Angular's event replay runs (pre-launch item 222) | none | none: the app calls the API directly, and API responses keep their policy |
| 6 | Sign-in (`POST /auth/login`, and the BFFs' sign-in through it) is refused for 15 minutes after 8 failed attempts on one account in 15 minutes: 429 with `code` `rate_limited`, the IP limiter's own code and generic title, and `Retry-After`; a success or a password reset clears the account's count; an unknown address is counted like a known one (pre-launch item 51) | new refusal, behavioural | installed builds already map a 429 to their rate-limited message (`api_failure.dart`), so nothing is misread; the per-IP limit is unchanged |
| 6 | A signed document link is bound to the person it was minted for: the same link in another signed-in session answers 404 (pre-launch item 14) | behavioural | none: every link a client follows is minted for the caller in the same session |
| 6 | Hidden reviews leave every public list and every rating; `GET/POST /api/v1/admin/reviews…` hide and restore one with a reason code, audited (pre-launch item 81) | behavioural, new endpoints | fewer reviews where one was hidden; the shape is unchanged. The new endpoints are administrators' only |
| 6 | A model-binding 400 (malformed JSON, a wrong type) carries `code` `request.invalid` (pre-launch item 121) | additive (body) | none: the status and `errors` are unchanged |
| 6 | `customerPage` on the administrator's dealer review; `isBreachingSla` on the administrator's dealer list; `pdfHolds` on the administrator's financial document and a `FinancialDocumentPdfsNotDrawn` attention row; `subjectLabelAr` on the audit log and the activity feed; `bookingReference` nullable on the dispute queue; the settings `source` worded as a code (items 113, 106, 197, 176, 108) | additive, value | none: administrators only |
| 6 | Audit entries written from now on store parts, not English, in `newValue`/`previousValue` for a dispute decision, a handover, a code lock and a lookup (compact JSON), and an actor with no name claim by short reference (items 50, 174, 103) | value | none: administrators only; stored rows are untouched |
| 6 | `actorStandIn` (`Customer`, `Colleague`, `RentalOffice`) on every notification whose actor is a stand-in; `actorName` keeps the English phrase it always had (item 103) | additive | none: named-key parsing ignores it, and the app keeps printing `actorName` |
| 6 | The invitation answer of `POST /dealers/me/employees` gains `invitationEmailSent`; two administrators adding the same offered city or car-type name at once now both get 409 `lookup.name_taken`, which one alone always got (a database index closes the race) (items 47, 52) | additive, behavioural | none: office and administrator endpoints |
| 6 | `GET /api/v1/dealers/me/vehicles/{id}/calendar?year=&month=`: one car's month in the platform's calendar, from the catalogue's own holds (item 54) | new endpoint | none: office endpoint |
| 6 | The console BFF sends `X-Khadra-Idle-Seconds` handling to itself only (never forwarded) and ends a session idle for 30 minutes on its own clock; both BFFs accept bodies up to 32 MiB explicitly; the renderer strips the forwarding headers it does not trust (items 129, 33, 223) | none | none: the app calls the API directly |

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
| F67 (W3) | A booking the customer cancelled by reporting non-delivery reads "Cancelled after you reported that the office did not hand over the car", with their report beneath, as the website does; today the app says "Cancelled by you · <report>". Recognise it by `penalty.reasonCode` `DealerDidNotHandOver` (or, assessed before codes, the office carrying the penalty on a customer cancellation). |
| F69 (W3) | Look a reason code up in the list it belongs to: a refusal coded `Other` reads the refusal list's "Declined by the rental office", not the cancellation list's "Another reason" (`booking_detail_screen.dart` `_codeLabel`), as the website now does. |
| F2/F1 (W3 E7) | Mark a search result "Delivery only at these times" when `selfPickupAvailable` is false, and word a refused quote by its reason (hours, dates) rather than one sentence, as the website does. |
| D10 (W3) | Word `YourDisputeOpened` in the notifications list ("Your dispute is open, and Khadra will decide it", as the website does). It already opens the booking, its subject. |
| F7 (W4-8) | Ask for consent: a required checkbox on registration naming the texts in `/app-config.legal.documents`, sending `acceptedLegalVersions` and `legalLanguage`; and, for a signed-in customer with `pendingConsents` on `/auth/me` or a 403 `legal.consent_pending`, a prompt in place of the app that offers the texts, the acceptance (`POST /auth/me/legal-consents`) and sign-out, as the website does. A 409 `legal.version_not_current` reloads `/app-config` and asks again. Then the minimum rises to 1.4.0, and the server stops sparing the app (pre-launch item 238). |
| 103 (W6) | Word a notification whose `actorStandIn` is `RentalOffice` in the reader's language ("The rental office" / «مكتب التأجير»), as the website does, instead of printing `actorName`'s English phrase. Rare: only an office that left the platform before the notification was raised. |
| W4-9 | **Done on `fix/polish-wave4` (Staging finding, 2026-10-07)**, in the app's code and in no API: `YourDocumentRejected` is worded in Alerts, and a tap on it (its row, or its push from the foreground, the background or a closed app) opens My Documents (`notificationRoute`); the reason reads in its own direction under "Why:", with "Upload a new one". A push tap that launches a closed app is no longer lost while the session restores (`openForPush`, every kind). The Alerts row opens before its read is recorded, so a token rotation can no longer drop the tap (second device finding). It reaches phones only with the next app build. The temporary `PushTrace` that helped verify it on a phone was deleted in Wave 5 (pre-launch item 27), so a build from `fix/polish-wave5` onward has no "push trace (staging)" row. **Still for 1.4.0:** on `booking.documents_incomplete`, name the `rejectedDocumentTypes`, as the website does. |
