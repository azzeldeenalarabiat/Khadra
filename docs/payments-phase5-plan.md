# Payments Phase 5 — issued financial documents: the plan

**Status: approved by the owner on 2026-09-27, with the decisions in §17** (also recorded in
`docs/payments-programme.md`). The owner approved the SQL of §13 for a disposable scratch database and the
local `khadra_web_it` only: the scratch proof passed (35/35) and the local database was migrated. Staging
and Production are NOT migrated. **5a, the backend, is built** (§20); 5b, the clients, is next. The plan
was reviewed by the architecture advisor before it was written up; what that review changed is in §19.

---

## 1. What this phase starts from

### Fixed by the owner on 2026-09-27

1. **The database record is the official source of truth** for every issued financial document. A PDF
   (Phase 6) and an email (Phase 7) are outputs generated from the stored, immutable snapshot — never
   the record.
2. **Documents are immutable.** Renaming the office, editing the car, changing prices or platform rules
   never changes an issued receipt or statement.
3. **A refund never overwrites a payment receipt.** It gets its own Refund Receipt, linked to it.
4. **Booking Statements are versioned.** When money moves again, a new version is issued and every older
   version is kept.
5. **Customers get an Invoices / الفواتير area** in the app, permanently, and reach documents from
   Booking Details too.
6. **Phase 6 renders PDFs** from the stored snapshots; **Phase 7 emails** them. Email delivery tracking
   (queued / sent / failed, timestamps, retries, audit) is designed now.
7. **Nothing is called a "Tax Invoice" / «فاتورة ضريبية»** until the legal and tax requirements for that
   term are implemented.
8. **The backend is the only financial source of truth.** No client reconstructs a document figure.

### Fixed earlier (`docs/payments-programme.md`)

- Decision 3 — each reader sees only its own dispute share.
- Decision 4 — each refund's split into booking money and processing fee is **stored** in Phase 5. Today
  it is recomputed on every read by `Payment.FeeInside`.
- Decision 6 — the booking's "Payments" section becomes **"Payments & Invoices"** once documents exist.
- Decision 7 — customers see an orphaned capture as a transaction being refunded in full, and never see a
  failed attempt that charged nothing.
- Decision 8 — rental offices never see processing fees.
- Pre-launch item 172 — the required customer scope of Phases 5–7.
- Sandbox — `payments.provider = SANDBOX` is the one marker of test money; there is no second flag.

### What Phase 4 hands over

