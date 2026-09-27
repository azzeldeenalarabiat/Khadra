# Payments Phase 5b — the clients: the plan

**Status: PROPOSED on 2026-09-27, for the owner's review. Nothing in it is built.** It turns §6–§8 and §16
of the approved `docs/payments-phase5-plan.md` into work on the three clients, against the endpoints 5a
built and verified locally (§20 there; 27 `TEST-` documents, 26 read-only checks passed). **It changes no
API, no schema and no server behaviour** — the one optional server change (D6) is the owner's call. If
building it shows that anything else is needed, the work stops and the change comes back first. The
architecture advisor reviewed this plan before the owner saw it; what that changed is in §17.

---

## 1. What 5b is, and what it is not

**In:**
- the customer website (`Khadra.Web`): Booking Details' "Payments & Invoices", the Invoices & Receipts
  area, and a document's page with a print style;
- the customer app (`Khadra.Mobile`, in the unreleased 1.3.0): the same three;
- the administrator's console (`Khadra.Dashboard`): a Financial documents tab, a document's page with
  Void, the holds and their work-queue row, the booking's Money section and the payment page listing
  their documents, and the audit log naming a void;
- one shared contract fixture that all three clients' tests read (D5);
- tests for all of it, and the browser and mobile verification of §13 in English and Arabic, locally.

**Out:** PDF and download (Phase 6); email (Phase 7); payables (Phase 8); the rental office's console,
which shows no customer document (§7); any API, schema or server behaviour change; Staging and
Production.

---

## 2. Rules every surface keeps

These come from decisions already taken (plan §1, §3.7, §4; programme decisions 3, 6, 7, 8; the global
rule on static data). They are restated because every screen below depends on them.

1. **A document is rendered, never re-made.** Every word and figure INSIDE a document comes from its
   stored `snapshot.content`. A client adds no label, sums nothing, subtracts nothing, counts no days and
   converts no currency.
2. **What surrounds a document is live, and each client words it** — its standing (current, superseded,
   voided), the void notice, the version links, "being prepared", the link to its booking. These are
   facts the DTO carries outside the snapshot (plan §3.7: "a live screen is not a record").
3. **Each screen calls its own endpoint, and only that.** Booking Details calls
   `GET /bookings/{id}/financial-documents`; the area calls `GET /customers/me/financial-documents`; a
   document's page calls `GET /financial-documents/{id}`; the console calls the `admin/…` twins. No
   screen loads another screen's data.
4. **Customers see no test marker but the number itself, no void reason, no hold, no issue code.**
5. **No business number in a screen.** No "within a minute", no reason-length literal: the server's
   answer is what a screen shows.
6. **A document is permanent.** Every version — superseded and voided included — stays reachable from
   the customer's account.
7. **"Documents" already means other papers** on every client — identity papers (`/profile/documents`,
   `documents.*` keys, `features/documents/`) and the console's application documents (`kh-doc-tile`,
   `.doc-*`). Everything new is named for invoices or financial documents — `invoices` routes and keys
   for customers, `financial-documents` routes in the console, `FinancialDocument…` types — so the two
   never share a name.
8. **The addresses are permanent.** `/{lang}/invoices/{id}` and `/profile/invoices/{id}` will be emailed
   in Phase 7 and live in inboxes for years; they are never moved.

---

## 3. Reading a document: one renderer per client

### 3.1 The grammar (snapshot schema version 1)

Fixed in 5a (plan §20): `content { title, headline { label, money }, sections[] { key, heading, lines[] },
timeNote, notice }`, where a line is `{ key, label | null }` plus exactly one of `money`, `instant`,
`text` or `plain`, and every label, heading, text, note and notice is `{ en, ar }`. The stored content
already lays out everything a reader needs — the document's number, version and cause, the issuer and
the parties, the booking and the car, then the type's own sections — so **a renderer needs no wording of
its own**. Each client builds ONE renderer for this grammar and uses it for all three document types.

### 3.2 How each value is shown

| Value | Shown as | Never |
|---|---|---|
| `money { amount, currency }` | formatted **from the stored string**: the integer part grouped for the page's language, the fraction exactly as stored, the value's own currency placed and isolated as on every other figure on the platform. `document.amountScale` is checked against the fraction, never used to round | a floating-point number; a sum or difference; a default currency; a sign typed in front |
| `instant { utc, local }` | **`local`** — the frozen Amman wall time (`"2026-09-27 11:17"`) — formatted as a date and time in the page's language, Latin digits, on the wall-clock path both customer clients already have (the website's `formatCalendarDate` with `timeZone: 'UTC'`, the app's `Formats.calendarDate`); the document's `timeNote` says it is Amman time | converted to the device's or browser's zone (`toAmman`, `timeZone: zone`); re-derived from `utc` |
| `text { en, ar }` | the page's language | the other language, or a mix |
| `plain` | verbatim: isolated **left to right** when it is Latin (numbers, references, plates, e-mails, phones), and in **its own direction** (a first-strong isolate) when it contains an Arabic letter — a name as registered, which may be mixed | translated or re-formatted; an Arabic name forced left to right |
| `label: null` | the value alone, across the line (e.g. "Paid in full") | a label a client invented |

A value that does not match its pattern — an amount that is not digits and a point, a `local` that is not
`yyyy-MM-dd HH:mm` — is **printed verbatim, isolated**, never hidden (§3.3).

**List rows and "being prepared" rows are live screens, not the record.** A row's `headline.amount`
arrives as a plain number with no scale of its own, so rows use the platform's live money formatter, as
every other live figure does.

