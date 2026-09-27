# The customer app's contract with the API

What a customer-app build already on a phone depends on, how the API turns away a build too old
for what it now serves, and the order in which a breaking change has to reach phones.

The rule is in [CLAUDE.md](../../CLAUDE.md), "The customer app's contract", settled by the owner on
2026-09-22:

> Any breaking mobile/API contract change ships with a corresponding raise of the minimum supported
> app version, and the compatible mobile build is published before the server minimum is raised.

This page is the reference behind it.

---

## Why a rule, and not care

An installed build cannot be patched. It goes on reading exactly what it was built to read until its
owner installs another, and the API cannot tell it to without a mechanism both sides agreed on in
advance. The first breaking change — the office texts becoming `{ "text", "language" }` where they
had been strings (pre-launch items 132 and 133) — found that 1.0.0 had no such mechanism: it would
have crashed on every car page and most gallery pages, the pages where booking starts. 1.1.0 is the
first build that takes part.

## What counts as breaking

Anything an installed build would misread or fail on:

- a field it reads removed or renamed;
- a field's type or shape changed — a string becoming an object was the first;
- a code or enum value it keys on renamed, or given a new meaning;
- an endpoint it calls removed, moved, or answering with a different status for the same case;
- a request it sends refused where it used to be accepted — a field newly required, a format newly
  rejected.