`BookingFinancialsCalculator` computes ONE answer per booking from facts that never change once written
(frozen pricing and terms, each payment's amount and fee, each refund's amount and reason, each dispute
decision's shares) plus refund status; `BookingFinancialsDto.For` projects it per reader. Its own
documentation already says why: *"Phase 5 freezes this whole answer into an issued booking statement,
which is why every state is a stable code and CalculatorVersion travels with it."* So:

- a **Booking Statement** is `BookingFinancialsDto.For(financials, BookingParty.Customer)` — the
  customer's projection — frozen, with the parties frozen beside it;
- a **Payment Receipt** and a **Refund Receipt** are frozen descriptions of one payment and one refund,
  in the customer's terms, with the parties frozen beside them.

---

## 2. What belongs to which phase

| | **Phase 5 — now** | **Phase 6 — PDF** | **Phase 7 — email** | **Phase 8 — settlements** |
|---|---|---|---|---|
| Records | the three document types: issued, numbered, immutable, versioned; the refund split stored; voids and corrections; issuance holds | a stored PDF per document and language, with its hash | a delivery record per email, with every attempt | the office payables ledger |
| Customer | the Invoices & Receipts area (app and website); documents on Booking Details; each document in English and Arabic; "Payments & Invoices" | view / download the PDF | the email itself | — |
| Administrator | documents list and page; void and correct; holds on the work queue | the PDFs | delivery status and history per document; resend | office statements, payouts |
| Rental office | nothing (§4) | — | — | its own payables statements |
| Tables | `financial_documents`, `financial_document_voids`, `financial_document_series`, `financial_document_issuance_holds`; the `payment_refunds` split | `financial_document_renditions` | `financial_document_deliveries`, `financial_document_delivery_attempts` | ledger tables |

---

## 3. The model

### 3.1 A bounded context of its own: `FinancialDocuments`

`Khadra.Domain/FinancialDocuments`, `Khadra.Application/FinancialDocuments`, and its persistence under
`Khadra.Infrastructure/Persistence/Configurations/FinancialDocuments`. It **reads** bookings, payments,
refunds, dispute tickets, users, dealers, vehicles and the lookups; it references them **by id only**
(no foreign keys across contexts — `payments.booking_id` has none today); it never writes to another
context. The only change it makes elsewhere is decision 4's refund split (§3.12), which the owner has
already approved.

It is called *financial documents*, never *documents*: this codebase already uses "documents" for three
other things — a customer's identity documents (`/customers/me/documents`), a booking's renter documents,
and the signed-file endpoint (`/api/v1/documents`).

### 3.2 The three documents, and when each is issued

| Document | Number | Issued when | About | Linked to |
|---|---|---|---|---|
| **Payment Receipt** / إيصال دفع | `PAY-…` | a payment is **captured**: applied to the booking, or captured and not applied (orphaned — being refunded whole, decision 7). Never for a failed, expired or abandoned attempt. | one payment | its booking |
| **Refund Receipt** / إيصال استرداد | `RFD-…` | a refund is **settled** — the provider confirmed the money is back. A refund that is recorded or on its way shows on the statement as in progress; one that is refused stays owed and gets no receipt. | one refund | the Payment Receipt of the payment it returns — which it never alters |
| **Booking Statement** / كشف حساب الحجز | `STM-…` | the first time money is captured on a booking, then after each later **checkpoint** (§3.3) | one booking | its previous version |

Relationships: a booking has many payments; a payment has many refunds. A payment has one receipt
family, a refund one, a booking one statement family; each family is a chain of versions (a receipt
only gains a version through a correction, §3.5). Every document also carries its booking, customer and
office, for listing and for access control.

### 3.3 When a statement version is issued — a closed list of checkpoints

Money moving is defined as exactly these **checkpoints**, each a fact stored with its instant:

1. a payment **captured** — applied or orphaned (`payments.applied_at` / `orphaned_at`, its amount);
2. a refund **settled** (`payment_refunds.settled_at`, its amount);
3. a dispute **resolved** (`dispute_tickets.closed_at` on a resolved ticket, its shares);
4. the booking **ended** — cancelled, no-show, completed or closed by a dispute (`bookings.finished_at`);
5. cash **recorded at a handover** by the office (the handover's amount and time);
6. a **receipt corrected** — the latest version of a payment or refund receipt, when that version is an
   administrator's correction (its issue instant). *Added by the owner on 2026-09-28 (pre-launch item 181,
   `07a7284`): the statement's new version lists the correction in place of the voided receipt and keeps
   the instant of the last money it states. A statement's own correction is never one.*

Nothing else is a checkpoint: not a refund being recorded, sent or refused; not a dispute opening; and
never a state that changes with the clock alone (a deposit's window closing flips its state with no row
changing — it must not issue a version on every pass afterwards).

Each statement stores **what it covered**: the latest checkpoint instant it saw (`covers_through`) and a
fingerprint of the checkpoint facts themselves (`checkpoint_fingerprint`). A booking is a candidate for
a new version when a checkpoint instant is later than `covers_through`, **or** when its latest statement
was issued within the late-commit margin (`FinancialDocuments:LateCommitMarginMinutes`, 10): a handler
takes its time before it commits, so a fact can carry an instant older than a statement that did not see
it. The fingerprint then decides — a new version only when the checkpoint facts differ. (As first written,
"a checkpoint later than `covers_through` less a margin" is true of every statement's own latest checkpoint
for ever, so every booking would have been re-examined on every pass; the margin now bounds the WINDOW in
which a statement is looked at again. The owner approved this direction on 2026-09-27: statements follow
real financial checkpoints and their fingerprints, never every background pass.) The statement's CONTENT
is the customer projection at issue, so it also shows in-progress refunds and the deposit's state at that
moment; only the decision to issue is driven by checkpoints.

Everything a document is composed from is read **as committed** (owner, 2026-09-27): the booking comes
from the repository itself, never the clock-settling one every handler uses, which applies a lapse in
memory and stamps the ending with the current instant — an ending nobody saved, and a checkpoint that
would move on every pass. The settlement pass commits every lapse before it issues anything.

Worked example — a paid booking cancelled late, disputed, and settled:

| When | What happened | Documents issued |
|---|---|---|
| 25 Sept 20:56 | full payment captured (102.750 JOD) | `PAY-2026-000001`; `STM-2026-000001` (v1: paid in full) |
| 26 Sept 05:14 | customer cancels late: 84.750 to be refunded above the deposit, 18.000 held for the penalty | `STM-2026-000002` (v2: refund on its way, deposit held) |
| 26 Sept 05:15 | that refund settles | `RFD-2026-000001` (84.750, linked to PAY-…001); `STM-2026-000003` (v3) |
| 26 Sept 05:37 | dispute resolved: 9.000 to the customer | `STM-2026-000004` (v4: the customer's share decided) |
| 26 Sept 05:40 | the 9.000 refund settles | `RFD-2026-000002` (9.000, dispute decision); `STM-2026-000005` (v5) |

`PAY-2026-000001` never changes. Versions 1–4 stay readable forever as superseded.

### 3.4 Status — derived, never stored

A document row never changes, so its status is worked out when it is read:

- **Voided** — a void was recorded against it (§3.5);
- **Superseded** — a later version of the same family exists;
- **Current** — otherwise.

### 3.5 Voids and corrections

A document issued wrongly — a bug, bad data — is **voided, never edited or deleted**:

- only the **current** version of a family can be voided; superseded versions are preserved history;
- the void and its **replacement** are written in ONE transaction: the void (with a reason and the
  administrator) and the correction — the receipt's next version, or a fresh statement version — with a
  new number, pointing back at the voided one; the whole action is audited in the same transaction
  (`IAuditTrail`, as every privileged action is);
- two administrators voiding the same document at once: one wins, the other gets 409;
- the voided document stays readable, marked void, linking to its replacement. Customers see "voided and
  replaced by …"; only administrators see the reason.

Because the void always carries its replacement, the issuing sweep asks one question for receipts —
**does any row exist for this payment (or refund)?** — and never re-issues something an administrator
voided.

### 3.6 What every document contains (snapshot, schema version 1)

The snapshot is ONE JSON document, stored exactly as issued (§3.10). Every figure in it is the server's,
computed when it was issued and never again.

**Every document**

| Part | Contents |
|---|---|
| Header | type, number, version, the checkpoint that caused it, the money event's instant, the issue instant, snapshot schema version, calculator version, currency and the decimals its amounts are written with (the platform's scale), the zone local times are given in (Asia/Amman) |
| Issuer | Khadra's legal name (EN/AR), commercial registration, address (EN/AR), support email and phone — from configuration at issue (§17) |
| Customer | id and name at issue — the name only, never the email or the phone (owner, 2026-09-27; §3.13) |
| Rental office | id, business name, commercial registration, city (EN/AR), area and street — read past the soft-delete filter, so an office that has since left is still named on money it received |
| Booking | id, reference, status at issue, rental start and end (UTC and Amman local), the frozen day count, pickup method |
| Vehicle | make, model, year, plate, car type (EN/AR) — read past the soft-delete filter too |
| Notice | "This document is not a tax invoice." / «هذا المستند ليس فاتورة ضريبية.» |
| Content | the document laid out: title, sections and lines, **each label in English and Arabic**, each value a money amount with its currency, an instant, or a code (§3.7) |

**Payment Receipt** — in the capture's currency:

| Figure | Rule |
|---|---|
| Purpose — deposit, full payment, remaining balance | the payment |
| Amount charged (paid amount) | the capture |
| Processing fee, and whether it is refundable | frozen on the payment |
| Amount applied to the booking | charged less fee; zero for an orphaned capture |
| Booking total, required deposit | the booking's frozen pricing |
| Paid online so far, and the **balance after this payment** | ONE rule: the frozen total price less the applied money of every payment applied at or before this one, in (applied at, id) order — so a receipt reads the same whenever it is issued; for a deposit-only booking it equals the frozen balance due, and it is labelled as due to the office at handover. Orphaned captures never reduce it. |
| For an orphaned capture | "Not applied to the booking — being refunded in full", the amount to be refunded, and no booking arithmetic at all. A capture in a currency other than the booking's is described in its own currency and never set against the booking. |

**Refund Receipt** — in the refund's currency:

| Figure | Rule |
|---|---|
| Refund amount; booking money in it; processing fee in it | the refund's **stored** split (§3.12) |
| Reason — free cancellation, paid above the deposit, dispute decision, deposit released, cancelled by Khadra, capture not applied | the refund |
| Recorded at, settled at | the refund |
| The original payment: purpose, captured at, amount charged, and its receipt's number | the payment and its receipt |
| Refunded so far on that payment | its settled refunds, settled at or before this one, in (settled at, id) order |
| For a dispute's refund: the decision date | the ticket |

**Booking Statement** — `BookingFinancialsDto.For(financials, BookingParty.Customer)` at issue, and
nothing from outside that projection:

| Section | Contents |
|---|---|
| The booking | rental (days × daily rate), delivery fee, booking total, required deposit |
| Paid online | each captured payment: when, purpose, charged, fee, applied — orphaned captures as being refunded; another currency listed, never summed |
| Refunds | each refund: reason, amount, booking money and fee, and where it stood at issue — refunded (date), on its way, delayed |
| Deposit | its state in the owner's words (item 164's included), and the customer's own dispute share only (decision 3) |
| Penalty | amount assessed, reason, and where it stands (item 173's wording) |
| Balance | its state and amount; cash the office recorded, as information |
| Totals | charged online, refunded, in progress, net paid online |
| History | the receipts issued so far, and the previous version's number |

A statement is never issued from records that contradict one another (`NeedsReview`): the calculator
serves such records to a live screen on purpose — a broken booking page is worse — but an official record
must not freeze a contradiction. The booking goes on hold instead (§3.9).

An illustrative payment receipt, abridged (final key names are fixed in the 5a review, then kept for good
— the Phase 6 renderer will read them):

```json
{
  "schemaVersion": 1,
  "document": { "type": "PaymentReceipt", "number": "PAY-2026-000001", "version": 1,
                "trigger": "PaymentCaptured", "occurredAt": "2026-09-25T17:56:13Z",
                "issuedAt": "2026-09-25T17:57:01Z", "timeZone": "Asia/Amman",
                "calculatorVersion": 1, "currency": { "code": "JOD" }, "amountScale": 3 },
  "issuer":   { "legalName": { "en": "…", "ar": "…" }, "commercialRegistration": "…" },
  "customer": { "customerId": "…", "name": "…" },
  "office":   { "dealerId": "…", "name": "Al-Nadeem Rentals", "city": { "en": "Amman", "ar": "عمّان" } },
  "booking":  { "reference": "KH-NY8AHLNK", "days": 3, "pickupMethod": "Delivery",
                "vehicle": { "make": "BMW", "model": "525i", "year": 2002, "plate": "12345678" } },
  "facts":    { "purpose": "FullPayment", "status": "Applied",
                "amountCharged": { "amount": "102.750", "currency": "JOD" },
                "processingFee": { "amount": "0.000", "currency": "JOD" }, "feeRefundable": true,
                "appliedToBooking": { "amount": "102.750", "currency": "JOD" },
                "balanceAfter": { "amount": "0.000", "currency": "JOD" }, "balanceState": "PaidInFull" },
  "content":  { "title": { "en": "Payment receipt", "ar": "إيصال دفع" },
                "sections": [ { "heading": { "en": "Payment", "ar": "الدفعة" },
                                "lines": [ { "key": "amountCharged",
                                             "label": { "en": "Amount paid", "ar": "المبلغ المدفوع" },
                                             "money": { "amount": "102.750", "currency": "JOD" } } ] } ],
                "notice": { "en": "This document is not a tax invoice.",
                            "ar": "هذا المستند ليس فاتورة ضريبية." } }
}
```

### 3.7 English and Arabic, frozen with the figures

A document is a record, so its **words** are frozen with its figures: every label is stored in English
and in Arabic, from a server-side wording table, when the document is issued. Web, app, console and the
Phase 6 PDF all render the stored content and never word a document themselves — which is also what stops
any client from reconstructing a figure. A wording mistake in an issued document is corrected the way any
mistake is: a void and a correction. (Live screens keep today's rule — the server sends facts and each
client words them — because a live screen is not a record.)

Language rules follow the rest of the platform: Latin digits; absolute Amman times; the Customer App's
Arabic vocabulary (مكتب التأجير, سيارة, خضرا); the office's name as it registered it, bidi-isolated in
Arabic (it has no Arabic form yet — item 161).

### 3.8 Numbering

`PAY-2026-000001`, `RFD-2026-000001`, `STM-2026-000001`:

- one series per document type per **Amman issue year** (`IReportingCalendar.DayOf(issuedAt).Year`: money
  from December issued in January is in the new year's series, as a receipt should be); six digits,
  growing to seven if a year ever needs it;
- **gapless**: the next number is taken from a counter row (`INSERT … ON CONFLICT DO UPDATE … RETURNING`)
  in the SAME transaction that inserts the document, with nothing in between; if the insert loses — say
  to a concurrent issuer on the family's uniqueness — the transaction rolls back and the number goes back
  with it. (A PostgreSQL sequence would leave a gap on every rollback.) Each document is issued in its own
  scope and database context, as the settlement pass already runs each command;
- every document — every statement version — has its own number, unique across the database;
- a number is never reused: a voided document keeps its number, and its correction gets a new one;
- **test documents** are numbered `TEST-PAY-2026-000001`, `TEST-RFD-2026-000001`,
  `TEST-STM-2026-000001` (owner, 2026-09-27): a test document must never be confused with a real one.

### 3.9 How documents get issued

**The money never waits for its paperwork.** Issuing is a separate step that runs after money is
recorded, as a fourth command in the existing settlement pass (`BookingSettlementService`), right after
the payment sweep — so a refund settled in a pass gets its receipt in the same pass. No new hosted service
and no second schedule. The durable facts ARE the queue:

1. **Payment receipts** — every captured payment with **no row at all** for its receipt family.
2. **Refund receipts** — every settled refund (by `settled_at`) with no row for its family; the payment's
   receipt is issued first if it is somehow missing.
3. **Statements** — every booking whose checkpoints (§3.3) are newer than its latest statement's
   `covers_through` less the margin, issued only if the checkpoint fingerprint changed and the records do
   not need review.

Each document is issued in one transaction: compose the snapshot, take the number, insert the row.

**Holds, never silence.** When a document cannot be issued, a **hold** is recorded for its family —
upserted, not one row per attempt — with a reason, its attempts, when it first and last failed, and when
to try again (a growing delay):

| Reason | Meaning | Clears when |
|---|---|---|
| `RecordsNeedReview` | the booking's records contradict one another; no statement is frozen from them | an administrator corrects them and the next pass issues |
| `IssuerNotConfigured` | Khadra's legal identity is not configured (§17); nothing is issued with a blank issuer | the configuration exists |
| `SnapshotFailed` | composition failed — a bug; logged at Error | a fix is deployed |

Unresolved holds show on the administrator's work queue ("N financial documents are on hold"); the boot
log says so too, as it already says `PAYMENTS ARE NOT ACCEPTED`.

Why this way, and not inside the webhook's transaction: a bug in documents must never be able to stop a
payment or a refund being recorded; numbering takes a row lock that has no place on the money path; and
the money handlers stay untouched. Nothing can be lost, because detection works from the facts
themselves — which is also how the history gets its documents (§14). The cost is up to one settlement
interval (60 seconds today) between the money and its document; the screens say "your receipt is being
prepared" in that gap, from the server's own answer (§5).

### 3.10 What makes a document immutable

- **The record never changes.** `financial_documents` and `financial_document_voids` are append-only:
  the application refuses to modify or delete them (the existing `IAppendOnly` guard in
  `KhadraDbContext`), and so does the database — the existing `khadra_table_is_append_only()` function,
  with a row trigger for `UPDATE`/`DELETE` and a statement trigger for `TRUNCATE`, as
  `document_access_entries` already has.
- **The snapshot is stored exactly as issued**, in a PostgreSQL `json` column — which keeps the exact
  text, unlike `jsonb` — with its SHA-256 beside it, so what was issued can be proven byte for byte, and
  Phase 6 records which hash each PDF was rendered from. It is written by a canonical serializer: fixed key
  order, invariant culture, amounts at the currency's minor-unit scale.
- **Nothing is read live.** Parties, names, the car, prices and wording are copied in at issue. An office
  renamed next month keeps its old name on every document already issued; the next statement version
  carries the new one.
- **Schema versions accumulate.** A stored snapshot is never migrated; readers support every schema
  version ever issued.
- **No leak can be retracted**, so the statement is composed from the customer projection only, and a test
  asserts the stored JSON contains none of: commission, the office's or the platform's dispute share, a
  charge to the office, provider references, failure codes, orphan reasons, the sandbox marker, the
  calculator's issues, or internal timestamps.

### 3.11 Test documents

The document row stores **`provider`** — a copy, frozen at issue, of `payments.provider`, the one marker
of test money — and derives "is this test money" through `PaymentProviders.IsSandbox`, exactly as a
payment does. It is not a second flag: it is the same value, frozen like every other figure. Consoles
show the "Test" pill they already use. `PaymentsStartupCheck` guarantees a database holds one kind of
money, so test and real numbers can never mix in one series.

**The `TEST-` prefix — decided yes by the owner on 2026-09-27.** It is visible inside the customer app,
where a sandbox banner was ruled out on 2026-09-23; a document is different, because a staging receipt
forwarded or screenshotted without the prefix would look exactly like a real one.

### 3.12 Decision 4 — the refund split, stored as THE source

`payment_refunds` gains `booking_part` and `fee_part`. `Refund.Request` takes them, and `Payment` computes
them once, when it adds the refund — by the rule `FeeInside` applies today. From then on the calculator
reads the stored split, and `FeeInside` shrinks to that creation-time rule. There is one split, not two
that could disagree. The migration fills existing rows with the same rule written in SQL (§13.2), the
database checks that the two parts add up to the amount, and a PostgreSQL test creates refunds through the
aggregate for every reason × fee × refundable × currency combination and proves the SQL agrees with the
C#. Every screen shows the same figures as before. This is a change inside the Payments context, and
decision 4 is the owner's approval for exactly this and nothing wider.

### 3.13 Personal data in an append-only record

`User.Delete` is a soft delete with no anonymisation, and a document freezes the customer's name into a
table nothing can erase. That is right for a financial record, and it must be said rather than discovered:
the snapshot carries the **minimum** — the name only; no email and no phone (owner, 2026-09-27: email
addresses and delivery attempts belong to the delivery history, Phase 7) — and a pre-launch item records
that retention needs a stated legal basis and period before real customers exist.

---

## 4. Who may see what

| | Customer | Rental office | Administrator |
|---|---|---|---|
| Payment and Refund Receipts, Booking Statements | **own bookings only** | — | all |
| Superseded and voided versions | own | — | all |
| A void's reason, the test marker, holds | — | — | yes |

**Rental offices see no customer documents in Phase 5.** Receipts carry processing fees (decision 8) and
dispute refunds carry the customer's share (decision 3); the office already reads its money from its own
projection of the financial state. The office's own documents — payables statements — are Phase 8. An
office asking for a booking's documents gets an empty list, not a refusal that would say documents exist.

A customer asking for someone else's document gets 404, never a hint that it exists.

---

## 5. API

All new endpoints; nothing existing changes shape.

**Customer** (bearer token, or the customer BFF — its authenticated catch-all already forwards these):

| Endpoint | Returns |
|---|---|
| `GET /api/v1/customers/me/financial-documents?type=&page=&pageSize=` | the Invoices area: every document of the customer's, newest first by (issued at, id), paged |
| `GET /api/v1/bookings/{bookingId}/financial-documents` | the booking's documents for Booking Details, and what is **still being prepared** (a captured payment or settled refund whose receipt is not issued yet, or on hold) — so no client guesses; an office gets an empty list |
| `GET /api/v1/financial-documents/{documentId}` | one document: its stored content and facts, its status, and links to its other versions and related receipts |

**Administrator** (`SecurityPolicies.Admin`):

| Endpoint | Returns |
|---|---|
| `GET /api/v1/admin/financial-documents?type=&status=&number=&reference=&from=&to=&page=` | every document, filtered, paged |
| `GET /api/v1/admin/financial-documents/{documentId}` | as the customer's, plus the void and its reason, the provider, and its family |
| `POST /api/v1/admin/financial-documents/{documentId}/void` `{ reason }` | voids a current document and issues its correction; **201** with the replacement's `Location`; 409 if it is no longer current, is already voided, or its correction cannot be issued (records need review, no issuer); 422 if the correction could not be composed (a defect) — nothing is voided in any of these; audited |
| `GET /api/v1/admin/financial-documents/holds` | documents on hold, with reasons |
| `GET /api/v1/admin/bookings/{bookingId}/financial-documents` | a booking's documents for the Money section |
| `GET /api/v1/admin/payments/{paymentId}` | gains the payment's receipts — additive |

**Shapes.** A list row: id, type, number, version, status, booking id and reference, title (EN/AR), a
headline (label EN/AR and amount: amount charged, amount refunded, or net paid online), the money event's
instant, the issue instant. A document adds its stored `content` and `facts` exactly as issued, and links:
previous and next version, the payment receipt a refund receipt belongs to, a payment receipt's refund
receipts, and a voided document's replacement.

**The customer app's contract** — new endpoints only; no field of any existing response changes. The
minimum supported version stays 1.1.0.

---

## 6. The customer website (`Khadra.Web`)

- **Booking details** — the "Payments" section is renamed **"Payments & Invoices" / «المدفوعات
  والفواتير»**; under the live payments it lists the booking's documents (title, number, version, status,
  issue date, headline), each opening its page, and "your receipt is being prepared" where the server says
  one is.
- **Invoices & Receipts** — a new account page, `/{lang}/invoices` («الفواتير والإيصالات»): every
  document, filterable by type, paged; linked from the account navigation, the header's account menu and
  the mobile drawer.
- **A document's page** — `/{lang}/invoices/{documentId}`: the stored content in the page's language
  (both are in the data, so switching language re-renders it), links to its other versions and related
  receipts, and a print style so it prints cleanly until Phase 6 adds the PDF.
- Account pages as today: rendered in the browser, `noindex`, behind sign-in.

## 7. The customer app (`Khadra.Mobile`)

- **Invoices & Receipts / الفواتير والإيصالات** — a dedicated screen at `/profile/invoices`, entered from
  My Account. The tab bar keeps its five destinations (owner, 2026-09-20/21 and 2026-09-27;
  `bottom_nav_test.dart`).
- **A document's screen** — `/profile/invoices/:documentId`, rendering the stored content natively in the
  app's language, right-to-left in Arabic, with links to other versions and related receipts.
- **Booking Details** — the section becomes "Payments & Invoices" and lists the booking's documents, with
  the server's "being prepared" entries.
- Both routes join the hard-redirect set (they act on an account); a guest never reaches them.
- New endpoints only, so it ships in **app 1.3.0**, which is not yet released; the minimum is unchanged.

## 8. The consoles

**Administrator:**

- a **Documents** tab beside Payments and Refunds: every document, filterable by type, status, number,
  booking reference and date;
- a **document page**: the stored content, its facts, its family and links, the Test pill, and a **Void**
  action behind the confirm dialog, which states the consequence (the correction it issues) first;
- the booking's **Money** section and each **payment's page** list their documents;
- a **work-queue row** — "N financial documents are on hold" — opening the holds with their reasons.

**Rental office:** nothing in Phase 5 (§4).

---

## 9. Phase 6 — PDF (designed now, built then — built 2026-09-29, see §22)

- `financial_document_renditions` (append-only): document, language, format, storage key, SHA-256 and
  size of the PDF, the snapshot hash it was rendered from, template and renderer versions, rendered at;
  one per document, language and template version.
- Rendered **only from the stored snapshot**, once; stored privately through the existing document
  storage; downloaded through short-lived signed links (`IDocumentLinkSigner`) under §4's rules.
- Arabic right-to-left with the platform fonts (Noto Kufi Arabic, Manrope), Latin digits; test documents
  watermarked.
- **Licence first:** QuestPDF only if Khadra qualifies for its Community licence (owner, 2026-09-24);
  otherwise stop and propose another library.

## 10. Phase 7 — email (designed now, built then)

- `financial_document_deliveries`: document, channel (email), the recipient address at the time, state —
  **Queued → Sent**, or **Failed** after the last attempt, or **Skipped** (no verified address) —
  attempts, next attempt at, queued at, sent at, failed at, provider message id, last error, and who asked
  (the system when the document was issued, or an administrator's resend).
- `financial_document_delivery_attempts` (append-only): every try — its start, end, outcome and error —
  the history the administrator sees (item 172).
- Queued **in the same transaction that issues the document** (an outbox, as `notification_deliveries`
  already is); sent by a dispatcher within the existing `Email:MaxAttempts` / `Email:TimeoutSeconds`
  budget; a resend is a new delivery row, audited.
- The email's language is the customer's preferred language **when it is sent**, not when the document
  was issued; the document itself holds both.
- Which documents are emailed, and attachment or link, are Phase 7 decisions.

## 11. Phase 8 — what is left for settlements

The office payables ledger and its settlement; the office's own statements; commission earned versus
undecided; anything Khadra invoices to offices — which is where a genuine **tax invoice**, and Jordan's
national e-invoicing (JoFotara), may become a requirement. None of it is in Phase 5.

---

## 12. Backward compatibility

- **Existing API responses do not change.** The financial state, `BookingDto` and every customer endpoint
  are untouched; installed apps are unaffected; the minimum version stays.
- **The refund split** changes storage, not results: every screen shows the figures it shows today.
- **"Payments" → "Payments & Invoices"** is wording on the website and in the app.
- **Production** takes no payments (`Payments:Provider = None`): the migration creates empty tables, fills
  zero refunds, and the pass finds nothing to issue.

---

## 13. Schema and migration

### 13.1 EF Core impact

- One migration, `FinancialDocuments`, after `20260924162513_PaymentFeeRefundable`; the model snapshot is
  updated with it.
- New configurations for `FinancialDocument`, `FinancialDocumentVoid`, `FinancialDocumentIssuanceHold` and
  the series table; new `DbSet`s; `FinancialDocument` and `FinancialDocumentVoid` implement `IAppendOnly`.
- `PaymentConfiguration`: the refund's two split columns, and the check constraint.
- Raw SQL inside the migration for what EF does not model: the refund-split backfill and the append-only
  triggers (reusing the existing function).
- SQLite (the unit-test database) gets the tables but not the triggers; the application's guard covers it
  there, and opt-in PostgreSQL tests prove the triggers, the backfill and the numbering.

### 13.2 The SQL — generated, NOT applied

Generated with `dotnet ef migrations script 20260924162513_PaymentFeeRefundable FinancialDocuments` from
migration `20260926230609_FinancialDocuments`, written in a throwaway worktree so that the Development API
— which applies pending migrations when it starts — could not apply it. It has been run against no
database. It is hand-edited beyond EF's output in three places, each explained in the migration's own
comments: the refund split is added nullable, filled by the rule, then made NOT NULL (EF would have added
it NOT NULL DEFAULT 0 and stamped every existing refund as carrying no fee); a PostgreSQL-only CHECK holds
the split to the amount; and the append-only triggers reuse the existing `khadra_table_is_append_only()`.

Names that differ from the first draft: the number column is `document_number`; the event column is
`cause` (`trigger` would read as a database trigger); holds have their own `id` with a unique
(document_type, subject_id); and, as on every aggregate table here, `updated_at` exists and — on the two
append-only tables — stays null for good.

```sql
START TRANSACTION;
ALTER TABLE payment_refunds ADD booking_part numeric(18,3);

ALTER TABLE payment_refunds ADD fee_part numeric(18,3);


UPDATE payment_refunds AS r
SET fee_part = CASE
        WHEN p.processing_fee = 0 OR r.currency <> p.currency THEN 0
        WHEN r.reason = 'OrphanedCapture' THEN LEAST(p.processing_fee, r.amount)
        WHEN r.reason IN ('FreeCancellation', 'PlatformCancellation', 'EndedBeforePickup')
             AND p.fee_refundable THEN LEAST(p.processing_fee, r.amount)
        ELSE 0
    END
FROM payments AS p
WHERE p.id = r.payment_id;

UPDATE payment_refunds SET booking_part = amount - fee_part;

ALTER TABLE payment_refunds ALTER COLUMN booking_part SET NOT NULL;

ALTER TABLE payment_refunds ALTER COLUMN fee_part SET NOT NULL;


ALTER TABLE payment_refunds ADD CONSTRAINT ck_payment_refunds_split
    CHECK (fee_part >= 0 AND booking_part >= 0 AND booking_part + fee_part = amount);

CREATE TABLE financial_document_issuance_holds (
    id uuid NOT NULL,
    document_type character varying(20) NOT NULL,
    subject_id uuid NOT NULL,
    booking_id uuid NOT NULL,
    reason character varying(30) NOT NULL,
    attempts integer NOT NULL,
    first_failed_at timestamp with time zone NOT NULL,
    last_failed_at timestamp with time zone NOT NULL,
    next_attempt_at timestamp with time zone NOT NULL,
    last_error character varying(300),
    resolved_at timestamp with time zone,
    updated_at timestamp with time zone,
    CONSTRAINT pk_financial_document_issuance_holds PRIMARY KEY (id)
);

CREATE TABLE financial_document_series (
    series_key character varying(24) NOT NULL,
    last_number bigint NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_financial_document_series PRIMARY KEY (series_key),
    CONSTRAINT ck_financial_document_series_last_number CHECK (last_number >= 1)
);

CREATE TABLE financial_documents (
    id uuid NOT NULL,
    document_type character varying(20) NOT NULL,
    document_number character varying(32) NOT NULL,
    subject_id uuid NOT NULL,
    version integer NOT NULL,
    previous_version_id uuid,
    related_document_id uuid,
    booking_id uuid NOT NULL,
    booking_reference character varying(20) NOT NULL,
    customer_id uuid NOT NULL,
    dealer_id uuid NOT NULL,
    payment_id uuid,
    refund_id uuid,
    cause character varying(20) NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    issued_at timestamp with time zone NOT NULL,
    covers_through timestamp with time zone,
    checkpoint_fingerprint character(64),
    headline_amount numeric(18,3) NOT NULL,
    currency character varying(3) NOT NULL,
    provider character varying(30) NOT NULL,
    calculator_version integer NOT NULL,
    snapshot_schema_version integer NOT NULL,
    snapshot json NOT NULL,
    content_sha256 character(64) NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT pk_financial_documents PRIMARY KEY (id),
    CONSTRAINT ck_financial_documents_previous CHECK ((version = 1) = (previous_version_id IS NULL)),
    CONSTRAINT ck_financial_documents_statement_coverage CHECK ((document_type = 'BookingStatement') = (covers_through IS NOT NULL AND checkpoint_fingerprint IS NOT NULL)),
    CONSTRAINT ck_financial_documents_subject CHECK ((document_type = 'PaymentReceipt' AND payment_id IS NOT NULL AND refund_id IS NULL AND subject_id = payment_id) OR (document_type = 'RefundReceipt' AND payment_id IS NOT NULL AND refund_id IS NOT NULL AND subject_id = refund_id) OR (document_type = 'BookingStatement' AND refund_id IS NULL AND subject_id = booking_id)),
    CONSTRAINT ck_financial_documents_version CHECK (version >= 1),
    CONSTRAINT fk_financial_documents_financial_documents_previous_version_id FOREIGN KEY (previous_version_id) REFERENCES financial_documents (id) ON DELETE RESTRICT,
    CONSTRAINT fk_financial_documents_financial_documents_related_document_id FOREIGN KEY (related_document_id) REFERENCES financial_documents (id) ON DELETE RESTRICT
);

CREATE TABLE financial_document_voids (
    document_id uuid NOT NULL,
    voided_at timestamp with time zone NOT NULL,
    voided_by_admin_id uuid NOT NULL,
    reason character varying(500) NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT pk_financial_document_voids PRIMARY KEY (document_id),
    CONSTRAINT fk_financial_document_voids_financial_documents_document_id FOREIGN KEY (document_id) REFERENCES financial_documents (id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX ix_financial_document_issuance_holds_document_type_subject_id ON financial_document_issuance_holds (document_type, subject_id);

CREATE INDEX ix_financial_document_issuance_holds_next_attempt_at ON financial_document_issuance_holds (next_attempt_at) WHERE resolved_at IS NULL;

CREATE INDEX ix_financial_documents_booking_id_issued_at_id ON financial_documents (booking_id, issued_at DESC, id DESC);

CREATE INDEX ix_financial_documents_customer_id_issued_at_id ON financial_documents (customer_id, issued_at DESC, id DESC);

CREATE UNIQUE INDEX ix_financial_documents_document_number ON financial_documents (document_number);

CREATE UNIQUE INDEX ix_financial_documents_document_type_subject_id_version ON financial_documents (document_type, subject_id, version);

CREATE INDEX ix_financial_documents_issued_at_id ON financial_documents (issued_at DESC, id DESC);

CREATE INDEX ix_financial_documents_payment_id ON financial_documents (payment_id) WHERE payment_id IS NOT NULL;

CREATE INDEX ix_financial_documents_previous_version_id ON financial_documents (previous_version_id);

CREATE INDEX ix_financial_documents_related_document_id ON financial_documents (related_document_id);


CREATE TRIGGER financial_documents_append_only
BEFORE UPDATE OR DELETE ON financial_documents
FOR EACH ROW EXECUTE FUNCTION khadra_table_is_append_only();

CREATE TRIGGER financial_documents_no_truncate
BEFORE TRUNCATE ON financial_documents
FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();

CREATE TRIGGER financial_document_voids_append_only
BEFORE UPDATE OR DELETE ON financial_document_voids
FOR EACH ROW EXECUTE FUNCTION khadra_table_is_append_only();

CREATE TRIGGER financial_document_voids_no_truncate
BEFORE TRUNCATE ON financial_document_voids
FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();

INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
VALUES ('20260926230609_FinancialDocuments', '10.0.11');

COMMIT;
```

### 13.3 Down

Generated as `dotnet ef migrations script FinancialDocuments 20260924162513_PaymentFeeRefundable`. It
**refuses to run once a real (non-test) document exists**: the append-only triggers stop `UPDATE`, `DELETE`
and `TRUNCATE` but not `DROP TABLE`, so without that guard a rollback would destroy issued receipts.
Otherwise it drops the four triggers (never the shared function, which `document_access_entries` also
uses), the check, the four tables and the split columns, in one transaction. Once a real document exists
this migration is fixed forward — by a new migration and, for a document, by a void and its correction.

```sql
START TRANSACTION;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM financial_documents WHERE document_number NOT LIKE 'TEST-%') THEN
        RAISE EXCEPTION 'financial_documents holds real documents: this migration is fixed forward, never reverted';
    END IF;
END $$;

DROP TRIGGER IF EXISTS financial_document_voids_no_truncate ON financial_document_voids;
DROP TRIGGER IF EXISTS financial_document_voids_append_only ON financial_document_voids;
DROP TRIGGER IF EXISTS financial_documents_no_truncate ON financial_documents;
DROP TRIGGER IF EXISTS financial_documents_append_only ON financial_documents;

ALTER TABLE payment_refunds DROP CONSTRAINT IF EXISTS ck_payment_refunds_split;

DROP TABLE financial_document_issuance_holds;

DROP TABLE financial_document_series;

DROP TABLE financial_document_voids;

DROP TABLE financial_documents;

ALTER TABLE payment_refunds DROP COLUMN booking_part;

ALTER TABLE payment_refunds DROP COLUMN fee_part;

DELETE FROM "__EFMigrationsHistory"
WHERE migration_id = '20260926230609_FinancialDocuments';

COMMIT;
```

### 13.4 Checks after applying

```sql
SELECT count(*) FROM payment_refunds WHERE booking_part + fee_part <> amount;   -- 0
SELECT reason, count(*), sum(fee_part) FROM payment_refunds GROUP BY reason;   -- matches the C# rule
-- on a scratch copy only: UPDATE financial_documents SET number = number;     -- must raise
```

---

## 14. Rollout and backfill

1. **Local** — after approval, applied by the Development auto-migration to `khadra_web_it`; the pass then
   issues documents for the sandbox money already there (the history, below), and every screen is checked
   in both languages (§16).
2. **Staging** (sandbox) — the generated SQL is shown to the owner first; only then applied. The pass
   issues test documents for staging's history.
3. **Production** (`Payments:Provider = None`) — the same migration creates empty tables and fills zero
   refunds; the pass finds nothing to issue. Nothing a user sees changes until money exists.
4. **Order** — the API with the migration; then the website; then the app build (new endpoints only, so an
   older app is unaffected throughout).
5. **Before the first real document** — the issuer identity is configured, the `TEST-` decision is taken,
   and the retention item (§3.13) is on the checklist.

**The history is issued by the same pass, once:**

- a receipt for every captured payment and every settled refund that exists, oldest first — accurate as of
  its money event, because receipt figures come only from immutable facts (§3.6);
- **one** statement per booking with money: its position at the time it is issued. Earlier positions are
  not reconstructed — they cannot be recomputed as they stood, and pretending otherwise would be an
  invented figure.

Such documents show both instants — the money event and the issue — so their late issue is visible, never
disguised. The numbers they take follow the issue year (§3.8).

---

## 15. Automated tests

- **Domain** — number format; series per type and Amman year (22:30 UTC on 31 December is already the new
  year in Amman); document invariants (type ↔ subject, version ↔ previous version, statement coverage);
  status derivation; void rules (current only, always with a replacement).
- **The refund split** — written once, at creation, equal to today's `FeeInside` for every reason ×
  fee × refundable × currency; the calculator reads the stored split.
- **Composition** — each type's complete snapshot, English and Arabic, against expected content; the
  **leak test** (§3.10) on every statement; receipt figures identical whether issued now or a year later;
  the currency-mismatched orphan never set against the booking; names read past the soft-delete filter.
- **Issuance** — detection finds exactly what is missing and nothing an administrator voided; the
  checkpoint fingerprint ignores clock-only changes and refund Requested/Sent; the margin catches a fact
  committed after a statement that did not see it; `NeedsReview` becomes a hold, not a statement; a
  missing issuer becomes a hold; holds are upserted, retried with a growing delay, and cleared on issue.
- **Persistence** (SQLite) — every snapshot field round-trips; unique number and family version; the
  append-only guard refuses a change or delete of a document or a void.
- **PostgreSQL** (opt-in) — the triggers refuse `UPDATE`, `DELETE`, `TRUNCATE`; numbers stay gapless with
  two concurrent issuers and a rolled-back issue; the migration's split SQL agrees with the C# rule; the
  detection queries translate; the `json` column returns the exact text written.
- **Security** — 401 without a session; a customer never reads another customer's document (404); an
  office gets an empty list; the administrator's endpoints need the Admin policy; a void is audited in the
  same transaction and a second void of the same document is 409.
- **Contract** — the JSON of a list row and of a document; no existing response changes.
- **Clients** — website and console presenter specs and app widget tests in both languages; the app at 360
  and 375 pixels wide and right-to-left.

## 16. Browser end-to-end scenarios (local, sandbox)

1. **Deposit booking** — pay the deposit → a Payment Receipt (deposit, fee, applied, balance due at
   handover) and statement v1, in the app, on the website and in the admin console, English and Arabic.
2. **Full payment, free cancellation** — the refund settles → a Refund Receipt linked to the payment's;
   statements v2 (refund on its way) and v3 (refunded); the Payment Receipt unchanged; older versions
   still open, marked superseded.
3. **Late cancellation, dispute** — the resolution issues a new version with the customer's share only;
   its settlement issues a Refund Receipt with the dispute's date; the office console shows no customer
   document; the administrator sees them all.
4. **Captured but not applied** — a receipt that says so, then a Refund Receipt when it settles.
5. **Immutability** — rename the office, edit the car, change a business rule → every issued document
   reads exactly as before; the next version carries the new names.
6. **Void and correct** — the administrator voids a receipt with a reason → it reads voided everywhere, the
   correction has a new number, the audit log has the entry.
7. **Holds** — records that contradict one another put the statement on hold, shown on the work queue;
   fixing them issues it on the next pass.
8. **Access** — customer B opening customer A's document link gets not-found.
9. **Test money** — every sandbox document reads `TEST-…` (if approved) and carries the console's Test pill.

## 17. Decided by the owner (2026-09-27)

1. **Khadra's legal identity** — no permanent document is issued with placeholder or incomplete issuer
   information; until the real identity is configured (`FinancialDocuments:Issuer`), issuance stays on
   hold (`IssuerNotConfigured`).
2. **`TEST-` numbering** for sandbox documents: `TEST-PAY-2026-000001`, `TEST-RFD-2026-000001`,
   `TEST-STM-2026-000001`; the existing sandbox history receives `TEST-` documents.
3. **The customer on a document: the name only.** No email in the immutable snapshot; addresses and
   delivery attempts belong to the delivery history (Phase 7).
4. **Cash recorded at a handover** is a financial event and issues a new statement version.
5. **The customer area: "Invoices & Receipts" / «الفواتير والإيصالات»**, under My Account. Inside it the
   documents keep their accurate names — Payment Receipt, Refund Receipt, Booking Statement — and none is
   called a "Tax Invoice"; each states that it is not one.
6. **Also approved:** offices see no financial documents in Phase 5; voids and corrections are append-only
   and preserve the original; the app ships in the unreleased 1.3.0; the history is issued once; PDF is
   Phase 6, email Phase 7, payables and office settlement Phase 8.
7. **The record:** the database row and its immutable snapshot are the official record; a PDF and an email
   are representations and deliveries derived from it.
8. **The SQL first:** implementation continues, and the migration is applied to any database, only after
   the owner approves the generated SQL (§13.2).

Still to settle when it is built: the booking section's name — decision 6 (2026-09-26) says "Payments &
Invoices" / «المدفوعات والفواتير»; the area is now "Invoices & Receipts".

## 18. Commits

Like Phase 4, two commits, each with its own tests:

- **5a — the backend:** the refund split; the context, tables and triggers; numbering; composition and
  wording; issuance in the settlement pass, with holds; the customer and administrator endpoints.
- **5b — the clients:** the website, the app, the administrator console; the browser scenarios of §16 in
  both languages.

## 19. What the architecture review changed

The advisor (2026-09-27) found the shape sound — a context of its own, append-only rows, gapless numbers,
issuing by detection — and changed seven things that could not be patched once a document exists:

1. **Statement versions follow a closed list of checkpoints**, fingerprinted on the checkpoint facts
   themselves. The first draft hashed the whole customer view, which changes with the clock (a deposit's
   window closing) and would have issued a version on every pass.
2. **`provider` on the document, not an `is_sandbox` flag** — the sandbox rule allows one marker, not two;
   and the `TEST-` prefix became an explicit owner decision.
3. **Statements are composed from the customer projection only**, with a leak test: a leak into an
   append-only record is a disclosure that cannot be retracted.
4. **No statement from contradictory records** — a hold instead.
5. **The refund split is THE source** — computed once at creation and read by the calculator — not a
   second copy beside a recomputation.
6. **Receipts are detected by "no row exists"**, and a void always carries its replacement, so a voided
   receipt is never re-issued by the next pass.
7. **The number and the document share one transaction and one scope per document**, which is what keeps
   the series gapless.

And five simplifications: the per-request ledger became a holds table (the facts are already the queue);
issuing runs inside the existing settlement pass, not a new service with an in-memory nudge; the snapshot
column is `json` (exact text) rather than `text`; the customer's language left the document (Phase 7 reads
it when it sends); and the customer's phone left the snapshot.

## 20. What 5a built (2026-09-27)

The backend of this plan, with the two directions the owner approved on 2026-09-27 (statements follow
checkpoints and fingerprints, §3.3; documents are composed from committed records only).

**Issuing.** `IssueFinancialDocumentCommand`, one per candidate, in the settlement pass right after the
payment sweep (`BookingSettlementService`): `ListFinancialDocumentWorkQuery` finds the work
(`FinancialDocumentCandidateReader`: payment receipts, then refund receipts once their payment's receipt
exists, then statements), and each document is issued in its own scope and transaction —
`DocumentPreparation` gathers the committed facts and the parties, `FinancialDocumentIssuing` takes the
number (`FinancialDocumentSeriesCounter`, the raw `INSERT … ON CONFLICT … RETURNING` inside the
transaction) and inserts the row. What cannot be issued goes on hold through
`RecordFinancialDocumentHoldCommand`, in a scope of its own; a missing issuer waits the longest delay at
once, and its holds stop waiting the moment an issuer is configured. A second issuer that loses the race
for a family's version is told "lost race" and changes nothing.

**Composition.** `FinancialDocumentComposer` (pure) writes the canonical snapshot through
`SnapshotJson`/`DocumentContent`; every word comes from `DocumentWording` in English and Arabic, with
Latin digits, Amman wall-clock times beside UTC, and Unicode isolates around every left-to-right run in
an Arabic sentence. The keys are now fixed for schema version 1:

- top level: `schemaVersion`, `document` { `type`, `number`, `version`, `cause`, `occurredAt`, `issuedAt`,
  `timeZone`, `calculatorVersion`, `currency` { `code` }, `amountScale` (the decimals every amount is
  written with — the platform's scale, 3, whatever the currency), `previous`, `isCorrection` },
  `issuer`, `customer` { `customerId`, `name` }, `office`, `booking` (with `vehicle`), `facts`, `content`;
- an instant is `{ utc, local }`; an amount is `{ amount, currency }` with the amount a string at three
  decimals;
- `content` is `{ title, headline { label, money }, sections[] { key, heading, lines[] }, timeNote,
  notice }`, and a line is `{ key, label | null }` plus exactly one of `money`, `instant`, `text`
  (English and Arabic) or `plain` (a literal: a number, a reference, a name as registered).

A statement's `facts` are the customer projection written field by field — `summary` (with the frozen
`netPaidOnline`), `balance`, `deposit` (the customer's own dispute share only), `penalty` (only one against
the customer), `payments[]` with their `refunds[]`, and `receipts[]` — and a leak test asserts that no
commission, office or platform share, charge to the office, provider reference, failure code, orphan
reason, sandbox marker, issue list or attempt timestamp reaches it.

**Configuration.** `FinancialDocuments:Issuer` — `LegalNameEn`, `LegalNameAr`, `CommercialRegistration`,
`AddressEn`, `AddressAr`, `SupportEmail`, `SupportPhone`: all or nothing, a partial identity refuses to
start naming what is missing — and `TestIdentity`, the owner's local testing allowance (2026-09-27: a
clearly marked TEST identity in user-secrets, local sandbox only, never committed, never Staging or
Production). `Program.cs` refuses `TestIdentity` in any environment but Development and with any provider
but `SANDBOX`, and the issuer refuses to let it sign real money. `MaxDocumentsPerPass` (200),
`RetryInitialSeconds` (60), `RetryMaxSeconds` (3600) and `LateCommitMarginMinutes` (10) pace the work and
change no figure. The boot log says FINANCIAL DOCUMENTS ARE NOT ISSUED while there is no issuer, names a
test identity, and counts the documents on hold.

**Endpoints.** As §5, plus `GET /api/v1/admin/financial-documents/vocabulary`. A void answers 201 with the
correction's `Location`; when the correction cannot be issued nothing is voided — 409 when the records
need review or there is no issuer, 422 when composition failed (a defect, not a state conflict). The work queue gains `FinancialDocumentsOnHold`; the admin payment
page gains `documents[]`. The console, the website and the app read none of it until 5b.

**Tests.** Domain rules (numbers, invariants, voids, holds, standing); composition in both languages with
the leak test and byte-identical recomposition; the checkpoint rules (clock-only changes, a refund
recorded, sent or refused, a pickup without cash — none is a checkpoint); the whole pass on SQLite with the
real repositories (issuing, holds, the missing and the test identity, voids and corrections, gapless
numbering across a rollback, the Amman issue year, the late-commit margin, names past the soft-delete
filter, the customer's and the office's readings, the append-only guard); configuration and the startup
guard; endpoint security; the wire contract. On PostgreSQL (opt-in): the pass and every reader translate,
two issuers never take one number and a rolled-back number comes back, and the `json` column returns the
snapshot byte for byte.

**For 5b.** A client renders the `snapshot` it is given and never proves `contentSha256` against it: the
API re-serialises the stored text (escaping, spacing), so the hash proves the stored `json` column only. A
refund receipt links the payment receipt it was issued against; if that one is later voided, its own page
says so and links to the correction — the refund receipt's frozen text names the same number the link opens.

**Not yet done.** The clients (5b); the local issuance run with the test identity, after the advisor's
review of 5a, then the browser scenarios of §16; the Staging migration, which waits for the owner.

## 21. What 5b built (2026-09-27 to 29)

The clients of this plan, planned in `docs/payments-phase5b-plan.md` and approved with its decisions D1–D6
on 2026-09-27. That plan's §18 has the commits, the browser run and the review in full.

**The customer's side.** The website (`b813b2c`, `3c2df2b`) and the app (`1337d54`, in the unreleased
1.3.0) each gained Invoices & Receipts — every version of every document, each marked, filtered by kind and
paged — a page per document that renders the stored snapshot, and the booking's documents inside Booking
Details' "Payments & Invoices", with what is being prepared and "Check again". The website's document pages
render in the browser only, with `noindex` and `no-store`. The app's change is additive: no field,
endpoint, status or request changed, and the minimum supported version stays 1.1.0.

