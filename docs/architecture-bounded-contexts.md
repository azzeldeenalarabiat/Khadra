# Khadra — Architecture and Bounded Contexts

Khadra is a modular monolith built with Clean Architecture and DDD building blocks. Bounded contexts are folders in `Khadra.Domain` and `Khadra.Application`; they share one process, one `KhadraDbContext` and one PostgreSQL database, but never reference each other's aggregates directly. A context can be extracted into its own service later without changing its public contracts.

## Status

| Context | Domain model | Persistence | Use cases and endpoints |
|---|---|---|---|
| Identity & Access | done | done | auth done; dashboard read model done |
| Auditing | done | done | read model done; every dealer review decision writes an entry; `document_access_entries` records every renter-document disclosure, an administrator's included (Wave 4 W4-9: no dealership, no booking) |
| Dealers | done | done | **registration + full review lifecycle** (approve / reject / clarify / resubmit / suspend / reactivate) |
| Fleet | done | done | dealer fleet management done; **customer catalogue done** (search, listing, gallery page) |
| Bookings | done | done | dealer decisions and handover done; **quote done**; creation NOT built |
| Disputes | done | done | dashboard read model only |
| Reviews | done | done | **both directions done**; customer reputation read model done |
| Platform Settings | done | pending (configuration-backed) | pending |
| Payments | done | done | **deposit checkout + provider webhook done**; NO PROVIDER CONFIGURED |
| Shortlist | done | done | **save / forget / list / membership done** |
| Financial Documents | done | done | **issued by the settlement pass, with holds; customer and administrator endpoints; void and correct** (payments Phase 5a); **read on the website, in the app and in the console** (payments Phase 5b) |
| Payables | done | done | **the office payables ledger, recorded by the settlement pass; manual settlements, voids and holds; administrator and office endpoints; Payouts and Finance in the console** (payments Phase 8) |
| Legal | done | done | **the Terms of Service and the Privacy notice, published from the console one append-only version at a time; the version in force read anonymously; pages on the website, links in the console** (Wave 2 G1); **consent captured at registration and invitation, the record readable by its subject, and the website and console refused until a text in force is accepted** (Wave 4 W4-8; the app in 1.4.0, item 238) |

"Dashboard read model only" means the tables and the read-side queries behind the `GET /api/v1/admin/dashboard/*` panel endpoints exist, but no command handlers do: nothing yet approves a dealer or resolves a dispute through the API.

Payments was built on 2026-09-08 with the owner's explicit approval. "No provider configured" is not a
qualifier on the table above: the aggregate, the state machine, the idempotency guards, the refunds and
both endpoints are complete and tested, and the ONE thing missing is a merchant account. Every checkout
is refused with `payments.provider_unavailable` (503) until `Payments:Provider` names a real one, and
the startup log says so on every boot.

## Context map

```text
Identity & Access ──supplies actor (UserId, role, verified)──▶ every context
Dealers ──(DealerId, delivery settings, approval status)──▶ Fleet, Bookings
Fleet ──(VehicleId, rate, deposit, delivery-eligible)──▶ Bookings
Bookings ──events (Approved, PickedUp, Cancelled, NoShow, Completed)──▶ Payments, Disputes, Reviews
Disputes ──resolution (money instructions)──▶ Payments, Bookings
Platform Settings ──IBusinessRulesProvider──▶ Bookings (frozen onto each booking as BookingTerms), Shortlist (the cap)
Shortlist ──(VehicleId only)──▶ reads Fleet's catalogue; Fleet learns nothing about customers
Financial Documents ──reads, by id only, never writes──▶ Bookings, Payments, Disputes, Dealers, Fleet, Identity (names), Platform Settings (lookups)
Payables ──reads, by id only, never writes──▶ Bookings, Payments, Disputes, Dealers, Identity (names); records from the Payments calculator's office position
Legal ──(version id)──▶ consents (Wave 4, beside the user's bare id from Identity & Access); reads Identity (publisher names) by id only
```

Communication is by `Id`, by explicit application contracts, or by domain events. A context never mutates another context's aggregate, and there are no navigation properties across contexts.

## Shared kernel (`Khadra.Domain/Common`)

`Id` (UUIDv7), `Entity`, `AggregateRoot` (domain events), `ValueObject`, `Enumeration` (smart enum), `Error` + `ErrorKind`, `Money` (three minor units for JOD fils, no cross-currency arithmetic), `Percentage`, `GeoPoint` (haversine distance), `DateRange` (half-open, both ends normalised to UTC; it carries no day count -- see below), `ISoftDeletable`, `IUnitOfWork`, `DomainException`, `ConcurrencyConflictException`.

## 1. Identity & Access

`User` (credentials, role, status, email verification, security stamp, soft delete), `RefreshToken` (families, rotation, replay detection, absolute deadline), `VerificationToken` (single-use links). Fully implemented with endpoints; see `docs/auth-and-sessions.md`.

## 2. Dealers

`Dealer` aggregate with `Employee` and `DealerDocument` children. Verification lifecycle is `PendingReview → Approved | Rejected | ClarificationNeeded`, with resubmission restarting the 48-hour admin SLA clock. Approval is refused until all three required documents are on file.

