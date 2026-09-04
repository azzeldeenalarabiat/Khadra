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

### 11. Admin screens without a backend

**Status:** open by design · **Raised:** 2026-09-03

The Admin Console design draws more screens than the platform can answer. Their sample data has been
removed, and each now states what it will show and what is missing: customers, payments, payouts,
finance, reviews, cities, car types, audit logs, admin users, platform settings, notifications and
security. Two of them are closer than the rest and worth doing first:

- **Audit logs.** The entries are real, append-only and already written in the same transaction as
  every dealer decision and dispute resolution. Only a read endpoint and a screen are missing, so
  this is the cheapest of the group and the one an auditor asks for first.
- **Platform settings.** The numbers are real and enforced, but they come from configuration through
  `IBusinessRulesProvider`. `BusinessRuleSettings` is designed to replace that source without
  touching a single consumer; a booking already freezes what it was made under, so making them
  editable cannot rewrite history.

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

**Status:** open · **Raised:** 2026-09-04

`admin-sidebar.component.html` renders an "Account settings" button with no click handler. The
topbar's own comment states the principle it breaks: "A control that does nothing when clicked costs
an administrator more than a missing one does" — which is why the search box, period picker and
notification count were removed rather than left inert.

**To close:** build the screen, or remove the button until there is one.