**The administrator's side.** The console (`d26304f`, `b252c70`): a Financial documents tab on Payments
(every version, filtered and paged), the documents on hold, a page per document with its recorded facts and
its proof of issue, and Void and correct — the consequence stated first and a reason required; a refusal
that can never succeed (`not_current`, `already_voided`) closes the dialog and reloads, and every other
refusal keeps it open. A booking's Money section lists its documents, a payment's page its receipts; the
work queue counts the documents on hold; the audit log and the activity strip word a void.

**One reader, three implementations.** Each client implements the reader's contract in
`docs/contracts/README.md` — website `document-content.ts`, app `document_content.dart`, console
`financial-document-content.ts`: it gates on the DTO's `snapshotSchemaVersion`; ignores a key it does not
know and keeps the stored order; fails closed on a structural break and shows the facts outside the
snapshot instead; prints an amount or a time off its pattern as stored; formats money from the stored
string and times from the frozen Amman `local`; and takes the newest version to be the highest member of
`links.versions`. All three test suites read the shared fixture, `docs/contracts/financial-documents-v1.json`,
which the server's own composer writes.

**Privacy, D6 included.** Every customer read is scoped to the signed-in customer, and a document or a
booking that is not the caller's answers exactly as a missing one does — byte for byte, verified live with
a second customer. The void reason and the administrator reach no customer DTO, snapshot or page. The office
reaches no document: no endpoint, no DTO, no console route. The three customer document endpoints answer
`Cache-Control: no-store, private`, refusals included (`ApiControllerBase.KeepOutOfCaches`), and since
`ec176c4` (pre-launch item 180) so does every administrator document endpoint and both financials
endpoints, the parties' and the administrator's — verified live on 2026-09-29.