Verification and suspension are **separate**: verification is the one-time licence check, suspension is an ongoing policy sanction. A suspended dealer stays `Approved`, so reactivating does not send them back through review. `CanTrade` is the single question every other context asks.

Employees never self-register. `CanActOnBookings` and `CanViewReports` encode spec 4.2: acting on bookings is the default permission, report access is off until the owner grants it.

## 3. Fleet

`Vehicle` aggregate with `VehicleImage` children, `PlateNumber`, `VehicleDetails`, `MileagePolicy` and `FuelPolicy` value objects. Lifecycle `Draft → Active → Hidden`. Publishing requires an approved dealer and at least one photo.

Availability is **not** stored on the vehicle. It is derived from bookings, because storing it would create a second source of truth that drifts the first time a booking is cancelled. Mileage excess is charged against the whole-rental allowance, not per day.

## 4. Bookings

`Booking` aggregate with `HandoverRecord` and `BookingStatusChange` children, plus `BookingPricing`, `BookingTerms`, `PenaltyAssessment` and `BookingReference` value objects.

**Lifecycle.** `Requested → Approved → Confirmed → PickedUp → Returned → Completed`, with terminal exits `Rejected`, `Cancelled`, `NoShow` and `Expired`. Every state has an exit:

- `Requested` expires when the dealer's answer window closes, releasing the held vehicle.
- `Approved` expires when the customer's payment window closes without the deposit, likewise.
- `Returned` completes when the post-return settlement window passes with no open dispute, or immediately once a dispute is resolved.

The customer reserves first and pays after the dealer approves — reordered on 2026-09-07, superseding spec §5.3; see `docs/spec-amendments.md` for what that cost and what was done about it. `Confirmed` means the deposit has cleared. `PendingPayment` is retired, along with its enumeration id, and must never be reused.

**Creating a booking** is `POST /api/v1/bookings` (`CreateBookingHandler`), and it is deliberately the quote handler plus a write: both judge the dates through `BookingWindowPolicy`, price through `BookingPricer`, and ask `HasOverlappingBookingAsync`, so a screen can never show a price beside a button that is refused. What it adds is who the customer is (email verified, a licence and an identity document uploaded — `HasCompleteRenterDocuments`), serialising against other creators, clearing the stale holds in the way, and the insert. Those happen in one transaction, and the order is the point: **lock, expire, ask, insert**. The exclusion constraint cannot mention `now()`, so a request past its decision deadline or an approval past its payment deadline still occupies its index while `BookingHolds.Live` has stopped counting it; without the expiry the guard says free and the database says taken.

The lock (`IVehicleHoldLock`, a transaction-scoped Postgres advisory lock keyed on the vehicle) is not the correctness floor — `bookings_one_hold_per_vehicle` is, and it alone makes double-booking impossible. The lock is what makes the ANSWER truthful: without it, two customers booking the same car for different weeks can both try to expire the same stale hold, and the loser would be refused over dates that were free. The stale query is scoped to holds that OVERLAP the candidate for the same reason. A 23P01 from the constraint surfaces as `booking.vehicle_unavailable`; a lost `xmin` race deliberately does not, because it says nothing about whether the car is free.

**Three bounds on a rental**, all configured. Two are about when it may START, judged together in `BookingWindowPolicy` so search, quote and create agree: `BusinessRules:MinimumBookingLeadTimeMinutes` (240 today — it must stay above the payment window, see CLAUDE.md) and `MaxAdvanceBookingDays` (180). Neither converts through `IReportingCalendar` — "four hours from now" and "180 days from now" are elapsed time, which has no time zone. The third is how LONG it may run, `MaxRentalDays` (90), judged in the same `BookingWindowPolicy` but on the Amman calendar-day count the pricing produced, because those days only exist once the period has been through the calendar — so a customer is refused on the same number they were quoted. (Corrected 2026-10-05: this paragraph said 120 minutes and placed the third bound in `BookingPricer`.)

**A gallery's counter hours bind a self-pickup, and never a delivery** (owner, 2026-09-07). `PickupHoursPolicy` judges both ends of a self-pickup rental — collection and return are both counter events — and `BookingPricer` applies it, so the quote and the create agree and the catalogue search, which spans galleries, does not try to. `IReportingCalendar` grew a `TimeOfDay` to go with `DayOf`, because opening hours are wall-clock times and 09:00 in Amman is 06:00 UTC.

**A request notifies the gallery** in the same transaction that creates it (`NotificationKind.BookingRequested`), and the row names no customer: `DealerTeamNotifier.NotifyTeamOfCustomerActionAsync` writes "A customer" and no actor id, because notifications are never deleted and a customer's name must not outlive their account.

**Two consecutive clocks, and each releases the car at its own deadline.** `Requested` holds the vehicle only while `DecisionDeadline > now`, `Approved` only while `PaymentDeadline > now`. `Confirmed` and `PickedUp` hold it outright. So a car returns to the market at the instant a window closes, with no job involved; a background job then settles the status afterwards.

