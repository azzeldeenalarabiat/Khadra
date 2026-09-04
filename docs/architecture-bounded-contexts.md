# Khadra — Architecture and Bounded Contexts

Khadra is a modular monolith built with Clean Architecture and DDD building blocks. Bounded contexts are folders in `Khadra.Domain` and `Khadra.Application`; they share one process, one `KhadraDbContext` and one PostgreSQL database, but never reference each other's aggregates directly. A context can be extracted into its own service later without changing its public contracts.

## Status

| Context | Domain model | Persistence | Use cases and endpoints |
|---|---|---|---|
| Identity & Access | done | done | auth done; dashboard read model done |
| Auditing | done | done | read model done; every dealer review decision writes an entry |
| Dealers | done | done | **registration + full review lifecycle** (approve / reject / clarify / resubmit / suspend / reactivate) |
| Fleet | done | pending | pending |
| Bookings | done | done | dashboard read model only |
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

`Id` (UUIDv7), `Entity`, `AggregateRoot` (domain events), `ValueObject`, `Enumeration` (smart enum), `Error` + `ErrorKind`, `Money` (three minor units for JOD fils, no cross-currency arithmetic), `Percentage`, `GeoPoint` (haversine distance), `DateRange` (half-open, whole days rounded up), `ISoftDeletable`, `IUnitOfWork`, `DomainException`, `ConcurrencyConflictException`.

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

**Lifecycle.** `PendingPayment → Requested → Approved → PickedUp → Returned → Completed`, with terminal exits `Rejected`, `Cancelled`, `NoShow` and `Expired`. Every state has an exit:

- `PendingPayment` expires after the payment window, releasing the held vehicle.
- `Requested` expires at the rental start if the dealer never answered, refunding in full.
- `Returned` completes when the post-return settlement window passes with no open dispute, or immediately once a dispute is resolved.

**Terms are frozen at booking time.** `BookingTerms` snapshots the deposit and commission percentages, the free-cancellation window, the no-show timeout and the penalty range as they stood when the booking was made. Rules are admin-editable, so judging a cancellation against today's settings would retroactively penalise customers and make past decisions unreproducible.

**Pricing is frozen too.** `BookingPricing` snapshots the daily rate, the security deposit, the mileage policy and the fuel policy. A dealer raising a rate or tightening a mileage cap cannot rewrite a contract already accepted. The deposit and commission are taken on `RentalTotal`, deliberately excluding the delivery fee, which is a pass-through for the driver's trip rather than rental revenue.

**Penalties are assessed, never charged.** Spec 3.3 and 5.5 make "no ticket, no penalty" the default, so `Cancel`, `ReportDealerNonDelivery` and `MarkNoShow` record a `PenaltyAssessment` and stop. Money moves only when an Admin resolves a dispute. Attribution is honest: a self-pickup no-show is attributed to the customer, but a **delivery** no-show is `Unattributed`, because the dealer was the party who had to travel. The free-cancellation window is capped at the period start so a late approval cannot grant free cancellation after pickup was due.

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