**A business-rule change cannot re-judge an issued document**, by construction rather than by a test of its
own: nothing under `FinancialDocuments` or `Payments/Financials` reads `IBusinessRulesProvider` — the
calculator reads the booking's frozen pricing and terms — and the clock alone never issues a version
(`The_clock_alone_never_issues_a_version`).

**What voiding a receipt does** (owner, 2026-09-28; pre-launch item 181, `07a7284`). A receipt's correction
is §3.3's sixth checkpoint, so the settlement pass issues the booking's statement one new version within a
pass — never inside the void's transaction, which a statement's own hold must not block. Its cause is
"Receipt corrected" / «تصحيح إيصال»; its Documents section lists the correction and never the voided
receipt; its `occurredAt` stays the last money it states and only its `coversUntil` reaches the correction.
A statement's own correction is not a checkpoint, and bookings never corrected keep a byte-identical
fingerprint. The console's void dialog tells the administrator that a new booking statement follows
shortly. Verified live on 2026-09-29: KH-P6UW4FB9's TEST-STM-2026-000020, version 4 and current, lists
TEST-PAY-2026-000014 and not the voided TEST-PAY-2026-000013.

**The concurrency proof** (pre-launch item 182). Two administrators voiding one document at once is forced
by a test-only synchronization (`1e7f720`): both voids are held at the number series' row lock until both
have passed their checks, so the loser can be refused only through the race branch. The owner ran the
PostgreSQL suite against disposable scratch databases on 2026-09-29: 23 of 23, none skipped, and item 182
is closed on it. The repository has no CI able to run that opt-in suite; automating it once CI exists is a
separate infrastructure follow-up.