**Terms are frozen at booking time.** `BookingTerms` snapshots the deposit and commission percentages, the free-cancellation window, the no-show timeout and the penalty range as they stood when the booking was made. Rules are admin-editable, so judging a cancellation against today's settings would retroactively penalise customers and make past decisions unreproducible.

**Rentals are billed in CALENDAR days**, settled by the owner on 2026-09-07: the difference between the Amman pickup and return dates, never fewer than one. Monday 09:00 to Thursday 11:00 is three days. The count lives in `RentalDays.Between(DateOnly, DateOnly)` and is frozen onto `BookingPricing` beside the two dates it came from, so changing `ReportingTimeZone` can never re-judge a rental that was already agreed. `DateRange` deliberately has no day count: it is the shared kernel's INSTANT interval, and a calendar day is a question about a local calendar the kernel has no zone to answer with. Two consequences the owner has been told: the rule is never dearer than the elapsed-time one it replaced, and a late return costs nothing, because the return time of day no longer affects the price.

**A booking claims the car before the customer collects it.** `Booking.HoldStart` is the period start moved back by `BookingTerms.TurnaroundBuffer` (`BusinessRules:TurnaroundMinutes`, 120), the gap a gallery needs to clean and check the car. The pad is on the LEADING edge only -- padding both would double-count the gap and refuse one exactly equal to the buffer, and a trailing pad would make an extension, which starts where its parent ends, impossible to store. An extension carries no pad at all. `HoldStart` is a real column because the `bookings_one_hold_per_vehicle` exclusion constraint indexes it, and Postgres refuses to index `timestamptz` arithmetic.

**Pricing is frozen too.** `BookingPricing` snapshots the daily rate, the security deposit, the mileage policy and the fuel policy. A dealer raising a rate or tightening a mileage cap cannot rewrite a contract already accepted. The deposit and commission are taken on `RentalTotal`, deliberately excluding the delivery fee, which is a pass-through for the driver's trip rather than rental revenue.

**Penalties are assessed, never charged.** Spec 3.3 and 5.5 make "no ticket, no penalty" the default, so `Cancel`, `ReportDealerNonDelivery` and `MarkNoShow` record a `PenaltyAssessment` and stop. Money moves only when an Admin resolves a dispute. Attribution is honest: a self-pickup no-show is attributed to the customer, but a **delivery** no-show is `Unattributed`, because the dealer was the party who had to travel. The free-cancellation window runs from the moment the deposit clears, not from approval, and is capped at the period start so a deposit paid just before pickup cannot grant free cancellation after the car was due to be collected.

`ConfirmDepositPaid` is idempotent, since payment gateways retry webhooks. Booking is not soft-deletable: it is a financial record, and `Cancelled` / `Expired` are its deletes. An extension is a new booking carrying `ExtendedFromBookingId`, never a mutation of the original.

## 5. Disputes

`DisputeTicket` with `DisputeStatement` children. One live ticket per booking; both parties add statements to it rather than opening competing tickets. Lifecycle `Open → UnderReview → Resolved`, plus `Withdrawn` as the amicable exit. The 48-hour SLA matches the dealer-approval SLA.

`DisputeResolution` carries **money instructions, not labels**: a `DepositDisposition` splitting the held deposit between refund, platform and dealer (which must balance to the exact amount held), plus an optional `DealerCharge`. Payments can then act mechanically without interpreting an outcome name.

## 6. Reviews

`Review` with a `Rating` value object. One per booking per direction, only on a completed booking. Both directions ship, and they are shaped very differently on purpose.

**The customer's review of a gallery is PUBLIC**: anonymous to read, free text allowed, and hiding it removes the TEXT and keeps the SCORE, so a gallery cannot erase a bad rating by reporting the comment attached to it (spec 3.2, 4.1).

**The gallery's rating of a customer is not public and never becomes so.** It is a bare score with NO free text at all -- unverified prose about a named private individual, circulating between competing businesses and invisible to the person it describes, is not something this platform will store. It is readable only as an AGGREGATE, only by a gallery holding a LIVE booking with that customer, and only through the booking that gives them the relationship: `GET /api/v1/bookings/{id}/customer-reputation`. There is deliberately no endpoint anywhere that takes a customer id, because that would be a lookup oracle over the whole customer base for anyone with a dealer session. Hiding one of these does NOT keep the score (`ReviewDirection.HiddenScoreStillCounts`): there is no text to moderate, so the only thing an administrator can be hiding is a score that was wrong.

**Both directions are BLIND until the window closes or both sides are in.** `Review.VisibleFrom` is a real column, and the invariant -- nobody sees the counterpart before submitting -- holds by construction rather than by checking: the first review sets its own reveal instant, the second party's deadline to submit IS that instant, and a second review reveals both at once. Without it, publishing the customer's review the moment it is written would hand the gallery a retaliation button with the platform's own machinery behind it. `BusinessRules:ReviewWindowDays` is the number, proposed at 14 and not yet an owner decision.

