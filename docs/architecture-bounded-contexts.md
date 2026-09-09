# Khadra — Architecture and Bounded Contexts

Khadra is a modular monolith built with Clean Architecture and DDD building blocks. Bounded contexts are folders in `Khadra.Domain` and `Khadra.Application`; they share one process, one `KhadraDbContext` and one PostgreSQL database, but never reference each other's aggregates directly. A context can be extracted into its own service later without changing its public contracts.

## Status

| Context | Domain model | Persistence | Use cases and endpoints |
|---|---|---|---|
| Identity & Access | done | done | auth done; dashboard read model done |
| Auditing | done | done | read model done; every dealer review decision writes an entry |
| Dealers | done | done | **registration + full review lifecycle** (approve / reject / clarify / resubmit / suspend / reactivate) |
| Fleet | done | done | dealer fleet management done; **customer catalogue done** (search, listing, gallery page) |
| Bookings | done | done | dealer decisions and handover done; **quote done**; creation NOT built |
| Disputes | done | done | dashboard read model only |
| Reviews | done | done | **both directions done**; customer reputation read model done |
| Platform Settings | done | pending (configuration-backed) | pending |
| Payments | done | done | **deposit checkout + provider webhook done**; NO PROVIDER CONFIGURED |

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
Platform Settings ──IBusinessRulesProvider──▶ Bookings (frozen onto each booking as BookingTerms)
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

**Three bounds on a rental**, all configured. Two are about when it may START, judged together in `BookingWindowPolicy` so search, quote and create agree: `BusinessRules:MinimumBookingLeadTimeMinutes` (120) and `MaxAdvanceBookingDays` (180). Neither converts through `IReportingCalendar` — "two hours from now" and "180 days from now" are elapsed time, which has no time zone. The third is how LONG it may run, `MaxRentalDays` (90), and it sits in `BookingPricer` instead, because it is counted in Amman calendar days and those only exist once the period has been through the calendar. Judging it on the count the pricing produced also means a customer is refused on the same number they were quoted.

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

`Payment` is ONE CHECKOUT ATTEMPT for one booking's deposit, with `Refund` as a child entity, plus
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
booking settlement pass. Two triggers exist: an orphaned capture (automatic, in the capture's own
transaction) and an admin's dispute resolution returning money to the customer. Cancellation refunds are
deliberately NOT wired — owner decision 3 is open, and `BookingDisputeSettlement.DepositHeldFor` assumes
the full deposit is still held while a booking is disputable.

**What is not built, and why.** No `DealerLedger`, no payout rail, no dealer charge: at the confirmed
20% commission and 20% deposit the two are equal, so the platform never pays a dealer and never holds
dealer funds. Of the three spec cases that would need a rail, two are closed by construction —
`CreateBookingHandler` only ever writes `PaymentOption.DepositOnly`, and both `BookingTerms.Create` and
`BusinessRuleSettings` refuse a commission above the deposit. What remains is the dealer non-delivery
penalty, which is already an instruction with no rail (`DisputeResolution.DealerCharge`) and is settled
by hand.

**Nobody may add a provider that simulates success.** `UnconfiguredPaymentProvider` is the only
implementation, and a stub that confirmed bookings without money would be indistinguishable, in every
table and on every screen, from a real payment. Tests substitute `IPaymentProvider` at the handler
boundary. Writing a real adapter is one class implementing four methods; nothing above it changes.

## Owner decisions required

These change field shapes, so they are worth settling before the affected context is built.

1. **Customer cancellation penalty.** Spec 5.5 says a penalty applies to a late-cancelling customer but never names it. The code currently assumes 100% of the deposit, consistent with "deposit is forfeited" on a no-show. Confirm or replace.
2. **Dealer non-delivery tier.** Spec 2.2 leaves 25%-50% open. The range travels with each booking and an Admin picks inside it; confirm whether that stands or a flat rate is preferred.
3. **Held deposit with no ticket.** When a cancellation or no-show passes with nobody opening a ticket, is the held deposit refunded or retained? "No penalty is auto-applied" reads as refund, which contradicts "deposit is forfeited". This one is genuinely ambiguous in the spec.
4. **Vehicle security deposit.** Does the damage deposit pass through the platform on card, or is it cash at handover? Card authorization holds typically lapse after about seven days, so holding one for a ten-day rental invites chargebacks. The model currently records it as cash on the handover record.
5. **Delivery fee ownership.** Does the dealer keep the 10 JOD, or the platform?
6. **Quick-cancellation processing fee** (spec 2.3), **minimum renter age**, and the **international driving permit requirement** for foreign renters (spec 2.2), all still unset.

## Roadmap

Every context above is built, including Payments and both directions of Reviews, and the Flutter
customer app ships. What is left is not another context:

1. **A merchant account and one `IPaymentProvider` adapter.** The single thing standing between an
   approved booking and a confirmed one (pre-launch item 76).
2. **The owner's four open answers**: the cancellation-refund rule (item 77), the review window
   length (item 80), the dealer non-delivery tier, and the held deposit with no ticket.
3. **A way to moderate a review** (item 81). `Hide` exists on the aggregate, every reader honours it,
   and nothing calls it — which matters more now that a rating follows a person.
4. **An outbox for cross-context events.** Domain events dispatch after commit with nothing to
   replay them, which is why the Payments seam is a call and not an event.
5. Token pruning, MFA for Admin, push notifications (item 73), and request localisation on the API so
   a refusal reaches a client in the reader's language rather than in English.