Not breaking: an added optional field, a new endpoint, a new code the app does not know (it falls
back to the server's own sentence). **Prefer the additive shape** — the old field kept beside the new
one until no build reads it — whenever it is honest. When it is not, the rule applies in full.

## How a build introduces itself

| | Sent by | Value |
|---|---|---|
| `X-Khadra-App-Version` | every build from **1.1.0**, on every request to the API's own origin — never to another host | `<versionName>+<versionCode>` from `package_info_plus`, e.g. `1.1.0+2` |
| `User-Agent: Khadra (<platform>)` | every build since d6fddef (2026-09-21), e.g. `Khadra (Android 16)` | platform identification |

A request carrying the app's User-Agent **without** the version header is a build from before 1.1.0,
and the API treats it as older than any minimum. Builds from before 2026-09-21 send Dart's default
User-Agent, which any Dart program sends; nothing tells them apart from other traffic, so they cannot
be refused. A web build cannot set its User-Agent at all, so an old web build is never identified; a
new one sends the header.

## What the API publishes

`GET /api/v1/app-config`, open to every build:

```json
"mobileApp": { "minimumSupportedVersion": "1.1.0", "updateUrl": null }
```

| Setting | Value |
|---|---|
| `MobileApp:MinimumSupportedVersion` | A release version (`1.1.0`, never `1.1.0-rc.1`), or empty for no minimum. Shipped in `Khadra.WebAPI/appsettings.json` |
| `MobileApp:UpdateUrl` | An absolute `http(s)` address where the current build can be downloaded, or empty. Set per environment |

An invalid value stops the API at startup. Every boot logs which builds it refuses, category
`Khadra.MobileApp`: `Customer app: builds older than 1.1.0 are REFUSED with 426 app.update_required…`
or `Customer app: no minimum version is set, so every build is served.`

## The refusal

`MobileAppVersionGate` runs after CORS and the rate limiter and **before authentication**, so a refused
build never reaches a token check, a session or a handler. A request is refused when it is the app and
its version is below the minimum, or its header is empty, repeated or unreadable, or it carries the
app's User-Agent without the header:

```http
HTTP/1.1 426 Upgrade Required
Content-Type: application/problem+json
Cache-Control: no-store

{"type":"https://httpstatuses.com/426",
 "title":"حدّث تطبيق خضرا للمتابعة · Update the Khadra app to continue",
 "status":426,
 "detail":"هذا الإصدار من التطبيق لم يعد مدعومًا. ثبّت أحدث إصدار من خضرا. · This version of the app is no longer supported. Install the latest version of Khadra.",
 "instance":"/api/v1/vehicles",
 "code":"app.update_required",
 "traceId":"…",
 "minimumSupportedVersion":"1.1.0",
 "updateUrl":null}
```

The title and detail carry both languages when the request names no `Accept-Language` — which is how
1.0.0 calls, and it prints the title verbatim — and the request's language otherwise. There is no
`Upgrade` header: HTTP/2 forbids it. **Clients key on `code`, never on the status alone.** From 1.1.0
the app answers it with a full-screen update screen that replaces the router, keeps the stored
session, and never reads it as bad credentials.

**Never gated:** anything when no minimum is set; every path outside `/api` (`/health/live`,
`/health/ready`, `/sandbox-checkout/…`, the development OpenAPI and Scalar pages);
`/api/v1/app-config`; and every request that is not the app — the console through the BFF, browsers,
server-to-server calls, CORS preflights.

## Version order

[Semantic Versioning 2.0.0](https://semver.org/#spec-item-11) precedence, never text comparison:
numbers as numbers (`1.10.0` > `1.9.0`), a prerelease below its release (`1.1.0-rc.1` < `1.1.0`), build
metadata ignored (`1.1.0+2` = `1.1.0`). A build passes when its version is at or above the minimum.

The cases live in [app-version-vectors.json](app-version-vectors.json), and **both** suites run that one
file — `AppVersionTests` in C# and `app_version_test.dart` — so the server and the phone cannot come to
disagree about which build is newer. An unreadable version is refused by the server; an unreadable
minimum is ignored by the app, which then defers to the server.

## Changing the contract

### In the repository — one change set

1. **The app reads the new contract, and still runs against the old one.** A field it cannot read
   degrades to "nothing to show" rather than throwing, so the build can be published before the API
   changes. `ResolvedText.maybe` is the pattern.
2. **`Khadra.Mobile/pubspec.yaml` rises to a new MAJOR.MINOR.PATCH.** The `+build` number alone does
   not count: the comparison ignores it.
3. **The API serves the new contract.**
4. **`MobileApp:MinimumSupportedVersion` in `Khadra.WebAPI/appsettings.json` rises to that version**,
   with a test saying why the previous build must stay refused —
   `The_minimum_this_release_ships_with_keeps_the_installed_1_0_0_build_out` is the example.
   `MobileAppMinimumVersionTests` fails if a tracked settings file ever sets the minimum above the
   app's own version.

### In production — the order is fixed

1. **Build and sign the new app** — with the same key as the builds on phones, or it will not
   install over them — and **publish it** where `MobileApp:UpdateUrl` points.
2. **Confirm it works against the API that is live.**
3. **Only then deploy the API carrying the raised minimum**, with whatever migration it needs
   ([production.md](../production.md)). Read the `Customer app: builds older than …` boot line.
4. **Confirm the new build is served, not refused.** It must load normally, and the API log must show
   no `Refused an old customer-app build (…)` line for it. If it does, clear the minimum
   (`MobileApp__MinimumSupportedVersion` set empty) and restart while you find out why.

A minimum raised before step 1 refuses every customer with nothing to update to. That order has no
test: it is the rule.

## Issued financial documents: the reader's contract

A financial document's `snapshot` (payments Phase 5; `GET /api/v1/financial-documents/{id}`) is a
permanent record, rendered exactly as stored by the website, the app and the console. Its content
grammar is a contract with every installed app, and one the wire cannot show: a new snapshot version
changes no field of the DTO and fires no 426. So both halves are written down here, and one fixture
proves them (payments Phase 5b; the plan's §3.3).

**The grammar, version 1.** `content { title, headline { label, money }, sections[] { key, heading,
lines[] }, timeNote, notice }`; a line is `{ key, label | null }` plus exactly one of `money { amount,
currency }`, `instant { utc, local }`, `text { en, ar }` or `plain`; every label, heading, text, note and
notice is `{ en, ar }`.

**What a reader does.**

- **It gates on the DTO's `snapshotSchemaVersion`**, never on the snapshot's own `schemaVersion`, and
  renders only the versions it knows.
- **It ignores** a key it does not know, anywhere; renders sections and lines **in the order given**,
  never picking one out by its `key` (keys are for tests and for the Phase 6 layout); reads a line's
  ABSENT `label` as null (the value stands alone); and reads an absent `timeNote` or `notice` as nothing
  to show — a client never writes the tax-invoice sentence itself.
- **It fails closed** — no partial rendering; the page shows the facts the DTO carries outside the
  snapshot (title, number, version, standing, headline, dates) and says the document cannot be shown
  here, the app with its update action — on: a schema version it does not know; a missing `title`,
  headline `label` or `money`, a section's `heading` or `lines`, or a line's `key`; a line with no value
  or with more than one (a value key holding `null` counts as no value); a text that is not two strings;
  a known key of another type (an `amount` or a `plain` that is not a string). A financial record shown
  with a line quietly missing is worse than none on screen.
- **It degrades, never fails,** on a value it recognises but cannot format: an `amount` that does not
  match `^-?\d+(\.\d+)?$`, or a `local` that is not a real `yyyy-MM-dd HH:mm`, is printed as stored,
  isolated.
- **It reports a refusal with the document's id and schema version only** — never the snapshot, which
  holds a customer's name and their money.

**How a value is shown.** `money`: from the stored string — the whole part grouped for the reader, the
fraction exactly as stored, the value's own currency placed and isolated as every other amount is —
never through a floating-point number and never re-scaled by today's `/app-config`. `instant`: its
`local` Amman wall time, never moved through the device's zone or re-derived from `utc`. `text`: the
screen's language. `plain`, a literal as registered: **left to right** when it is Latin (a number, a
reference, a plate, an e-mail, a phone — "+962 6 000 0000" has no strong character and would be
reordered on an Arabic page), and **in its own direction** (a first-strong isolate: `<bdi>`, U+2068)
when it contains an Arabic or Hebrew letter — a customer's or an office's name as registered. A LIST row
is a live screen, not the record: its `headline.amount` is a plain number with no scale of its own, so
it goes through the live money formatter.

**What the server may do within version 1**: add keys to any object; add, remove or reorder sections
and lines; use new section or line keys; change any wording; leave `previous` null; send a `type`,
`status` or `cause` no client has seen (clients word an unknown standing as the server's own name
rather than say nothing, which would present the document as current).

**What forces a new schema version**: a known key changing type or meaning; a fifth kind of line value,
or "exactly one of `money`, `instant`, `text`, `plain`" no longer holding; a change to the `amount` or
instant formats; a required element that may be absent.

**Publish first.** The composer writes a new schema version only **after an app build that renders it
is published and confirmed working** — the order of `MobileApp:MinimumSupportedVersion` above, for the
same reason and more so, because documents are permanent and issued on the server's own schedule.
Every client keeps each older renderer for good: version 1's reader stays beside any later one.

**The shared fixture.** [financial-documents-v1.json](financial-documents-v1.json) holds customer pages
of every shape version 1 has — a voided receipt and its correction, a superseded statement, cash
recorded at handover, a payment in full, a free cancellation's refund, a capture never applied, a
dispute's refund and the statement it decided — composed by the server's own composer, linked by its
own page builder and serialised as the endpoint serialises them, with ids and booking references
renumbered. `FinancialDocumentFixtureTests` fails when the composer writes anything else; a change to
the words of a permanent record is meant to show as a diff in review. Regenerate with
`KHADRA_REGENERATE_CONTRACT_FIXTURES=1 dotnet test --filter FinancialDocumentFixtureTests` (PowerShell:
`$env:KHADRA_REGENERATE_CONTRACT_FIXTURES='1'; dotnet test --filter FinancialDocumentFixtureTests`, then
`Remove-Item env:KHADRA_REGENERATE_CONTRACT_FIXTURES`). A new grammar is a NEW file beside this one,
never an edit to it. The website's reader and renderer specs read it (Phase 5b, slice 1), and so do the
app's reader, presenter and screen tests (`financial_documents_contract_test.dart`,
`invoice_presentation_test.dart`, `invoices_screen_test.dart`, `booking_payments_test.dart`; slice 2);
the console's presenter spec will read the same file.

## Additive changes on record

Changes that needed no raised minimum, because no installed build reads or sends anything different:

- **Additive (2026-09-24):** `GET /api/v1/vehicles/facets` gained `carTypes` — `{ carTypeId, listedVehicleCount, coverImageUrl }` per type, `coverImageUrl` nullable — beside `carTypeIds`, which is unchanged and still read by installed apps.
- **Additive (2026-09-24):** `GET /api/v1/galleries` accepts two optional query parameters, `text` (part of the office name, case-insensitive) and `deliveryOnly` (default `false`); a request without them is answered exactly as before.
- **Additive (2026-09-24):** the booking's `terms` gained `commissionBasis` (`"OneDay"` or `"RentalTotal"`); `commissionPercent` is unchanged. A customer's copy of a booking now carries `commissionAmount: null` — no customer client has ever read it.
- **Additive (2026-09-24):** `PaymentDto` gained `purpose` (`"Deposit"` / `"FullPayment"`) and `processingFee`; `payment` on a customer's booking gained `options[]` — `{ purpose, selectedPaymentAmount, processingFee, totalChargedNow, remainingBalanceAfter }`. New endpoints: `GET /api/v1/bookings/{id}/payment-options` and `POST /api/v1/bookings/{id}/checkout { purpose }`. `POST …/deposit-checkout` is unchanged and still opens the deposit.
- **Additive (2026-09-25):** a booking gained `isPaidInFull` (boolean; `false` from an older API) and `confirmingPayment` — `{ purpose, amountCharged, processingFee, appliedToBooking, paidAt, refundOnFreeCancellation }`, `null` until a payment confirms the booking, `purpose` only ever `"Deposit"` or `"FullPayment"`. The website, the consoles and app 1.3.0 word a booking paid in full from them instead of calling it a deposit. An installed 1.2.x build ignores both and keeps its deposit wording — for a booking paid in full on the website it still reads "Deposit paid", beside the correct `balanceDue` of zero; only updating the app changes that. The push and email texts of `YourBookingConfirmed` and `YourDepositRefunded` now say "payment" instead of "deposit"; both kinds keep their names.
- **Additive (2026-09-26, payments Phase 3):** a booking gained `refunds[]` — `{ refundId, paymentId, reason, amount, status, requestedAt, sentAt, settledAt, failedAt, disputeTicketId }`, every refund against the booking's payments, oldest first — and the server's two totals `refundedAmount` (settled) and `refundOutstandingAmount` (requested, sent or failed), both in the booking's currency. `reason` is one of `FreeCancellation`, `PlatformCancellation`, `EndedBeforePickup`, `DisputeWindowClosed`, `DisputeResolution`, `OrphanedCapture`; a client words one it does not know as a plain "Refund". `cancellation` gained `refundAmount` (what cancelling now returns to the card, null when nothing), and `confirmingPayment` gained `refundableFee`. `POST /api/v1/bookings/{id}/cancel` accepts an optional `expectedRefund`: when it no longer matches, nothing is cancelled and the answer is **409 `booking.refund_changed`** with the current figure top-level as `currentRefund: { amount, currency }`; a request without it is answered exactly as before. A new notification kind, `YourPartialRefundSettled`, is sent when a refund settles while part of the payment is still out; an installed 1.2.x build shows its generic notification line for it. **Same field, wider meaning:** `depositRefund` now also carries an administrator's whole-payment refund (`PlatformCancellation`) and the deposit released when the dispute window closed cleanly (`DisputeWindowClosed`), not only a free cancellation's — installed builds word it neutrally as a refund of the deposit or the payment, which both are. An installed build does not show the refund of the money above the deposit (`EndedBeforePickup`); only updating the app does.
- **Additive (2026-09-26, pre-launch item 169):** a dispute (`GET /api/v1/disputes/{id}`, and the answer to opening one) gained `depositOnBooking` — the deposit held for disputes before any was resolved — and `decidedByEarlierTickets` — what the booking's earlier RESOLVED disputes already decided, zero on a first dispute — both `{ amount, currency }`. **Same field, narrower value:** `depositHeld` still means what this ticket can split, and on a later ticket that is now what earlier ones left (`depositOnBooking` less `decidedByEarlierTickets`, computed by the server; no client subtracts), so after a resolved dispute a second one reads `0.000` where it used to offer the whole deposit again. Opening a second ticket is accepted exactly as before. An installed 1.2.x build reads nothing new: on a resolved later ticket its decision card shows the `0.000` that ticket split, under the "Security deposit" label it has always misused there; app 1.3.0 labels it as the deposit, adds a row for what earlier disputes decided, and a notice on a live ticket; the website and both consoles show the same notice. A resolution whose split does not add up to that basis is refused (`dispute.disposition_unbalanced`, unchanged), and a charge to the office is now bounded by the booking's penalty range across all its disputes (`dispute.dealer_charge_out_of_range`, unchanged code). New refusal `dispute.deposit_over_allocated` (409) when earlier decisions already allocated more than the deposit, which nothing on the platform can produce: it guards data corrected by hand. Resolving is an administrator's action; no customer build sends it.
- **Console only (2026-09-27, payments Phase 4b):** the rental office's copy of a booking (`GET /bookings/{id}` for the office's owner and staff, and the answers to approve, reject, pickup and return) no longer carries `refunds`, `refundedAmount`, `refundOutstandingAmount` or `depositRefund` — the refund list holds a dispute decision's row, the customer's share — and `confirmingPayment` keeps only `purpose`, `appliedToBooking` and `paidAt`: `amountCharged`, `processingFee`, `refundOnFreeCancellation` and `refundableFee` are null, as is `cancellation.refundAmount`, because each carries the processing fee the office never sees (owner decisions 3 and 8). The office's copy of a dispute (`GET /disputes/{id}` and the answers to open, add a statement and withdraw) carries `resolution.refundToCustomer`, `resolution.retainedByPlatform` and `resolution.waivesEverything` as null: the office is shown the basis, its own share and any charge assessed to it (pre-launch item 151, office half, owner 2026-09-27). The customer's and the administrator's copies are unchanged — the customer's half of item 151 is deferred — and no customer build reads the office's. New administrator endpoints, read by the console only: `GET /api/v1/admin/payments` (paged, filtered), `/admin/payments/vocabulary`, `/admin/payments/{id}`, `/admin/refunds` and `/admin/dashboard/finance`; the attention queue's `slaDeadlineAt` may be null (a row with no clock) and it gains the kinds `RefundFailed`, `OrphanedCaptureOwed` and `DepositAwaitingDecision`. The financial state gains `summary.days`, `summary.dailyRate`, `summary.depositPercent` and each refund's `bookingPart` — additive.
- **Additive (2026-09-27, payments Phase 5a):** issued financial documents, on new endpoints only. The customer: `GET /api/v1/customers/me/financial-documents?type=&page=&pageSize=` (paged, newest issued first), `GET /api/v1/financial-documents/{id}` (404 for anyone but the document's customer) and `GET /api/v1/bookings/{id}/financial-documents` — `{ bookingId, documents[], beingPrepared[] }`, both lists empty for the rental office, 404 for anyone who is not a party. A list row is `{ documentId, type, number, version, status, bookingId, bookingReference, title { en, ar }, headline { label { en, ar }, amount }, cause, occurredAt, issuedAt }`; a document adds `snapshotSchemaVersion`, `snapshot` (the stored document itself, a JSON object, rendered as it is — every figure and word a client shows), `links { versions[], previousVersion, nextVersion, replacedBy, paymentReceipt, refundReceipts[] }` and `voided { voidedAt, replacedBy }` (never the reason); `beingPrepared[]` is `{ type, subjectId, occurredAt }`. `type` is `PaymentReceipt`, `RefundReceipt` or `BookingStatement` and `status` `Current`, `Superseded` or `Voided`; a client words neither — the titles are in the data — and a type or status it does not know it lists as the data says. The administrator, console only: `GET /api/v1/admin/financial-documents` (filtered, paged), `/vocabulary`, `/holds`, `/{id}`, `POST /{id}/void` (201 with the correction's `Location`; 409 when not current, already voided, or the correction cannot be issued — records need review, no issuer; 422 when it could not be composed; nothing is voided in any of these) and `GET /api/v1/admin/bookings/{id}/financial-documents`; `GET /api/v1/admin/payments/{id}` gains `documents[]`, and the attention queue the kind `FinancialDocumentsOnHold`. One value is aligned: in the financial state (`GET /bookings/{id}/financials`) and the administrator's payments list, a capture that could not be applied now gives `occurredAt` as the instant it was recorded, as an applied payment always has, so a document and the live page state one time for one payment; the field and its type are unchanged. Nothing an installed build reads changed; app 1.3.0 (unreleased) is the first to read these, so no minimum is raised.
- **Additive (2026-09-26, pre-launch item 173):** `penalty.state` on a booking — `Assessed` (assessed, and nothing charged: no dispute on the booking has been resolved) or `ResolvedByDispute` (a dispute on the booking was resolved, and its decision is what the assessment became) — read by the server from the booking's own dispute tickets. `cancellation.penalty` is a preview, not yet assessed, and carries none. The website and app 1.3.0 word the two states in the owner's sentences and say nothing for a state they do not know; installed 1.1.0/1.2.x do not read it, so no minimum is raised.
- **Additive (2026-09-26, payments Phase 4a):** two new endpoints serve a booking's financial state, `GET /api/v1/bookings/{id}/financials` — the customer's projection for the booking's customer, the office's for its owner and active employees, 404 for anyone else, exactly as `GET /bookings/{id}` — and `GET /api/v1/admin/bookings/{id}/financials`. The answer is `{ bookingId, bookingStatus, currency, generatedAt, calculatorVersion, needsReview, summary { rentalSubtotal, deliveryFee, bookingTotal, requiredDeposit, securityDeposit, paidOnline, processingFees, chargedOnline, refunded, refundInProgress, refundDelayed }, balance { state, amount, cashRecorded[] }, deposit { state, amount, windowEndsAt, refund, decision }, commission, payments[] { paymentId, purpose, status, refundProgress, occurredAt, appliedToBooking, amountCharged, processingFee, feeRefundable, refunds[], … }, issues }`. A field a reader may not see is `null` for that reader: the customer never receives `commission`, provider references, failure codes or the sandbox marker; the office never receives a processing fee or the customer's dispute share; `issues` is the administrator's. The office's `refundProgress` is read over the refunds the office is shown, and is `Complete` only when those return all the booking money the payment applied. While `needsReview` is true the customer clients state the review notice and the recorded facts (what was paid, each refund and its status) and no sentence about the deposit: the records contradict one another, and a reading of them could be the contradiction itself. The state codes (`balance.state`, `deposit.state`, `commission.state`, `refundProgress`) may grow: a client words one it does not know by saying nothing about it. `BookingDto` is unchanged. App 1.3.0 reads the new endpoint and, on a 404 (an API without Phase 4), keeps showing what the booking carries; installed 1.1.0/1.2.x never call it, so no minimum is raised.
- **Same field, now always true (2026-09-24):** `pricing.balanceDue` is served as what is STILL owed — the frozen cash balance until a payment lands, then the total less what was paid online. Installed apps print it as "pay at pickup"; before full payment existed the two were always equal, and after it a fully paid booking correctly reads zero instead of asking for cash.