**`CustomerReputation` counts what the PLATFORM adjudicated**, never what a gallery asserted, and it reads `Penalty.AttributedTo` rather than the status. That distinction is the whole correctness of the reader: a DELIVERY no-show is `Unattributed` because the gallery had to travel, and `ReportDealerNonDelivery` cancels with `CancelledBy = Customer` while attributing the penalty to the DEALER -- counting by status would put the gallery's own failure on the customer's permanent record. A customer reads the same figures about themselves at `GET /api/v1/customers/me/reputation`, because a semi-private score somebody cannot see is the thing privacy law objects to, and it is the only way they learn to dispute a wrong no-show inside the window.

Ratings are aggregated in SQL as read models.

## 7. Platform Settings

`BusinessRuleSettings` is a single versioned aggregate holding every number from spec section 2, and it refuses a commission above the deposit, because commission is collected from the card deposit. `PendingOwnerDecisions()` surfaces the questions the owner has not answered rather than pretending a default is a decision. `CarType` and `City` are bilingual lookups that deactivate rather than delete.

## 8. Payments

`Payment` is ONE CHECKOUT ATTEMPT for one booking — its deposit or, since 2026-09-24, its full amount
(`PaymentPurpose`) — with `Refund` as a child entity, plus
`ProviderEventReceipt` — an append-only log of provider notifications that is deliberately OUTSIDE the
aggregate, because an event naming a reference this platform never issued has no payment to hang off
and still has to be recorded.

`Commission` and `SecurityDeposit`, which the old design sketched, were NOT built. Commission is
already derivable from the booking's frozen `Terms.CommissionPercent` over `Pricing.RentalTotal` and a
table for it would be a second source for one number; the security deposit is open owner decision 4 and
lives today as cash on the handover record.

**States: `Initiated -> Pending -> Failed | Applied | Orphaned`.** There is deliberately no persisted
`Captured`. A capture is a FACT about money that has already moved, and the webhook handler must
resolve it, in the same transaction, to `Applied` (the booking took it) or `Orphaned` (it could not,
and a refund is recorded). A captured-but-unresolved row would be permanently stuck: its receipt makes
the provider's retries look like replays.

**Five things must hold before a capture confirms anything.** The signature verified over the raw body;
the delivery never seen before; the reference resolving to a `Payment` this platform issued; the
captured amount AND currency equal to what that row asked for; and the booking still able to take it.
Anything short of all five is an orphan, and an orphan always carries its refund — `Payment.Orphan`
creates it, so the two cannot be separated.

**Two database guards, not two handler checks.** `ux_payments_one_live_attempt_per_booking` (partial
unique over `booking_id` where the status is live) stops one booking having two card forms open;
`(provider, provider_event_id)` unique on the receipt table stops a replayed webhook, and is INSERTED
in the same transaction as the effect rather than read first, because a read-then-write leaves a window
two concurrent deliveries both pass.

**The deadline is enforced at the DOOR, never at the capture.** `BookingDepositSettlement.DepositDue`
is the only place the payment window is checked. Once a provider has captured, refusing the money would
mean keeping it, so `ConfirmDepositPaid` stays deadline-blind (pre-launch item 62) and a late capture is
applied if the booking can still take it and refunded if it cannot. The cheap half of the fix is
`Payments:CheckoutClosesBeforeDeadlineMinutes`, which kills the provider's own session before the
booking's window closes, so the expensive path is rare rather than routine.

**The seam to Bookings is a CALL, not an event.** `BookingDepositSettlement` is the named door, the
twin of `BookingDisputeSettlement`, and it is the only production caller of `Booking.ConfirmDepositPaid`.
This project has no outbox and dispatches domain events after commit, so a cross-context event lost
between the two would mean money captured, booking unconfirmed, and the car released at its deadline.

**Refunds are RECORDED when owed and SENT afterwards**, by the payment sweep that runs beside the
booking settlement pass. The triggers: an orphaned capture (automatic, in the capture's own
transaction), an admin's dispute resolution returning money to the customer, and — since payments
Phase 3 (owner, 2026-09-24/26) — every PAID booking that ends before pickup. Those endings are recorded
through ONE seam, `BookingEndingRefunds` (Payments-owned, twin of `BookingDepositSettlement`), in the
same save as the transition that ended the booking: the whole payment for a customer's free
cancellation and an administrator's cancellation, everything above the deposit for any other paid
ending. The deposit itself goes back when the booking's own dispute window closes with no claim on it
and no penalty against the customer, released by the settlement sweep. `BookingDisputeSettlement
.DepositHeldFor` reads zero once the whole payment went back or the deposit was released, so a
dispute can never split money already on its way back, and the money above the deposit is never part
of what a dispute splits.

**The commission changed on 2026-09-24, and with it the "never holds dealer funds" property.** It is
now 20% of ONE day's rental price, frozen on each booking as `Pricing.CommissionAmount` with the rule
that produced it (`Terms.CommissionBasis`); bookings made before keep the whole-rental figure they always
showed, written onto them by the `FrozenCommission` migration. Against a 20% deposit on the whole rental,
every booking longer than a day now leaves the platform holding `Deposit − Commission` that belongs to
the office. The owner accepted that on 2026-09-24, with a manual office-payable ledger (payable at
`Completed`, marked paid by an administrator, audited) and no payout rail yet — built in payments Phase 8
as its own context, Payables (§11).

