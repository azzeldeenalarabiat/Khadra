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
| Reviews | done | pending | pending |
| Platform Settings | done | pending (configuration-backed) | pending |
| Payments | **not started, blocked** | — | — |

"Dashboard read model only" means the tables and the read-side queries behind the `GET /api/v1/admin/dashboard/*` panel endpoints exist, but no command handlers do: nothing yet approves a dealer or resolves a dispute through the API.

Payments is deliberately unbuilt. It needs owner decisions and explicit approval (see "Owner decisions required" below and the forbidden-actions list in `CLAUDE.md`).

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

`Review` with a `Rating` value object. One per booking per direction, only on a completed booking. The customer's review of the dealer is public and feeds the dealer's rating; the dealer's review of the customer is visible to other dealers to inform approve/reject decisions (spec 5.6). Moderation hides the text but keeps the score, so a dealer cannot erase a bad rating by reporting it. Ratings are aggregated in SQL as read models.

## 7. Platform Settings

`BusinessRuleSettings` is a single versioned aggregate holding every number from spec section 2, and it refuses a commission above the deposit, because commission is collected from the card deposit. `PendingOwnerDecisions()` surfaces the questions the owner has not answered rather than pretending a default is a decision. `CarType` and `City` are bilingual lookups that deactivate rather than delete.

## 8. Payments — not built

Designed shape: `Payment`, `Commission`, `SecurityDeposit`. Blocked on the owner decisions below, and on explicit approval per `CLAUDE.md`.

The central difficulty: at the confirmed 20% commission and 20% deposit the two are equal, so the platform never pays a dealer and never holds dealer funds. Three things in the spec nonetheless require money to move in a direction that has no rail:

1. A dealer non-delivery penalty of 25-50% has nothing to deduct from.
2. `PaymentOption.FullUpfront` means the customer pays 100% by card, so the dealer's 80% must be paid out.
3. Any configured commission above the deposit produces a shortfall to collect.

The likely answer is a single `DealerLedger` aggregate with typed entries and manual settlement, rather than separate payout and penalty mechanisms.

## Owner decisions required

These change field shapes, so they are worth settling before the affected context is built.

1. **Customer cancellation penalty.** Spec 5.5 says a penalty applies to a late-cancelling customer but never names it. The code currently assumes 100% of the deposit, consistent with "deposit is forfeited" on a no-show. Confirm or replace.
2. **Dealer non-delivery tier.** Spec 2.2 leaves 25%-50% open. The range travels with each booking and an Admin picks inside it; confirm whether that stands or a flat rate is preferred.
3. **Held deposit with no ticket.** When a cancellation or no-show passes with nobody opening a ticket, is the held deposit refunded or retained? "No penalty is auto-applied" reads as refund, which contradicts "deposit is forfeited". This one is genuinely ambiguous in the spec.
4. **Vehicle security deposit.** Does the damage deposit pass through the platform on card, or is it cash at handover? Card authorization holds typically lapse after about seven days, so holding one for a ten-day rental invites chargebacks. The model currently records it as cash on the handover record.
5. **Delivery fee ownership.** Does the dealer keep the 10 JOD, or the platform?
6. **Quick-cancellation processing fee** (spec 2.3), **minimum renter age**, and the **international driving permit requirement** for foreign renters (spec 2.2), all still unset.

## Roadmap

Persistence and use cases for Dealers, then Fleet, then Bookings including the expiry and no-show background jobs, then Disputes and Reviews, then Payments once approved. After that: an outbox for cross-context events, token pruning, MFA for Admin, and the Flutter customer app.
