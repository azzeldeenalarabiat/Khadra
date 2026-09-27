# The payments programme: phases, owner decisions and required scope

Payments, receipts and booking invoices are built phase by phase on `feature/payments-receipts`,
each phase its own commit with its own tests (owner, 2026-09-24). This file is where the owner's
decisions for the programme are recorded, with their dates, and where the scope later phases MUST
deliver is written down so it cannot be dropped. The rules already in force are also stated in
`CLAUDE.md` and `docs/architecture-bounded-contexts.md`; what is knowingly deferred is in
`docs/pre-launch-checklist.md`.

## The phases

| Phase | What | State |
|---|---|---|
| 1 | Khadra's commission frozen on each booking, at 20% of one daily rate for new bookings | done (`9e3db1d`) |
| 2 | Payment purpose and processing fee, the amount paid online, deposit or full payment on the website and in the app | done (`43f4128`, `e04a55f`, `de384c5`) |
| 3 | Refunds generalised: what a paid booking owes back when it ends before pickup, the clean-close release, named refund events | done (`82a66e7`); items 169/170 (`b7c9db0`, `43b3707`) |
| 4a | One server calculator of a booking's financial state; `GET /bookings/{id}/financials` and its admin twin; the "Payments" section on the website and in app 1.3.0 | done (`c50ab9b`); item 173 (`673c586`) |
| 4b | The consoles: the office's Financial section and the admin's Money section from the calculator; the admin Payments and Refunds screens; the dashboard's money panel | done (`8a0eed0`) |
| 5 | Issued documents: payment receipts, refund receipts and booking statement versions, with numbering and immutable snapshots | plan approved 2026-09-27 (`docs/payments-phase5-plan.md`); SQL approved and applied locally only (scratch proof 35/35, then `khadra_web_it`); **5a, the backend, built** — issued by the settlement pass with holds, customer and administrator endpoints, void and correct; 5b (website, app, console) next; Staging not migrated |
| 6 | PDF rendering and secure download | not started (QuestPDF only if its Community licence applies) |
| 7 | Receipt and invoice email | not started |
| 8 | The office payables ledger (manual settlement) | not started |

## Owner decisions

### 2026-09-24 — the architecture

- **Office money:** an office-payable ledger per booking. Money Khadra collects beyond its commission
  and refund obligations is owed to the rental office; settlement stays manual (amount owed, amount
  paid, settlement date, the administrator who marked it paid, an immutable audit entry). No automated
  payout rail, and no real money movement until the provider/acquiring arrangement says how office
  settlement works.
- **Fully paid booking cancelled before pickup:** the amount above the deposit is refunded
  automatically; cancellation penalties and disputes stay deposit-based.
- **The office's share becomes payable at `Completed`**, never at pickup, and is never marked settled
  while a relevant refund or dispute is open.
- **Paying the remaining balance online after confirmation:** later, not in this release. The model
  may carry `RemainingBalance`; no customer flow offers it.
- **Processing fee on refunds:** refundable with the payment for the sandbox and the current
  architecture, configurable, and the fee itself stays disabled in Production until approved.
- **Deposit base:** `RequiredDeposit = 20% × RentalTotal`, delivery excluded, three-decimal JOD rounding.
- **PDF library:** QuestPDF only if Khadra qualifies for its Community licence (recorded in the
  checklist); otherwise stop and propose an alternative.

### 2026-09-26 — Phase 3 and the disputes that followed

- An administrator's cancellation of a paid booking before pickup, with no penalty on the customer,
  refunds the whole payment, deposit included.
- A cancel sheet sends the refund it showed; a changed figure is refused with `409
  booking.refund_changed` and the current figure, and the customer confirms again.
- A held deposit goes back when the booking's dispute window closes cleanly — decided by the backend,
  never because time alone passed.
- A second dispute may be opened but splits only what earlier disputes left (item 169). Every
  resolution allocates the whole amount available to it; the split between customer, office and
  platform stays the administrator's choice. No partial, held-for-later decision (item 170).

### 2026-09-26 — Phase 4

1. **Commission** (never shown to customers):
   - **Earned** at `Completed` when no dispute or refund affects settlement.
   - A booking that completed through a dispute resolution: **Undecided** until Phase 8 defines
     settlement and payables.
   - A deposit held for a customer penalty: Phase 4 takes **no** commission from it; Phase 8 decides.
   - The whole payment refunded: **NotEarned**.
2. **A deposit held for a penalty with no dispute (item 164)** — factual and neutral, promising nothing
   to either side until Phase 8 defines the settlement rule:
   - English: "Your deposit remains held because a customer penalty was assessed and no dispute was
     opened. Final settlement is still pending."
   - Arabic: "لا يزال عربونك محتجزًا لأنّ غرامةً قُدِّرت على العميل ولم يُفتح أيّ نزاع. التسوية
     النهائية لا تزال معلّقة."