Tests on each client pin the digits (`0.000`, `12.750`, `1234567.890`, grouped per language), an
off-pattern amount printed verbatim, and `local` rendered unchanged whatever the machine's own zone.

### 3.3 A document renders whole, or not at all — the reader's contract

The DTO's `snapshotSchemaVersion` decides which renderer reads the snapshot (the snapshot's own
`schemaVersion` is not consulted). The version 1 reader:

- **ignores** keys it does not know, anywhere; renders sections and lines **in the order given**, never
  picking them by `key` (keys are for tests and for Phase 6's layout); reads a line's absent `label` as
  null; reads `timeNote` and `notice` as optional — absent, nothing is shown, and no client ever writes
  the tax-invoice sentence itself;
- **fails closed** only on a structural break: a missing `title`, headline `label` or `money`, a section's
  `heading` or `lines`, a line's `key`; a line with no value or with more than one (a value key holding
  `null` is no value); an `{ en, ar }` that is not two strings. A version it does not know is treated the
  same way. The page then shows the facts
  the DTO carries outside the snapshot (title, number, version, standing, headline, dates) and says the
  document cannot be shown here — in the app with the update action (`MobileAppConfig.updateUrl`, as the
  update screen uses it: a link when published, a sentence when not), on the website as "cannot be shown
  yet". A financial record with a line quietly missing is worse than none on screen;
- **degrades** on a value it recognises but cannot format (§3.2) — hiding a whole receipt over a missing
  leading zero would be the wrong failure;
- logs the document's id and schema version when it fails closed — **never the snapshot**, which holds a
  name and money.

Unknown `type`, `status` or `cause` values never break a list or a page: a row keeps its stored title and
headline, and an unknown standing is shown as the server's name in a neutral chip, isolated. That departs,
deliberately, from the customer clients' usual "say nothing about a code you do not know": saying nothing
about a document's standing would present it as current.

**The server's half, written into `docs/contracts/README.md` with 5b:**

- Within version 1 the server MAY, without a new version: add keys to any object; add, remove or reorder
  sections and lines; use new section or line keys; change any wording; leave `previous` null; send a
  `type`, `status` or `cause` no client has seen.
- It MUST issue a new schema version when: a known key changes type or meaning; a fifth kind of line value
  appears, or "exactly one of `money`, `instant`, `text`, `plain`" stops holding; the amount or instant
  formats change; or a required element may be absent.
- **Publish first.** A new schema version is invisible on the wire — no field of the DTO changes and no
  426 fires — so **the composer starts writing a new schema version only after an app build that renders
  it is published and confirmed working**: the same order as `MobileApp:MinimumSupportedVersion`, for the
  same reason, and more so, because documents are permanent and issued on the server's own schedule.
  Every client keeps each older renderer for good: `DocumentContentV1` stays beside any later version,
  never replaced.

List rows never depend on the grammar: `title` and `headline` are read by the server for every version.

### 3.4 What is worded around a document

| Fact (from the DTO) | Customer surfaces | Console |
|---|---|---|
| `status` `Current` | no chip; "Current version" in the version list | chip |
| `status` `Superseded` | chip "Earlier version", and "A newer version exists: {number}" linking the **newest version — the highest member of `links.versions`**, which is always current, because a void always carries its correction. Never `links.nextVersion`: that is only the next member, and can be a voided one | chip |
| `status` `Voided` | chip "Voided", and "Voided on {date}. Replaced by {number}." linking the correction — or "Voided on {date}." alone if `voided.replacedBy` is ever absent | chip, plus the void: who, when, the reason |
| `links.versions` | "Version {n} of {total}", every version listed with its number and standing | the same |
| `links.paymentReceipt` | on a refund receipt: "Issued against payment receipt {number}" | the same |
| `links.refundReceipts` | on a payment receipt: the refunds' receipts, each linked | the same |
| `bookingReference` | links to the booking's page | links to the booking's admin page |
| `beingPrepared[]` | "{type} — being prepared", **with the date of the money it is about** (`occurredAt`), so two payments give two distinguishable rows; an unknown type as "A document — being prepared" | the same, plus the holds |

"Earlier version" only ever appears on a statement: a receipt gains versions only through a void, and a
void wins over superseded (`FinancialDocumentStatus.Of`). The words for the three types in "being
prepared" are the server's own titles (إيصال دفع, إيصال استرداد, كشف حساب الحجز), so a row and the
document it becomes say the same thing.

---

## 4. The customer website (`Khadra.Web`)

### 4.1 Routes and entry points

- `invoices` and `invoices/:documentId` join the `pages` array in `app.routes.ts`, as siblings of
  `bookings` (so both languages get them), behind `signedInGuard`, each page setting
  `SeoService.set({ title, noindex: true })`.
- Both paths join **both** `PRIVATE_PAGES` and `BUILT_PRIVATE_PAGES` in `app.routes.server.ts`, which
  makes them browser-rendered and also drives the `X-Robots-Tag`/`no-store` headers, `robots.txt` and the
  `/en/` document direction.
- "Invoices & Receipts" is added to the account side navigation (`account-shell.component.html`), the
  header's account menu and the mobile drawer's signed-in block (`site-header.component.html`), with the
  existing `receipt` icon.
- No BFF change: the customer deployment's authenticated catch-all already forwards the three endpoints
  (`customer-web.json`, pinned by `BffProxyRouteTests`), and nothing new is anonymous.

