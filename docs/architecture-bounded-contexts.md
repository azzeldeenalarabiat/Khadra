# Khadra — Architecture and Bounded Contexts

Khadra is a modular monolith built with Clean Architecture and DDD building blocks. Bounded contexts are folders in `Khadra.Domain` and `Khadra.Application`; they share one process, one `KhadraDbContext` and one PostgreSQL database, but never reference each other's aggregates directly. A context can be extracted into its own service later without changing its public contracts.

## Context map

```text
Identity & Access ──supplies actor (UserId, role, verified)──▶ every context
Dealers ──(DealerId, delivery settings, approval status)──▶ Fleet, Booking
Fleet ──(VehicleId, rate, deposit, delivery-eligible)──▶ Booking
Booking ──events (Approved, Cancelled, NoShow, Completed)──▶ Payments, Disputes, Reviews, Notifications
Payments ──(deposit paid fact)──▶ Booking ; ──commission/payout──▶ Reporting
Disputes ──resolution──▶ Payments (penalty / refund), Booking (record)
Platform Settings ──IBusinessRulesProvider──▶ Booking, Payments (never constants)
```

Communication rules: by `Id`, by explicit application contracts, or by domain events. A context never mutates another context's aggregate.

## Shared kernel (`Khadra.Domain/Common`)

`Id` (UUIDv7), `Entity`, `AggregateRoot` (domain events), `ValueObject`, `Enumeration` (smart enum), `Error` + `ErrorKind` (the single error currency: `Code`, `Message`, `Kind`, optional field `Details`), `Money` (3 minor units, no cross-currency math), `GeoPoint` (haversine distance), `DateRange` (half-open, whole-day rounding), `ISoftDeletable`, `IUnitOfWork`, `DomainException`, `ConcurrencyConflictException`.

## 1. Identity & Access — implemented

| Aggregate | Table | Purpose |
|---|---|---|
| `User` | `users` | credentials, role (`Admin`, `DealerOwner`, `DealerEmployee`, `Customer`), status (`Active`, `Suspended`), email verification, `MustChangePassword`, `SecurityStamp`, soft delete |
| `RefreshToken` | `refresh_tokens` | single-use refresh tokens grouped in families; rotation, replay detection, absolute family deadline; xmin concurrency token |
| `VerificationToken` | `verification_tokens` | single-use email-verification / password-reset links (hash only) |

Invariants: normalised unique email and E.164 phone; login requires verified email and active status; suspension/password change/deletion rotate the security stamp and revoke every refresh family (domain events handled after commit); deleted accounts behave like bad credentials.

Factories reserved for later contexts: `User.RegisterDealerOwner`, `User.CreateEmployee` (temporary password, verified), `User.CreateAdmin`.

## 2. Dealers — designed

`Dealer` aggregate: `OwnerUserId`, `BusinessName`, `Description`, `CommercialRegistrationNumber`, `Location: GeoPoint`, `OperatingHours` (VO), `VerificationStatus` {PendingReview, Approved, Rejected, ClarificationNeeded} + reason/note, `DeliverySettings` (Enabled, RadiusKm), logo/cover keys, private `DealerDocument` entities (commercial registration, vehicle registration, owner ID), `Employee` child entities (`UserId`, `CanViewReports`, `IsActive`). Soft-deletable.
Invariants: only `Approved` dealers publish vehicles or receive bookings; approval transitions only from `PendingReview`/`ClarificationNeeded`; employee `UserId` unique per dealer; `CoversLocation(GeoPoint)` = delivery enabled and distance ≤ radius. Events: `DealerRegistrationSubmitted`, `DealerApproved`, `DealerRejected`, `DealerClarificationRequested`, `EmployeeAdded`, `EmployeeDeactivated`. 48-hour SLA reminder is a scheduled job.

## 3. Fleet — designed

`Vehicle` aggregate: `DealerId`, `CarTypeId`, make/model/year, `PlateNumber` (green plate), `DailyRate: Money`, `SecurityDepositAmount: Money`, `IsDeliveryEligible`, `MileagePolicy`, `FuelPolicy`, `Status` {Active, Hidden}, images. Availability is derived from Booking, never stored on the vehicle.

## 4. Booking — designed (core)

`Booking` aggregate: `CustomerId`, `DealerId`, `VehicleId`, `Period: DateRange`, `PickupMethod` {SelfPickup, Delivery}, `DeliveryLocation`, `Pricing` snapshot (daily rate, days, rental total, delivery fee, total, deposit rate, deposit amount, balance due), `PaymentOption` {DepositOnly, FullUpfront}, `Status` state machine `PendingPayment → Requested → Approved|Rejected → PickedUp → Returned → Completed`, plus `Cancelled` (from Requested/Approved) and `NoShow` (system, after the no-show timeout), `ActedByUserId`, `HandoverRecord` entities (optional photos, mileage, fuel).
Policies fed by `IBusinessRulesProvider`: `PricingPolicy` (deposit %, delivery fee), `CancellationPolicy` (free within the window after approval, penalty to the canceller afterwards), `NoShowPolicy`, `DealerNonDeliveryPenaltyPolicy` (25–50%, tier open).
Invariants: delivery only inside the dealer radius; no overlapping active booking per vehicle (DB exclusion constraint + handler check); only the dealer's owner/employees approve; handover photos are a neutral record, never arbitration input.

## 5. Payments — designed (owner approval required before coding)

`Payment` (deposit / full / refund / penalty / processing fee; provider reference; status), `Commission` (rate applied, amount, dealer payout, payout status), `SecurityDeposit` (Held / Released / Claimed). Commission is deducted from the always-card deposit (spec §2.1). Card data never touches the API.

## 6. Disputes — designed

`DisputeTicket`: `BookingId`, opened by (party + user), reason, evidence, `Status` {Open, Resolved}, `Resolution` {ApplyPenalty, WaivePenalty, PartialPenalty, RefundDeposit} + amount, resolver, SLA deadline (48h). No penalty is ever auto-applied without a ticket.

## 7. Reviews — designed

`Review`: `BookingId`, reviewer, direction (customer→dealer, dealer→customer), rating 1–5, comment. One per completed booking per direction; dealer rating is a read model.

## 8. Platform Settings & Lookups — designed

`BusinessRuleSettings` (single versioned row, admin-editable): commission %, deposit %, no-show hours, delivery fee, penalty range, free-cancellation window, processing fee, SLA hours, minimum renter age, IDP requirement. Lookups: `CarType`, `City`. Today these values are the `BusinessRules` configuration section exposed through `IBusinessRulesProvider`; the aggregate replaces the source without touching consumers.

## Roadmap

1. Dealers (registration + documents + approval + employees) · 2. Fleet · 3. Booking + no-show scheduler · 4. Payments (gateway decision) · 5. Disputes · 6. Reviews · 7. Platform Settings + Admin console · then outbox for cross-context events, token pruning jobs, MFA for Admin, Flutter app.
