# Pre-launch checklist

Things that are deliberately deferred while Khadra is a development system, and that **must** be
closed before it holds real users, real bookings or real money. Each entry says what is wrong, why it
was acceptable to defer, and what closing it looks like.

Add to this list whenever you knowingly leave something for later. An item leaves this list only by
being fixed, not by being forgotten.

---

## Security & data integrity

### 1. The audit trail can be erased with TRUNCATE

**Status:** open · **Raised:** 2026-09-03 · **Owner decision:** deferred deliberately

`audit_entries` is protected by `khadra_audit_entries_are_append_only`, a `FOR EACH ROW BEFORE DELETE
OR UPDATE` trigger, plus a matching guard in `KhadraDbContext.GuardAuditTrailIsAppendOnly`. Both
refuse `UPDATE` and `DELETE`.

Neither stops `TRUNCATE`: row-level triggers do not fire on it. Anyone with table privileges can
erase the entire audit trail in one statement, which is exactly the record that exists to survive a
dispute about what an administrator did.

This is currently acceptable only because the database is reseeded regularly and holds no real audit
history — the reseed itself relies on being able to truncate.

**To close:** add a `FOR EACH STATEMENT ... ON TRUNCATE` trigger in a migration, and give the
development seeder a documented way past it (drop and recreate around the reseed, or seed into a
fresh database). Verify by attempting a `TRUNCATE audit_entries` and expecting failure.

---

## Money

### 2. Dispute resolutions are recorded but never executed

**Status:** open by design · **Raised:** 2026-09-03 · **Owner confirmed:** no real money moves yet

An Admin resolving a dispute records a `DepositDisposition` — a three-way split of the deposit that
must balance — and an optional `DealerCharge`. Nothing acts on it: the Payments context is not built,
no deposit is ever captured, and `DepositPaymentId` on a seeded booking is a placeholder.

The owner confirmed on 2026-09-03 that **no deposits will be taken out of band** (bank transfer, cash
or otherwise) before Payments ships, so a recorded resolution has no counterparty money behind it and
creates no liability. The console labels every resolution "Decision recorded — no funds moved".

**To close:** build Payments so it reads a resolution by ticket id and settles it. `DepositHeld` is
stored on the disposition specifically so Payments can verify the split without re-deriving the
basis.

**Re-check immediately if** the go-live plan changes to taking deposits before Payments exists. Every
resolution then becomes an instruction to a human being and needs execution tracking, which is a
different design.

---

## Data model

### 3. Reviews have no persistence

**Status:** open · **Raised:** 2026-09-03

The `Review` aggregate exists in `Khadra.Domain/Reviews/` but has no `DbSet`, no EF configuration, no
table and no data. Spec 4.1 says a dealer's rating is computed from customer reviews and is never
editable by the dealer, so until this exists there is no rating to show. The Admin dealers list says
"No reviews yet" rather than inventing a number.