### 4.2 Booking Details

- The payments card's heading (`payments.title`, today "Payments") becomes **"Payments & Invoices" /
  «المدفوعات والفواتير»** (decision 6; D1).
- Inside that card, under the payment history, a new `kh-booking-invoices` lists the booking's documents
  — title, number, standing, issue date, headline — each opening its page, then the "being prepared" rows
  with their dates. It has its own request and its own states: a compact skeleton line while it loads, a
  compact error with Retry if it fails, **never hiding the payment figures above it**; nothing at all when
  the booking has neither documents nor anything being prepared.
- It reloads on the card's existing `version` input (a payment, refund, handover or dispute changing), and
  a "being prepared" row carries **"Check again"**, which reloads only this list. No timer: the issuing
  pass runs on the server's schedule, and a screen that promised "in a minute" would be printing a server
  setting.

### 4.3 Invoices & Receipts — `/{lang}/invoices`

- Newest issued first, as the server orders them. Every version is listed, each marked (D2).
- Type filter as the bookings page's tabs (query parameter `type`): All · Payment receipts · Refund
  receipts · Booking statements. An unknown `type` in the address is read as All — the server refuses an
  unknown type with 400, and a mistyped link should not open on an error. Paging as the bookings page
  (query parameter `page`).
- A row: the stored title in the page's language, the number (isolated LTR), the version when above 1,
  the standing chip when not current, the booking reference, the issue date, the stored headline label
  and amount. The whole row opens the document.
- States: skeleton rows; empty; empty for a filter; error with Retry; a signed-out visitor goes to sign-in
  by the guard, and a session that ends mid-way by the interceptor, as elsewhere.

### 4.4 A document's page — `/{lang}/invoices/{documentId}`

- An id that is not a GUID, a 404 and a 403 all show the same "This document isn't available" panel, with
  the way back to Invoices & Receipts: nothing says whether it exists (as `booking-detail` already treats
  them).
- Top: the stored title, the number, the standing chip, "Version n of total", the booking link, and
  **Print**. Then the notices of §3.4, then the rendered document (headline, sections, time note,
  notice), then its versions and related receipts.
- **Switching language re-reads the page.** The website's two languages are separate routes, so — as on
  every account page — the page is rebuilt; both languages are in the one answer, so nothing else changes.
- **Accessibility:** each section is a heading and a `<dl>`; the standing is said in words, never by colour
  alone; the document region is labelled (`aria-labelledby`), as the website's cards are.
- **Print** (`window.print()`): a new `@media print` partial (`styles/_print.scss`; the website has none
  yet) prints the document — title, number, **the standing chip and the notices**, the rendered sections,
  the time note and the notice — and hides the header, navigation, footer, buttons and the related-
  documents list; black on white; no line split across pages. The standing and the notices printing is a
  tested requirement: a printed voided receipt without its notice would read as a valid one. A test
  document prints its `TEST-` number like any other; the watermark is the Phase 6 PDF's. The printer icon
  joins `icon-paths.ts`.

### 4.5 Files

New: `core/api/financial-documents.api.ts` (types); `features/invoices/invoices.component.{ts,html}`,
`invoice-page.component.{ts,html}`, `invoice-content.component.{ts,html}` (the renderer),
`invoice-presentation.ts` (pure: standing, notices, rows, the v1 reader — unit tested against the real
dictionaries and the shared fixture); `features/bookings/booking-invoices.component.{ts,html}`;
`styles/_print.scss`; specs beside them. Changed: `app.routes.ts`, `app.routes.server.ts`,
`account-shell.component.html`, `site-header.component.html`, `booking-payments.component.html`,
`core/i18n/en.ts` + `ar.ts` (`invoices.*` keys, and the one renamed value), `format.service.ts` (money
from a stored string; a frozen local time), `icon-paths.ts`, `styles.scss`. Components follow the
website's own conventions: `httpData` for requests, types only in `core/api`, global styles only, and no
field named `document` (components already inject `DOCUMENT` under that name).

---

## 5. The customer app (`Khadra.Mobile`)

### 5.1 Routes, guard and entry

- `Routes.invoices = '/profile/invoices'` and `Routes.invoice(id) => '/profile/invoices/$id'`, top-level
  `GoRoute`s like `/profile/saved`, each with `KhadraBack` (to Profile, and to the list).
- The guard (`router.dart`) matches exact paths, so `Routes.invoices` joins the `guarded` set **and**
  `location.startsWith('/profile/invoices/')` joins `needsAccount`; a guest is sent to sign-in with
  `next`, as for every route that acts on an account. `entry_gate_test` gains both.
- My Account (`profile_screen.dart`) gains one `_Row` — "Invoices & Receipts", the receipt icon — in
  "Your details". The tab bar keeps its five destinations (owner, 2026-09-20/21 and 2026-09-27).

### 5.2 Booking Details

- `paymentsTitle` ("Payments" / «المدفوعات») becomes "Payments & Invoices" / «المدفوعات والفواتير» (D1).
- Inside `_PaymentsSection`, under the payment history, an `_Invoices` block reads
  `bookingFinancialDocumentsProvider(bookingId)` — its own loading line, its own inline error with Retry
  that never hides the figures above, nothing when there is nothing — and "Check again" on a
  being-prepared row, which shows its date.
- The provider joins `invalidateBookings(ref, bookingId:)`, so pull-to-refresh and every booking action
  refresh it with the rest of the screen.

### 5.3 Invoices & Receipts