**Not yet done.** Pre-launch items 183 and 194, both non-blocking; items 184–189, found during the run and
older than 5b; the Staging migration, which waits for the owner; Phases 6–8.

## 22. What Phase 6 built (2026-09-29)

§9's design, built as one local work package on the owner's instruction of 2026-09-29, after the owner
confirmed that Khadra qualifies for QuestPDF's Community licence (`docs/payments-programme.md`, Phase 6).
The implementation is four local commits: the backend `9bc92de`, the website `c2e9ff4`, the app `bd52ac4` (in the
unreleased 1.3.0) and the console `487c4b4`.

**Drawn once, from the record.** The settlement pass's last step draws every PDF owed: an issued document
with no PDF in English or Arabic is the work, as a captured payment with no receipt is issuing's — nothing
records that a PDF is due, so nothing can be lost. One scope per PDF. A PDF is drawn only from a snapshot that
still hashes to the row's `content_sha256`, through `DocumentPrintLayout` — the snapshot contract's fourth
reader (`docs/contracts/README.md`), which refuses a document whole rather than print it with a line
missing — and QuestPDF draws the page: A4, mirrored for Arabic, Manrope then Noto Kufi Arabic glyph by glyph,
Latin digits, direction carried by Unicode isolates as the website carries it, TEST documents watermarked
"TEST" / «تجريبي», "Page n of m" / «صفحة n من m». The body leaves out every commercial registration — Khadra's,
the rental office's and a (future) business customer's — which the snapshot keeps (owner, 2026-09-29;
`DocumentPrintLayout.IsPrintedInBody`): a named presentation rule applied after each line is read whole, so a
broken line that is left out still refuses the document. `DocumentPrintLayout.TemplateVersion` stayed at 1
through that change because no rendition had been drawn anywhere but in throwaway test databases when it
landed; from the first PDF drawn on a real database, any change to a rendition's appearance raises it. A document is drawn in both languages whatever its standing:
a voided document's PDF is the record as issued, for the administrator. `FinancialDocuments:MaxRenditionsPerPass`
(40) bounds a pass.

