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
The same is true of the payment-expiry, decision-expiry and no-show jobs.

The reordering of 2026-09-07 raised the stakes on two of them without changing this item. A request
and an unpaid approval both stop HOLDING the car at their own deadline, because the availability
predicate reads the clock — so no car is stranded by the missing job. What is missing is the status:
until something runs, a booking whose window closed still reads as Requested or Approved to both
parties, and neither is told it ended.

**To close:** a hosted background service driving the four `ListDueFor*` queries.

---

## Dealer console (2026-09-03)

### 5. Seeded vehicle photos are flat placeholders

**Status:** closed · **Closed:** 2026-09-05 — the development seeder was deleted, so nothing fabricates this data any more.

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

**Status:** closed · **Closed:** 2026-09-05 — the development seeder was deleted, so nothing fabricates this data any more.

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

**Status:** CLOSED 2026-09-07 · **Raised:** 2026-09-03 (found by end-to-end testing)

`IBookingRepository.HasOverlappingBookingAsync` is implemented and covered by repository tests, and
its own comment says "a database exclusion constraint backs this up, because a check-then-act in
application code loses the race between two customers booking the same car for the same dates."

**There is no such constraint.** No migration creates one, `btree_gist` is not enabled, and
`pg_constraint` holds no exclusion constraint on `bookings`. The seeded database currently contains
59 pairs of Approved/PickedUp bookings that overlap on one vehicle, which is how this was found.

It is not causing harm yet only because nothing creates bookings outside the seeder: the customer
booking flow is not built. The danger is precisely that the next person to build it will read that
comment, trust the database, and ship a race that double-books cars.

**Closed by** migration `20260907021642_CalendarDaysAndVehicleHolds`, which enables `btree_gist` and
adds `bookings_one_hold_per_vehicle`:

```sql
EXCLUDE USING gist (vehicle_id WITH =, tstzrange(hold_start, period_end, '[)') WITH &&)
  WHERE (status IN ('PendingPayment', 'Requested', 'Approved', 'PickedUp'))
```

The status list was rewritten to `('Requested', 'Approved', 'Confirmed', 'PickedUp')` by
`20260907121340_ReserveNowPayAfterApproval` when the lifecycle was reordered. Everything else about
the constraint is unchanged.

It excludes on `hold_start`, not `period_start`, so the gallery's turnaround gap is enforced by the
database too. The 59 overlapping pairs are gone with the seeder that made them: they lived in the
abandoned `khadra` database, while the API has been on `khadra_e2e` (0 bookings) since 2026-09-05.

The guard is still called first for a friendly error, and it now shares one predicate with the
catalogue's availability query (`BookingHolds`). Three caveats moved to items 51-53 rather than being
considered closed here: the constraint cannot be tested on SQLite, the create handler must expire
stale unpaid holds before inserting, and SQLSTATE 23P01 needs its own error code.

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

**Status:** closed · **Closed:** 2026-09-05 — the development seeder was deleted, so nothing fabricates this data any more.

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

**Status:** open by decision · **Raised:** 2026-09-04 · **See also:** item 63, which the owner has made a hard launch blocker

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

**Status:** closed · **Closed:** 2026-09-05 — the development seeder was deleted, so nothing fabricates this data any more.

`car_types` and `cities` ship empty; the seeder now writes one car type at the id every seeded
vehicle carries, so a FRESH database is self-consistent. A database seeded before that migration has
71 vehicles pointing at an id with no row, and the seeder will not run again.

No production impact, and no data is wrong — the fleet screens read the type by correlated lookup and
show nothing rather than inventing a name.

**To close:** reseed a development database, or add the row through the Car Types screen at that id.

### 29. Nothing checks that a car's type is a real car type

**Status:** CLOSED 2026-09-07 · **Raised:** 2026-09-04 · **Narrowed:** 2026-09-05

Both forms read `GET /api/v1/car-types` and offer a select, and neither will save without one, so the
hardcoded id went from the console first. The server-side half was still missing until now:
`VehicleHandlers` took whatever `CarTypeId` it was given, so a request made outside the console could
point a car at a type that does not exist. Confirmed live before the fix — a made-up id returned
**201 Created**, and the row sat in the database with `car_type_exists = 0`.

**Closed by** an existence check in `AddVehicle` and `UpdateVehicle` (`vehicle.unknown_car_type`,
`vehicle.car_type_retired`), with one asymmetry that matters: a type the dealer is CHANGING to must
be offered, but the one already on the car need only exist. A strict check both ways would mean that
retiring a category froze every car in it — the owner could not correct a price until they had
re-categorised, which is not a decision a price edit should force.