- `MyFinancialDocumentsNotifier` on the `PagedList` pattern of `MyBookingsNotifier` and
  `NotificationsNotifier`: infinite scroll through `EndOfListLoader`, `PagedListFooter`, pull-to-refresh,
  a failed later page keeping the pages already loaded (`showKhadraMessage(isError: true)`), and nothing
  fetched for a guest.
- Filter chips: All · Payment receipts · Refund receipts · Booking statements.
- Rows and states as the website's (§4.3), with `KhadraLoading`, `KhadraEmpty` and `KhadraError`.

### 5.4 A document's screen

- `financialDocumentProvider(id)`; a 404 answers null and shows the "isn't available" state.
- The renderer is a set of widgets over a parsed `DocumentContentV1`, built from the tokens in
  `khadra_theme.dart` only (no colour or radius outside it). Plain values follow §3.2's direction rule
  (`LatinRun` for a Latin literal; a first-strong isolate for one containing an Arabic letter); money
  through a `Formats` method that formats from the stored string (§3.2);
  instants from `local` on the `calendarDate` path, never through `toAmman`, which would move a time
  already in Amman.
- Switching language re-renders in place: the app's language is in-app state, and both languages are in
  the answer already loaded.
- The unsupported state of §3.3 uses `KhadraNotice` and `MobileAppConfig.updateUrl`.
- `khadra_api.dart` logs nothing today, and the parser keeps it so: the fail-closed path reports the
  document's id and schema version only.
- No share or print in the app in 5b: download is Phase 6's PDF.

### 5.5 Contract, version and the shared fixture

- **Additive only; no version change.** The app reads three endpoints that 5a added;
  `docs/contracts/README.md` already records them as read first by the unreleased 1.3.0, and the minimum
  stays 1.1.0. 5b adds to that README the reader's contract and the server's rules of §3.3.
- Models are hand-written in `dtos.dart`, as the app's others are: a new `BilingualText { en, ar }`, the
  row, links, void notice, booking documents and pending entries; server enums stay `String`s with a
  `switch` fallback; the snapshot stays a raw map until `DocumentContentV1.tryParse` accepts it whole.
- New error codes are mapped in `api_failure_messages.dart` (`financial_documents.not_found`,
  `financial_documents.unknown_type`).
- **One shared fixture pins the grammar for all three clients (D5).** `docs/contracts/financial-documents-v1.json`
  — versioned, and kept when a version 2 arrives — holds documents composed by the REAL composer with a
  plainly fake fixture issuer (never the local test identity, which lives in user-secrets). A C# test
  asserts, byte for byte, that the composer's output for the current schema version equals the file,
  with a stated switch to regenerate it; a wording change therefore shows as a diff in review, which is
  right for the words of a permanent record. It covers every shape the grammar has: an applied payment
  receipt with a fee; an orphaned one (the unlabelled line); a refund receipt from a dispute decision;
  statements with a penalty, cash recorded at handover, a deposit decision and the documents section; and
  a correction (`isCorrection`, the `corrects` line, `previous`). The app's parser test, the website's
  renderer spec and the console's presenter spec all read it. The Dart test also asserts that every value
  kind and the unlabelled line were seen — so the fixture is proven to cover the grammar — and adds
  synthetic fail-closed cases (no value, two values, an unknown version). It is created in 5b-1.

### 5.6 Files

New: `features/invoices/invoices_screen.dart`, `invoice_screen.dart`, `invoice_content.dart` (the
renderer), `invoice_presentation.dart`, `invoice_providers.dart`; tests beside the existing ones. Changed:
`core/router.dart`, `features/profile/profile_screen.dart`,
`features/bookings/booking_detail_screen.dart`, `features/bookings/booking_providers.dart`,
`lib/api/khadra_api.dart`, `lib/api/dtos.dart`, `core/format/formats.dart`,
`core/api/api_failure_messages.dart`, `lib/l10n/app_en.arb` + `app_ar.arb` (and the generated files).

---

## 6. The administrator's console (`Khadra.Dashboard`)

Built to `.claude/rules/frontend/angular-dashboard.md`: `.component.ts` + `.component.html` only, rules in
`src/styles/`, logic in presenters that take `t`, `enumLabel` and `format` and are unit tested; no
component specs.

### 6.1 Routes, tabs and titles

- `payments/financial-documents` (the list), `payments/financial-documents/holds` and
  `payments/financial-documents/:documentId`, declared **before** `payments/:paymentId` (as
  `payments/refunds` already is), and `holds` before `:documentId`.
- The Payments screen's hand-written tab bar gains a third tab, **"Financial documents" / «المستندات
  المالية»**, in `PaymentsComponent` and in the new list. The sidebar's Payments entry already lights for
  any `/payments/*` path.
- `SCREEN_TITLES` and `SCREEN_PARENTS` (`nav.data.ts`) gain the three paths. The topbar names a UUID
  segment by its route's title, so the breadcrumb reads Payments › Financial documents › Document — as the
  payment page's reads "Payment details" — and **the number leads the page's own header**.

### 6.2 The list

- `AdminFinancialDocumentsService`, on the Payments service's pattern: a root service with `filters` and
  `page` signals and `httpResource`s that stay idle until a screen shows them (list, vocabulary, one
  document, holds).
- Filters from `GET /admin/financial-documents/vocabulary` — type, standing, number, booking reference —
  and the issue days from/to as Amman calendar days (`<input type="date">`, sent as Payments sends them).
  A filter change returns to page 1. Query parameters are read once on entry, as Payments does, so the
  booking page can link the list filtered by its reference.