**What was not built, and why (true until 2026-09-24).** No `DealerLedger`, no payout rail, no dealer
charge: at the confirmed 20% commission and 20% deposit the two were equal, so the platform never paid a
dealer and never held dealer funds. Of the three spec cases that would need a rail, two are closed by construction —
`CreateBookingHandler` only ever writes `PaymentOption.DepositOnly` (since Wave 3, 2026-10-06, a booking a full payment confirms reads `FullUpfront`, from the payment's purpose: E2E F54), and both `BookingTerms.Create` and
`BusinessRuleSettings` refuse a commission above the deposit. What remains is the dealer non-delivery
penalty, which is already an instruction with no rail (`DisputeResolution.DealerCharge`) and is settled
by hand.

**A booking's financial state is ONE calculator** (payments Phase 4a, owner 2026-09-26).
`BookingFinancialsCalculator` (Application/Payments/Financials) reads the booking, its payments with their
refunds, and its resolved disputes, and answers every figure a screen shows: what was charged and applied,
what went back and where it is, the balance (`BalanceStates`), the deposit (`DepositStates`), Khadra's
commission (`CommissionStates`, never to a customer) and the payment history. It adds no rule of its own —
each state reads a verdict the aggregates already give (`Booking.EndedBeforePickup`,
`HasPenaltyAgainstCustomer`, `DisputeWindowEndsAt`, `ReturnsWholePayment`; `Payment.FeeInside`,
`WholePaymentRefundAmount`) — and it never throws on records that contradict one another: it reports
them (`FinancialIssues`) and serves what they say. Three projections of one answer — the customer's, the
office's (no processing fees, no customer or platform dispute share) and the administrator's — are served
by `GET /bookings/{id}/financials` and `GET /admin/bookings/{id}/financials`. It reads only frozen or
immutable facts plus refund status, so Phase 5 can freeze its answer into an issued booking statement;
`calculatorVersion` travels with it. See `docs/payments-programme.md` for the owner's decisions behind
each state.

**The administrator's money screens read the same verdicts** (payments Phase 4b). A payment's refund
progress is the payment's own (`Payment.RefundProgress`, a smart enum), read identically by the
calculator and by the payments list; a payment's page is `BookingFinancialsCalculator.Describe` under the
administrator's projection. `IPaymentAdminReader` serves the payments list, the refunds queue (refused
first, owed longest first, by `RequestedAt`) and a payment's provider events, tied by the payment's id OR
its provider reference — a receipt that arrived before the reference was saved carries only that.
`IPaymentDashboardReader` returns FACTS that `FinanceSummaryBuilder` adds up: the month's applied payments
loaded as payments, so booking money and fee are the aggregate's own arithmetic, and the refunds settled
in it or still owed. The work queue's money rows have no deadline (`slaDeadlineAt` null): refused refunds
and captures being refunded as one grouped row each. (Until Phase 8 each deposit pre-launch item 164 was
about had a watched row of its own; the ledger gave those deposits an exit and retired the row.) Rows with
a deadline come before refused refunds, which the payment sweep is already sending again; the bell carries
the refused refunds (and, since Phase 8, the offices' payables the ledger holds back) and leaves the watched
rows to the dashboard. The office reads its money from its projection only: its
own copy of a booking (`BookingDto.ForDealer`) carries no refund list and no fee, and its copy of a
dispute decision (`DisputeResolutionDto.ForDealer`) only the basis, its own share and any charge to it.

**Nobody may add a provider that simulates success — except the one recorded exception.**
`UnconfiguredPaymentProvider` answers "no provider" in production; `SandboxPaymentProvider` is the
owner-approved exception (2026-09-21) for clicking the lifecycle through before a merchant account
exists, and the three guards that make it allowable are in CLAUDE.md. A stub that confirmed bookings
without money would otherwise be indistinguishable, in every table and on every screen, from a real
payment. Tests substitute `IPaymentProvider` at the handler boundary. Writing a real adapter is one class
implementing four methods; nothing above it changes.

## 9. Shortlist

`CustomerShortlist` is ONE AGGREGATE PER CUSTOMER, keyed by the customer's own id, with
`ShortlistEntry` children carrying a `VehicleId` and the moment it was saved. Nothing else: a snapshot
of the make, model or price would go stale the first time a gallery corrected a listing, and a screen
rendering last month's price beside today's car is worse than a screen rendering neither.

**Why its own context and not Fleet.** Fleet is the dealer's inventory, and its customer-facing side is
an anonymous read model whose own remarks warn against sharing types with dealer DTOs — a per-customer
flag on the catalogue is exactly that leak, and it would teach Fleet what a customer is. Not
Identity & Access either: that holds `CustomerDocument` because identity verification is identity; a
shopping preference is not.

**Both mutations are idempotent**, because a heart is a toggle on a mobile network and a retried tap
must not become an error. Saving what is already saved succeeds; forgetting what was never saved
succeeds. Forgetting deliberately does NOT require the car to still be visible — an entry for a
withdrawn listing is precisely the one a customer most wants gone.

**Saving goes through `ICatalogueReader.GetAsync`, never a visibility check of its own.** Saving is
otherwise the cheapest enumeration oracle on this platform: "saved" on an id the public catalogue
answers 404 to would confirm that id exists, and anyone with an account could walk a competitor's
unpublished inventory a request at a time. One predicate, in one place, answering the same for a
draft, a hidden car, one in maintenance, a suspended gallery's, a deleted one and an unknown id.

**A saved car that stops being bookable keeps its row.** `Maintenance -> Hidden -> Active` is a
normal round trip for a gallery, and an entry auto-removed on the way through would be a customer's
list quietly editing itself. `IShortlistReader` returns such an entry NAMED — make, model, year and
its gallery — with no listing beside it, and no reason.

The split is the point. Naming the car is safe because nothing reaches a shortlist that the public
catalogue did not return first: `SaveVehicleCommand` refuses any id `ICatalogueReader.GetAsync`
answers null to, so every name on this list is a car the customer was already shown. Naming the
REASON would distinguish the cases the catalogue is shaped never to distinguish, and there is no
field on the wire that could.

The name is read live and **past the soft-delete filter**, which is a correctness requirement rather
than a convenience: with the filter respected, a deleted car would come back unnamed while a hidden
one came back named, and deletion would become the single de-listing reason a customer could tell
apart.

The cap is `BusinessRules:MaxShortlistEntries`, configured rather than constant, settled by the owner
at 100 on 2026-09-11.

## 10. Financial Documents

The receipts and statements the platform issues about a customer's money (payments Phase 5, owner
2026-09-27; the full design and the owner's decisions are in `docs/payments-phase5-plan.md`). Three
documents, none of them a tax invoice and each saying so: a **Payment Receipt** when money is captured, a
**Refund Receipt** when a refund settles, and a **Booking Statement**, versioned each time money moves.

**The row and its snapshot are the official record.** `FinancialDocument` is append-only twice over (the
`IAppendOnly` guard and the database's `khadra_table_is_append_only()` triggers), and everything it shows —
parties, figures, and every word in English and Arabic — is frozen into one canonical JSON snapshot with
its SHA-256 beside it. A PDF (Phase 6) and an email (Phase 7) are representations of it. A wrong document
is voided and corrected in ONE transaction (`FinancialDocumentVoid`), audited in the same one; its
standing — current, superseded, voided — is derived when read, never stored.

**Its own context, reading everything, writing nothing elsewhere.** It references bookings, payments,
refunds, tickets, offices, cars and customers by id, reads them as COMMITTED (never through the
clock-settling booking repository, whose in-memory lapse stamps an ending with the current instant), and
reads the office and the car past the soft-delete filter so a document still names them after they leave.
Statements are composed from `BookingFinancialsDto.For(financials, Customer)` alone — the customer's
projection — so nothing a customer may not see can reach an append-only record.

**The facts are the queue.** No row records that a document is due. The settlement pass, right after the
payment sweep, asks: which captured payment has no receipt row, which settled refund has none, which
booking's checkpoints — a capture, a settled refund, a resolved dispute, the ending, cash at a handover, a
receipt corrected, a penalty the office payables ledger kept (payments Phase 8, item 212), and nothing else — are newer than its latest statement (or were committed just after it, inside
`LateCommitMarginMinutes`), with the checkpoint fingerprint deciding. Each document is issued in its own
scope and transaction: compose, take the number from its series row (`INSERT … ON CONFLICT … RETURNING`,
so a rollback returns it and a series has no gaps), insert. What cannot be issued goes on HOLD
(`RecordsNeedReview`, `IssuerNotConfigured`, `SnapshotFailed`) with a growing retry delay, shown on the
administrator's work queue and in the boot log — never silence.

**Test money is marked by the money itself.** `provider` on the document is a frozen copy of
`payments.provider`; sandbox documents are numbered `TEST-PAY-…`, `TEST-RFD-…`, `TEST-STM-…`. No document is
issued without Khadra's legal identity (`FinancialDocuments:Issuer`, all or nothing); a clearly marked test
identity exists for local sandbox testing only and `Program.cs` refuses it anywhere else.

**Who sees what.** The customer: their own documents (`/customers/me/financial-documents`,
`/financial-documents/{id}`, and a booking's under `/bookings/{id}/financial-documents` with what is still
being prepared); anyone else's is 404. The rental office: nothing in Phase 5 — an empty list. The
administrator: everything, with the provider, the proof hash, holds and the void's reason, under
`/admin/financial-documents`.

**PDFs are representations, drawn once** (payments Phase 6, 2026-09-29). The settlement pass draws each
issued document as a PDF in English and Arabic from its stored snapshot alone — and only while the snapshot
still hashes to what was issued — stores it privately, and records it in the append-only
`financial_document_renditions` with the PDF's hash and the snapshot hash it was drawn from. It is handed out
through links minted on request (`…/pdf-link?language=`) to the one private-file endpoint, ownership decided
first. A rendition has a KIND (owner, 2026-09-29): the document as issued, or — for a voided document — its
voided copy, a second rendition drawn once after the void from the snapshot and the void's facts, stamped VOID
on every page and naming the correction. The original is never re-drawn or touched and stays the
administrator's; the customer of a voided document is only ever handed the copy. The commercial registrations —
Khadra's, the rental office's, a future business customer's — are frozen in the snapshot and left out of the
PDF's body and of the customer's document pages; the console shows them in its proof of issue (owner,
2026-09-29). QuestPDF under its Community licence draws the
page; the fonts are embedded, never the host's.

**Emails are deliveries, never records** (payments Phase 7, 2026-09-29). Every payment and refund receipt — never a
statement — is owed an email in the transaction that issues it: a row in `financial_document_deliveries`, the outbox
a background service of its own works every minute, never the settlement pass, so a mail server that stalls cannot
delay a booking deadline. An email is decided as it is sent: the customer's verified address and language (both,
Arabic first, for one who never chose), the PDFs as issued attached, and never without them — it waits, however
long, for a PDF not drawn yet. A send attempt is spent before the transport is called and its outcome recorded
after; a row is worked only by the process whose claim count it still carries, and a retry is the same message under
the same idempotency key. Every attempt is kept in the append-only `financial_document_delivery_attempts`, which
holds no address, name or body; the address is on the mutable delivery row. The administrator reads each email's
history and can email a receipt again, audited by its number, and a Failed email or one queued too long is a row on
the work queue. TEST receipts reach only the local Mailpit or, through a real provider, an allowlist; replies go to
Khadra's support address.

## 11. Payables

What Khadra owes each rental office, and what each office owes Khadra, booking by booking, with the settlements an
administrator records by hand (payments Phase 8; owner, 2026-09-24 and 2026-09-29 — the decisions are in
`docs/payments-programme.md`, the design in `docs/payments-phase5-plan.md` §24). There is no payout rail: a
settlement records a payment made outside the platform.

**One payable per final paid booking, recorded from the one calculator.** `OfficePayable` (`Khadra.Domain/Payables`)
is written by the settlement pass, after the payment sweep, for every paid booking whose outcome is final — a
completed rental past `Payables:FinalityMarginMinutes`, a cancellation or a no-show once its dispute window has
closed, never while a dispute is live — and only from `BookingFinancialsCalculator`'s office position, so the ledger
adds no arithmetic of its own. Its lines (`OfficePayableLine`: the rental, a dispute's share or the kept penalty
towards the office; the commission and any dispute charge against it) and figures are frozen: the aggregate derives
them from the lines, CHECK constraints tie them together, and a trigger refuses any change except the settlement
pointer, and that pointer only from nothing to a settlement, or back when the settlement is voided. A payable is
recorded even when it nets to zero, so every final booking has its answer. A booking the ledger cannot record — its
records contradict one another, or its penalty is not the whole deposit — is HELD (`OfficePayableHold`) with a
growing retry, never silently skipped; an unsettled payable whose records later disagree is held `Contradicted`; and
an administrator may hold a payable by hand. Holds are the work queue's `PayablesOnHold` row.

**Due is derived, never stored.** A payable is due when it is final, unsettled, not held, not blocked, and not zero.
Blocks are read live from the other contexts on every read — a refund not yet settled on any payment of the booking,
or a live dispute — so nothing has to remember to clear them.

**Settled by hand, all of an office's due balance at once.** `OfficeSettlement` pays every due payable of one office
in one currency and one kind of money, for exactly the figure the administrator was shown: the handler re-reads what
is due inside the transaction, after taking the settlement's number from its series row (`SET-{year}` or
`TEST-SET-{year}`, through the same `IFinancialDocumentSeries` as the documents, so two settlements serialise on the
row and a rollback leaves no gap), and refuses a changed balance with the balance due now. The net can run either way
(owner, 2026-09-29: netted), so a settlement is a payout, money received, or a netting at zero. Settlements, their
lines and their voids are append-only; a void (`OfficeSettlementVoid`, with a reason) reopens what it covered. Every
settlement, void, hold and release by an administrator is audited in the same transaction as the action.