**To close:** build the Reviews context (configuration, migration, repository, the read model that
computes a dealer's average) and replace the placeholder.

### 4. No settlement job

**Status:** open · **Raised:** 2026-09-03

`Booking.Settle` closes a Returned booking once its frozen `PostReturnSettlementWindow` elapses with
no dispute, and `IBookingRepository.ListDueForSettlementAsync` exists to feed it. Nothing calls
either. Returned bookings past their window therefore sit visibly un-Completed.

The rule is still enforced correctly wherever it is asked — the gap is that nobody asks on a timer.
The same is true of the payment-expiry and no-show jobs.

**To close:** a hosted background service driving the four `ListDueFor*` queries.

---

## Dealer console (2026-09-03)

### 5. Seeded vehicle photos are flat placeholders

**Status:** open · **Raised:** 2026-09-03

The development seeder writes a plain grey JPEG at every seeded car's image key so listings load
without broken images. Before any demo to a real dealer, replace them with real photos or accept that
seeded fleets look like grey tiles. The console hides an image that fails to load (`khFallback`) and
shows the car icon instead, so a missing file is never a broken-image glyph.

### 6. Listing edits are not logged

**Status:** open · **Raised:** 2026-09-03

The vehicle page's Activity tab shows booking changes on that car (from booking history) plus "Vehicle
added". Price changes, photo changes and delivery-eligibility changes are not recorded anywhere. The
design shows them; the tab says they are not logged yet.

**To close:** raise domain events from `Vehicle` behaviours and project them into a per-vehicle log,
or extend the audit trail to dealer-side edits.

### 7. Screens the design has and the platform does not

**Status:** open by design · **Raised:** 2026-09-03

Reviews, a notification feed, two-factor sign-in, notification preferences, bank details for payouts,
pausing or closing a dealership, a map picker for the dealer location, a vehicle-type lookup, and
per-day blocked dates. Each screen says plainly that the feature is not live and what exists in its
place (the dashboard for attention items, "take off the road" for blocking dates, coordinates typed
by hand for the location). None of them shows invented data.

### 8. Reseeding truncates the database by hand

**Status:** open · **Raised:** 2026-09-03

The seeder only runs on an empty `dealers` table, so applying seeder changes (the 2026-09-03 change
that names the real dealer owner as the actor on seeded approvals, pickups and returns, and writes the
placeholder photos) needs every table except `__EFMigrationsHistory` truncated first, then an API
restart. That is a manual, destructive step with no guard — see item 1.

### 9. Staff invitation reveals whether an email or phone already has an account

**Status:** open · **Raised:** 2026-09-03 (Fable advisor review)

`EmployeeAccountProvisioner` tells an approved dealer owner whether the email or phone they invite is
already a Khadra account, customers included. Fine for development; before launch, return a neutral
"invitation sent" for a taken identity (and email the existing account instead) and rate-limit
invitations per owner.

### 10. Delivery radius ceiling is a constant

**Status:** open · **Raised:** 2026-09-03

`DeliverySettings.MaxRadiusKm = 200` is a business number in code, mirrored in the API request
validation and now surfaced to the console through `GET /dealers/me/delivery`. Move it into
`BusinessRules` the next time delivery rules are touched.

### 11. Admin screens the platform still cannot answer

**Status:** open by design · **Raised:** 2026-09-03 · **Reduced:** 2026-09-04

The Admin Console design draws more screens than the platform can answer. Bookings, customers, admin
users, security, platform settings, cities, car types and the audit log have since been built on
real data. Four remain, each blocked on something that does not exist rather than on effort:

- **Finance, Payments, Payouts.** The Payments context is NOT built and is blocked on owner
  decisions (provider, the non-delivery penalty tier, the quick-cancellation fee). No money has ever
  moved through the platform. CLAUDE.md forbids touching it without explicit owner approval.
- **Reviews.** The `Review` aggregate is written but has no table, and nothing in this repository can
  create one — reviews come from the customer app, which is not here. A moderation screen would list
  an empty table for ever, and its actions could never be tested end to end.
- **Notifications.** No notification context exists at all. Email is the only channel the platform
  sends on.

**Also removed with the fixtures:** the topbar's platform-wide search box, its reporting-period
button and its notification count, none of which were connected to anything. Restore them with the
features, not before.

**To close:** each screen leaves this list by getting a real reader, endpoint and component.

### 12. Nothing stops two bookings holding the same car on the same dates

**Status:** open · **Raised:** 2026-09-03 (found by end-to-end testing)

`IBookingRepository.HasOverlappingBookingAsync` is implemented and covered by repository tests, and
its own comment says "a database exclusion constraint backs this up, because a check-then-act in
application code loses the race between two customers booking the same car for the same dates."

**There is no such constraint.** No migration creates one, `btree_gist` is not enabled, and
`pg_constraint` holds no exclusion constraint on `bookings`. The seeded database currently contains
59 pairs of Approved/PickedUp bookings that overlap on one vehicle, which is how this was found.

It is not causing harm yet only because nothing creates bookings outside the seeder: the customer
booking flow is not built. The danger is precisely that the next person to build it will read that
comment, trust the database, and ship a race that double-books cars.

**To close:** enable `btree_gist`, add an exclusion constraint over `(vehicle_id WITH =,
tstzrange(period_start, period_end) WITH &&)` limited to the statuses where `HoldsVehicle` is true,
and call the guard in the booking-creation handler for a friendly error before the constraint fires.
The seeder must stop generating overlaps first, or the migration will not apply to an existing
development database.

## Admin console static data (2026-09-04)

### 13. The console and the API each hold their own SLA warning threshold

**Status:** open · **Raised:** 2026-09-04 (found while removing static data from the admin screens)

The dealer list and the dealer review screen call an application "at risk" from
`Khadra.Dashboard/src/app/core/services/sla.ts`, which uses 0.75 of the window the application froze.
The dashboard makes the same judgement server-side in `AttentionQueueBuilder`, from
`AdminDashboard:SlaWarningThreshold`, which is also 0.75.

Two copies of one number. They agree today because both say 0.75; nothing keeps them in step, so the
sidebar's queue and the dealer list can start disagreeing about which applications need attention
without anything failing.

This is already better than what it replaced — the console had a literal `hours < 12`, which was
0.75 of a 48-hour SLA and would have gone on meaning 12 hours after the SLA changed — but it is not
finished.

**To close:** send the severity from the API, as `AttentionItemDto` already does: a `ReviewSeverity`
(or an `IsAtRisk` flag) on `DealerListItem` and `DealerReviewDto`, computed from the frozen window
and the configured threshold. Then delete `sla.ts`.

### 14. A signed document link is not bound to the admin who minted it

**Status:** open · **Raised:** 2026-09-04

`GetDealerForReviewQuery` says a signed link "cannot outlive the session that legitimately produced
it", and the review screen tells the admin the links are "admin only". Neither is quite true.
`HmacDocumentLinkSigner` binds the signature to `storageKey|expires` and nothing else, and
`DocumentsController` carries no policy attribute, so it inherits the authenticated-only fallback.
Within its lifetime a minted link works for **any** signed-in session, including a customer's.

The exposure is small — the link is short-lived and never leaves the admin's browser — but the claim
on screen is wider than the check behind it, and licence documents are spec 7 private data.

**To close:** bind the signature to the minting actor (`Sign(storageKey, actorUserId, now)`, verified
against `ICurrentActor` on the way back in), and put an admin policy on the endpoints that serve
dealer and customer documents.

### 15. An upload ticket can be replayed and will overwrite the file it names

**Status:** open · **Raised:** 2026-09-04

`LocalDocumentStorage.SaveAtAsync` carries the comment "Create rather than overwrite: a ticket is
meant to be spent once, and silently replacing an existing file would make a replayed ticket look
successful" — and then opens the file with `FileMode.Create`, which overwrites. `SaveAsync`, on the
path where the key is generated fresh, correctly uses `FileMode.CreateNew`.

`HmacUploadTicketService` is stateless, so a ticket is genuinely replayable inside its lifetime, and
a replay does exactly what the comment says must not happen.

Harmless today because every key in play carries a fresh GUID. It stops being harmless the moment a
key is predictable or a ticket names a key twice.

**To close:** use `FileMode.CreateNew` and let the second write fail, or record spent tickets.

### 16. The dispute console rounds money to two decimals; JOD has three

**Status:** open · **Raised:** 2026-09-04

`Money` persists at `(18,3)` and rounds to three places — JOD's minor unit is the fils, a thousandth.
The dispute resolution screen's `round()` works at two. A deposit with a non-zero third decimal
cannot be split into three legs that add up to it, so the Resolve button stays disabled with no
explanation the admin can act on.

Latent: every seeded amount comes from integer rates and a 20% deposit, so no such figure exists yet.
It becomes reachable as soon as Payments produces real amounts.

**To close:** round to three places in the console, or have the API state the currency's minor units
and follow it.

### 17. "Account settings" in the sidebar is wired to nothing

**Status:** CLOSED · **Raised:** 2026-09-04 · **Closed:** 2026-09-04

The button was removed when it was found, because a control that does nothing costs an administrator
more than a missing one does. The screen behind it now exists: `/security` shows the signed-in
administrator's own account, their sessions and a password change.

Deliberately their OWN account only. Reading another person's devices, addresses and sign-in times is
surveillance rather than administration, and there is no endpoint for it.

## Audit log (2026-09-04)

### 18. A customer erasure request will meet an append-only table

**Status:** open · **Raised:** 2026-09-04 (while building the audit-log screen)

`audit_entries` cannot be updated or deleted — a database trigger and a `SaveChanges` guard both
refuse — and that is the point of it. But `subject_label` is a free-text snapshot of what an action
was taken on, and a customer has the right to have their personal data erased. If a handler ever
writes a customer's name, phone or email into that column, the platform has put personal data
somewhere it has promised never to remove.

No handler writes a `Customer` audit entry yet, so the rule can be established before the first one
exists rather than discovered afterwards: **for `Customer` entries, `subject_label` is a reference,
never an identity** — the seeded "Customer #882" is the right shape. The same goes for
`previous_value` and `new_value`.

**To close:** state the rule on `AuditEntry` where the other snapshotting decisions are recorded, and
hold it in review when the first customer-facing admin action ships.

### 19. Renaming an AuditAction or UserRole would make historical entries unreadable

**Status:** open · **Raised:** 2026-09-04

Both are persisted by NAME and materialised through `Enumeration.FromName`, which throws for a name
that no longer exists. Rename or remove one member of `AuditAction`, `AuditEntityType` or `UserRole`
and every historical row carrying it becomes unreadable — the list endpoint fails outright, and the
record is lost even though the data is still in the table.

These enumerations are effectively **add-only** once a row references them. Renaming one is a data
migration, not a refactor.

**To close:** a domain test pinning the exact member names, and a comment on each enumeration saying
why they cannot be renamed.

### 20. The audit log has no export

**Status:** open by design · **Raised:** 2026-09-04

An auditor asked for "everything about this dealer for the year" can filter to it on screen but
cannot take it away. Not built because nobody has asked yet, and the shape is already there when
they do: `GET /admin/audit-logs/export` streaming `text/csv` over the same `AuditLogFilter`, via
`IAsyncEnumerable` so a large range does not materialise in memory.

### 21. The dealer console still reads `Resource.value()` unguarded

**Status:** open · **Raised:** 2026-09-04

`httpResource.value()` **throws** while a resource is in an error state; it does not return
undefined, so `value() ?? null` is not a guard. Reading it outside the branch that already proved the
request succeeded takes change detection down with it.

Observed on the Admin side and fixed there: a 502 on `GET /admin/workload` made the sidebar's
`badge()` throw, and because the sidebar wraps every admin screen, the console froze on its loading
skeleton — no error, no Retry — while each screen's own "couldn't load this" block sat unrendered
behind it. `core/services/loaded.ts` now stands between every admin screen and its resource.

The dealer console has the identical pattern and has not been changed, because it was outside the
pass that found this and nothing here has been driven end to end since. `dealer-gate.component.ts`
(`locked()`, lines 65 and 74) is the dangerous one: it wraps every dealer screen exactly as the
sidebar wraps every admin one, so a failed `GET /dealers/me` freezes the whole dealer console. The
effects in `car-form.component.ts` and `vehicle-wizard.component.ts` fail the same way for a vehicle
that 404s, as `dispute-detail` did before the fix. `dealer-bookings`, `dealer-delivery`,
`dealer-profile`, `dealer-reports`, `dealer-activity`, `dealer-employees` and `booking-detail` all
read `value()` unguarded too, and are protected only by the order of their template branches.

**To close:** route every root derivation in `features/dealer/` and `features/fleet/` through
`loaded()`, then drive the dealer console end to end with the API stopped and confirm each screen
shows its own failure and a Retry rather than a skeleton.

### 22. A dealer suspension is missing from the application timeline

**Status:** open by design · **Raised:** 2026-09-04

`GetDealerForReviewQuery.BuildTimeline` derives the trail from the aggregate, and `Dealer` records
`SuspensionReason` but no `SuspendedAt` — so a suspension has no instant to sit on and does not
appear. The reason itself is now shown under "Decision on record", and who suspended the dealership
and when is one click away in the audit log, which the screen links to filtered to that dealer.

Not fixed by adding `SuspendedAt`: one column cannot represent suspend → reactivate → suspend again,
so it would be a partial mirror of what the audit trail already holds completely, and `BuildTimeline`
exists precisely to avoid a second source of truth.

**To close:** if the trail is wanted on the screen, read it from `IAuditLogReader` for
`(Dealer, dealerId)` in the query handler. Not a column.

### 23. The seeded audit entries are not derived from the aggregates they describe

**Status:** open · **Raised:** 2026-09-04

`DevelopmentSeeder` writes the aggregates and the audit rows from separate literals, so the two
disagree: entries name actors and instants that the tickets and dealerships they refer to do not
have. It did not matter while the audit log was a glance on the dashboard; it does now that the
dealer review and dispute screens deep-link the log filtered to one record, because the two views of
the same event are read side by side.

Development data only — no production impact.

**To close:** seed each audit entry from the aggregate it describes, taking the actor, the instant
and the before/after states from the object rather than restating them.

## Admin console completion (2026-09-04)

### 24. Revoking a session is not immediate, and the console says so

**Status:** open by design · **Raised:** 2026-09-04

Revoking a refresh-token family stops it being refreshed but does not rotate the security stamp, so
an access token already issued keeps working until it expires — up to `Authentication:Jwt:
AccessTokenMinutes`. The security screen states that figure, read from the server, rather than
promising "signed out immediately" as the design does.

Two things follow from the same gap. There is no `sid` claim on the access token, so the API cannot
tell which session is making the current request: the screen cannot mark "this device", and cannot
offer "revoke all others" without also killing the caller's own session on its next refresh.

**To close:** add the refresh family id as a `sid` claim in `JwtAccessTokenIssuer`, expose it on
`ICurrentActor`, then flag the current row and add "revoke all others". If the owner wants immediate
revocation, add an `AnyAsync(FamilyId == sid && RevokedAt == null)` check beside the security-stamp
check in `OnTokenValidated` — that is a per-request query, so it is a deliberate trade, not a tidy-up.

### 25. Platform settings are read-only, and cannot be made editable yet

**Status:** open · **Raised:** 2026-09-04

`/settings` serves the business numbers from `IBusinessRulesProvider` (configuration) and says so on
the screen. It cannot become editable as it stands, for two independent reasons:

- `BusinessRules` and the `BusinessRuleSettings` aggregate do not carry the same fields.
  `CustomerCancellationPenaltyPercent`, `PaymentWindowMinutes` and `PostReturnSettlementHours` exist
  on the record the platform reads and not on the aggregate meant to replace it. `BookingTerms
  .RulesVersion` is also hardcoded to 1 everywhere it is written.
- Two of the values are open owner decisions (spec 2.2): the dealer non-delivery penalty tier and the
  customer quick-cancellation fee. An editable form would let an administrator settle a business
  question by typing into a box.

**To close:** reconcile the aggregate with the provider record, decide what `RulesVersion` increments
on, and get the two figures settled. A booking freezes its own terms, so editing them later can never
rewrite a past booking — that part is already safe.

### 26. Cities have no consumer yet

**Status:** open · **Raised:** 2026-09-04

`/cities` curates the list and `GET /api/v1/cities` serves it, but nothing reads it. Dealer locations
are coordinates and distance is measured from them, so the platform works without it; a city cannot
yet be attached to a dealership or used to filter a search.

**To close:** add `CityId` to `UpdateDealerProfileCommand` and a select to the dealer profile, then
let the customer search filter by it.

### 27. Admins cannot view a customer's identity documents

**Status:** open by decision · **Raised:** 2026-09-04

The customer profile lists what is on file — type, state, format, size, when — and mints no signed
URL. `CustomerDocument` scopes viewing to the customer themselves and to a dealer with an active
booking request; an administrator is not named there, spec 7 keeps these private, and spec 5.1's
review mechanism has not been decided. `MarkVerified` and `MarkRejected` are `internal` with no
public path, so nothing can move a document out of `PendingReview` today.

Deliberately conservative: minting links for passports and national IDs widens item 14's exposure and
is the owner's call, not a convenience to add quietly. Cheap to add once decided.

**To close:** the owner decides whether an Admin may open these, and whether admin review is how a
document becomes verified. Then a signed link and the two review actions.

### 28. Seeded vehicles in an already-seeded database point at a car type that is not there

**Status:** open · **Raised:** 2026-09-04 · Development data only

`car_types` and `cities` ship empty; the seeder now writes one car type at the id every seeded
vehicle carries, so a FRESH database is self-consistent. A database seeded before that migration has
71 vehicles pointing at an id with no row, and the seeder will not run again.

No production impact, and no data is wrong — the fleet screens read the type by correlated lookup and
show nothing rather than inventing a name.

**To close:** reseed a development database, or add the row through the Car Types screen at that id.

### 29. Vehicle forms still send a hardcoded car type id

**Status:** open · **Raised:** 2026-09-04

`car-form.component.ts` and `vehicle-wizard.component.ts` post a literal
`01a06675-0000-7000-8000-000000000001` for every car a dealer lists. That was the placeholder for a
table that did not exist; the table exists now, and `GET /api/v1/car-types` serves it to any signed-in
caller.

Left alone because these are dealer-console screens, outside the admin pass that built the lookup,
and changing a form nobody has re-driven end to end is how a working screen breaks quietly.

**To close:** both forms fetch the list and offer a select; `VehicleHandlers` rejects a CarTypeId that
is not an active car type.