- Columns: the number (LTR) with the **Test pill** — the inline `pill s-warn` the console already uses,
  driven by `isTest` —, the stored title, the standing, the version, the booking reference (linked), the
  stored headline, the issue time. Skeleton, empty, failure and pager blocks as the Payments list's.

### 6.3 A document's page

- **Header:** the number, the Test pill, the stored title, the standing, "Version n of total", and links to
  the booking (`/bookings/:id`), the office (`/dealers/:id`), the customer (`/customers/:id`) and the
  **audit log** (`/audit-logs?entityType=FinancialDocument&entityId=…`, the inbound filter the booking page
  already uses).
- **The document**, rendered by the console's `kh-financial-document` under the rules of §3, in the
  console's language.
- **Recorded facts:** the snapshot's `facts`, read-only, as formatted JSON — the administrator's evidence,
  not a customer's reading.
- **Proof:** the frozen `provider`, `contentSha256`, and for a statement `coversThrough` and
  `checkpointFingerprint`, each in an `.ltr` run.
- **Family and links** as §3.4. **The void**, when there is one: when, by whom (`voidedByName`), the reason
  as typed text (`.user-text`), and the correction, linked.
- **Void and correct**, on a CURRENT document only — a superseded or voided one can never become current,
  so there is no disabled button to wait on:
  - `ConsoleUiService.openAction` with one required field named **`reason`** — a fixed name, never a
    translated one;
  - the dialog states the consequence first: permanent; a corrected document issued at once under a new
    number, from the booking's records as they stand; the customer sees it voided and replaced, never the
    reason;
  - the POST carries the antiforgery header, as every console write does; **201** → the toast "Voided
    {voided}. Issued {replacement}." and the correction's page;
  - **409 `not_current` or `already_voided`**: the document can never be current again, so a retry cannot
    succeed — the dialog closes, the refusal is a toast, and the page reloads to show the void or the newer
    version;
  - **409 `correction_records_need_review` or `correction_issuer_not_configured`, 422
    `correction_failed`, or a 400 on `reason`**: the dialog stays open with the words. All five codes
    join `WORDED_CODES` in both languages; the reason's length is the server's rule, answered by its 400
    through the named-field path — never a literal in the console.

### 6.4 Holds and the work queue

- `payments/financial-documents/holds`: type, booking reference (linked), reason
  (`enumLabel('financialDocumentHoldReason', …)`), attempts, first and last failure, next attempt
  (relative), and the last error in an `.ltr` run. Paged.
- The `FinancialDocumentsOnHold` row gets its target and title in `dashboard.presenter.ts`: "{count}
  financial documents on hold" (every Arabic plural form, from the row's own `count`), the server's
  references as its subtitle, and **Open → `/payments/financial-documents/holds` by path alone**, because
  the topbar bell reuses these rows and drops query parameters. Today the row already renders — an unknown
  kind is handled, and a spec pins that — but its Open goes to `/dashboard`.

### 6.5 The booking's Money section and the payment page

- **Booking page:** a documents block in the Money section, from
  `GET /admin/bookings/{id}/financial-documents`, keyed on the same `viewing()` as the booking and its
  financials and reloaded by the service's `refresh()`: the documents (number, Test pill, title, standing,
  headline, issued — each linked), what is being prepared, and the booking's holds with their reasons;
  "All documents for this booking" opens the list filtered by its reference.
- **Payment page:** `documents` joins `AdminPayment` in `payments.api.ts` (optional, as the API made it) and
  renders as the payment's receipts, each linked.

### 6.6 The audit log and the activity strip

- `ACTION_LABELS` and `ENTITY_TYPE_LABELS` (`audit-log.component.ts`) gain `FinancialDocumentVoided` and
  `FinancialDocument`, with keys in both languages; the action joins the voiding tone (it would read
  neutral today).
- The dashboard's recent-activity strip gains a sentence for a void (`dashboard.presenter.ts`).

### 6.7 Labels and formatting

- `EnumFamily` (`status-key.ts`) gains `financialDocumentType`, `financialDocumentCause` and
  `financialDocumentHoldReason`; the standing goes through `statusLabel` with a new `financialDocument`
  scope. A name the console does not know is spelled out, as every family already is.
- `FormatService` gains the two document formats of §3.2 — money from a stored string, and a frozen Amman
  local time — inside `core/i18n`, the only place `Intl` may be used.

### 6.8 Files — and the ones another session is editing

New: `core/models/financial-documents.api.ts`, `core/services/admin-financial-documents.service.ts`,
`features/payments/financial-documents.component.{ts,html}`, `financial-document-page.component.{ts,html}`,
`financial-document-holds.component.{ts,html}`, `shared/financial-document/financial-document.component.{ts,html}`
(the renderer), `features/payments/financial-documents.presenter.ts` + spec, and a styles partial.
Changed: `app.routes.ts`, `core/data/nav.data.ts`, `features/payments/payments.component.html`,
`payment-detail.component.html`, `core/models/payments.api.ts`,
`features/bookings/booking-detail.component.{ts,html}`, `core/services/admin-bookings.service.ts`,
`core/i18n/{status-key,problem,format.service,en,ar}.ts`, `core/services/dashboard.presenter.ts` + spec,
`features/audit/audit-log.component.ts`.