**Test money stays test money.** `provider` on a payable and a settlement is copied from the booking's confirming
payment, so a sandbox booking's payable is `SANDBOX`, it is settled only with other sandbox money, and its settlement
is numbered `TEST-SET-…`; the migration's rollback refuses once real money is in the ledger.

**Who sees what.** The administrator: everything, under `/admin/office-balances`, `/admin/office-payables`,
`/admin/offices/{dealerId}/settlements`, `/admin/office-settlements/{id}` and `/admin/finance/summary`. The office's
owner and any employee granted the reports: its own payables and settlements, under `/dealers/me/payouts`, whatever
the dealership's standing — a suspended office is still owed, or still owes — without the kind of money, the notes,
who recorded or voided a settlement and why, the holds or the blocks. The customer:
nothing — the ledger reaches a customer only as the deposit's `KeptAsPenalty`, the penalty's `KeptFromDeposit` and the
booking statement version a kept penalty issues (item 212), with the owner's approved sentence said once.

## 12. Legal

The legal texts (Wave 2 G1; pre-launch item 224). The owner decided on 2026-10-05 that they live in the database and are
published through an Admin screen. The advisor reviewed the schema before its migration was written.

`LegalDocumentVersion` is one published version of one document (`LegalDocumentKind`: `Terms`, `Privacy`; add-only), in
English and Arabic. Append-only, like `financial_documents`: the `IAppendOnly` guard, row and TRUNCATE triggers, and a
migration `Down` that refuses while any version exists.