3. **Dispute shares by reader:** the customer sees only their own share; the rental office sees its
   own share and any charge assessed to it; the administrator sees every share.
   - What follows from it in 4a: the office is not shown the refund that carries the customer's
     share, so the office's refund badge is read from the refunds it IS shown, and reads "refunded in
     full" only when those return all the booking money the payment applied. A badge judged by the
     whole payment would tell an office that got nothing from a dispute that the customer got
     everything. The office console shows no badge at all (4b): it sees one payment, and "partly
     refunded" would invite the question the share it is not shown answers.
   - And in 4b: the office's own copy of a booking (`GET /bookings/{id}` and every answer a decision
     returns) no longer carries the refund list — a dispute decision's row is on it — or any figure
     that includes the processing fee (decision 8). The office reads its money from the financial
     state's office projection only. Its copy of a dispute decision carries only the basis, its own
     share and any charge assessed to it (owner, 2026-09-27; pre-launch item 151). The customer's copy
     of a dispute still carries every share: that half of item 151 is deferred.
4. **Phase 5 refund storage:** each refund's split into booking money and processing fee is stored in
   the Phase 5 persistence model, in addition to being frozen into the issued document.
5. **Two commits:** 4a (backend and the customer surfaces), then 4b (the consoles).
6. **Naming:** the booking's section is called "Payments" in Phase 4 and "Payments & Invoices" once
   invoices exist (Phase 5).
7. **Customers see an orphaned capture** (a payment that took money and could not be applied) as a
   transaction being refunded in full. **Customers do not see failed attempts that charged nothing**,
   for now.
8. **Rental offices never see processing fees.**

### 2026-09-26 — the penalty notice (pre-launch item 173)

The notice on a booking with an assessed penalty states where the penalty stands, from the server's
state (`penalty.state`), never from a guess in the client:

- Before a dispute is resolved (`Assessed`):
  - English: "A penalty has been assessed, but no amount has been charged yet."
  - Arabic: "تم تقدير جزاء، ولكن لم يتم خصم أي مبلغ بعد."
- After a dispute is resolved (`ResolvedByDispute`):
  - English: "This penalty was resolved through a dispute. See Payments for the final amount."
  - Arabic: "تم حسم هذا الجزاء من خلال نزاع. راجع قسم المدفوعات لمعرفة المبلغ النهائي."

### 2026-09-27 — Phase 5, issued financial documents

The plan in `docs/payments-phase5-plan.md` is approved, with these decisions:

1. **The record.** The database financial-document row and its immutable snapshot are the official
   record. A PDF (Phase 6) and an email (Phase 7) are representations and deliveries derived from that
   record, never the record.
2. **Khadra's legal identity.** No permanent financial document is issued with placeholder or incomplete
   issuer information. Until the real legal identity is configured, issuance stays on hold.
3. **Sandbox numbering.** Documents about sandbox money are numbered `TEST-PAY-2026-000001`,
   `TEST-RFD-2026-000001`, `TEST-STM-2026-000001`: a test document must never be confused with a
   real one. The existing sandbox history receives `TEST-` documents when the history is issued.
4. **The customer on a document: the name only.** No email address in the immutable snapshot; addresses
   and delivery attempts belong to the delivery history (Phase 7).
5. **Cash recorded at a handover** is a financial event, and issues a new Booking Statement version.
6. **The customer area is "Invoices & Receipts" / «الفواتير والإيصالات»**, under My Account, not a new
   bottom-navigation tab. Inside it the documents keep their accurate names — Payment Receipt, Refund
   Receipt, Booking Statement — and no issued document is called a "Tax Invoice" until the legal and tax
   requirements for that term are implemented; each one states that it is not a tax invoice.
7. **Also approved:** rental offices see no financial documents in Phase 5; voids and corrections are
   append-only and preserve the original document; the app's changes ship in the unreleased 1.3.0; PDF
   stays Phase 6, document email Phase 7, payables and office settlement Phase 8.
8. **The SQL first.** The final schema and the exact generated migration SQL are shown to the owner before
   implementation continues, and no migration is applied to any database until the owner approves that SQL.

## Required scope for Phases 5–7: invoices and receipts reach the customer

Recorded 2026-09-26 as REQUIRED scope, not an option (pre-launch item 172). Invoices and receipts are
not a backend-only feature. When Phases 5–7 are done, the customer experience includes all of:

- a dedicated **Invoices / الفواتير** area in the Flutter app;
- every earlier invoice and receipt available **permanently** from the customer's account;
- the related receipt or invoice reachable from **Booking Details**;
- **immutable** payment receipts, refund receipts and booking statements;
- **PDF** viewing and download;
- **email delivery** of each issued invoice or receipt;
- document and email content in **English and Arabic**;
- refunds as **linked records** that never overwrite the original payment receipt;
- email **delivery status and history** the administrator can see: whether a document's email was
  queued, sent or failed.

Phases 5–7 may implement these in the order already planned; none of them may be dropped or
narrowed without the owner.