**The activity-feed session has uncommitted edits in five of those today** — `en.ts`, `ar.ts`,
`dashboard.presenter.ts` and its spec, and the audit log component — so the console is built last, and
only after that session has committed or the two sessions have agreed who edits what.

**A defect found while mapping this, outside 5b:** the booking Cancel dialog names its field
`t('myBooking.reason')`, which is `'reason'` in English and `'السبب'` in Arabic, while the handler reads
`values['reason']` (`booking-detail.component.ts:274`). An administrator using the console in Arabic
cannot cancel a booking: the reason arrives empty and is refused. The fix is one token (`name: 'reason'`)
in a file the other session is not editing. It is offered as its own task; if it has not landed by then,
it goes in as its own one-line commit before 5b-3.

Tests: presenter specs against the real dictionaries and the shared fixture — list rows, the document
page's view, the renderer's values (§3.2), the queue row's target and title in both languages and across
the Arabic plural forms, the void dialog's wording, the five refusals and which of them close the dialog;
`npm run i18n:check` at zero; `npm run build -- --configuration production`.

---

## 7. The rental office's console

Nothing changes (plan §4: offices see no customer document in Phase 5; the booking endpoint answers the
office two empty lists). Verified in §13: the dealer's booking page shows no document and makes no
documents request.

---

## 8. Test documents