**Stored before it is recorded.** The bytes go to private document storage under a fresh key per attempt
(`financial-documents/{documentId}/v{template}-{language}-{guid}.pdf`), then one row records them in
`financial_document_renditions`: language, format, template and renderer versions, the PDF's SHA-256 and
size, the snapshot hash it was drawn from and when. The table is append-only like the documents (the same
triggers refuse UPDATE, DELETE and TRUNCATE), unique per document, language, format and template, and its
rollback refuses to run once a real document has a PDF. A second process drawing the same PDF loses on that
key and removes only its own copy; any other failure to record keeps the bytes — the insert may have
committed without saying so, and removing them could leave a recorded PDF that no download can open.
Recording is not cancelled by a shutdown. A new template draws documents from then on beside the old PDFs,
never over what a customer may already have.

**Failures say so, once.** A host that cannot draw at all — a missing native library, a face that will not
load — fails a probe page at boot, and the boot log says FINANCIAL DOCUMENT PDFs ARE NOT DRAWN with the
reason; the pass then asks it for nothing. A document that cannot be drawn is logged once at Error and left
until the next start (pre-launch item 197); a full pass that draws nothing stops drawing until restart; storage
that refuses a PDF stops the step until the next tick.

**Handed out through short-lived links.** `GET /financial-documents/{id}/pdf-link?language=` and its
administrator twin mint a link to the existing `GET /documents/{token}`, which still needs a session.
Whose document it is is decided first: a stranger is answered exactly as for a missing document, never with
whether its PDF is ready. The customer's page carries `pdf { languages, preparing }`; the administrator's,
every rendition with its proof. The website saves the file as `{number}-{language}.pdf` through the session;
the app fetches the bytes over its own authenticated connection and opens them from its private cache, never
in a browser; the console lists every PDF drawn with its hashes and downloads it. A voided document's PDF is
not handed to the customer — a default awaiting the owner. The download endpoint moved from the `auth` rate
limit to `private-documents`. The change is additive for installed apps: no minimum raised.