**The FK this item originally asked for was deliberately NOT added.** It would contradict
`.claude/rules/backend/architecture.md` ("Cross-context references by Id only. No navigation
properties or EF relationships across contexts"), and with the lookups append-only it buys nothing
the handler check does not already give. The rule stands; the check is the enforcement.

**Consequence still to handle:** a car whose type was retired after it was listed matches no
`<option>` in the edit form, so the browser shows the first one and the next save would silently
re-categorise it. The form needs to include the car's current type as an option, marked retired,
when editing. Tracked as part of item 49's screen work rather than left implicit here.

### 30. Map tiles come from OpenStreetMap's public servers

**Status:** open · **Raised:** 2026-09-05

The dealer profile and the add-vehicle wizard draw a real map (`kh-map`, Leaflet) with raster tiles
from `tile.openstreetmap.org`. That is free and needs no key, and the attribution the OSM Foundation
requires is rendered on every map — but their tile usage policy is for modest traffic and explicitly
not for production applications at scale. CARTO's dark basemap was tried first and rejected: an
unkeyed request there returns HTTP 200 with an "API KEY REQUIRED" watermark painted into the tile,
which is a broken map that looks like a working one.

There is also no address search. Geocoding is an XHR to a third party and the BFF sends
`connect-src 'self'`, so it needs a server-side proxy before the screen can offer it.

**To close:** a keyed tile provider or self-hosted tiles, with the key held server-side; and a
decision on whether address search is wanted.

---

## Real account creation (2026-09-05)

### 31. Registration is an anonymous oracle for both email and phone

**Status:** open · **Raised:** 2026-09-05

`POST /api/v1/auth/register` and `/register-dealer-owner` are anonymous, and both answer 409
`auth.email_taken` or `auth.phone_taken`. A registration form has to say the address is already in
use or the person cannot proceed, so this is a real trade-off rather than an oversight — but it
lets anyone enumerate who holds an account. The phone case is worse than the email one: Jordanian
mobile numbers are a small, dense space that can be walked exhaustively.

Same class as item 9 (staff invitation), and the two should be closed together.

**To close:** decide the policy once for every account-creating endpoint. The usual answer is to
answer 202 uniformly and move the conflict into an email ("someone tried to register with your
address"), which costs the applicant a round trip through their inbox.

### 32. Rate limits are partitioned by the BFF's address in production

**Status:** CLOSED 2026-09-06 · **Raised:** 2026-09-05

Closed, but the item had the failure mode backwards, and that is worth keeping: it read as an
availability risk to schedule, when it was a live authentication bypass.

An empty `KnownProxies` does not make everyone share one partition. ForwardedHeadersMiddleware only
verifies the sender when it has something to verify against —
`checkKnownIps = KnownNetworks.Count > 0 || KnownProxies.Count > 0` — so with both lists empty it
skips the check and applies `X-Forwarded-For` from ANY caller. The partition key was therefore
attacker-chosen, and every limit could be walked around by sending a fresh value per request.
Measured before the fix, straight at the API: 15 `POST /api/v1/auth/login` with a rotating header
were never limited, while 5 from the same connection with no header were all 429.

The BFF had the same defect and it was the more serious half, because it sits on the production
path. `Khadra.Bff/Program.cs` cleared both lists and read no configuration at all, so a browser's
own `X-Forwarded-For` overwrote `RemoteIpAddress`, which `AuthApiClient.AddClientAddress` then
forwarded to the API as the value the API trusts. Fixing only the API left the bypass fully open
through `/bff/login` — verified before and after.

**Closed by:** both hops now trust `X-Forwarded-For` only from addresses or CIDR ranges named in
`KnownProxies`, and enable the middleware only when that list is non-empty. The API refuses to
start outside Development when it is empty, because neither available behaviour is acceptable
there; the BFF does not, because terminating TLS itself with no edge in front is an ordinary
deployment and empty is the right answer for it. Both log what they trust at startup, and
`appsettings.Development.json` names loopback so development takes the production path.
Regression tests: `Khadra.Tests/Security/ForwardedHeaderTrustTests.cs` — five tests pinning both
the trust refused and the trust intended, using an `IStartupFilter` to set a real client address,
because `WebApplicationFactory` leaves `RemoteIpAddress` null and the middleware deliberately skips
its check for a null address, which makes a naive test appear to prove the fix does not work.

**Still open, tracked separately:** IP-partitioned limiting cannot see a botnet and punishes an
office behind one NAT. Per-account lockout is the control that actually protects a single account,
and is a schema and configuration change rather than a hotfix — see item 51.

**Superseded detail below, kept for the record:**

The API partitions every rate limit by `ClientAddress(context)`, and `KnownProxies` is empty in
`Khadra.WebAPI/appsettings.json`. In production every browser request arrives through the BFF, so
they all share one partition: 10 registrations per minute **platform-wide**, and one abusive client
exhausting the login policy locks every other user out of signing in for fifteen minutes.

`docs/auth-and-sessions.md` already says to configure the trusted proxy; nothing enforces it, and
nothing failed loudly when it was left empty.

**To close:** set `KnownProxies` to the BFF's address in the production configuration, and fail
startup outside Development when it is empty — a silent misconfiguration here is a denial of
service that looks like the rate limiter working.

### 33. The BFF's body limit is lower than the API's, and nothing keeps them in step

**Status:** open · **Raised:** 2026-09-05

`POST /api/v1/dealers` accepts 32 MiB (`[RequestSizeLimit]`) so a gallery can file three licence
documents at the configured 8 MiB ceiling. The BFF in front of it runs on Kestrel's default
30,000,000 bytes. Three 8 MiB documents plus the form fields is about 25.2 MB, so it fits today —
and becomes a silent 413 from the proxy, before the API is ever reached, the moment
`Documents:MaximumSizeBytes` is raised.

**To close:** set the BFF's `MaxRequestBodySize` explicitly from the same figure the API derives its
limit from, so raising one raises the other.

### 34. The gallery application form states the document rules rather than reading them

**Status:** open · **Raised:** 2026-09-05

`dealer-apply.component.ts` holds the three required document types as a constant, and the template
says "JPEG, PNG or PDF". All three are the server's: `DealerDocumentType.Required`,
`Documents:AllowedContentTypes` and `Documents:MaximumSizeBytes`. The screen currently states no
figure — no size, no count — so it cannot contradict the server on a number, but it can still offer
the wrong set of document types or the wrong file formats if either is changed.

**To close:** `GET /api/v1/dealers/application-requirements` (DealerOwner policy) returning the
required types, the accepted content types and the size limit, and drive the form from it.

### 35. A gallery application leaves its uploaded documents behind if the write fails

**Status:** open · **Raised:** 2026-09-05

`SubmitDealerProfileHandler` writes each document to storage and then calls `SaveChangesAsync`. If
the commit fails — including on the new unique index when two submissions race — the files are
already on disk with no record pointing at them, and nothing will ever clean them up. Identity
documents are exactly the kind of litter that must not accumulate (spec 7).

The pre-checks in the handler make this rare, and the unique index makes the race lose cleanly
rather than corrupt anything, so this is untidiness rather than a defect.

**To close:** delete the stored blobs when the commit fails, or move to the two-step upload ticket
flow the branding and vehicle photos already use, where the bytes are written against a key the
record commits to first.

### 36. The vehicle form's manufacturer list is a literal

**Status:** open · **Raised:** 2026-09-05

`vehicle-wizard.component.ts` holds nine manufacturers (`makes`) as a hardcoded array. It feeds a
`<datalist>`, so it only suggests — a dealer can type any make and the value on the record is always
theirs — which is why this is a note rather than a defect. But it is still a list on a screen with
nothing behind it, and a reader could easily mistake it for a lookup the platform curates.

Raised while fixing the wizard's real defect: it also pre-filled `make: 'Toyota'`, `dailyRate: 30`
and `securityDeposit: 150`, so a dealer who tabbed past those published a real car at a price the
console invented. Those are gone; every field a dealer must state now starts empty and the step
refuses to advance until it is answered.

**To close:** either a `manufacturers` lookup an administrator curates alongside cities and car
types, or delete the list and leave the field free text.

---

## Delivery fee and email delivery (2026-09-06)

### 37. A resend or a reset that fails reveals that the address is registered

**Status:** open · **Raised:** 2026-09-06

`ResendVerificationHandler` answers 200 for an unknown address and for an already-verified one, so
neither can be told apart from a real resend — that is deliberate anti-enumeration. It now also
returns `auth.verification_email_not_sent` when a send was ATTEMPTED and the mail server refused it,
which implies the address exists.

The leak is narrow: it only appears when mail delivery is actually failing, and the alternative was
telling someone a link is on its way when the server rejected it. But an attacker who can provoke a
per-recipient failure (a bounce, a suppression list) can use it as an oracle.

`ForgotPasswordHandler` now does the same, for the same reason and at a slightly wider cost: its
failure implies ANY account, not only an unverified one. Extended here rather than filed separately,
because it is one leak with one close.

The marginal cost today is nil. `POST /auth/register` already answers 409 `auth.email_taken` to
anyone, always (item 31), so an attacker wanting to know whether an address is registered uses that
instead -- it works whether or not mail is healthy. Whatever closes item 31 closes this.

Same class as items 9 and 31, and all three should be settled together.

**To close:** decide the platform's enumeration policy once. If failures must stay invisible, queue
the message and answer 202 uniformly, reporting delivery problems out of band rather than in the
response.

Settle one tension with the owner at the same time: that close and the standing rule -- never say
"sent" unless it was sent -- cannot both hold literally. With a queue the honest sentence becomes
"queued; if nothing arrives in N minutes, ask for another", and any endpoint reporting a queued
message's fate is the oracle again.

### 38. Mail delivery has no queue, no retry and no bounce handling

**Status:** open · **Raised:** 2026-09-06

`SmtpEmailSender` connects, sends and disconnects inside the request. A slow relay slows the
registration that triggered it, a transient failure is final, and a message the relay accepts but
later bounces is never noticed — the platform believes it was delivered.

`AuthEmailDispatcher` now reports the outcome rather than swallowing it, so the console stops
claiming a send that did not happen. That is the honesty fix, not a delivery guarantee.

**To close:** an outbox — persist the message, hand it to a background sender, retry with backoff,
and record the terminal state. Bounce handling needs a provider with a webhook, which is also the
point at which `Email:Provider` should stop being raw SMTP.

### 39. The delivery fee has no history

**Status:** open · **Raised:** 2026-09-06

A gallery owner can change what they charge for delivery at any time, from `/dealer/delivery`.
`DealerDeliveryChanged` carries the new amount, but nothing subscribes to it, so there is no record
of what a gallery charged last week or who changed it. Bookings are safe — each freezes the fee it
was made under — so this is about accountability, not correctness.

Same gap as item 6 (listing edits are not logged), and the same fix serves both.

**To close:** persist dealer-side changes to an activity trail the owner and an administrator can
read, fed from the domain events these actions already raise.

### 40. Email leaves under a borrowed sender, and only ever in English

**Status:** open · **HARD BLOCKER before real customers** · **NOT a blocker for Flutter development**
· **Raised:** 2026-09-06 · **Rewritten:** 2026-09-07

Superseded the Resend problem this item was opened for: the platform now sends through **Brevo over
HTTPS** (port 443), which was the answer to a network that silently swallows SMTP on 587 — the
handshake succeeds, the greeting never arrives, and every message stalls until it times out. Real
delivery to arbitrary recipients is verified working, most recently on 2026-09-07 by an actual send,
not by trusting the startup probe. Two things about it are still wrong for real customers.

**The sender is not ours.** `Email:FromAddress` is set to a confirmed Gmail address, and Brevo
rewrites the visible sender to a subdomain of its own — mail arrives from
`khadrajordan02@12062026.brevosend.com`. Nobody can DKIM-sign `gmail.com`, so a confirmed ADDRESS
buys delivery while only a verified DOMAIN buys your own From. For a customer that means a password
reset arriving from a domain that is not Khadra, and a materially higher chance of the spam folder.

**Every message is English.** `AuthEmailComposer` has no culture, language or locale parameter
anywhere — subjects and bodies are hard-coded English literals. So a customer who registers in
Arabic reads an Arabic screen telling them to check their mail, and receives English. This is
server-side, so neither the Angular console's language switch nor the Flutter app can compensate.

**Why it does not block Flutter.** The API contract does not change for either fix, and the app never
composes mail; it triggers server-side sends and reads ProblemDetails codes. Both fixes are
configuration and a server-side template pass. Building against the current behaviour is safe.

**To close — one pass, when khadra.jo is set up:**
- Verify `khadra.jo` at Brevo (SPF, DKIM, and a DMARC record), then set `Email:FromAddress` to
  `no-reply@khadra.jo`. Configuration only, no code change; the startup probe already reports which
  sender it is using and whether mail will be delivered.
- Give `AuthEmailComposer` the recipient's language and key its subjects and bodies the way the
  console's `en.ts` / `ar.ts` are keyed. That needs somewhere to READ the language from, which is the
  real decision: a `PreferredLanguage` on the user, captured at registration and settable later. It is
  a schema change, so it wants deciding rather than defaulting — and note the Flutter app must send
  it at registration for a customer ever to get Arabic mail.

Related and separate: item 38 (mail has no queue, no retry and no bounce handling) still stands, and
matters more once real customers depend on delivery.

### 41. The Employee Console design is not in `docs/design/`

`Employee Console.dc.html` is the source of truth for how the employee's screens look, and it is
cited from `nav.data.ts` and every component under `features/employee/`, but it was never exported
into `docs/design/` — the console was implemented from screenshots. The frontend rules say to diff
against the export before changing a screen's appearance, and right now there is nothing to diff
against.

That is not a cosmetic gap. Diffing the export is exactly what would have caught the design's "48h
limit" copy (item 42), and the next person to touch these screens has no way to tell a deliberate
deviation from a mistake.

**To close:** export the project at `https://claude.ai/design/p/abfd4b04-c3e3-43e6-99c8-747bf8e2ebb0`
into `docs/design/`, add the file to the table in `docs/design/README.md`, and diff the four
deviations recorded in the components' own comments.

### 42. The design promises a 48-hour answer window that the platform does not keep

**Status:** closed · **Closed:** 2026-09-07 — the window exists, on the terms this item asked for.

`BusinessRules:BookingAnswerWindowHours` (48) is frozen onto every booking as
`BookingTerms.AnswerWindow`, and `Booking.Create` sets `DecisionDeadline = min(now + window,
Period.Start)` from it. `ExpireUnanswered` judges against that column, `Approve` refuses once it has
passed (a late REJECT is still allowed — it costs nobody anything and closes the record honestly),
and the availability predicate stops counting the request as a hold at the same moment, so the car
returns to the market on the deadline rather than whenever a job next looks.

It is its own setting rather than `AdminSlaHours`, which happens to share the number: they are
different clocks owned by different people, and one owner must be able to move without the other.

The refund consequences this item asked to be worked out turned out not to exist. Under the
reordering of 2026-09-07 (`docs/spec-amendments.md`) a request carries no deposit, so an unanswered
one refunds nothing — there is no money to give back and no penalty to assess.

The dealer console now counts down to that deadline instead of to the rental date. The original
report follows.

`Employee Console.dc.html` shows "Requests expire 48h after they arrive", "48h limit" beside the
pending-requests tile, and "answer within 34h" on notification rows. No such rule exists.
`Booking.ExpireUnanswered` refuses until `now >= Period.Start` — a request expires when the RENTAL
DATE arrives unanswered, not on a clock from when it was made — and `BookingTerms` freezes no answer
window. `BusinessRules.AdminSlaHours: 48` is the admin's clock for reviewing a dealer application and
a dispute, an unrelated thing that happens to share the number.

The copy is not in the built console: the employee dashboard shows the age of the oldest request and
claims no deadline. But the design still says it, and the same line was already struck once from the
Dealer design.

**To close:** decide whether a booking answer window is wanted. If it is, it is a business rule
(`BusinessRules:BookingAnswerWindowHours`) frozen onto `BookingTerms` at request time as
`min(RequestedAt + window, Period.Start)`, judged in `ExpireUnanswered`, with the deposit refund
consequences worked out — it is a refund promise to a customer, not a label. If it is not, correct
the design. Either way the two must agree.

### 43. Three notification kinds have handlers but no producer

`NotificationKind` deliberately carries only kinds something raises. Three more are worth having and
their handlers already exist: `BookingCancelledByAdmin` (`AdminBookingCommands`), `DisputeOpened` and
`DisputeResolved` (`DisputeCommands`). Each is a few lines — resolve the dealership, call
`DealerTeamNotifier.NotifyTeamAsync` before the handler's own `SaveChangesAsync` — and each is
something a dealership currently finds out about only by looking.

**To close:** wire the three, add the kinds back, and extend the console's `describe`/`icon`/`tone`
maps. Add the kind WITH its producer, never before it.

### 44. An employee cannot change their own name, email or phone

The Employee Console design draws all three as editable on the Settings screen. They are shown
read-only, because no endpoint accepts them and because email is not a cosmetic field: it is the
sign-in identifier AND the password-reset destination, so a session alone must not be enough to move
it — a stolen BFF cookie could redirect recovery to an attacker's mailbox and take the account
permanently. Name and phone were also entered by the OWNER at invitation and appear on the owner's
staff list, so an employee renaming themselves silently changes what their owner sees.

**To close (an IdentityAccess feature for every role, not an employee-console one):** current password
required; a `VerificationPurpose.EmailChange` token carrying the pending address, applied only when
the new mailbox is proved; notice to the old address; `RevokeAllSessions` on completion with fresh
tokens issued through the BFF's change-password path so the cookie's name/email claims are re-signed.
Settle the enumeration-oracle policy (items 9, 31, 37) at the same time, since a 409 `email_taken`
from an authenticated endpoint is a fourth one.

### 45. The dealership has no contact details, and My Business says so

The employee's "My business" screen shows the dealership's name, description, map pin, opening hours,
delivery settings, registration and verification status — and states plainly that a street address, a
dealership phone number and a public email are not recorded, because `Dealer` holds a `GeoPoint` and
nothing else. The design shows all three, plus the owner's name and a "4.8 · 96 reviews" rating.

The owner's name has a legitimate source and was deliberately NOT bolted onto `DealerProfileDto`,
which is built from the aggregate alone in five handlers and cannot reach IdentityAccess; it needs its
own query composing the two through a reader. The rating needs the Reviews context, which is not built.

**To close:** a `ContactDetails` value object on `Dealer`, collected by the application form and
editable from `/dealer/profile`; a `GetMyBusinessQuery` returning the profile plus the owner's name
through a reader correlated by id. A street address stays display-only — every distance calculation
is haversine on the pin.


### 46. `BusinessRuleSettings` still carries a platform-wide delivery fee

The owner moved the delivery fee onto the dealership on 2026-09-06: each gallery sets its own on
`DeliverySettings`, and there is no platform-wide figure any more. `BusinessRules` (the configuration
snapshot actually in force) and `BusinessRulesDto` were both cleaned out, and nothing reads a central
fee today.

But the `BusinessRuleSettings` aggregate — the admin-editable version of the rules, not yet wired to a
table — still has a required `Money DeliveryFee` threaded through `Create`, `Update` and `Apply`. It is
dormant, so it changes no behaviour now. It is a trap for later: whoever wires that aggregate up gets a
platform-wide delivery fee back, sitting beside the per-dealer one, with nothing to say which wins.

**To close:** drop `DeliveryFee` from the aggregate and its factory/update signatures, and from
`BusinessRuleSettingsTests`. Nothing else references it. Do it before the editable-settings screen is
built, not after.

### 47. A first staff invitation that is never delivered looks identical to one that is

`InviteEmployeeCommand` sends the invitation after commit and discards the result, on purpose: the
employee record is already saved and discarding it because a relay hiccuped would be the worse
outcome. But the owner is never told. The row reads `INVITED - Has not set a password yet` whether
the mail went out or was refused, so the owner waits for something nobody sent. This happened on
2026-09-06: an invitation to a real address failed at the relay, the owner saw nothing, and reached
for Forgot Password on the invitee behalf -- which failed the same way and also claimed success.

`ResendEmployeeInvitationCommand` now reports the failure (that button exists only to send an email,
so success there is a lie with no upside). The first invitation cannot use the same fix: it returns
`EmployeeListItem`, a READ MODEL the reader rebuilds from the database, and "was this one message
accepted" is not a persisted fact it can carry.

**To close:** return `InviteEmployeeResult(EmployeeListItem Employee, bool InvitationEmailSent)` from
the command, and have the Employees screen mark that row `email not sent - resend` instead of plain
`Invited`. Four layers: command result, controller, Angular service, template.

### 48. An administrator invitation whose email fails cannot be re-sent, ever

`InviteAdminCommand` now reports delivery on `InviteAdminResult.InvitationEmailSent` instead of
throwing out of an unhandled send (which answered 500 for an invitation that had in fact been
created, so the obvious retry met 409 `auth.email_taken`). The console does not read the flag yet.

The deeper gap is that there is no way to try again. Dealer employees have
`POST /dealers/me/employees/{id}/resend-invitation`; administrators have no equivalent. Once an
invitation is created and its email refused, that address is spent: re-inviting hits the unique
index, deactivating is not deleting, and the account cannot sign in to fix itself. The only routes
back are waiting for the token to expire with nothing to re-trigger it, or editing the database.

**To close:** a `ResendAdminInvitationCommand` mirroring the employee one (reissue the token, report
delivery, audit it), and an Admin users screen that marks a row `email not sent` and offers the
button. Until then, an administrator invited while mail is down is stuck.

### 49. Arabic covers every template; some component copy is still English

**Updated 2026-09-07.** Partly closed. An end-to-end pass in Arabic measured this on real screens
rather than by grep, and the worst of it is fixed: the **fleet screen is now fully Arabic** (measured
zero English strings on it, bar the JOD currency code, which is meant to stay Latin), and the
**opening-hours tables** on /dealer/profile and /employee/business no longer read "Sunday … Saturday"
down the side of a right-to-left page. Day names come from ICU via `FormatService.weekday()` now,
rather than seven hand-written keys, so every locale gets its own.

One fix worth repeating elsewhere: the fleet filter chips were a `readonly` FIELD initialised with
`this.t(...)`, which resolves once at construction — switching language with the screen already open
left them in the old one. They are a `computed` now. Any other chip or column list built the same way
has the same latent bug.

**Still open: roughly 330 strings across the other screens**, unchanged in nature from the list
below. Heaviest are dealer/booking-detail, dealer/dealer-dashboard, disputes/dispute-detail,
fleet/vehicle-wizard, bookings/booking-detail and employee/employee-dashboard — the last is the most
visible, because every stat tile caption on an employee's landing screen is English. Deferred as its
own piece of work, not a blocker: the mechanism (switch, RTL mirroring, persistence across reload and
logout, switching back) is correct and was re-verified.

`core/i18n/` holds 1,202 keys in both languages. EVERY template is keyed -- all 54 of them -- along
with the shell, the auth screens, the dealer gate, both not-built placeholders, the dashboard KPI
cards and attention queue, the activity verbs, relative time, the confirmation dialogs, list columns
and the pagination. `ar.ts` is typed against `en.ts`, so a missing translation fails the build, and
`dictionaries.spec.ts` also fails on a key that drifts, a dropped placeholder, or an Arabic plural
missing one of its six forms.

What is still English, measured by `node scan-i18n.js` in `Khadra.Dashboard` (413 hits, 59 files --
the scan is deliberately noisy, so perhaps 300 are real):

- **Copy in component TypeScript that is not a dialog field.** The codemod covered `title`, `body`,
  `confirm`, `note`, `label`, `placeholder` and `hint`. Copy assembled in other shapes -- KPI
  sub-labels, greetings, row actions, `describe()` failure sentences -- is still English. The
  heaviest are `dealer/booking-detail`, `dealer/dealer-dashboard`, `disputes/dispute-detail`,
  `fleet/vehicle-wizard`, `bookings/booking-detail`, `employee/employee-dashboard`.
- **Status pills.** `status.*` keys exist for every enum member the console shows, but the pills
  still render the server's raw `Enumeration.Name`. They need one `statusLabel(name)` helper applied
  at each render site, with the CamelCase-split fallback the audit screen already uses.
- **Route `title` literals in `app.routes.ts`.** Dead weight rather than a bug: `TranslatedTitleStrategy`
  resolves every mapped route from `SCREEN_TITLES`, and these are only the fallback for one it does
  not know.
- **Server sentences.** Unchanged from before: `Error.Message` and ProblemDetails `title` are English,
  and FluentValidation messages cannot be keyed client-side at all. See the note below.
- **`toLocaleString('en-GB')` sites on feature screens.** `FormatService` exists and the gate and the
  dashboards use it; the rest have not moved onto it, so some dates stay English under Arabic.
- **`strictTemplates` is off.** The Angular compiler does still check `t()` key arguments against the
  union -- that caught real mistakes during this work -- but turning it on would catch more.

**To close:** run `node scan-i18n.js --detail <path>` per screen and key what it lists. The tooling
used for the bulk pass is in `Khadra.Dashboard/`: `scan-i18n.js` (audit), `key-templates.js` and
`key-components.js` (codemods), `add-en.js` / `add-ar.js` (append a batch to a dictionary).

The backend half is unchanged and still the bigger job: `UseRequestLocalization(en, ar)` with an `ar`
resource keyed by error code, applied in `ApiControllerBase.Failure`, the authorization result handler
and the exception handler; `AuthApiClient` forwarding `Accept-Language`, since the BFF's own auth calls
are not proxied; and `PreferredLanguage` on `User`, because emails are composed without a request.
Doing it server-side also spares the Flutter app a third copy of the same 140 codes.

### 50. `DisputeAuditor` writes an English sentence into an append-only table

`DisputeAuditor.Describe` composes `"Resolved: of {amount} {currency} held, refund …, platform …,
dealer …"` and stores it in `audit_entries.new_value`. That table refuses UPDATE by trigger and by
a `SaveChanges` guard, so every dispute resolved from now on is an English-only row for ever -- and
the audit screen is one of the screens due to be translated.

Every other call site stores a machine name (`Status.Name`, `Role.Name`) that the screen phrases, so
this one is the outlier rather than the pattern. Nothing is lost yet: no dispute has ever been
resolved on this platform.

**To close:** store the parts (`refund=80;platform=20;dealer=20;charge=30;currency=JOD`, or compact
JSON inside `MaxValueLength`) and let the audit screen compose the sentence from
`AuditAction.DisputeResolved`. Do it before the first real dispute, not after.


### 51. Nothing throttles failed sign-ins for one account

**Status:** open · **Raised:** 2026-09-06

Item 32 closed the bypass that made the IP-based limiter ineffective, but the limiter it restored is
still the only brake on password guessing, and it partitions by address. That has two ends it cannot
cover: an attacker spread across many addresses is never slowed against a single account, and a
dealership whose staff share one office NAT spend a single 10-per-15-minutes budget between them.

**To close:** count consecutive failures on the user and refuse for a configured period.
`MaxFailedLoginAttempts` and `LockoutMinutes` belong in `BusinessRules`/`AuthOptions`
configuration, never as constants. Keep the rule the login path already follows — account state is
disclosed only after the password proves out (`LoginHandler`) — so a wrong password during a
lockout still answers `auth.invalid_credentials`, and only a CORRECT password during a lockout
gets a distinct code with the remaining time. Do not extend the window on further failures, or an
attacker can hold an account locked indefinitely; clear the counter on a successful password reset.
The new error code has to reach the console in both languages.

### 52. Lookup name uniqueness is enforced in the handler, not by an index

**Status:** open · **Raised:** 2026-09-07

Item closed alongside the duplicate-name fix, but only half of it. Creating, renaming and
reactivating a city or car type all now refuse a name another OFFERED entry already uses
(`lookup.name_taken`), with a comparison that folds case for English and tashkeel/tatweel for Arabic
— the collision that actually happens there is the same word with and without its vowel marks.

That is a handler check, so two simultaneous requests can still both pass it and both insert. The
second layer is a partial unique index on `lower(name_en) WHERE is_active` and the same for
`name_ar`, which EF cannot express in the model and needs raw SQL in a migration — the pattern
`DealerConfiguration` already uses for one-dealer-per-owner. With it, the loser of a race fails as
`data.conflict` 409 instead of creating a duplicate.

**Note before writing it:** the migration will refuse to apply while any duplicate rows are active,
so it must not attempt to fix data itself — retire the duplicates through `/cities` and `/car-types`
first. The development database was cleaned this way on 2026-09-06.

### 53. Value objects are re-parsed on read, and a failed parse throws

**Status:** CLOSED 2026-09-07 · **Raised:** 2026-09-07

Seven EF converters rebuilt their value object through `Create(...).Value` on every read, and
`.Value` on a failed `Result` throws. Nothing was broken while every stored row happened to satisfy
the current rule — but the rules move. The commercial registration rule was tightened the day before
this was raised, so letters are refused rather than silently deleted; had one stored row contained a
letter, that change would have turned every query touching a dealer into an exception on load. Not a
validation error a caller could handle: a crash, on read, for an aggregate nobody was editing.

**Closed by** a trusted `FromPersisted(string)` on each of the seven, used by the converters:
`EmailAddress`, `PhoneNumber`, `PersonName`, `BusinessName`, `CommercialRegistrationNumber`,
`PlateNumber`, `BookingReference`. Writes still go through `Create`, which is the only door new
values come in by, so nothing is loosened — the rule simply stops being re-applied to rows that were
already accepted under an older one.

`OperatingHoursConverter` was already safe and is untouched: it falls back to `AlwaysClosed()`
instead of throwing.

Regression tests: `Khadra.Tests/Persistence/StoredValuesSurviveTighterRulesTests.cs` loads values the
write path refuses on purpose ("AB1234" as a plate and a registration, a one-character name, a
malformed address) and asserts each still reads back, with a final test asserting the write path is
unchanged. If a converter is ever routed back through `Create`, it fails there rather than in
production on the next rule change.

## Calendar-day billing and vehicle holds (2026-09-07)

### 51. A late return costs the customer nothing

**Status:** open · **Raised:** 2026-09-07 (a direct consequence of the owner's calendar-day decision)

Rentals are now billed by the difference between two Amman calendar dates, so the return *time* no
longer affects the price at all. A car due back Thursday at 09:00 and returned Thursday at 23:59
costs exactly the same. Under the elapsed-time rule it replaced, the overrun rolled into another
billed day on its own.

This is not a defect in the implementation — it is what the rule says, and the owner chose it
knowing the rule is never dearer to the customer than the old one. It is on this list because the
platform now has no answer at all for a late return, and `RecordReturn` has no concept of lateness to
build one from. The usual answer in this trade is a grace period plus an hourly overage; the spec
(v3.1) specifies neither, so nothing was invented.

Nobody is affected yet: no booking can be created. The first gallery to lose a day's rental to a
customer who returns at midnight will raise it, and by then bookings will exist to be re-judged.

**To close:** the owner names a grace period and an overage rate, both frozen onto `BookingTerms`
like every other number, and `RecordReturn` assesses the overage against the frozen figures. Decide
before the booking flow ships, because retrofitting a term onto bookings already made means either
re-judging them or carrying two rules.

### 52. The exclusion constraint cannot be tested

**Status:** open · **Raised:** 2026-09-07

`bookings_one_hold_per_vehicle` is the only thing that actually prevents two customers holding one
car in a race. The persistence suite runs on SQLite through `EnsureCreated`, which never executes the
raw SQL in a migration, so **no test exercises the constraint at all**. What is tested is the
application guard, which is the check-then-act the constraint exists to backstop.

The constraint has been verified by hand against the development database — `pg_constraint` holds it
and `btree_gist` is installed — and that is a person looking once, not a test that keeps looking.

**Partly answered on 2026-09-07** by the create-booking slice. Six concurrent `POST /bookings` for one
car and one window were driven against the real API and Postgres: exactly one returned 201 and five
returned 409 `booking.vehicle_unavailable`, and a query for overlapping live pairs found none. That
proves the OBSERVABLE contract holds under a real race. It does not prove the constraint is what held
it — the application guard may have caught every loser — so this item stays open, and what it now
needs is a test that reaches the constraint with the guard deliberately bypassed.

**To close:** a Postgres-backed test (Testcontainers, or a dedicated test database) that inserts two
overlapping held bookings for one vehicle and asserts the second is refused with SQLSTATE 23P01.
Until then this is one migration edit away from silently guarding nothing, and the only thing
standing between it and that is `VehicleHoldStatusTests`, which pins the four status names the
constraint's `WHERE` clause spells out.

### 53. Creating a booking must expire stale unpaid holds first, and translate 23P01

**Status:** closed · **Closed:** 2026-09-07 — both halves are in `CreateBookingHandler`.

`ExpireStaleHoldsAsync` clears the vehicle's expired requests and unpaid approvals inside the same
transaction, before the overlap guard and the insert, through the new
`IBookingRepository.ListStaleHoldsForVehicleAsync`. The expiries are attributed to nobody, because
the clock ended those bookings and not the customer who arrived next.

`UnitOfWork` now translates SQLSTATE 23P01 into `ExclusiveHoldConflictException`, carrying the
constraint name; the handler turns `bookings_one_hold_per_vehicle` into `booking.vehicle_unavailable`
(409) and leaves any other constraint to surface as a generic conflict.

A lost optimistic-concurrency race on a stale hold is **not** given that answer, and an earlier draft
of this note said it was. Losing a row to another writer says nothing about whether the car is free:
an administrator, or the expiry job when it exists, can change a stale hold under a request whose
dates are perfectly available, and answering "no longer free for those dates" would send that
customer away from dates that are fine. It surfaces as the generic `concurrency.conflict`, which
tells them to reload and try again, and that is true.

Two things keep it rare rather than routine. `ListStaleHoldsForVehicleAsync` is scoped to holds that
OVERLAP the candidate window, so two customers booking the same car in different months never touch
the same row. And `IVehicleHoldLock` takes a transaction-scoped Postgres advisory lock on the vehicle
as the first statement inside the transaction, so the whole check-then-act sequence is serial per
car: the second creator waits, then reads a world that has stopped moving.

The original report follows.

Two things the create handler must do that nothing does yet, because nothing creates bookings:

**Expire stale holds in the same transaction.** The exclusion constraint's predicate cannot mention
`now()`, so a booking whose deadline passed a week ago still counts there as holding the car. The
application predicate (`BookingHolds.Live`) correctly ignores it. The two therefore disagree: the
guard says the car is free, and the insert is refused by the database. The handler must clear both
kinds of stale hold on that vehicle before it inserts — `ExpireUnanswered` on requests past their
decision deadline, `ExpireUnpaid` on approvals past their payment deadline. Both halves are required:
the deadline terms alone leave the constraint refusing bookings the guard allowed, and lazy expiry
alone leaves a stale hold keeping a car off the market forever (see item 4).

**Give 23P01 its own code.** An exclusion violation surfaces as `DbUpdateException`, which
`ApiExceptionHandler` maps to 409 `data.conflict`. That is not a 500, but a phone cannot say "this
car was taken while you were deciding" from a generic conflict code. Catch it and return
`booking.vehicle_unavailable`.

### 54. The fleet screen buckets vehicle holds in the browser's own calendar

**Status:** open · **Raised:** 2026-09-07 (found by the Fable advisor while reviewing calendar days)

`Khadra.Dashboard/src/app/features/fleet/vehicle-detail.component.ts` turns a car's bookings into
day cells using the browser's local calendar, and knows nothing about the turnaround gap. A dealer
in a different time zone sees a car blocked on the wrong days, and every dealer sees it free during
the two hours it is actually being cleaned.

It predates calendar-day billing, but that decision makes it worse: "which day" is now a business
fact the server owns, and this screen is a second source for it.

**To close:** serve the occupancy from an availability read model that uses the same `BookingHolds`
predicate and the same Amman calendar as everything else. The customer catalogue needs that read
model anyway, so this closes with it. Until then the strip is approximate and does not say so.

### 55. The turnaround gap is one number for the whole platform

**Status:** open by design · **Raised:** 2026-09-07 · **Owner confirmed:** 120 minutes

`BusinessRules:TurnaroundMinutes` is a single figure every gallery is held to. A city-centre office
that valets at the counter and an airport operator who drives cars to a depot have genuinely
different needs, and a delivery return includes the driver's trip back, which self-pickup does not.

Deferred deliberately: one number is the right thing to ship first, and the design already absorbs a
per-gallery or per-method figure without a schema change, because each booking stores the resolved
value in `BookingTerms.TurnaroundBuffer` and derives `hold_start` from it. Nothing reads the platform
setting after a booking is made.

**To close:** if the owner wants it per gallery, add it to `DeliverySettings` beside the radius and
the fee — the same move the delivery fee already made on 2026-09-06 — and resolve it in the create
handler. No migration of existing bookings is needed; they keep the gap they were made under.

## The customer app's view of the API (2026-09-07)

### 56. Rate limits assume one customer per IP address

**Status:** CLOSED 2026-09-07 · **Raised:** 2026-09-07 (Fable advisor, reviewing the API as a mobile client sees it)

Every limit in `Program.cs` is partitioned by client address: login 10 per 15 minutes, auth 10 per
minute, refresh 60 per minute, the public catalogue 120 per minute. That is the right shape for a
browser on a home connection and the wrong one for a phone in Jordan. Zain, Orange and Umniah put
thousands of subscribers behind a single IPv4 address, so the partition key is the carrier, not the
customer.

Forty people opening the home screen in the same minute from one carrier — three calls each, cities,
car types and a search — exhaust the public bucket for everyone behind it. Ten sign-ins per quarter
hour is shared by an entire network. The API's own comment calls 120 "generous"; behind carrier-grade
NAT it is not a limit on abuse, it is a limit on customers.

This is the single most likely production incident for the app, and no amount of client care fixes
it. Item 32 already records that the partitioning is wrong behind a proxy; this is the same fault
with a much larger blast radius.

**Closed by** partitioning the auth limits on (address, account) instead of the address alone.
`CredentialSubject` reads the account out of the request body before the limiter runs — the limiter
is upstream of model binding, so nothing else has read it yet — and hands the partitioner a key of
the address plus a SHA-256 prefix of the normalised email. Only the prefix, because partition keys
sit in memory for the length of a window and an email address is not something to leave lying in
them. The address stays in the key: dropping it would let one attacker spread attempts on one
account across many addresses.

Brute-forcing one account from one address is still ten attempts per quarter hour. Forty strangers
on one carrier address signing in to forty accounts no longer collide at all.

Public raised to 1200/minute and refresh to 600/minute, both still per address: they are read-only
public prices and per-device rotations respectively, and the ceiling is there to stop a scraper
rather than to ration customers. `Retry-After` now accompanies every 429, taken from the limiter's
own metadata, so a client backs off honestly instead of guessing.

The body is rewound after reading, and `CredentialSubjectTests` asserts that explicitly — an
unrewound body would make every sign-in on the platform bind an empty model and fail validation,
with a cause that looks like anything but a rate limiter.

Verified against the running API: ten attempts on one account are allowed, the eleventh is refused
with `Retry-After: 900`, and a different account from the same address is unaffected.

### 57. A lost refresh response signs a customer out of a working session

**Status:** CLOSED 2026-09-07 · **Raised:** 2026-09-07 · **Owner confirmed:** retry before sign-out

`RefreshTokensHandler` rotates the refresh token and revokes the whole family when a consumed one is
presented again. That is correct replay detection, and on a phone it fires on something that is not
an attack: the request reaches the server, the rotation commits, and the response is lost — a radio
handover, a tunnel, iOS suspending the app mid-flight. The client still holds the old token, its next
refresh looks exactly like a replay, and the customer is signed out for a network hiccup. On mobile
this is the common case, not the exotic one.

There is a second, smaller version of it in the same handler: when two refreshes race, the `xmin`
loser also revokes the family — which destroys the replacement token the WINNER was just issued.

**Closed by** a reuse grace, `Authentication:Policy:RefreshReuseGraceSeconds`, default 60. A consumed
token presented inside the window whose replacement has never itself been used is treated as the
retry it is: the unused replacement is retired and a fresh pair issued. Past the window, or once the
replacement has been spent, the family dies exactly as before — the narrowing that matters is "never
used", because if anyone has spent it then two parties hold live tokens and that IS the attack.

The owner asked for sign-out only after REPEATED failures, so the handler follows the replacement
chain up to five hops rather than one. A customer on a failing connection retries and can lose that
response too; signing them out on the second hiccup is the same mistake as the first. Bounded,
because each hop is a whole request that reached the server and came back to nobody, and a genuine
client cannot need many.

The concurrency loser no longer kills the family either. Two refreshes of one token can only race
because one client sent both, and the winner is holding a perfectly good replacement — revoking the
family destroyed a token the customer legitimately had, over a bug on their own device. The loser is
refused, and its retry falls into the grace above.

### 58. There is no platform-context endpoint, so the app must hard-code what the platform knows

**Status:** CLOSED 2026-09-07 · **Raised:** 2026-09-07

Several facts the customer app needs are known only to the server and reach it nowhere, or only
inside a response it cannot get before it needs them:

- **The reporting time zone.** `RentalQuote.timeZone` carries it, but the date pickers run BEFORE the
  first quote, and a picker in the wrong zone prices a different number of days.
- **A currency's minor units.** JOD has three; the app currently carries its own table.
- **The minimum renter age.** Reachable only as English prose inside `auth.under_minimum_age`.
- **Upload limits.** `Documents:MaximumSizeBytes` and the allowed content types are enforced but never
  published, so a client can only discover them by being refused.
- **Filter vocabularies.** Transmissions and fuel types are smart enums with no endpoint, so filter
  chips would be a literal in a widget — which the standing rule forbids.

**Closed by** `GET /api/v1/app-config` — anonymous, on the public rate limit, called once at startup.
It returns the reporting time zone, the currency with its minor units, the minimum renter age, the
document limits, and the transmission, fuel-type and pickup-method vocabularies in both languages.

The owner's reason for it is the one that matters: a customer app lives in shops, so a number baked
into it needs a release to change, and until every customer updates there are two answers to one
question.

The vocabularies are projected from the domain's own smart enums, so a fuel type added tomorrow
appears without anyone remembering to add it — which is exactly why a filter chip is not a literal in
a widget. A member with no Arabic falls back to its own name rather than throwing, so a new member
cannot take the whole endpoint down.

**It also surfaced a live defect.** `Documents:AllowedContentTypes` was coming back with every type
twice: the options class carried a property initialiser AND appsettings configured the same list, and
the binder APPENDS to a collection that already has items rather than replacing it. Nothing had ever
broken, because every use was a `Contains` check — it became visible the moment the list was
published to a client. The initialiser is now empty, configuration is the only source, and the
existing "at least one" validation makes a missing key fail at startup.

### 59. The deposit payment window is 24 hours only because nothing can tell the customer

**Status:** open · **Raised:** 2026-09-07 · **Owner decision recorded**

`BusinessRules:PaymentWindowHours` is 24. The number the flow wants is closer to one hour: a car sits
held against nothing for the whole window, and a dealership that has said yes deserves an answer
sooner than the next day.

It is 24 because there are no push notifications. A customer learns their booking was approved only
by opening the app. A one-hour window would auto-expire most bookings approved overnight or during a
working day before the customer ever saw the approval, wasting the dealer's decision and losing the
rental — a worse failure than a car held a day too long.

It is configuration, not a constant, precisely so this can be shortened without a release.

**To close:** once approval reaches a customer's phone (item 43's notification producers plus a push
transport), shorten the window and say so on the screen that counts it down.

### 60. Nothing tells a customer their approval is waiting for money

**Status:** open · **Raised:** 2026-09-07 · **Blocks:** the booking-creation slice being usable

The reordering of 2026-09-07 puts a deadline on the customer that they are never told about. The
dealer console counts its own answer window down; the customer app has no booking screens at all yet,
and no notification is raised when a booking is approved.

The consequence is not theoretical: a booking approved and never paid for expires silently, and the
customer's account shows a car they thought they had.

**To close:** a `BookingApproved` notification to the customer carrying `PaymentDeadline`, and a
booking screen in the customer app that shows the deadline and the amount. The payment step itself
can stay the honest "not built yet" screen until Payments exists.

### 61. A booking can be made minutes before its own pickup, and every window collapses

**Status:** closed · **Closed:** 2026-09-07 — the owner set a 120-minute minimum lead time.

`BusinessRules:MinimumBookingLeadTimeMinutes` (120) is judged by `BookingWindowPolicy`, which the
create endpoint, the quote and the catalogue search all share — so a customer is told about a date
they cannot use at the moment they type it, rather than at the last step. It is published on
`GET /api/v1/app-config` beside the horizon, so the date picker bounds itself from the server.

The horizon itself was being published and never enforced; the same policy now refuses a pickup
beyond `MaxAdvanceBookingDays`.

The second half of the original report stands and is NOT closed: the customer app must count down the
`paymentDeadline` the booking carries, never `paymentWindowHours` from app-config. That belongs to the
customer-app booking screens (item 60).

The original report follows.

`Booking.Create` requires only that the period starts in the future. Nothing stops a request being
made twenty minutes before the car is due out. Every window on a booking is capped at the rental
start, so all three collapse at once: the dealer gets twenty minutes to answer, the customer gets
whatever is left to pay, and free cancellation is already over.

None of that is wrong — a window that outlived the rental it governs would be worse — but the
platform is meanwhile telling the customer, on `GET /api/v1/app-config`, that they have 24 hours to
pay. Two answers to one question is the failure this endpoint exists to prevent.

**To close:** two things.

A **minimum lead time** in `BusinessRules`, refused at creation, long enough that the three windows
mean something. The owner picks the number.

And the customer app must count down `paymentDeadline` — the instant on that booking — never
`paymentWindowHours` from app-config. The window is what the platform offers; the deadline is what
this booking got.

### 62. Payments must honour the deposit deadline the booking carries

**Status:** open · **Raised:** 2026-09-07 · **Blocks:** nothing yet — Payments is unbuilt

`ConfirmDepositPaid` deliberately does not check `PaymentDeadline`. A webhook settles a checkout that
was already started, and refusing it there would mean money captured against a booking the platform
then refuses to confirm. The deadline belongs at the other end — where a checkout is opened — and
Payments does not exist to enforce it yet.

Two obligations for whoever builds it:

- **Refuse to open a checkout once `PaymentDeadline` has passed.** Otherwise the customer pays for a
  car the catalogue released at the deadline and somebody else may already hold.
- **Treat `booking.not_awaiting_payment` on a webhook as a refund**, not an error to retry. It means
  the money was captured for a booking that expired or was cancelled in the race. `bookings.xmin`
  makes that race resolve to exactly one winner, and this is the loser's side of it.

`ConfirmDepositPaid` is idempotent by payment id, so a retry of the SAME payment is always a success
whatever the booking's state — a gateway must never be made to retry forever.

### 63. HARD BLOCKER — a dealer cannot see the documents they are required to check

**Status:** open · **Raised:** 2026-09-07 · **Owner: hard requirement before real launch**

Spec 5.1 makes the dealer the party who checks a renter's licence. They cannot. `CustomerDocument`
already scopes viewing to the customer themselves *and to a dealer with an active booking request* —
the rule is written, and no endpoint implements it. There is no way, anywhere in the platform, for
the gallery handing over a car to look at the licence of the person taking it.

Since 2026-09-07 the booking-creation guard requires only that a licence and an identity document
have been UPLOADED. Nothing verifies them: `MarkVerified` and `MarkRejected` are `internal` with no
public path, so every document on the platform sits in `PendingReview` for ever. The guard is a
checkbox, and the owner has accepted it as one **for development only**.

The owner has recorded this as a hard requirement, not a nice-to-have. Handing a real car to a real
stranger on an unverified claim is the failure this closes.

**To close:** an endpoint that mints a short-lived signed link to a customer's licence and identity
document, authorised exactly as `CustomerDocument` already says — the dealer of a booking that is
live, for as long as it is live, and no longer. Then the dealer console screen that shows them at the
handover, and a decision (item 27) on whether an admin reviews documents at all or the dealer's
look at pickup is the check.

### 64. A free hold is renewable, so the 72-hour ceiling is per request, not per customer

**Status:** open by decision · **Raised:** 2026-09-07 · **Accepted exposure**

A request holds a car for up to 48 hours unanswered, and an approval holds it a further 24 unpaid:
72 hours, none of it paid for. Nothing then stops the same customer requesting the same car again the
instant it expires, so one account can keep a car off the market indefinitely at no cost.

The deposit used to make that expensive. Under "reserve now, pay after approval" nothing does.

The owner has accepted this exposure while the platform has no Payments module and no real customers.
It is recorded so it is reconsidered deliberately rather than discovered.

**To close (when it matters):** the cheapest effective limit is a cap on live unpaid requests per
customer — a business number, not a constant — refused at creation with its own error code. A
per-customer-per-vehicle cooldown after an expiry is the next step if that is not enough. Neither is
worth building before there is a customer to abuse it.

### 65. Turning on EF retry-on-failure would break the create-booking transaction

**Status:** open · **Raised:** 2026-09-07 · **A trap, not a defect**

`UnitOfWork.ExecuteInTransactionAsync` runs its work through an EF execution strategy. No
`EnableRetryOnFailure` is configured, so today that strategy never retries and the delegate runs
exactly once. `CreateBookingHandler` depends on that: it captures its result in variables closed over
by the delegate, and it adds the new booking to the change tracker inside it.

Enabling retries — an ordinary thing to reach for against a managed Postgres — would break it
quietly. The handler already clears its captured state at the top of each attempt, but the
`DbContext` is not reset between attempts, so a retried delegate would re-add an entity the first
attempt had already tracked.

**To close, if retries are ever wanted:** give `ExecuteInTransactionAsync` a shape that survives one
— a fresh `DbContext` per attempt, or an explicit `ChangeTracker.Clear()` at the start of the
delegate — and re-check every caller of it, not only this one.

### 66. Two questions the create endpoint raises

**Status:** open · **Raised:** 2026-09-07 · **One answered, one still an owner decision**

Both surfaced while building `POST /bookings`. Neither is a defect; both are cheap now and awkward
once the customer app's date picker has shipped without them.

**~~There is no maximum rental length.~~ Closed 2026-09-07 at 90 days.**
`BusinessRules:MaxRentalDays` (90) is judged in `BookingPricer` on the BILLED day count, so a customer
is refused on the same number they were quoted, and the quote, the catalogue listing and the create
endpoint all inherit it from one place. It is published on `/app-config` beside the other two bounds
so the date picker cannot offer a span the server refuses. Ninety days is a proposal, not a
principle: long hires are a real business, and the number is one line in configuration.

**~~Nothing judges a pickup against the gallery's operating hours.~~ Closed 2026-09-07.**
The owner chose: **refuse a self-pickup outside the gallery's hours; never judge a delivery against
counter hours.** The reasoning is physical rather than commercial — a customer cannot collect keys
from a closed office, but a gallery driving a car out may do that whenever it likes.

`PickupHoursPolicy` holds the rule and `BookingPricer` applies it, which puts it in the one place a
quote and a create both pass through, beside the other gallery-specific rules. The catalogue SEARCH
deliberately does not apply it: a search spans galleries and has no single schedule to judge against.

Both ends of a self-pickup are checked, not only the collection. The customer brings the car back to
the same counter, so a return booked for 03:00 is the same impossibility — it is just discovered
three days later. They are reported separately (`booking.pickup_outside_opening_hours` and
`booking.return_outside_opening_hours`) so a customer is told which date to move, and each refusal
carries the gallery's hours for that day, because "outside opening hours" alone tells somebody they
are wrong without telling them what would be right.

**The accepted cost:** a gallery whose hours are set wrongly refuses its own self-pickup bookings.
That is loud, immediately visible to them, and fixable from their own console — the better failure
against a customer turned away at the counter.

### 67. A dealership is not told a request arrived

**Status:** closed · **Closed:** 2026-09-07 — `NotificationKind.BookingRequested` now has a producer.

`CreateBookingHandler` stages a notification for the owner and every ACTIVE employee in the same
transaction as the insert, so a request and the alert about it land together or not at all. A
deactivated employee is not told (spec 4.2), and a refused booking tells nobody.

It goes through a new `DealerTeamNotifier.NotifyTeamOfCustomerActionAsync`, which differs from the
team feed in the way that matters: **nobody is named.** Every other row snapshots the actor's name so
it still reads correctly after that person leaves; doing that with a customer would copy their name
into a table that is never deleted from, which is a promise about their data this platform has not
made (spec 7). The row says "A customer" and carries no actor id. The dealership sees whose booking
it is on the booking itself, where closing an account removes it.

The customer-side twin is item 60, still open: nothing tells the CUSTOMER their approval is waiting
for money.

The original report follows.