| Surface | What marks a test document |
|---|---|
| Customer website and app | **the number itself** — `TEST-PAY-2026-000001` — shown verbatim; nothing else. No pill, no banner, and no client logic keyed on the prefix (owner, 2026-09-23: no sandbox marker in the customer's UI; 2026-09-27: on a document the prefix is the marker). The customer's DTOs carry no test flag, and a check on the prefix would be a second marker |
| Printed page (website) | the same number; the watermark is the Phase 6 PDF's |
| Console | the number, the existing **Test pill** from `isTest` — the server's reading of the frozen `provider`, never parsed from the prefix — and the existing standing Sandbox banner |
| The issuer block | as frozen: locally, "TEST ISSUER - local sandbox only, not a real company" |

On Staging, later: documents stay on hold until an issuer identity is configured there (the test identity
is refused outside Development), so a customer there sees "being prepared" rather than a document. That
is decided with the Staging rollout and pre-launch item 178, not in 5b.

---

## 9. States, surface by surface

| State | Area (list) | Document page | Booking section | Console |
|---|---|---|---|---|
| Loading | skeleton rows | skeleton | one compact line | table skeleton |
| Empty | "No receipts or statements yet." | — | nothing shown | "No documents match these filters" / "Nothing is on hold" |
| Being prepared | — | — | a row per pending document, with its date and "Check again" | the same, plus the holds |
| Error | panel with Retry, trace id | panel with Retry, trace id | compact error with Retry; the figures stay | ProblemSnapshot, trace id |
| Not found / not yours | — | "This document isn't available" | — | not-found state |
| Superseded / voided | chip | chip and notice linking onward | chip | chip, void panel |
| Unsupported snapshot | row as usual | the DTO's facts and "cannot be shown" (app: update action) | row as usual | the same, plus the raw facts |
| Off-pattern value | — | printed verbatim, isolated | — | the same |
| Signed out / session ended | guard to sign-in / interceptor | same | same | console sign-in |
| App below the minimum | the existing 426 update screen | same | same | — |

---

## 10. English, Arabic, right-to-left

- A document is shown in the language of the screen; in the app a switch re-renders it in place, on the
  website it re-reads the page (§4.4).
- Latin digits everywhere, as the platform's formatters already produce. Numbers, references, plates and
  e-mail addresses are isolated left-to-right; the server already isolates Latin runs inside its Arabic
  sentences.
- Direction-carrying icons (chevrons, back) mirror; the receipt and printer icons do not.
- Arabic vocabulary matches the customer app's (مكتب التأجير، سيارة، خضرا); inside a document the words
  are the server's.
- Checked at 360 and 375 pixels (app), 375 pixels and desktop (website), desktop (console), in both
  languages, with the longest real numbers (`TEST-STM-2026-000011`) and the longest Arabic labels.

---

## 11. New words, for review (D3)

| Where | English | Arabic |
|---|---|---|
| The area (owner) | Invoices & Receipts | الفواتير والإيصالات |
| Booking section (decision 6, D1) | Payments & Invoices | المدفوعات والفواتير |
| Console tab | Financial documents | المستندات المالية |
| Filter | All · Payment receipts · Refund receipts · Booking statements | الكل · إيصالات الدفع · إيصالات الاسترداد · كشوف حساب الحجز |
| Empty area | No receipts or statements yet. | لا توجد إيصالات أو كشوف حساب بعد. |
| Empty filter | Nothing of this kind yet. | لا يوجد شيء من هذا النوع بعد. |
| Standing: superseded | Earlier version | نسخة سابقة |
| Standing: voided | Voided | ملغى |
| Newer version | A newer version exists: {number} | توجد نسخة أحدث: {number} |
| Void notice | Voided on {date}. Replaced by {number}. (or "Voided on {date}.") | أُلغي في {date}، وحلّ محلّه {number}. (أو «أُلغي في {date}.») |
| Versions | Version {n} of {total} | النسخة {n} من {total} |
| Related | Issued against payment receipt {number} · Refunds from this payment | صدر مقابل إيصال الدفع {number} · المبالغ المستردة من هذه الدفعة |
| Being prepared | Payment receipt — being prepared · {date} (and per type) | إيصال دفع — قيد الإعداد · {date} |
| Check again | Check again | تحقّق مجددًا |
| Not available | This document isn't available. | هذا المستند غير متاح. |
| Cannot show (app) | This document needs a newer version of the app. | يحتاج هذا المستند إلى إصدار أحدث من التطبيق. |
| Cannot show (website) | This document can't be shown here yet. | لا يمكن عرض هذا المستند هنا بعد. |
| Print | Print | طباعة |
| Current version, in a version list | Current version | النسخة الحالية |
| The version list | Versions | النسخ |
| From a missing document back to the list | Back to Invoices & Receipts | العودة إلى الفواتير والإيصالات |
| From a document to its booking | Booking {reference} | الحجز {reference} |
| Under a document in a list | Issued {date} | صدر في {date} |
| Being prepared, a kind the client does not know | A document — being prepared | مستند — قيد الإعداد |

The last six rows were added while 5b-1 was built and approved by the owner on 2026-09-27; D3 approved
the whole table subject to the final bilingual visual check.

(The empty state deliberately promises nothing: "They appear here once a booking is paid" would be false
for the minute before the pass runs, and indefinitely while a document is on hold.)

---

## 12. Automated tests

- **Website** (`npx ng test --watch=false`, then `npx ng build`): the v1 reader and renderer against the
  shared fixture — every value kind, both languages, digits from the stored string, `local` untouched,
  plain values isolated, off-pattern values printed verbatim, a structural break and an unknown version
  refused whole; the presentation functions against the real dictionaries (the newest-version link from
  `links.versions`, the void notice with and without a replacement, an unknown standing spelled out); the
  list (filter, an unknown `type` read as All, paging, empty, error); the page (standing, notices, links,
  not-found for a 404 and a bad id); **the print rules keeping the standing and the notices**; the booking
  section (documents, dated being-prepared rows, its error leaving the figures); the guard; `PRIVATE_PAGES`
  containing the new paths; the dictionary parity spec.
- **App** (`flutter analyze`, `flutter test`): the models and `DocumentContentV1` against the shared
  fixture, with the coverage assertion and the synthetic fail-closed cases; unknown enum values and an
  unknown schema version; the renderer and screens at 360, 375 and 412 pixels in both languages with no
  overflow; the guard for both routes; the My Account row; `l10n_parity_test`, `rtl_audit_test` and
  `chevron_direction_test` as they stand.
- **Console:** §6.8.
- **Server:** the shared-fixture test (D5); `dotnet test Khadra.slnx` otherwise unchanged and green.

---

## 13. Verification in the browser (local, sandbox)

**Where.** The API with the sandbox and the test identity on 7112/5112 (`start-customer-web.ps1`; the
owner restarts it when asked — a restart is not something this session may do), the website on 4400, the
console on 4200 through the console BFF that the same script points at that API (the `bff` launch
configuration is not), and **the app as a Flutter web build on 4300** against 5112
(`--dart-define=KHADRA_API_BASE_URL=http://localhost:5112`; the development CORS policy already allows
4300), started by the owner with `flutter run -d web-server --web-hostname localhost --web-port 4300` in
`Khadra.Mobile` — not from `.claude/launch.json`, as first planned: the preview tool reads the main
checkout's launch configuration, not this worktree's, so an entry here could be neither used nor
tested. All of it in the browser pane: the website at desktop and 375
pixels, the app at 360 and 375, the console at desktop — each in English and Arabic.

**Who signs in (D4).** The owner signs each role in when asked — two customers, a dealer, an
administrator; this session never types a password. Everything else is driven through the screens, and
every new booking and payment is created through them, never in the database.

**Every screen in both languages; every flow once.**

| # | Scenario | Data |
|---|---|---|
| 1 | Every existing document: the list, each filter, paging, each type's page, the booking section of a booking with documents, print preview | the 27 existing `TEST-` documents |
| 2 | The open hold: `KH-95JGHJQZ` shows "Booking statement — being prepared" to its customer, and the hold to the administrator, on the work queue and the holds list | existing |
| 3 | Full payment, then a free cancellation: the payment receipt and statement v1; the cancellation's version; the refund receipt linked to the payment's receipt once it settles, and the next version; the older versions reachable and marked, each linking the newest | new, through the screens |
| 4 | A deposit booking: the receipt says what is due at handover; statement v1 | new |
| 5 | Void and correct, by the administrator: voided everywhere, the correction under a new number, the customer's notice without the reason, the audit entry; a second void of the same document refused and the dialog closed | on #3's receipt |
| 6 | Immutability: change the office's location and edit the car (its business name is locked after approval); every issued document reads as before. Then **record the pickup with cash** on #4 (or end it): editing a car or an office is not a checkpoint, so only a checkpoint issues the version that carries the change | #4 |
| 7 | Access: the second customer opening the first one's document link gets "isn't available" | #3 |
| 8 | The office: its console shows no document and requests none | #3 |
| 9 | A dispute's decision on a statement (the customer's share only) and its refund receipt | the existing `DisputeResolved` statement; a new dispute only if the owner wants one |
| 10 | Captured but not applied: pay in one tab, cancel in another, complete the sandbox checkout — a receipt "being refunded in full", then its refund receipt | new, if the sandbox checkout allows it; if it refuses a cancelled booking, 5a's tests cover it and the report says so |