**Verified.** Backend 2,324 tests; the PostgreSQL proofs on a scratch `postgres:18`, 46 of 46 with none
skipped — among them the table's keys and triggers, a forced race between two processes, and both rollback
branches; the 91 PDF tests on Ubuntu 24.04, the runtime image's base, with the network off (QuestPDF's native
libraries need only libc, libm, libstdc++, libgcc_s, libpthread and zlib, all in `aspnet:10.0-noble`);
website 231 (nine clean reruns after one failure under machine load that never reproduced), console 316
(i18n clean), app 706 (`flutter analyze` clean); production builds of the website and the console.

**Verified live** on 2026-09-29, on the local `khadra_web_it` through the owner's stack: the migration applied on
boot and the boot log named QuestPDF under its Community licence; the first passes drew 86 PDFs — every one
of the 43 documents, voided ones included, in English and Arabic — with no rendering error, and later passes
drew none again; 86 files in storage, one per language per document, each a PDF. On the website, the current
statement TEST-STM-2026-000020 offered Print and one button per language in both page languages, and each
saved `{number}-{language}.pdf` byte-identical (SHA-256) to the stored file; the link lasted five minutes and
every answer carried `no-store, private`, the file also `nosniff`. The voided TEST-PAY-2026-000013 offered no
PDF and no "being prepared"; its link answered 409 `financial_documents.pdf_voided`. A missing document
answered 404, a language other than `en`/`ar` 400, a tampered or expired link 404, a request with no session
401, and each role's route refused the other role (403). In the console, the PDFs section listed each
rendition with its proof in English and Arabic, the voided receipt carried the note that the customer is no
longer offered its files, its "drawn from" hash equalled its proof of issue, and the administrator's
download of it matched the file hash shown and the stored file. The stored PDFs, rendered with Windows's own
PDF renderer, read correctly in both languages — mirrored in Arabic, amounts "200.000 JOD", Amman times, the
phone number in order, the correction listed and the voided receipt not, and no commercial registration.

**Not yet done.** Two live checks nothing on the stack could reach: another customer asking for this
customer's PDF (it needs a second signed-in customer; the handler and endpoint tests prove the answer is the
missing document's), and the app on a phone (the tests drive the open flow with the platform viewer faked).
The owner's word on the voided-PDF default and on whether the on-screen pages should also leave the
registrations out; pre-launch items 195–200; the fonts' licence text (item 196); the Staging migration, which
waits for the owner; Phases 7 and 8.