- **In force when published.** `effective_from = published_at`; nothing schedules until a scheduled version can be
  withdrawn (item 229). Every version is later than the one before it, so the version in force is the newest, and a
  unique index on (kind, effective_from) settles two administrators publishing at once.
- **The text is the record.** Markdown in a checked subset, LF line ends, each body hashed on its own (SHA-256 of the
  UTF-8 bytes, what `sha256sum` gives), so a file approved outside the system can be checked against what was
  published. The publisher is an id; their name is in the audit entry written in the same transaction.
- **One renderer.** Markdig on the server checks a text against a closed list of node types (raw HTML, images, code
  and any link but `https:`, `mailto:` or a path on the site are refused, with the line) and renders it to HTML at
  read time. The console's preview and the public page therefore come from the same function.
- **Reads.** `GET /api/v1/legal-documents/{terms|privacy}/current` is anonymous, publicly cached for five minutes, with
  a weak ETag naming the version and the renderer. `/app-config` carries a `legal` block: each text in force, and its
  page on the website from `App:CustomerAppBaseUrl`. The block is null, "not known", when the database cannot be
  read, never an empty list (item 228).
- **Consent (Wave 4 W4-8, built 2026-10-07).** `LegalConsent` (`legal_consents`), append-only like the versions: the
  user's bare id, the version id (FK, restrict), when, channel (`Website`, `App`, `Console`) and language as smart
  enums with CHECKs, and an action (`Accepted`; a withdrawal would be a new row). Not unique, but a version already
  accepted writes nothing. No IP address or user agent. Its `Down` refuses once anybody has consented.
  - **Captured** in the account's own save by `LegalConsentRecorder` (website and app registration, dealer-owner
    registration, staff invitations; an administrator's invitation asks nothing), and later through
    `POST /auth/me/legal-consents`. `ILegalConsentReader.PendingAsync` is the one statement of "pending": the gate,
    the prompt and `/auth/me` all ask it.
  - **Enforced** by `LegalConsentGate`, after authorization: 403 `legal.consent_pending` for a signed-in request from
    somebody with a text pending, except what resolves it. It never judges an administrator (the texts do not address
    Khadra's staff) or a request that declares an app version (item 238); both BFFs strip that header.

## Owner decisions required

These change field shapes, so they are worth settling before the affected context is built.

1. **Customer cancellation penalty.** Spec 5.5 says a penalty applies to a late-cancelling customer but never names it. The code currently assumes 100% of the deposit, consistent with "deposit is forfeited" on a no-show. Confirm or replace.
2. **Dealer non-delivery tier.** Spec 2.2 leaves 25%-50% open. The range travels with each booking and an Admin picks inside it; confirm whether that stands or a flat rate is preferred.
3. **Held deposit with no ticket — decided.** The owner decided on 2026-09-26 that a deposit goes back when the booking's dispute window closes CLEANLY (no ticket that was not withdrawn, no penalty against the customer; Phase 3), and on 2026-09-29 that a deposit held for a penalty against the customer with no ticket is KEPT as the penalty when the window closes, owed to the office less Khadra's commission (pre-launch item 164, closed by payments Phase 8). The customer's sentences for it are drafts awaiting the owner (item 208).
4. **Vehicle security deposit.** Does the damage deposit pass through the platform on card, or is it cash at handover? Card authorization holds typically lapse after about seven days, so holding one for a ten-day rental invites chargebacks. The model currently records it as cash on the handover record.
5. **Delivery fee ownership — decided (owner, 2026-09-06).** The fee is the dealership's own (`DeliverySettings.Fee`, set on `/dealer/delivery`), kept out of the deposit and the commission and collected in cash with the balance; there is no platform-wide figure. A booking freezes the fee it was made under.
6. **Quick-cancellation processing fee** (spec 2.3), **minimum renter age**, and the **international driving permit requirement** for foreign renters (spec 2.2), all still unset.

## Roadmap

Every context above is built, including Payments and both directions of Reviews, and the Flutter
customer app ships. What is left is not another context:

1. **A merchant account and one `IPaymentProvider` adapter.** The single thing standing between an
   approved booking and a confirmed one (pre-launch item 76).
2. **The owner's three open answers**: the cancellation-refund rule (item 77), the review window
   length (item 80) and the dealer non-delivery tier. (The held deposit with no ticket was answered on
   2026-09-29 and built in payments Phase 8.)
3. **A way to moderate a review** (item 81). `Hide` exists on the aggregate, every reader honours it,
   and nothing calls it — which matters more now that a rating follows a person.
4. **An outbox for cross-context events.** Domain events dispatch after commit with nothing to
   replay them, which is why the Payments seam is a call and not an event.
5. Token pruning, MFA for Admin, push notifications (item 73), and request localisation on the API so
   a refusal reaches a client in the reader's language rather than in English.