**Left to automated tests, and said so in the report:** a business-rule change (it needs a configuration
change and a restart); clearing a hold (it needs records corrected, which no screen does today); the
unsupported-snapshot state (producing one would need a database edit, which is not allowed); and the 426
path (the minimum, 1.1.0, is below the app's 1.3.0).

---

## 14. Order of work and commits

1. **5b-1 — the website**, with its tests, **and the shared fixture** with its C# test, so the website's
   renderer is written against it from the start.
2. **5b-2 — the app**, with its tests.
3. **5b-3 — the console**, with its tests — last, because another session is editing the console's
   dictionaries, dashboard presenter and audit screen today (§6.8). The Cancel dialog's one-line fix goes
   before it as its own commit, unless the separate task has already landed it.
4. The browser verification of §13 across all three; fixes land in the commit they belong to.
5. The advisor reviews the finished work; then the docs — plan §21 "What 5b built", the programme's row 5,
   the contracts README (the reader's contract and the server's rules of §3.3), and the checklist if
   anything is deferred.

Each commit is local and made only on the owner's go-ahead, as 5a's were. Nothing is pushed.

---

## 15. Decisions for the owner

| # | Decision | Recommendation |
|---|---|---|
| D1 | The booking section's name, needed now because it is renamed in three clients. Decision 6 (2026-09-26) says "Payments & Invoices" / «المدفوعات والفواتير»; the plan's §17 left it to be confirmed now that the area is "Invoices & Receipts" | **Keep decision 6**: the section and the area share the word customers look for, and inside both every document keeps its accurate name and says it is not a tax invoice |
| D2 | What the customer's area lists | **Every version, each marked** — earlier and voided ones included, as the endpoint already answers and "permanent access" requires. If the list proves noisy once statements have four or five versions per booking, the alternative — current documents only, the rest behind each document's version list — needs an additive API filter, and would come back as its own change |
| D3 | The words of §11, in both languages | as written, or amended |
| D4 | Signing in for the browser verification | **The owner signs each role in when asked**; the alternative is local test accounts this session creates through the sign-up screens |
| D5 | The shared contract fixture (§5.5): one committed file and one server test beside the clients | **Yes** — the cheapest guard against breaking an installed app, and the one place all three renderers are proven against the same documents |
| D6 | Optional, a server change: `Cache-Control: no-store, private` on the three customer document endpoints. The API sets it today only on file bodies; booking JSON has the same posture, so this is not a 5b failure — but a document is the record with the customer's name on it | **Yes, as its own small change in 5b-1** if you approve it; otherwise left as it is |

---

## 16. Risks, and what answers them

| Risk | Answer |
|---|---|
| A new snapshot schema version written before any installed app can read it — every new document unreadable in the app, with nothing to install | the publish-first rule of §3.3, in the contracts README; every client keeps each older renderer |
| A reader too strict (hiding a receipt over an extra key or a new section) or too loose (showing a receipt with a line missing) | the three tolerance classes of §3.3 — ignore, fail closed, degrade — each tested against the shared fixture and synthetic breaks |
| "A newer version exists" linking a voided document | the newest is the highest member of `links.versions`, never `nextVersion`; tested |
| Another session is editing the console's `en.ts`/`ar.ts`, dashboard presenter and audit screen (uncommitted) | the console comes last; before touching those files, agree with that session who edits what, or wait for its commit |
| A time shown in the device's zone | instants render from the frozen `local` string, never through a zone conversion; tested with the machine's zone set elsewhere |
| Digits drifting through a float or today's scale | money formatted from the stored string; the digits tests of §3.2 |
| A long number or Arabic label overflowing at 360 pixels | widget tests at 360/375/412 in both languages; the browser pass at 360 and 375 |
| A printed voided receipt read as valid | the standing and the notices print, and a test pins it |
| A "being prepared" row that never clears (a hold) | it is the truth — the document is owed and not issued — and the administrator sees the hold on the work queue |

---

## 17. What the architecture review changed (2026-09-27)

The advisor found the plan sound — it reads the 5a code correctly and needs no API or schema change — and
changed these before the owner saw it:

1. **The contract that outlives every build (§3.3).** The server's half gained a publish-first rule — a new
   schema version is invisible on the wire, so the composer writes one only after an app that renders it
   is published — and the reader's contract became three written classes: what it ignores, what fails it
   closed, and what merely degrades.
2. **The newest version is the highest member of `links.versions`**, not `nextVersion`, which can be a
   voided document; the void notice has a fallback for a missing replacement; the printed standing and
   notices are a tested requirement.
3. **Money is formatted from the stored string** — the integer part grouped, the fraction kept — rather
   than through a float at a given scale; list rows, which carry no scale, use the live formatter.
4. **The website re-reads a page when the language changes** (its languages are separate routes); the
   draft's "no second request" was not true of it, and only the app keeps that promise.
5. **The void dialog closes on a refusal that can never succeed** (`not_current`, `already_voided`) and
   reloads; the console's routes are named `payments/financial-documents`, since "documents" is taken there
   too; the breadcrumb claim was corrected.
6. **The shared fixture** is versioned, covers every shape of the grammar including a correction, is read
   by all three renderers, and comes first, in 5b-1.
7. **"Being prepared" rows carry their date**, and the empty state no longer promises when documents
   appear.
8. **The browser scenarios name the checkpoint** that issues the version carrying an edited car or office,
   and list what only automated tests can reach.
9. **The fail-closed path logs ids, never the snapshot**; an unknown `type` in a website address reads as
   All; the addresses are recorded as permanent; accessibility is stated.
10. **Cache headers** on the customer document endpoints were raised as an optional server change for the
    owner (D6).
