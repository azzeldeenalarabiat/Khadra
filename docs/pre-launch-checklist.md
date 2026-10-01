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

**Half of this is already done, and the pattern to copy is in the tree (2026-09-11).** The disclosure
log added for item 86 ships with both guards from the start —
`document_access_entries_no_truncate`, `FOR EACH STATEMENT`, sharing a generic
`khadra_table_is_append_only()` function — and a `TRUNCATE` against it was verified to fail. It was
NOT added to `audit_entries` in the same migration on purpose: this item carries an owner decision
about the reseed workflow, and silently making a developer's database reset fail is not a side effect
to slip into an unrelated change. The seeder it refers to was deleted on 2026-09-05, so the question
is now only "what still truncates, and what should it do instead" — which is the owner's to answer.
When it is answered, the trigger is two lines beside the existing one.

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

**Status:** closed · **Closed:** 2026-09-08 — the `reviews` table exists and ratings on the platform are real.

`ReviewConfiguration`, a migration, `ReviewRepository`, `GalleryReviewReader` and the customer
endpoints (`POST`/`GET /api/v1/bookings/{id}/review`, `GET /api/v1/galleries/{id}/reviews`) all
exist, and `CatalogueReader` fills `AverageRating` and `ReviewCount` from one grouped query per page
rather than the nulls it used to send.

Three decisions worth naming, because each was a fork:

- **`Rating` is an OWNED type over one column, not a value converter.** With a converter,
  `review.Rating` is the column and `review.Rating.Value` is an unreadable member access on a
  converted value, so `AVG()` and `COUNT()` do not translate and EF falls back to loading every
  review to average them in memory — on the query the catalogue runs for every card on the screen.
- **Hidden reviews still count towards the average.** Spec 3.2 moderation removes abusive TEXT; spec
  4.1 makes the score something a dealer can never edit. Excluding a hidden review's rating would
  hand a gallery a way to erase a bad score by reporting the comment attached to it.
- **No reviewer is named on a public review.** Who left it is not something the platform asked a
  customer's permission to publish, and a name beside the dates of a rental says more than either
  fact alone. `GalleryReviewDto` therefore carries no name and no id, which is the whole point of it
  being a different shape from `ReviewDto`.

The dealer's review OF a customer (spec 5.6) has a domain model and no endpoint — that half is now
item 71.

The original report follows.

**Status:** was open · **Raised:** 2026-09-03

The `Review` aggregate exists in `Khadra.Domain/Reviews/` but has no `DbSet`, no EF configuration, no
table and no data. Spec 4.1 says a dealer's rating is computed from customer reviews and is never
editable by the dealer, so until this exists there is no rating to show. The Admin dealers list says
"No reviews yet" rather than inventing a number.

**To close:** build the Reviews context (configuration, migration, repository, the read model that
computes a dealer's average) and replace the placeholder.

### 4. No settlement job

**Status:** closed · **Closed:** 2026-09-08 — `BookingSettlementService` runs all four queries on a timer.

A hosted service in `Khadra.Infrastructure/Scheduling/` drives `SettleDueBookingsCommand` every
`Scheduling:SettlementIntervalSeconds` (60), plus one pass at startup so a process that was down over
a deadline does not wait a whole interval to notice. Every rule stays in the aggregate; the service
only decides how often to ask.

It became urgent rather than tidy when the customer app arrived. No car was ever stranded — the
availability predicate reads the clock — but a booking whose window had closed still READ as
`Requested` or `Approved`, and a phone's "Upcoming" list would show a dead booking indefinitely. A
client must never invent the expiry for itself: the status is the server's word.

Each booking commits on its own, so a concurrency conflict with a dealer acting at the same instant
defers one booking to the next pass instead of rolling back everything the pass had done. Both
parties are notified in that same transaction.

**Still open, and deliberately:** the service assumes ONE instance. Two processes running it
concurrently is not a correctness problem — the transitions are idempotent and the concurrency token
makes the loser retry — but it is duplicated work, and a leader election belongs with the deployment
story. Revisit before running more than one API process.

The original report follows.

**Status:** was open · **Raised:** 2026-09-03

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

**Not the whole document surface any more (2026-09-10).** Item 63 needed a gallery to read a renter's
licence, and that grant is not stable for a session — it ends the instant `Booking.IsLive` does — so
it is served by `GET /bookings/{id}/renter-documents/{documentId}`, which re-runs the full
authorization per request and mints no link at all. That route is therefore **out of scope for this
item**: there is no signature to bind, and no storage key in the browser to leak. Three flows still
use the signer and still want this fix: the admin dealer review, the customer's own paperwork, and
dispute evidence. Do not "harmonise" the renter route back onto the signer to tidy this up — that
would reintroduce both problems at once.

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

**Half closed, 2026-09-20.** The `sid` claim exists. `JwtAccessTokenIssuer` writes the refresh
family id, `AuthTokenFactory` supplies it on both the sign-in and the rotation path (a replacement
inherits its family, so the value is stable for the life of a session), `ICurrentActor.SessionId`
reads it, and `GetMySessionsHandler` marks `SessionSummary.IsCurrent` from it. Both clients mark a
row only when that is TRUE and infer nothing from its absence, because a token minted before the
deploy carries no claim — a state that lasts one access token. `OnTokenValidated` does not look at
the claim and must not start refusing requests over it.

The customer app also sends a `User-Agent` of its own now (`Khadra (<os> <version>)`, from
`deviceStamp`), so a row on Registered Devices names an operating system rather than the literal
word "Khadra", which is what the screen used to render for every session this app created.

**Still to close:** "revoke all others", which the claim now makes possible, and the immediacy of a
revocation itself. If the owner wants immediate revocation, add an
`AnyAsync(FamilyId == sid && RevokedAt == null)` check beside the security-stamp check in
`OnTokenValidated` — that is a per-request query, so it is a deliberate trade, not a tidy-up.

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

**Progress (payments Phase 7, 2026-09-29).** Issued receipts now go through exactly that outbox:
`financial_document_deliveries`, queued in the transaction that issues the receipt, worked by its own
background service, retried with backoff, and ended Sent, Skipped or Failed, with every attempt kept in the
append-only `financial_document_delivery_attempts` that the administrator reads. The account emails —
verification, invitations, password resets, booking notices — still send inside the request, and bounces
are reported back for none of them: "Sent" still means only that the provider ACCEPTED a message.

### 39. The delivery fee has no history

**Status:** open · **Raised:** 2026-09-06

A gallery owner can change what they charge for delivery at any time, from `/dealer/delivery`.
`DealerDeliveryChanged` carries the new amount, but nothing subscribes to it, so there is no record
of what a gallery charged last week or who changed it. Bookings are safe — each freezes the fee it
was made under — so this is about accountability, not correctness.

Same gap as item 6 (listing edits are not logged), and the same fix serves both.

**To close:** persist dealer-side changes to an activity trail the owner and an administrator can
read, fed from the domain events these actions already raise.

### 40. PARTLY CLOSED — email leaves under a borrowed sender; the English-only half is fixed

**Updated 2026-09-08.** Every one of the five account emails is now BILINGUAL: Arabic first, then a
rule, then English, in both the HTML and the plain-text part. The subject line carries both. Verified
by delivering one and reading it back out of the mailbox.

Both languages in one message rather than one chosen per recipient, because there is no language
stored on an account — the console and the app each keep the reader's choice in their own browser —
and adding a column would still pick WRONG for the two invitation emails, which go to somebody who
has never used the platform. The `dir` attribute is on the Arabic BLOCK, never on the document, or
the English half below it would flip with its punctuation and its link text.

The borrowed sender is unchanged and is what keeps this item open.

The original entry follows.

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

**CLOSED 2026-09-20.** `ResendAdminInvitationCommand` and
`POST /api/v1/admin/admin-users/{id}/resend-invitation` exist, and the Admin users screen offers the
button on every row whose invitation is still open. The invalidate-then-issue pair that both kinds
of invitation need now lives in one place, `InvitationReissuer`; the employee provisioner delegates
to it.

Four refusals, each with a test. An administrator who has CHOSEN A PASSWORD — read from
`PasswordChangedAt`, not `IsEmailVerified`, because resend-verification gates on the address rather
than the role, so an invited administrator can prove their mailbox and still hold no password, and
that person is exactly who the button is for. A deactivated account, because `AcceptInvitation` does
not look at status and a fresh link would quietly put it back into service. A user who is not an
administrator. And a relay that refuses the message, which FAILS the command with 503
`admin.invitation_email_not_sent` while the reissued link stands, so the next press can deliver it —
the opposite of `AdminBootstrapper`, which retires a token it could not deliver because its only
retry is the next boot. The reissue is audited as `AdminInvitationResent`, committed with the token.

The console reads `InvitationEmailSent` now as well, and says plainly when an account was created
and nothing was posted. Item 47 is the same gap on the EMPLOYEE side and is still open.

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

**Updated 2026-09-08 (second pass). Substantially closed: 411 scanner hits down to 75, and 28 of
those 75 are the route-title literals below, which are dead weight rather than English on a screen.**
The dictionaries went from 1,202 keys to 1,505, in both languages, and `ar.ts` is still typed against
`en.ts` so a missing translation cannot ship.

Measured rather than asserted: at `/`, `/forgot-password` and `/register` in Arabic there is now
exactly ONE Latin word on the page, and it is the language switcher naming the language you would
switch to.

Three systematic fixes, each of which was worth more than the strings it removed:

- **`I18nService.statusLabel`.** Every status on every screen arrives as a server `Enumeration.Name`
  and a dozen screens rendered it raw, so an Arabic page read "PendingReview" in Latin script mid
  sentence. One helper now, with the CamelCase-split fallback the audit screen already used, so a
  status this console has never heard of still reads as words. `scope: 'booking'` disambiguates
  `Approved`, which means a licence check on a dealer and a gallery saying yes on a booking, and
  which Arabic does not share a word for.
- **The dispute workspace's resolution presets were a `readonly` FIELD calling `this.t(...)`.** That
  is the exact bug this item already records on the fleet filter chips: a field initialiser resolves
  once at construction, so switching language with the screen open left the old words on it. Two of
  the four labels were already keyed and already frozen. They are a `computed` now.
- **Module-level `describe(error)` helpers now take `t`.** Almost every feature file has one mapping
  the server's error CODE to a sentence, and every one of them was English prose in a function with
  no `this`. The mapping from a stable code is the only part of a refusal that can be translated at
  all, which is why the server's own `title` stays the last-resort fallback.

Notification sentences became messages with NAMED PARAMETERS rather than concatenated template
literals, because Arabic does not put the actor and the object where English does; and the attention
queue's counts became plural messages with all six Arabic forms rather than an `n === 1` ternary,
which picks the wrong form for every count from two upwards.

Two pieces of tooling made it tractable and are worth keeping: `key-copy.js`, which keys the copy
shapes `key-components.js` never knew (`k`/`v` rows, ternary arms, bare returns, status maps) and
REFUSES the two that would be bugs — a field initialiser, and anything outside the class body — and
`key-describe.js` for the `describe(error)` pattern. `missing-ar.js` lists the English keys with no
Arabic. `add-en.js` and `add-ar.js` now skip a key that already exists, after a hand-written batch
and the codemod both named one and broke the build.

`core/i18n/` holds 1,202 keys in both languages. EVERY template is keyed -- all 54 of them -- along
with the shell, the auth screens, the dealer gate, both not-built placeholders, the dashboard KPI
cards and attention queue, the activity verbs, relative time, the confirmation dialogs, list columns
and the pagination. `ar.ts` is typed against `en.ts`, so a missing translation fails the build, and
`dictionaries.spec.ts` also fails on a key that drifts, a dropped placeholder, or an Arabic plural
missing one of its six forms.

What is still English, measured by `node scan-i18n.js` in `Khadra.Dashboard` (75 hits, 27 files --
the scan is deliberately noisy, and most of what is left is a false positive):

- **Route `title` literals in `app.routes.ts` (28 of the 75).** Dead weight rather than a bug, and
  now VERIFIED rather than assumed: loading `/register` in Arabic gives the document title
  "سجّل معرضك · Khadra", so `TranslatedTitleStrategy` is resolving it from `SCREEN_TITLES` and the
  literal never reaches a tab. Removing them is tidying, not translation.
- **Units, separators and the scanner's own blind spots.** `km`, `JOD`, `·`, `to`, `of`, and the
  `t('key', { param })` calls the regex splits in the middle of. All false positives.
- **A handful of strings the codemod correctly refused**, each a template literal whose value lands
  in a different place in Arabic, in a screen whose copy is otherwise keyed.
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

**Updated 2026-09-18 (Wave Two). CLOSED for the console: the scanner reports 0 findings across every
screen, against a 12-entry allowlist in which each entry carries its reason.** Measured with a
STRONGER scanner than the one that reported 75 and later 505: it had two blind spots and could not
have found what it was missing.

- It never read inside a callback, so everything built in `rows.map((row) => …)` was invisible — a
  timeline calling `toLocaleString('en-GB')`, a document tile reading "Provided".
- It never read the words AROUND an interpolation, so `{{ radius() }} km`, `{{ a }} of {{ b }}` and
  "by", "to" passed unseen.
- It could not see money built by hand — `${value.amount} ${value.currency}` — which is the shape the
  "JOD 0−" defect came in by.

Both halves were fixed, plus false-positive rules so machine values are not reported as copy (a
membership test, a typed value list, the filter a chip sends, a route parameter). Re-measured on the
UNTOUCHED code, the honest baseline was **525 findings in 57 files**, not the 505 previously reported.

What moved, beyond keying the copy: one shared clock (a chance that runs out ends as "Expired"; a
promise that can be broken reads "Overdue by 13h"), status and enum names through `statusLabel` /
`enumLabel` with a spelled-out fallback, refusals held as `ProblemSnapshot` facts and worded at render
time, every date, number, percentage and amount through `FormatService`, and six API contracts that
used to send English sentences now sending facts the console words (see items 100–108).

Left open deliberately: the server's own messages (item 100), and the English written INTO records at
the moment of an action (item 103).

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

**Status:** CLOSED 2026-09-11 · **Raised:** 2026-09-07 · **Superseded by:** item 90

The owner shortened `BusinessRules:PaymentWindowHours` from 24 to 2 on 2026-09-11, taking the other
side of the trade this item described. Everything below was the reasoning for 24 and is kept as the
record of it; what the shorter window now costs is item 90, which is open.

It was configuration and not a constant precisely so this could happen without a release, and it did.

The original entry follows.

`BusinessRules:PaymentWindowHours` is 24. The number the flow wants is closer to one hour: a car sits
held against nothing for the whole window, and a dealership that has said yes deserves an answer
sooner than the next day.

It is 24 because there are no push notifications. A customer learns their booking was approved only
by opening the app. A one-hour window would auto-expire most bookings approved overnight or during a
working day before the customer ever saw the approval, wasting the dealer's decision and losing the
rental — a worse failure than a car held a day too long.

**To close:** once approval reaches a customer's phone (item 43's notification producers plus a push
transport), shorten the window and say so on the screen that counts it down.

### 60. Nothing tells a customer their approval is waiting for money

**Status:** closed · **Closed:** 2026-09-08 — the customer is notified, and the app has the screen.

`NotificationKind` gained five customer-facing kinds, each WITH its producer:
`YourBookingApproved` and `YourBookingRejected` from `BookingDecisionHandlers`, and
`YourBookingExpired`, `YourBookingMarkedNoShow` and `YourBookingCompleted` from the settlement
service (item 4). `DealerTeamNotifier.NotifyCustomerAsync` raises them naming the GALLERY rather than
the member of staff who pressed the button: which employee answered is the dealership's internal
business, and the gallery's name is already on the customer's booking.

The booking screen shows the deposit and `PaymentDeadline` from the BOOKING — never
`PaymentWindowHours` from `/app-config`, which is today's setting and not the one this booking froze
— and says plainly that paying is not available in this version. There is no Pay button, because
there is nothing behind one. See item 69.

The original report follows.

**Status:** was open · **Raised:** 2026-09-07 · **Blocked:** the booking-creation slice being usable

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
platform is meanwhile telling the customer, on `GET /api/v1/app-config`, that they have a full
payment window. Two answers to one question is the failure this endpoint exists to prevent.

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

### 63. CLOSED — a dealer cannot see the documents they are required to check

**Status:** CLOSED 2026-09-11 · **Raised:** 2026-09-07 · **Was: hard requirement before real launch**

Spec 5.1 makes the dealer the party who checks a renter's licence. They could not. `CustomerDocument`
already scoped viewing to the customer themselves *and to a dealer with an active booking request* —
the rule was written, and no endpoint implemented it. There was no way, anywhere in the platform, for
the gallery handing over a car to look at the licence of the person taking it.

**What was built (2026-09-10).**

```
GET /api/v1/bookings/{bookingId}/renter-documents          → what is on file
GET /api/v1/bookings/{bookingId}/renter-documents/{id}     → the bytes
```

`[Authorize(SecurityPolicies.DealerStaff)]`, and then, on **every** request including each
byte-serving one:

1. `DealerMembershipResolver` — the owner, or an **active** employee (a deactivated one has no
   standing, spec 4.2);
2. the booking is this dealership's, else `404 booking.not_found` — never 403, which would confirm
   another gallery's id is real;
3. `Booking.IsLive(now)`, else `409 booking.renter_documents_not_available`;
4. the renter is read **off the booking**, and a document that is not theirs is `404
   documents.not_found`.

Nothing the caller sends establishes a relationship. There is no customer id and no bare document id
to substitute, so a dealer session cannot be turned into a lookup service over the customer base —
the same argument, and the same shape, as `customer-reputation`.

**It deliberately does NOT mint a signed link,** which is what this entry used to propose. Two
reasons, both fatal to that plan. A five-minute link is a five-minute grant that outlives the
predicate which issued it, so a gallery would keep access after the booking stopped being live — the
opposite of "for as long as it is live, and no longer". And `HmacDocumentLinkSigner`'s token is
`base64url(storageKey)`, which would put `customers/{customerUserId}/…` into a gallery's browser: the
raw storage key, carrying a customer identifier the platform is otherwise careful never to hand them.
Streaming re-checks the rule instead. See item 14, and `CustomerDocument`'s own doc comment.

**It is deliberately NOT gated on the dealership being able to trade.** `CanActOnBookings` guards
approve and reject, and copying it here — the obvious thing to do, since `customer-reputation` uses
it — would leave a suspended gallery handing over a car it is still permitted to hand over while the
platform refused to show it who the renter is. That is this item reopened in a different costume.
`RenterDocumentAccessTests.A_suspended_dealership_can_still_see_the_licence_of_a_car_it_is_holding`
exists to stop it.

Console: the booking detail screen's "Renter's documents" panel. Nothing is fetched until the gallery
presses **View driving licence** — these are photographs of a private individual's passport, and a
screen that loaded them on open would put them in front of whoever walked past the counter. English
and Arabic, RTL-correct through logical properties.

**What closed it (2026-09-11): the dealership can now RECORD that it checked.**

```
POST /api/v1/bookings/{bookingId}/renter-documents/{documentId}/review
```

Same four gates, plus two decisions that are the point of the feature:

- **It is not a verification, and the wording is load-bearing.** "Reviewed by dealer" /
  "تمت مراجعتها من المعرض". The panel states beside the control that Khadra does not confirm a
  document is genuine, current or registered with any authority. `CustomerDocument.MarkVerified` is
  still unreachable and the platform's own `Status` field is **not on the dealer's DTO at all** — it
  can take the value `Verified`, and beside a gallery's own review that would read as a platform
  guarantee.
- **The review is keyed on the UPLOAD, not just the document.** `CustomerDocument.Replace` keeps the
  row id and swaps the file, so a review keyed on the id alone would survive a re-photograph and show
  "reviewed by dealer" over a picture nobody at the dealership had seen — this item's own failure,
  reopened by the feature meant to close it. Replacing the file clears the badge.

No request body: the reviewer comes from the validated token and the timestamp from the server clock,
so neither is forgeable. A repeat answers 200 with the review already there, timestamp intact. The
first reviewer keeps the credit. Reviewing is allowed exactly where viewing is, enforced inside
`Booking.RecordRenterDocumentReview` as well as the handler, so the action disappears with the access.

Every view and every review writes an append-only row — see item 86, closed with this.

**What this item does NOT claim, and never did.** Nothing on the platform verifies a document.
The booking-creation guard still asks only that files were UPLOADED, and item 27 still decides
whether an admin ever reviews them. What has changed is that the gallery can now look, and that
looking and checking both leave a record. "A dealer checked the licence" is something the platform
can now show; whether the document was genuine is not, and the screen does not pretend otherwise.

Verified end to end on 2026-09-11 against a live server: owner and employee reviews, a second
dealership refused 404 on both routes, the customer 403, anonymous 401, a cancelled booking 409, and
the disclosure log written for all of it. Tests: `RenterDocumentAccessTests`,
`RenterDocumentReviewTests` (application and domain), `DocumentAccessPersistenceTests`,
`RenterDocumentEndpointTests`, `renter-documents.presenter.spec.ts`.

### 64. A free hold is renewable, so the ceiling is per request, not per customer

**Status:** open by decision · **Raised:** 2026-09-07 · **Accepted exposure** · **Updated:** 2026-09-11

A request holds a car for up to 48 hours unanswered, and an approval holds it for the payment window
unpaid, none of it paid for. Nothing then stops the same customer requesting the same car again the
instant it expires, so one account can keep a car off the market indefinitely at no cost.

The ceiling was 72 hours when this was raised. Shortening the payment window to 2 hours on 2026-09-11
made it 50, which narrows the exposure without removing it: the 48 hours a gallery may take to answer
is the bulk of it, and that window has not moved. The figure is the sum of two settings and is
deliberately not written down in the code.

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

---

## Customer mobile app (2026-09-08)

### 68. CLOSED — how late is late enough to report non-delivery

**Status:** closed · **Raised:** 2026-09-08 · **Settled:** 2026-09-08, owner, at **15 minutes**

`Booking.ReportDealerNonDelivery` was unguarded until 2026-09-08: it checked only that the booking was
Confirmed and the reason non-blank, so a customer could file it at any time after the deposit cleared —
days before the car was ever due.

That was reachable and expensive. A customer past their free-cancellation window, facing an assessment
of the whole deposit for cancelling, could instead report non-delivery; the record would then say the
GALLERY failed, with 25–50% of the rental assessed against them, and the gallery would have to open a
dispute to clear a claim made without them. `MarkNoShow` — the mirror-image accusation, that the
CUSTOMER never appeared — has always been guarded by `Period.Start + NoShowTimeout`.

It is now guarded by `Period.Start + BookingTerms.NonDeliveryGrace`, frozen onto each booking like
every other rule, and configured as `BusinessRules:NonDeliveryGraceMinutes`.

**The owner settled it at 15 minutes on 2026-09-08.** The setting changed unit to carry the answer:
it was `NonDeliveryGraceHours`, an `int`, and fifteen minutes is not expressible in it. The asymmetry
with the gallery's mirror figure of 8 hours (`NoShowTimeoutHours`) is deliberate and the owner's — a
customer standing at a counter knows within minutes that nobody is coming, while a gallery holding a
car cannot tell a late renter from an absent one for hours.

Three things closed with it:

- `NonDeliveryGraceMinutes` had **no startup validation**, and the provider dereferences it with `!`.
  A deleted key surfaced as a `NullReferenceException` on the first booking priced rather than at
  startup, unlike the four sibling settings that are all checked. It is checked now.
- The customer app offered the report button on any Confirmed booking, so with a non-zero grace it
  became a button the server refuses. `BookingDto` now carries `CanReportNonDelivery` and
  `NonDeliveryReportableFrom`, both server-judged like `IsAwaitingDecision`, and the app shows a
  disabled control saying when instead.
- `nonDeliveryTooEarly` said "the rental has not started yet", which stopped being true the moment
  the grace stopped being zero. Reworded in both languages.

### 69. PARTLY CLOSED — a customer cannot pay, because there is no merchant account

**Status:** open, narrowed · **Raised:** 2026-09-08 · **Narrowed:** 2026-09-08, Payments shipped

**Payments was built on 2026-09-08 with the owner's explicit approval.** What was "the context does not
exist" is now exactly one missing thing: a merchant account. The aggregate, the state machine, the
idempotency guards, the refunds, the sweep and both endpoints are complete, tested and running; every
checkout is refused with `payments.provider_unavailable` (503) and the startup log says
`PAYMENTS ARE NOT ACCEPTED` on every boot.

**To close:** choose a provider, get an account, set `Payments:Provider`, `Payments:ApiKey` and
`Payments:WebhookSecret` in user-secrets or the environment, and write one class implementing
`IPaymentProvider`'s four methods. Nothing above that class changes. Then register the webhook URL
`POST /api/v1/payments/webhooks/{provider}` with the provider.

**Two things that still need the owner**, both recorded as items 76 and 77 below.

**One correctness note worth carrying forward.** The webhook handler does NOT retry a lost
concurrency race, and that is deliberate rather than an omission. It did retry in the first draft;
a test of the exact race — the settlement job expiring a booking at the instant a capture lands on
it — proved that wrong. EF keeps the in-memory mutations after a failed `SaveChanges`, so the second
pass decided against dirty state: it found a payment that already read `Applied`, could not orphan
it, and would have left a customer's money attached to an expired booking with no refund recorded.
The exception escapes, the endpoint answers 5xx, and the provider re-delivers into a fresh scope with
a clean context. The receipt rolled back with the transaction, so the re-delivery is not a replay.

The original entry follows, because its prohibition still stands.

**Status:** open by design · **Raised:** 2026-09-08 · **Depends on:** Payments

Stated here as one line rather than left implied across items 59, 60 and 62. Until Payments ships,
every customer whose booking is APPROVED will watch it expire, because there is no way to pay the
deposit and `Confirmed` is reachable only in tests. The gallery's decision is wasted each time.

The customer app shows this honestly — the amount, the deadline, and a sentence saying that paying is
not available in this version and that nothing is owed when the booking expires — rather than a button
that cannot work.

**Nobody must "unblock" this with a cash path.** `ConfirmDepositPaid` is keyed by a payment id for a
gateway's retries, and item 2 records the owner's decision that no deposits are taken out of band
before Payments exists.

**To close:** no real customers reach `Approved` before Payments ships.

### 70. There is no design export for the customer app

**Status:** open · **Raised:** 2026-09-08

`docs/design/` holds the Claude Design export for the Admin Console and the Dealer Console, and the
frontend rules make that export the source of truth for how those look. There is no equivalent for the
customer app, so it was designed against the platform's own tokens instead — the same white / green /
black palette, the same logo, the same spacing and radius geometry, read from
`Khadra.Dashboard/src/styles/_tokens.scss` and restated in `Khadra.Mobile/lib/core/theme/`.

That is a defensible way to keep one brand across three surfaces, and it is NOT the same as having a
design. Recorded so nobody later assumes the app was built to one.

**To close:** get an export, or record that the app's own theme file is the source of truth for it.

### 71. CLOSED — the dealer's review of a customer

**Status:** closed · **Raised:** 2026-09-08 · **Built:** 2026-09-08

Shipped as two things rather than one, because the interesting half was never the writing.

**A reputation READ MODEL**, `GET /api/v1/bookings/{id}/customer-reputation`, keyed on the booking
and not on a customer id -- an endpoint taking a customer id would be a lookup oracle over the whole
customer base for anybody with a dealer session, and no check inside it could undo that. A gallery may
read it while they are deciding about, or holding, a booking with that person, and no longer
(`Booking.IsLive`). It answers with aggregates only: a rating, five counts and an account age. No
contact details, no documents, no per-review rows, and no dates on individual ratings, because a rating
dated last Tuesday tells this gallery when the customer rented from a competitor.

**A rating with NO free text.** The audience is other galleries, so prose here would be unverified
writing about a named private individual circulating between competing businesses, unmoderated when
written and invisible to its subject. The platform has replaced free text with closed codes twice
already, for weaker reasons.

**A blind window**, `Review.VisibleFrom`, which was the thing missing from the original framing.
Without it the dealer direction is a retaliation tool: customer reviews publish instantly, so a gallery
reads its new one-star, finds the booking, and rates that customer one star before their reputation
reaches anyone else.

**Counts read `Penalty.AttributedTo`, never the status.** Counting by status would have blamed
customers the domain explicitly refused to blame -- a delivery no-show is `Unattributed`, and
`ReportDealerNonDelivery` is the CUSTOMER reporting the GALLERY while the row reads
`CancelledBy = Customer`.

**The customer can see their own**, at `GET /api/v1/customers/me/reputation`. A semi-private score
somebody cannot see is what privacy law objects to, and it is the only way they learn to dispute a
wrong no-show while the window is open.

What remains is items 80 to 83 below: the window length is the owner's, and nothing can moderate a
review yet.

The original entry follows.

**Status:** closed · **Raised:** 2026-09-08

Spec 5.6 makes reviews mutual: the dealer's review of a customer is visible to other dealers to inform
their approve/reject decisions. `ReviewDirection.DealerRatesCustomer` exists, the table stores it, and
nothing writes or reads it.

It is not a matter of passing a different string to the existing endpoint. The audience is other
dealerships rather than the public, which makes "who may read this" a question the customer-facing
reader does not answer, and showing one gallery what another said about a named customer is a privacy
decision the owner should make explicitly.

**To close:** a dealer-console endpoint and reader with their own visibility rule, and the owner's
answer on what a gallery may see about a customer before approving them.

### 72. Email is still not changeable, for any role

**Status:** open · **Raised:** 2026-09-08 · **Extends:** item 44

`PUT /api/v1/customers/me/profile` now lets a customer correct their own NAME and PHONE. Email is
deliberately absent, for exactly the reason item 44 gives: it is the sign-in identifier and the
password-reset destination, so moving it on the strength of a live session alone would hand the
account to anyone holding an unlocked phone.

The customer app shows the address read-only and says so. The `VerificationPurpose.EmailChange` flow
item 44 describes is still unbuilt, and is now missing from two consoles and an app rather than one.

### 73. Push notifications do not exist

**Status:** open — BUILT 2026-09-23, not yet live · **Raised:** 2026-09-08 · **Makes worse:** items 59, 69

**2026-09-23:** FCM push is built end to end (push devices tied to sessions, a transactional outbox,
an FCM HTTP v1 sender, the app's registration lifecycle) and reminders go out by push and email.
The item comes off when production has `Push__Provider=Fcm` with the `khadra-prod` project and a
real phone has received an approval push. Android only: iOS (APNs) is item 138.

The app has an in-app notification feed backed by `GET /api/v1/notifications`, which it polls. There
is no push channel, so a customer learns their booking was approved only by opening the app.

That is what forced the deposit payment window to 24 hours rather than the one hour first proposed
(item 59): a shorter window would auto-expire most bookings approved overnight before the customer
ever saw them.

**The owner shortened it to 2 hours anyway on 2026-09-11 (item 90), so this is no longer a nicety.**
The window now assumes a channel that does not exist. An approval email over the transport that
already sends verification mail would close most of it and is far cheaper than FCM/APNs; it should be
built before the first real gallery approves anything.

**To close:** a push transport (FCM/APNs), a device-token registration endpoint, and a decision about
which `NotificationKind`s justify waking a phone.

## Full-lifecycle test (2026-09-08)

Raised while driving the whole platform end to end through the real screens: register an office,
verify by email, submit the gallery, approve it as an administrator, publish a car, book it as a
customer, approve the booking as the dealer. Both items below are deliberately NOT blockers; the
owner has seen each and said so.

### 74. CLOSED — the admin console below roughly 500px wide

**Status:** closed · **Raised:** 2026-09-08 · **Fixed:** 2026-09-08

Two breakpoints, each fixing one half of it, and nothing at 1440×900 changed — every rule is
`max-width`, so at desktop widths none of them apply. Measured before and after at 1440: sidebar
248px, labels visible, heading 22px on one line, `overflow-wrap: normal`, page padding unchanged.

**Below 900px the sidebar becomes a 64px icon rail.** That was the whole of the first half: at 491px
a fixed 248px sidebar left the CONTENT 243px, and the heading on the dealer-application screen got
106px of it, which is where "one word per line" came from. Measured after: content 427px, heading
302px, one line.

**Below 640px the header blocks stop competing for one row.** `.detail-head` wraps, and every direct
child gets `min-width: 0` — a flex child's default `min-width` is `auto`, its CONTENT width, so a long
heading refused to shrink and pushed the SLA badge over the status pill instead of wrapping. That one
line was most of the overlap. The SLA box and the action row then take the full width rather than
being pushed to the far end by a `margin-inline-start: auto` that strands them under a gap once the
row has wrapped.

Three things the fix had to get right beyond the obvious:

- **`overflow-wrap: anywhere`, not `break-word`.** Only the former lets an element's min-content width
  shrink, which is what stops a long booking reference scrolling the whole page sideways.
- **Accessible names survive the collapse.** `display: none` removes the label from the accessibility
  tree as well as from the screen, so at exactly the width where the icon is all that is left, every
  nav link would have had no accessible name. The label is now on the link itself as `aria-label` and
  `title`, and the visible span is `aria-hidden`.
- **RTL was free, and verified rather than assumed.** The rail uses logical properties throughout, so
  in Arabic at 375px the sidebar sits on the right and the badge stays inside it.

Verified at 375, 491 and 1440 in both directions: no horizontal page scroll, no overlapping elements,
no element whose `scrollWidth` exceeds its `clientWidth` except the deliberately-clipped group heading.

The original entry follows.

**Status:** closed · **Raised:** 2026-09-08 · **Was:** accepted by the owner

On the dealer-application screen at a 491px viewport the heading wraps one word per line, the
subtitle breaks a character at a time, and the review-SLA badge overlaps the status chip and the
breadcrumb. The sidebar is a fixed width, so almost nothing is left for content.

Measured at 1440×900 the same screen has no horizontal overflow at all and no element whose
scrollWidth exceeds its clientWidth: this is narrow-viewport only, not a desktop defect. Admins and
dealer staff are expected to work at a desk, which is why the owner has accepted it as it stands.

Recorded rather than fixed because the fix is a responsive pass over the console shell - a
collapsing sidebar and a breakpoint for the detail headers - which is design work, not a bug fix,
and it would be done to a brief rather than guessed at.

**To close, if it is ever wanted:** a breakpoint below which the sidebar collapses to icons or a
drawer, and header blocks that stack instead of competing for one row.

### 75. CLOSED — switching on delivery now offers it for cars already listed

**Status:** closed · **Raised:** 2026-09-08 · **Fixed:** 2026-09-08

`POST /api/v1/dealers/me/delivery/offer-on-listed-vehicles`, and a prompt on the Delivery page that
appears only when the SAVED settings say delivery is on and the server's own count of listed cars not
offered for delivery is above zero. The count travels on the delivery settings the page already loads,
so nothing fetches a fleet list to derive a number the API knows.

The per-car flag stays and is not weakened: this is a bulk EDIT the owner asks for, on a page that
tells them how many cars it will touch, not a rule keeping the flag in step. There is deliberately no
action the other way — a gallery switching delivery off keeps its per-car answers, or turning it back
on would silently re-offer the van its owner had excluded on purpose. Active cars only: a draft is not
advertising anything.

The original entry follows.

**Status:** closed · **Raised:** 2026-09-08

A vehicle carries its own `IsDeliveryEligible`, and the wizard sets it from whether the dealership
offers delivery AT THE MOMENT THE CAR IS SAVED. A gallery that lists cars first and turns delivery
on afterwards therefore has a fleet that is all pickup-only, with nothing on screen connecting the
two facts. It was hit in testing within minutes of enabling delivery.

The per-car flag is right and should stay: an office with one van it will not drive across Amman
needs to say so. What is missing is the bulk action, and the prompt that offers it.

The customer app no longer misreports this. It used to say "this office does not deliver" for both
causes; it now distinguishes a gallery that does not deliver from a car that is not offered for
delivery, so the screen is at least honest about which it is.

**To close:** an "offer delivery on my existing cars" action on the Delivery page, and a prompt
when delivery is switched on for a dealership whose published cars are all ineligible.

## Payments (2026-09-08)

Built with the owner's explicit approval, which `CLAUDE.md` requires. Everything below is a gap that
survives the build, not a gap in it.

### 76. HARD BLOCKER — there is no merchant account, so no deposit can be taken

**Status:** open · **Raised:** 2026-09-08 · **Blocks:** every Confirmed booking

**Also due before a real provider is connected** (recorded 2026-09-24, from the automatic
free-cancellation refund): item 157 (back off a refund the provider keeps refusing), item 159 (the
adapter's refund events must name the refund they settle), item 156 (the owner's policy for a
gallery or admin cancelling a paid booking inside the free window), and item 158 (hide or zero the
frozen commission and payout on a refunded cancellation).

**A precondition since payments Phase 8 (2026-09-30): item 208.** A customer's uncontested penalty is now kept
from the deposit when the dispute window closes, and the apps already installed still promise otherwise. Before
the first real provider takes a real deposit, the owner signs off the D1 wording, a build carrying it is
published, and the minimum supported version is raised to it — publish first.

`Payments:Provider` is `None`, and `UnconfiguredPaymentProvider` is the only implementation this build
ships. Every checkout answers 503 `payments.provider_unavailable`; the webhook answers 401, because
with no secret there is no way to tell a provider from anyone else who found the URL.

**Nobody may close this with a simulated provider.** One that captured and confirmed would be
indistinguishable, in every table and on every screen, from a real payment: bookings would read
Confirmed, galleries would prepare cars, and nobody could tell which rentals had money behind them.
That is the same prohibition item 2 records about cash paid out of band, in a different costume.

**One `Payments:Provider` value now does exactly that, and it does not close this item.** `SANDBOX`
was approved by the owner on 2026-09-21 so the booking and payment lifecycle could be clicked through
end to end before a merchant account exists. It is an exception to the prohibition, not a repeal of
it, and what makes it allowable is three mechanisms rather than an intention — each of which
`SandboxPaymentGuardTests` proves actually fires:

1. **Environment.** `Program.cs` refuses to start a Production host on it, beside the mail and
   document guards. Render leaves `ASPNETCORE_ENVIRONMENT` unset, which defaults to Production, so
   this bites exactly where it should.
2. **Data, in both directions.** `PaymentsStartupCheck` refuses to start whenever this database's
   payments and this process's provider are different kinds of money. The sandbox will not run on a
   database holding real payments — the guard that catches a local process pointed at the production
   connection string — and **nothing else will run on a database holding sandbox payments**, which is
   the direction that would otherwise bite on launch day: sandbox-confirmed bookings, reading
   Confirmed with a deposit marked paid and a gallery already notified, still there when a real
   adapter is switched on. A database used for sandbox payments stays on the sandbox for good.
3. **The record.** Every row it writes carries `SANDBOX` in `payments.provider` for as long as the
   row exists, and that one stored value is what `PaymentDto.isSandbox` and the boot log both read.
   No second flag, because a second flag can disagree with the first.

It is also published: `/app-config` carries `payments.mode` (`None`/`Sandbox`/`Live`), and the
customer app and both consoles show a standing banner on Sandbox and on nothing else.

**This item stays open until a real provider takes real JOD.** The sandbox takes none.

**To close:** an account with a provider that can take JOD; one class implementing the four methods of
`IPaymentProvider`; the three settings in user-secrets or the environment; the webhook URL registered
with the provider. Two details that will bite whoever writes the adapter:

- **JOD has three minor units.** Most providers assume two. The adapter's money conversion needs a
  round-trip test, and it must REFUSE an amount it cannot represent exactly rather than round it.
- **`ParseEvent` must verify over the exact bytes received.** The controller passes the raw body
  through unparsed for that reason; anything that deserialises and re-serialises breaks every signature.

**Three things a 2-hour payment window (item 90) adds to this, raised 2026-09-11.** None of them can
happen while `Provider` is `None` — no checkout can open at all — so they are conditions on closing
this item rather than defects today.

1. **The Pay button and the checkout door disagree for the last five minutes.**
   `BookingPaymentAvailability.ForAsync` answers `CanPay = true` for the whole of `now <
   PaymentDeadline`, while `OpenDepositCheckoutHandler` refuses from `PaymentDeadline -
   CheckoutClosesBeforeDeadlineMinutes` (5). The app shows a Pay button under a countdown reading
   "4 minutes left" and the tap answers 409. At 24 hours that margin was 0.3% of the window; at two
   it is 4%, and the countdown is now something customers will be watching. Fix: one helper that both
   sides call, a distinct `payments.checkout_window_closed` code, and `PayBy` reporting the instant
   the door actually shuts.

2. **A late approval can produce a booking nobody can pay — AN OWNER DECISION, not a fix to make
   quietly.** `MinimumBookingLeadTimeMinutes` lets a rental start two hours from the request, and the
   payment deadline is capped at the rental start. Approve such a booking inside the last five
   minutes and the gallery has said yes to something that cannot be paid for; it will prepare a car
   that expires. Worse with a real provider — several refuse a checkout session shorter than about
   thirty minutes, which would make the un-payable tail 35 minutes, a quarter of a two-hour window.
   The shape of a fix is `Approve` taking a minimum-payment-window parameter the way
   `BookingWindowPolicy` takes `minimumLeadTime`, refusing with `booking.too_late_to_approve` and
   leaving rejection allowed. It changes what a gallery may do, so the owner decides.

3. **The provider's own session floor bounds the door, not our five minutes.** Whatever provider
   closes this item, its minimum session lifetime has to be read and folded into the same helper.

### 77. Cancellation does not refund, and that is the owner's decision to make

**Status:** closed · **Closed:** 2026-09-26 — payments Phase 3, on the owner's three decisions.

**Decided and built (owner, 2026-09-26):** a PAID booking that ends before the car is collected
records what it owes in the save that ends it, through one seam (`BookingEndingRefunds`), decided by
the booking's own frozen state: the whole payment for a customer's free cancellation and for an
ADMINISTRATOR's cancellation with no penalty on the customer (`PlatformCancellation`); everything paid
above the deposit, with a processing fee the payment froze as refundable, for every other paid ending
(`EndedBeforePickup` — a late cancellation, a no-show, a gallery's cancellation, a non-delivery
report). Penalties and disputes stay deposit-based; a deposit-only booking gains nothing new. The
deposit itself goes back when the booking's own dispute window closes CLEANLY — no ticket that was not
withdrawn, no penalty against the customer — released by the settlement sweep
(`DisputeWindowClosed`). Three readings of "cleanly" the advisor confirmed and the owner should know:
a penalty against the OFFICE does not hold the customer's deposit; a DELIVERY no-show is assessed
against nobody, so its deposit goes back unless the gallery opens a ticket inside the window; and a
deposit held for a penalty against the customer has no way out once the window closes (item 164).

What follows is the history of the item before Phase 3.

**Decided (owner, 2026-09-24):** a customer who has PAID the deposit and cancels inside the
free-cancellation window gets the WHOLE deposit refunded automatically to the original payment method,
with no admin. The cancel handler records the refund (`RefundReason.FreeCancellation`) through
the ending-refund seam (then `DepositRefundSettlement`, since Phase 3 `BookingEndingRefunds`) in the
same save as the cancellation; the payment sweep sends it within a
minute under the refund's own id; a provider refusal leaves it owed and re-sent. The customer is told
when the provider settles it (`YourDepositRefunded`, push and email).

**Still open:** everything else decision 3 covers — a customer cancelling after the window, a booking
that ends with no ticket at all, a no-show, and (item 156) a gallery or admin cancelling a paid booking
inside the window, which today assesses nothing and leaves the deposit held.

**Also open since a booking can be PAID IN FULL (2026-09-24, noted 2026-09-25):** a fully paid booking
cancelled after the free window, or marked no-show, is penalised on the deposit only, and the rest of
the payment has no refund path yet — the office's rental revenue if the rental happens, the
customer's own money if it does not (`BookingDisputeSettlement.DepositHeldFor` holds only the
deposit). The owner decided on 2026-09-24 that the amount above the deposit is refundable; the payments
Phase 3 ("refunds generalised") builds it. Until it does, no screen may say that money is held for
settlement or forfeit: the dealer's "Held pending settlement" line sits under a Deposit label and must
not be widened to the whole payment. The FREE cancellation of a full payment is not part of this gap:
it already returns the whole capture (less a fee taken as non-refundable), and every screen now says
so from `confirmingPayment.refundOnFreeCancellation`.

### 78. `DepositHeldFor` will need to read what is left, not what was taken

**Status:** closed for refunds · **Updated:** 2026-09-26 — still by the booking's rule; Phase 3 added the other refunds and kept it. One pre-existing gap about a SECOND ticket is logged on its own (item 169).

Phase 3 (2026-09-26) added the other refunds of an applied payment, and `DepositHeldFor` still reads
the booking's rule rather than "captured minus refunded": zero once the ending returned the whole
payment (`Booking.ReturnsWholePayment`, a free or an administrator's cancellation), and zero when the
window already released the deposit — which only a ticket opened in the very instant the window closed
can meet, and whose resolution must then be all zeros (`DisputeUseCaseTests`). The money above the
deposit is never part of it: it is refunded at the ending, never disputed. What it does NOT yet read is
an earlier RESOLVED ticket on the same booking: opening a dispute checks only for a live one, so a
second ticket inside the window would be offered the whole deposit again — found in the Phase 3
browser run, older than Phase 3, and logged as item 169 for the owner to decide.

The first refund of an APPLIED payment arrived with the free cancellation (item 77). `DepositHeldFor`
now reads zero for a booking whose deposit that cancellation returned
(`Booking.ReturnsDepositOnCancellation`), and `DepositHeldOnFreeCancellationTests` pins it.

The formula this item used to propose — captured less outstanding and settled refunds — was wrong and
is not what was built: `Payment.RefundedTotal` does not count a FAILED refund, so a refund the provider
refused would make the deposit read as fully held again, a dispute could split it, and the sweep would
still be re-sending the refund: two instructions for one deposit. A deposit a free cancellation owes
back is not held, whatever the provider has said so far. **Reopen** if any other refund of an applied
payment is added (a decision on the rest of item 77), with a partly-refunded test.

### 79. A stale attempt whose provider says it WAS captured needs a human

**Status:** open, accepted · **Raised:** 2026-09-08

The payment sweep asks the provider what became of a session it believes is dead. If the provider says
the money moved, the sweep logs at Error and does nothing: resolving a capture outside the webhook
handler would be a second, weaker copy of the most delicate code in the system, and it has no booking
loaded and no receipt to write.

This is the right call for now — the provider's own retry is the proper path, and the log line is what
tells somebody to look if none arrives. It is recorded because "the log tells somebody" is not a
process.

**To close, if it is ever wanted:** an admin screen listing payments whose provider state and stored
state disagree, with a button that replays the provider's event through the real handler.

## Customer reviews and reputation (2026-09-08)

Closes item 71. Everything below is a gap that survives the build.

### 80. OPEN OWNER DECISION — how long the review window is

**Status:** open · **Raised:** 2026-09-08 · **Shipped proposal:** 14 days

`BusinessRules:ReviewWindowDays` does two jobs at once, which is why there is one number and not two:
it is how long after a rental either party may review it, AND how long a first review stays hidden
waiting for the second. They are the same instant seen from both ends, and that equality is what makes
"nobody sees the counterpart before submitting" true by construction.

Fourteen days is a PROPOSAL. Nothing in the spec names a figure and the owner has not been asked. Too
short and one party loses the chance to answer; too long and a gallery's public rating lags a fortnight
behind reality.

**To close:** ask the owner, set the number, record it in `docs/spec-amendments.md`.

### 81. Nothing can moderate a review, in either direction

**Status:** open · **Raised:** 2026-09-08 · **Pre-dates this work**

`Review.Hide` and `Unhide` exist on the aggregate and no endpoint calls either. Every reader honours
them — the public listing drops the text, and the customer reputation drops the whole rating — so the
machinery works; there is simply no way for an administrator to reach it.

That was survivable while only the public direction existed and the worst case was an abusive comment.
It is worse now: a retaliatory rating of a CUSTOMER follows a real person to every future approval, and
hiding it is the only remedy the model has. There is no appeal path and no admin screen.

**To close:** an admin endpoint and screen to hide and unhide a review, with an audit entry, and a
route by which a customer can ask for one to be looked at.

### 82. `Review.Revise` is unreachable

**Status:** open, harmless · **Raised:** 2026-09-08

No endpoint calls it. It is now guarded twice — the edit window AND the reveal — so if it is ever
exposed it cannot be used to answer a counterpart after reading it. Recorded so the second guard is
not mistaken for dead code and removed.

### 83. Reputation is on the booking detail only

**Status:** open, deliberate · **Raised:** 2026-09-08

A gallery sees a customer's history when they open the booking, not on the pending list. That is a
scope decision rather than a privacy one — the list is already filtered to their own live requests —
and a per-row summary would be a batch reader like `SummariseAsync`, built if galleries ask for it.

## Production forwarded headers (2026-09-10)

### 84. CLOSED — the console loaded but nobody could sign in, and the obvious fix was a trap

**Status:** CLOSED 2026-09-10 · **Raised:** 2026-09-09

`GET /bff/antiforgery` — the first call the sign-in page makes — answered 500 in production with
`AntiforgeryOptions.Cookie.SecurePolicy = Always, but the current request is not an SSL request.`
The console itself rendered perfectly, which is what made it read as an antiforgery bug rather than
a networking one.

It was neither. Render's router connects to the container over **loopback**, so the transport peer
is `::1`; item 32 had deliberately cleared the framework's default trust of loopback on the grounds
that inheriting it silently was not a decision. Correct in principle, and it made `::1` an untrusted
sender, so `X-Forwarded-Proto: https` was discarded, `Request.IsHttps` stayed false, and antiforgery
refused to issue a `__Host-` cookie over what it believed was plain HTTP.

Two rounds of diagnostics were needed to see it, and the first was wrong in an instructive way: it
logged the FIRST request and spent its one shot on a platform health probe — loopback, no forwarding
headers at all — arriving a minute before the browser request that failed. Read literally, that line
argued for trusting a health check. Retargeting it at `/bff/antiforgery` and at non-loopback requests
produced the real chain.

**The trap.** Trusting `::1` alone makes sign-in work, and it is tempting to stop there. It resolves
every visitor to `10.24.207.134`, Render's load balancer. `CredentialSubject.PartitionKey` keys a
sign-in on `{address}|{account}`, so a constant address turns the ten-per-fifteen-minutes cap into a
**per-account bucket shared with the attacker**: aimed at a named administrator it holds that account
shut indefinitely, and the victim cannot move out of the way. The address-only limits collapse
outright, and every session records the same address, so "Where you are signed in" — the screen the
deployment guide told people to measure this with — stops being able to tell anyone anything.

`ForwardLimit` does not rescue it. It is a **ceiling, not a target**: trust is re-checked at each hop
against the address just consumed, so the walk stops at the first untrusted address however high it
is set. Measured on the real chain with only loopback trusted, 1, 2 and 3 resolved the identical
address. The guidance in `docs/deployment.md` — "raise it by one and repeat" — could therefore never
have worked, and has been rewritten.

**Closed by:** trusting the whole chain that actually exists — loopback, `10.0.0.0/8`, and
Cloudflare's 22 published ranges — with `ForwardLimit` at exactly 3. Not "trust everything": the
guarantee is checked per request from the transport peer outward, and the only way to exploit it is
to *connect* from a trusted range. Nothing on the internet can source `::1` or `10.x`, and only
Cloudflare can source Cloudflare's. Cloudflare **appends** the address it accepted the connection
from rather than replacing it, so anything a caller invents lands to the left of their true address
and the right-to-left walk reaches the truth first.

The limit is exactly 3 and must not be padded: when a visitor's own address falls inside a trusted
range — a Cloudflare Worker fetching this origin — the walk does not stop at them, and one hop too
many consumes a value they supplied. Measured: 3 resolves the Worker, 4 resolves the prepended junk.

Both services now cross-check the resolved address against `Cf-Connecting-Ip`, which Cloudflare
overwrites on ingress, and warn on disagreement — that catches a stale Cloudflare list and a request
that never traversed Cloudflare, the only two ways this configuration fails. The header is
cross-checked and never believed: using it as the source of truth would let a caller name its own
partition, and its safety would rest on the service's Render plan, since paid plans can receive
private-network traffic that free ones cannot.

Regression tests: `Khadra.Tests/Security/ForwardedHeaderChainTests.cs` — eleven, run against the
middleware directly, pinning the captured production chain, the loopback-only trap, the inert
higher limit, the prepend attack, the exact-limit edge case, and the untrusted caller.

**Also fixed here:** the API carried the same defect, and `Khadra.WebAPI/Program.cs` claimed in a
comment that it "answers the BFF, never a browser directly" — false, because the customer mobile
application calls it directly over the public address through the identical chain. Its list did not
cover a `::1` peer either, so every mobile customer resolved to `::1` and `UseHsts` emitted no
header, which is visible from outside and is how to confirm the fix landed.

**Still open, for the owner:** which plan the `khadra` API service is on. Free Render web services
cannot *receive* private network traffic, so if it is free the BFF must reach it over the public
address and console traffic resolves Render's shared outbound address at the API. That cannot be
fixed with a longer trust list — every Render tenant in the region shares those addresses, so
trusting them would be a real spoofing hole. The fix is a paid plan or an explicit BFF-to-API trust
channel. See `render.yaml`.

## Mail delivery (2026-09-10)

### 85. CLOSED — every email "sent" successfully to nobody, for days

**Status:** CLOSED 2026-09-10 · **Raised:** 2026-09-10

`POST /api/v1/auth/forgot-password` answered `202 Accepted` and logged
`Handled ForgotPasswordCommand in 975 ms`, and no request to Brevo followed. Registration,
administrator invitation and password reset were all in the same state: reported as sent, delivered to
nobody.

The transport is chosen by matching a string from configuration, and the branch that caught everything
unrecognised registered `LoggingEmailSender` — **silently**. That sender accepts every message, writes
it to the log, and returns success, so `AuthEmailDispatcher` reported a successful send, the handler
returned success, and the screen told people a link was on its way.

Three things had to line up to hide it for as long as it did, and each is worth keeping:

1. **The startup check said `Email ready`.** `LoggingTransportProbe` reported `IsReady = true`, so
   `MailStartupCheck` logged, at Information: *"Email ready. Email:Provider is 'Logging'. Messages are
   written to the log and delivered to nobody."* A sentence at war with itself, whose first two words
   are the only part anyone scanning a boot log reads.
2. **`Logging` is the DEFAULT.** `EmailOptions.Provider` defaults to it, so forgetting the variable
   selects it. Nothing outside Development objected.
3. **`docs/deployment.md` showed the value in shell quoting** — `Email__Provider="Brevo"`. A shell
   strips those quotes; a dashboard field keeps them, and `"Brevo"` matches no transport, so following
   the documentation exactly produced the fault.

**Closed by:** an unrecognised `Email:Provider` now throws at startup in every environment, naming the
value in quotes so a stray quote or trailing space is visible rather than inferred; `Logging` — or the
setting being absent — throws in **Production** specifically (`IsProduction`, not `!IsDevelopment`, so
test hosts on "Testing" and "Staging" still boot, having legitimately no mail server); the probe
reports NOT ready and names the value it read; the docs give bare values in a table with the shell/
dashboard distinction stated; and `render.yaml` lists `Email__Provider` beside the key.

**Also closed here — the reason was unknowable from outside.** `ForgotPasswordHandler` returned
success from three different situations and left no trace of which. It now logs one line per outcome
(1300 unusable address, 1301 no account, 1302 account exists but is soft-deleted, 1303 handed to the
transport, 1304 transport refused), keyed by correlation id and, where known, user id. The public
response is unchanged and must stay unchanged — it is the same 202 whatever happened, or the form
becomes a way to ask who holds an account — so the reason goes where an operator can read it and a
caller cannot. The address, the raw token and the link are never logged, and
`ForgotPasswordDiagnosticsTests` asserts their absence rather than trusting the next person to
remember. `AddJsonConsole` now includes scopes, so events 1100 and 1200 carry the RequestId that
produced them instead of only a timestamp.

**Answered while tracing, so nobody re-asks:** neither account status nor email verification blocks a
reset. A suspended person still owns their address, and somebody who never confirmed theirs is exactly
the person likely to have forgotten the password — refusing them would lock the account permanently.
`CanAuthenticate` still refuses a suspended sign-in, so the link buys them nothing. Pinned by test.

**Open, for the owner — LIVE CREDENTIALS IN THE LOG.** If the Logging transport was the cause, every
message it handled is in Render's log **including its link**, and those links are working credentials
for as long as their token lives: password reset 60 minutes, email verification 24 hours, employee and
administrator invitation **7 days**. A logged administrator invitation is a seven-day credential to
become the platform administrator. Search the retention window for event 1200 or the text
`accept-invitation` and, for anything still inside its lifetime, consume it:
`UPDATE verification_tokens SET consumed_at = now() WHERE consumed_at IS NULL AND purpose = '…'`.
Nobody loses anything — they ask again. Do **not** "invalidate by requesting a new one" while the
Logging transport is still selected, because that writes a fresh live link to the same log.

*2026-09-17:* the transport no longer does this. `LoggingEmailSender` now logs the subject and the
recipient's domain, never the body, as event 1400 rather than 1200, and `LoggingEmailSenderTests`
asserts the token is absent. That stops new links reaching the log. It does nothing about lines an
older build already wrote, so the search above — event 1200 — still stands for any retention window
that reaches back before the change.

**Related:** item 37 (the enumeration trade-off in reporting a failed send). Worth appending there:
the no-account path is one database round trip while the account path is three plus an HTTPS call, so
the response time distinguishes them — a channel that is open always, not only while mail is broken.
Queuing the send, as item 37 already proposes, removes the provider call from the request path and
shrinks it; equalising the remaining round trips would be the rest.

## Privacy (2026-09-10)

### 86. CLOSED — a gallery reading a renter's passport leaves only a log line

**Status:** CLOSED 2026-09-11 · **Raised:** 2026-09-10

Item 63 gave galleries a way to open a renter's driving licence and identity document. That is a
spec 7 disclosure of a named private individual's papers to a commercial third party, and the only
record of it was a structured log event — which rotates, is not queryable by the person it concerns,
and on the current hosting is retained for days rather than years.

**`document_access_entries` is now that record.** One row per disclosure and per review:

| | |
|---|---|
| who | `actor_user_id`, `actor_name` (snapshotted), `actor_role` |
| for whom | `dealer_id` |
| under what authority | `booking_id` — the relationship IS the authorization |
| about whom | `subject_user_id`, the renter |
| what | `document_id`, `document_type`, `document_uploaded_at` |
| which act | `action` — `Viewed` or `Reviewed` |
| when | `occurred_at`, plus `correlation_id` to tie it to a request |

Append-only twice: `KhadraDbContext` refuses to persist a modified or deleted `IAppendOnly` record
(the guard used to name `AuditEntry` in its own type argument and is now general), and the Postgres
triggers `document_access_entries_append_only` and `document_access_entries_no_truncate` refuse
`UPDATE`, `DELETE` **and** `TRUNCATE` for anything that bypasses the application. All three refusals
were exercised against the real database on 2026-09-11.

Its own table rather than a row in `audit_entries`, and that is a readership decision. `AuditEntry`
is the admin's trail — `IAuditFeedReader` puts its newest rows on the dashboard's activity glance,
and three or four rows per handover would turn that feed into a list of licence openings. This record
exists to answer a question the admin trail was never for, and `subject_user_id` is indexed so
"who has seen my documents" is one index scan rather than a join through `customer_documents` —
the table most likely to have been emptied by the time anybody asks.

**Four decisions worth keeping, because each could be quietly reversed.**

1. **No record, no disclosure.** The `Viewed` row is committed BEFORE any byte is streamed, and a
   failure to write it fails the request. The insert goes to the same database on the same connection
   as the reads that just authorised the call, so it adds no failure mode that was not already there.
   Serving anyway would make "the log shows nobody looked" stop meaning anything during exactly the
   incident somebody would later be investigating.
2. **No de-duplication.** Three opens are three rows. Repetition is itself evidence, and collapsing at
   write time cannot be undone; noise can always be collapsed at read time.
3. **The metadata LISTING is not logged, and "every access is logged" would be an overclaim.** It
   fires on every booking-detail open with nobody pressing anything, and it describes what exists
   rather than revealing it. If that is ever wanted, it needs its own `Listed` action — never folded
   into `Viewed`.
4. **Nothing on the row can reach the file.** No storage key, no URL, no bucket, no content type. A
   disclosure log carrying the key would be a second way into the documents it exists to protect.

**Two things this closure deliberately leaves open, so nobody assumes otherwise.**

- **Retention is "for ever", by construction.** There is no delete path, and the user ids on a row
  outlive account deletion. Defensible for a disclosure record, but it is a decision, not a default —
  reopen it if a retention policy is ever written.
- **The customer cannot see their own log yet.** The table and the index are shaped for that question;
  no endpoint asks it. That is the half that makes the record worth having to the person it is about,
  and it needs a screen in the mobile app before it is real.

Tests: `DocumentAccessPersistenceTests` (round trip, and the append-only guard on both tables),
`RenterDocumentReviewTests` (a row per view, none for a refused view, none for a repeat review, none
for the listing, and no storage key anywhere on a row).

## Customer app completion (2026-09-11)

### 87. Owner decisions on the shortlist

**Status:** CLOSED 2026-09-11 · **Raised:** 2026-09-11 · **Built:** 2026-09-11, at the owner's request

Favourites were built as a `Shortlist` bounded context — aggregate, migration, four endpoints and the
app screens — with three questions answered by DEFAULTS rather than by the owner. All three were put
to the owner on 2026-09-11 and settled the same day. This is the decision record; the wording here is
the authority, because the design handoff it came from is not in `docs/design/`.

1. **Account-only, no device-local list. CONFIRMED.** The catalogue itself is anonymous and stays so,
   but saving needs an account: a list kept on the phone would vanish with it, show nothing on a
   second one, and become a merge problem the day the real one arrived. The heart on an anonymous
   card goes through the ordinary sign-in redirect.
2. **The cap is 100** (`BusinessRules:MaxShortlistEntries`), raised from the proposed 50. A guard
   against a list nobody can read and a table one account can grow without bound, not a judgement
   about how many cars are worth comparing. Validated at startup like its siblings, inside
   `[Range(1, 500)]`. The refusal carries the figure and the app repeats what it was told; the app
   holds no copy of the number.

   The list is one unpaged response. A hundred rows is about five search pages — acceptable, and the
   point at which paging would be needed if the cap ever rose further, because the heart set is told
   the whole list at once (`markSaved`).
3. **A car that stops being bookable keeps its row, is shown as "Currently unavailable / غير متاحة
   حاليًا", and cannot start a booking.** Entries are NEVER auto-removed: `Maintenance → Hidden →
   Active` is a normal round trip, and a list that edited itself on the way through would lose a
   customer's choices without asking.

   The row still NAMES the car and its gallery, as the approved design does. That is safe because
   nothing reaches a shortlist that the public catalogue did not return first — `SaveVehicleCommand`
   refuses any id `ICatalogueReader.GetAsync` answers null to — so every name on the list is a car
   this customer was already shown. What stays private is the REASON, and there is no field on the
   wire that could carry one: hidden, in maintenance, suspended and soft-deleted must remain
   indistinguishable.

   The name is read live rather than snapshotted, so a gallery correcting a listing corrects the saved
   row; and it is read **past the soft-delete filter**, which is a correctness requirement rather than
   a convenience. With the filter respected, a deleted car would come back unnamed while a hidden one
   came back named, and deletion would become the single de-listing reason a customer could tell
   apart. `ShortlistPersistenceTests` asserts all four cases render alike.

**Two consequences the owner should know, accepted as they stand:** a car that is deleted for good
reads "Currently unavailable" for ever, because the platform will not say "deleted" and will not
auto-remove; and unavailable entries count toward the cap, so a customer at 100 with thirty gone
clears them one at a time. A "remove all unavailable" affordance would close the second and is not
built.

### 88. The shortlist is personal data and goes with the account

**Status:** open · **Raised:** 2026-09-11 · **Depends on:** item 18 (account deletion)

A shortlist is browsing interest about a named person. It is not soft-deletable — a removed entry is
a customer saying they are no longer interested, and a tombstone of that retains personal data for no
purpose anyone could name — but the LIST itself has to go when the account does, and account deletion
does not exist yet.

**To close:** whatever closes item 18 deletes `customer_shortlists` and its entries with the account.

### 89. A shortlist has no dates, so it can say nothing about availability

**Status:** closed by design · **Raised:** 2026-09-11

Recorded so nobody later "improves" it. A saved car carries no rental period, and
`CatalogueVehicle.IsAvailable` is null without one for exactly that reason — false would be a lie. The
saved list therefore shows today's daily rate and says nothing about whether the car is free; a
customer picks dates on the vehicle screen as they would from any other entry point.

Adding a per-entry "available on the dates you last searched" would mean storing a search on a
shortlist entry, which is a different feature wearing this one's clothes.

### 90. Two hours to pay, and no way to tell the customer their booking was approved

**Status:** open · **Raised:** 2026-09-11 · **Depends on:** item 73 (push notifications)

The owner set the payment window to **two hours** on 2026-09-11, replacing the twenty-four that came
in with the reserve-now-pay-later reordering (`BusinessRules:PaymentWindowHours`). The trade is
deliberate and in the platform's favour: a car that a customer never pays for goes back on the market
in two hours instead of a day, which is the difference between one lost rental and three.

What it costs is the other half of the same fact. **There is no push channel** (item 73), so a
customer finds out their request was approved by opening the app. Two hours is easy to miss entirely
— asleep, at work, driving. Every approval missed that way is a gallery's decision wasted, a car held
for nothing, and a customer who believes they booked a car and did not.

**An approval email was built on 2026-09-11 and closes most of this.** `BookingEmailDispatcher`
sends the customer a bilingual message over the transport that already sends verification mail,
naming the car, the reference, the deposit, and the deadline as an ABSOLUTE Amman date and time
rather than only as a duration. It is sent after the commit and its failure is logged and swallowed:
a mail server having a bad minute cannot undo a gallery's decision.

What is left:

- **Item 38 (no mail queue, no retry) is now on the critical path.** One failed send is probably one
  expired booking. The transport retries within a single call (`Email:MaxAttempts`) and nothing
  retries after it returns.
- **There is nowhere to link the customer to** (item 91), so the email names the reference and says
  to open the app.
- **Push is still the only channel that reaches a phone in a pocket** (item 73). Email complements it
  and does not replace it: a customer who reads mail once a day is still a customer who misses a
  two-hour window.

The other two surfaces already built: `GET /bookings/next` puts the deposit on the landing screen the
moment the app opens, ranked above everything else; and the booking screen re-reads itself when its
countdown runs out, so a spent clock never sits under "Deposit is due".

**To close:** item 73 ships, or the owner accepts the loss rate with email and the landing surface.
This is not a reason to lengthen the window — that decision is made — it is a reason the window needs
channels behind it.

**Do not confuse this with `MinimumBookingLeadTimeMinutes`, also 120.** That one is how far ahead of
now a rental may start. They are the same length today by coincidence and moving one must never move
the other; `Khadra.Tests/Application/Bookings/PaymentWindowTests.cs` holds them apart.

### 91. Customer deep linking — Android App Links and iOS Universal Links

**Status:** open · **Raised:** 2026-09-11 · **Owner decision recorded** · **Blocks:** the useful half
of item 90

**`App:CustomerAppBaseUrl` stays EMPTY for now**, by the owner's decision on 2026-09-11, and the
approval email names the booking reference and tells the reader to open the app. That is honest and
it costs one tap on a two-hour clock.

**`App:ClientBaseUrl` must never be used for a customer link.** It is the dealer and admin console; a
customer following it lands on a sign-in that refuses them, which reads as the platform being broken
at the exact moment they are trying to pay. Two settings exist so that this cannot happen by
accident, and `BookingEmailComposerTests` asserts the console URL never appears in a customer email.

**What it should become.** One customer-facing **HTTPS** link per booking —
`https://<customer host>/bookings/{id}` — that opens the booking in the Khadra app when it is
installed and, eventually, falls back to the customer website when it is not. Not a custom scheme
(`khadra://`): a custom scheme cannot fall back, shows an ugly failure when the app is absent, and is
not clickable in many mail clients. The same URL has to work in both cases, which is exactly what
App Links and Universal Links are for.

**To close, in order:**

1. **A host.** Decide the customer-facing domain and stand up TLS on it.
2. **Android App Links.** Serve `/.well-known/assetlinks.json` with the app's package name and the
   release signing certificate's SHA-256 fingerprint; add an `intent-filter` with
   `android:autoVerify="true"` for `https://<host>/bookings/*` to `AndroidManifest.xml`. Verify with
   `adb shell pm get-app-links <package>` — a debug build signed with a different key will NOT verify,
   which is the usual reason this looks broken in testing.
3. **iOS Universal Links.** Serve `/.well-known/apple-app-site-association` (JSON, no extension, no
   redirect, `application/json`) with the Team ID and bundle id; add the Associated Domains
   entitlement `applinks:<host>`.
4. **Routing in the app.** `go_router` already routes `/bookings/:id`; wire the incoming link to it
   and decide what an unauthenticated open does — the session-aware redirect should send them to
   sign-in and then ON to the booking, not drop them on the catalogue.

   That half is already built, as of 2026-09-12, and the Get Started gate does not get in its way:
   the gate is consulted at `/` ONLY, so a link opening any other route is untouched, and a guarded
   route opened before the session resolves parks its destination on `/?next=…`, which carries it
   through `/welcome` to the sign-in form and back out to the booking. `test/entry_gate_test.dart`
   covers both. What remains here is the link arriving at the app at all, which is steps 1-3.
5. **The web fallback**, whenever the customer website exists: the same URL rendering the booking, or
   at minimum a page that names the reference and links to the store.
6. **Then set `App:CustomerAppBaseUrl`** to that host. The composer renders the button the moment it
   is non-empty; `BookingEmailComposerTests` already covers both shapes.

**It is not only this email.** Booking confirmations, dispute updates and any later push notification
want the same link, so whatever closes this should be one helper rather than a second URL built by
hand somewhere else.

### 92. The four-hour lead time, and why it is four

**Status:** CLOSED 2026-09-11 · **Raised:** 2026-09-11 · **Owner decision recorded**

Settled: `MinimumBookingLeadTimeMinutes` is **240** and `PaymentWindowHours` is **2**. Both halves of
the four hours are the owner's, and the reasoning is theirs too — a last-minute request gives the
gallery roughly two hours to decide while preserving the customer's full two-hour payment window
before the rental starts.

The two numbers are RELATED, which is the part worth keeping in mind. A gallery may not approve
unless the customer can still have the whole payment window, so the last approvable instant is
`rental start − PaymentWindow`, and the DIFFERENCE between these two settings is the entire time a
gallery has to answer a request made at the earliest a customer may book for. At 120 and 120 that
difference was zero: every such request would have been born unapprovable and the customer would have
been told the office never responded.

**The invariant stays.** Startup refuses any configuration where the lead time does not STRICTLY
exceed the payment window, with a message naming the relationship rather than the numbers
(`Khadra.Infrastructure/DependencyInjection.cs`). `ShippedConfigurationTests` asserts the inequality
rather than the values, so moving either number deliberately does not fail a test that was only ever
about the pair.

**What it costs the customer, accepted:** a car cannot be booked for three hours from now. The
earliest is four.

**If either number moves,** the other is a decision too. Raising the payment window without raising
the lead time shrinks the gallery's decision window by the same amount, and the platform refuses to
boot once it reaches zero.

### 93. The PDF upload path has never been exercised on a real device

**Status:** open · **Raised:** 2026-09-11 · **Held open by the owner, 2026-09-11** · **Not a defect,
a gap in what has been proved**

The owner's instruction: this stays open until the app is installed on an actual Android device and
the whole path is walked — **select PDF → upload → persist → reopen/view**.

PDF selection and upload shipped on 2026-09-11: `DocumentPicker` offers a file entry when the server
advertises a non-image type, reads the content type from the file's leading bytes, and checks it and
the size against `/app-config` before anything leaves the phone.

**What has been proved:** the sheet, its server-driven labels and the whole accept/refuse decision, by
unit tests and by driving the running app in a browser in both languages.

**What has NOT been proved, and must not be described as end-to-end until it has:** the native
ANDROID path, start to finish, on a real handset —

1. the system file picker opening with the right filter,
2. a PDF chosen from Drive, Downloads and a third-party file manager,
3. the bytes reaching `POST /customers/me/documents` intact,
4. the row persisting with `content_type = application/pdf` and the right size,
5. the document reopening through a signed link and rendering.

The browser harness cannot do it: `file_picker` opens the chooser with `input.click()`, which a
browser refuses without a trusted user gesture, and the harness cannot produce one against a Flutter
canvas. Steps 3–5 are equally unproven on iOS.

**To close:** run the five steps on an Android device and an iPhone, and record the result here.

#### Run 2 -- Android emulator (Pixel, API 36), 2026-09-12: CLOSED for Android

All five steps pass on the corrected build.

| # | Step | Result |
|---|------|--------|
| 1 | System file picker with the right filter | **PASS** |
| 2 | PDF chosen from Downloads | **PASS** |
| 3 | Bytes reach `POST /customers/me/documents` | **PASS** -- 201 |
| 4 | Row persists with the right type and size | **PASS** -- `PDF · 1 KB`, survived a force-stop AND an in-place 9.2.4 to 10.3.2 upgrade |
| 5 | Reopens through a signed link and RENDERS | **PASS** -- `GET /api/v1/documents/...` answered 200 and the native viewer displayed the file’s own text |

Step 5 was the failure. The fix was on the CLIENT and the backend keeps both protections: the app
fetches the bytes over its own authenticated connection instead of handing the URL to a browser that
can never carry a bearer token. Repeated in Arabic, where a freshly minted link also answered 200.

The bytes are written to the app’s private cache under the document’s id, and
`DocumentViewer.discard()` empties that directory when the session ends -- verified on the device:
after sign-out `cache/khadra_documents` no longer exists.

**Still open for iOS.** The whole path is unexercised there, and the first Mac build is also where
the iOS 14 floor gets tested.

#### Run 1 -- Android emulator (Pixel, API 36), 2026-09-11, debug build

Four of the five steps pass. The fifth fails, for a reason that needs an owner decision.

| # | Step | Result |
|---|------|--------|
| 1 | System file picker opens with the right filter | **PASS** -- Android's SAF opens; the sheet advertises `JPG · PNG · WEBP · PDF · up to 8 MB`, read from `/app-config`, not written down in the app |
| 2 | A PDF chosen from Downloads | **PASS** |
| 3 | Bytes reach `POST /customers/me/documents` | **PASS** -- `201`, 9.3 s on the emulator |
| 4 | Row persists with the right type and size | **PASS** -- the listing reads back `PDF · 1 KB`, "Waiting to be checked", and survives a force-stop and cold restart |
| 5 | Document reopens through a signed link and renders | **FAIL** -- the browser receives `401 Unauthorized` (ProblemDetails), never the PDF |

Steps 2-4 were also exercised from a third-party source only in the sense that Downloads is one;
Drive and a third-party file manager are still untried, and so is the whole path on iOS.

#### Why step 5 fails, and why it is not a bug in either half

`DocumentsController.Download` is deliberately protected twice. Its own XML comment says so: the
signature proves the link was minted by this platform for this file and has not expired, and the
inherited authentication requirement proves there is still a live session behind the request. There
is no `[AllowAnonymous]`, and `Program.cs` sets a `FallbackPolicy` requiring an authenticated JWT
bearer user, so the endpoint needs BOTH the signature and a bearer token.

The customer app opens the link with `launchUrl(..., mode: LaunchMode.externalApplication)`. An
external browser has no bearer token. So the link is minted correctly (`GET .../link` returns `200`),
handed to Chrome, and refused.

Neither side is wrong on its own. The dealer and admin consoles open the same endpoint successfully
because they are browsers carrying a cookie session through the BFF. Nobody reconciled that with a
native app whose only credential lives inside the app.

**This is not a regression from the file_picker or secure-storage upgrade.** It has been true since
the View button was written; it could not be observed until an Android build existed to press it on.

#### The owner's decision

1. **Fetch in-app.** The app already holds the bearer token: download the bytes itself and render or
   share them. No change to the security model, but it is a real piece of client work -- a PDF
   viewer or a share sheet -- and it is the only option that keeps both checks.
2. **Make the signature sufficient.** Add `[AllowAnonymous]` to `Download` and rely on the signed,
   expiring URL alone. One line, and it deletes the second check the comment argues for: a leaked URL
   then works for anyone until it expires.
3. **Mint a single-use token bound to the session** and accept it in place of the bearer. Keeps two
   factors, costs a new concept and a store for the tokens.

Until this is decided, the View button is dead on Android and the app should not claim otherwise.

**Unblocked 2026-09-11:** item 94's first hop landed and `flutter build apk` produces an artifact
again, so the five steps are attemptable. They still need a handset; nothing below claims otherwise.

### 94. The Android app does not build, and nothing caught it

**Status:** open, BLOCKING · **Raised:** 2026-09-11 · **Decided by the owner, 2026-09-11** ·
**First hop landed; held open for the on-device migration test**

`flutter build apk` fails. `file_picker` 11.0.3 applies its own Kotlin Gradle Plugin, and this
toolchain has moved to Flutter's built-in Kotlin, which no longer links a plugin that does:

    WARNING: Your app uses the following plugins that apply Kotlin Gradle Plugin (KGP): file_picker
    error: cannot find symbol
      flutterEngine.getPlugins().add(new com.mr.flutter.plugin.filepicker.FilePickerPlugin());
    symbol: class FilePickerPlugin

No Android artifact of any kind can be produced. It is not a Dart error: `flutter analyze` is clean
and all 127 Flutter tests pass, because the failure lives in Gradle and is only reachable by actually
building for Android. Nothing in the branch ever did, which is how PDF upload came to be described as
shipped while the app it ships in could not be compiled.

**Why it is not a one-line bump.** Every `file_picker` 12.x needs `win32 ^6.3.0`;
`flutter_secure_storage` 9.2.4 pulls `flutter_secure_storage_windows`, which pins `win32 ^5.0.0`.
Version solving fails for every 12.x while secure storage stays on 9.x. So unblocking the build means
moving `flutter_secure_storage` across a major version -- and that package holds the REFRESH TOKEN
(`lib/core/session/session_store.dart`, `AndroidOptions(encryptedSharedPreferences: true)`).

Its changelog makes the hop a decision rather than a version number:

- **v10** deprecates `encryptedSharedPreferences` "due to Jetpack Crypto package deprecation" and
  offers `migrateOnAlgorithmChange: true` to move existing data onto a new cipher backend.
- **v11** removes the option outright, and says: "If you used a version prior to v10, upgrade to v10
  first so existing data is migrated."

Going 9 to 11 in one hop is the path its own authors tell you not to take: every refresh token already
on a device is stranded, and the at-rest protection of an auth credential changes without a migration.

**The owner's decision, and it is an auth decision under this project's own rules:**

1. two hops -- 9.2.4 to 10.x with `migrateOnAlgorithmChange: true`, verified on a device, then 11.x; or
2. one hop to 11.x, accepting that existing test installs are signed out; or
3. something that removes the collision without touching the token store.

**The owner chose (1), the two-hop, on 2026-09-11**, with the reason stated: this is authentication
credential storage, and the package author's own migration path is not to be skipped merely because
there are no production users yet. The advisor had recommended (2) -- see the dissent below, which is
recorded because it bears on the SECOND hop, not the first.

#### First hop, landed 2026-09-11

`flutter_secure_storage` 9.2.4 to **10.3.2**, `file_picker` 11.0.3 to **12.3.0**. `flutter build apk`
produces an artifact again, and the KGP warning is gone.

The root cause was never `file_picker` "applying KGP" as the warning implies. 11.0.3's
`android/build.gradle` applies the Kotlin plugin only `if (!isAgp9OrAbove)`, and this app is on AGP
9 -- so the `if` never fires. But Flutter's tooling decides whether to apply `kotlin-android` on a
plugin's behalf by REGEX over its build file, and the regex matches the line inside the dead branch.
Nobody applied Kotlin, so nothing compiled the `.kt` sources, so `FilePickerPlugin` did not exist.
`android_file_picker` 1.1.1 (pulled in by 12.x) reads `android.builtInKotlin` itself and applies the
plugin when it is false, which is the fix for exactly this configuration.

Three other things changed with it, each recorded because none is a version number:

- `AndroidOptions` now states `migrateOnAlgorithmChange: true`, `migrateWithBackup: true` and
  `resetOnError: true` rather than leaning on defaults. `migrateWithBackup` keeps a copy while the
  one-time move runs, which is what protects the credential if the app is killed mid-migration.
  `resetOnError` is a BEHAVIOUR CHANGE: v9 defaulted it to false.
- The `file_picker` call site moved to `pickFile` (singular). In 12.x `pickFiles` returns a list and
  `allowMultiple` defaults to **true**, so the old "take the one file" guard would have silently read
  a two-file selection as a cancel. `withData` is gone; bytes come from `readAsBytes()`.
- `FilePicker.clearTemporaryFiles()` is now called after the bytes are read. The picker copies the
  chosen file into this app's cache to give it a path, and a passport should not outlive its upload.

**iOS minimum rises from 13.0 to 14.0.** `file_picker_darwin` 1.2.0 requires it. Three
`IPHONEOS_DEPLOYMENT_TARGET` lines in the pbxproj were changed; this drops iOS 13 devices and is a
product decision the owner should confirm before release. It cannot be verified from Windows -- the
first Mac build is the test.

**A build setting was wrong independently of any of this.** `android/gradle.properties` asked for
`-Xmx8G -XX:MaxMetaspaceSize=4G` on a machine with 8 GB of RAM. The daemon died mid-build with
"Gradle build daemon disappeared unexpectedly" and a JVM crash log saying "insufficient memory".
That is not a Gradle bug and not a plugin problem; it would have hit any contributor on a 8-16 GB
machine. Now `-Xmx3G -XX:MaxMetaspaceSize=1G`.

#### The advisor's dissent, which matters for the SECOND hop

The advisor read the plugin sources rather than the changelogs and recommended going straight to v11,
on the grounds that v9's entries are unreadable to v11 but return `null` rather than throwing -- which
is the "no session" path the app already handles -- so the whole cost of the direct hop is that each
existing Android test install signs in once more.

One finding from that review bears directly on the owner's stated reason for choosing the two-hop and
must not be lost: **v10's migration is best-effort, not a guarantee.** On any failure it falls back to
EncryptedSharedPreferences silently and never sets its `ENCRYPTED_PREFERENCES_MIGRATED` marker, so a
device that fails the v10 migration is stranded by v11 anyway. The two-hop reduces the risk; it does
not remove it. That is the argument for testing the migration on a real device rather than assuming
it, which is what this item is now held open for.

#### What is still unproved, and blocks closing this item

**The migration itself has NOT been exercised.** Every install used for verification so far was a
FRESH one, which takes the "no data to migrate" branch -- the branch that cannot fail. The test that
matters is the one nobody has run:

1. install a build with `flutter_secure_storage` **9.2.4** on a real Android device,
2. sign in, and confirm a token is stored,
3. upgrade IN PLACE to this build (10.3.2) -- no uninstall,
4. cold-start, and confirm the session survives without a sign-in,
5. confirm `shared_prefs/FlutterSecureStorage.xml` no longer holds the Tink-encrypted entries.

Until step 4 passes on a handset, the two-hop has bought nothing that has been demonstrated.

#### The eight health checks the owner asked for, on the Android emulator, 2026-09-11

All eight pass. The storage assertions are made against the device's own
`shared_prefs/FlutterSecureStorage.xml` through `run-as`, not inferred from the screen.

| # | Check | Result |
|---|-------|--------|
| 1 | Dependency resolution succeeds | **PASS** -- `flutter_secure_storage 10.3.2`, `file_picker 12.3.0` |
| 2 | `flutter build apk` succeeds | **PASS** -- debug APK produced; the KGP warning is gone |
| 3 | `flutter analyze` clean | **PASS** |
| 4 | Full Flutter tests pass | **PASS** -- 127 |
| 5 | Authentication from a fresh install | **PASS** -- `POST /auth/login 200`; the store goes from an empty `<map />` to exactly two entries, `khadra.refresh_token` and `khadra.refresh_expires_at` |
| 6 | Refresh-token persistence across restart | **PASS** -- force-stop, cold start, `POST /auth/refresh 200`, profile restored without a sign-in |
| 7 | Logout removes the credentials | **PASS** -- `POST /auth/logout 204`, store back to zero entries |
| 8 | Expired/invalid refresh token | **PASS** -- password reset server-side revoked the family; cold start gave `POST /auth/refresh 401`, the app landed signed-out on Home, the store was emptied, and there was ONE 401, not a loop |

A fresh install now writes `FlutterSecureKeyStorage.xml` holding an RSA-wrapped AES key -- v10's own
cipher backend -- rather than Jetpack Crypto's Tink blobs. That is the new scheme working; it is NOT
evidence that a migration works, because a fresh install has nothing to migrate.

#### The in-place upgrade, run and PASSED on 2026-09-12

The test that matters, on an Android emulator (Pixel, API 36):

1. install a build with `flutter_secure_storage` **9.2.4** -- **done**, built from this branch with
   the storage package pinned back and `AndroidOptions(encryptedSharedPreferences: true)` restored;
2. sign in, and confirm a token is stored -- **done**: four entries appeared, two of them Tink-
   encrypted key NAMES (`AX3dqTca...`, `AX3dqTdP...`) plus the two
   `__androidx_security_crypto_*` keysets, which is exactly what Jetpack Crypto writes;
3. upgrade IN PLACE to 10.3.2 -- **done**, `adb install -r`, no uninstall;
4. cold-start, session survives without a sign-in -- **PASS**. The plugin logged it itself:

        Found data in EncryptedSharedPreferences (deprecated)
        Migrating data from EncryptedSharedPreferences to custom cipher storage...
        Migrated key: khadra.refresh_token
        Migrated key: khadra.refresh_expires_at
        Migration complete: 2 items migrated
        Migration completed successfully. Now using custom cipher storage.

   and the server accepted the migrated credential: `POST /auth/refresh` answered **200** on that
   cold start, with the account restored and no sign-in prompt;
5. the store now holds v10’s own prefixed entries beside the now-inert Tink keysets.

**The first attempt FAILED, and the cause is worth keeping.** `migrateWithBackup: true` -- added here
as the careful choice, to keep a copy while the one-time move ran -- is what stopped the move
happening at all. `FlutterSecureStorage.java:170` guards the whole EncryptedSharedPreferences
migration with `if (!isAlreadyMigrated && !config.shouldMigrateWithBackup())` and defers it to "step
6 of the backup-protected migration path", which is the ALGORITHM-CHANGE path -- and that never runs
on a v9 store, because there are no v10 algorithm markers to have changed. Nothing was corrupted and
nothing was lost; v10 simply never looked at the old store, treated the app as a fresh install, and
the customer was signed out. Both flags are defensible on their names; only one combination works,
and nothing but a real upgrade on a real install would have said so.

**Also disproved: the `win32` override.** The advisor offered `dependency_overrides: win32: ^6.4.0`
as an emergency way to keep 9.2.4 while unblocking `file_picker`. It RESOLVES but does not COMPILE:
the Dart front end still type-checks `flutter_secure_storage_windows` 3.1.2 even for an Android
target, and that package does not build against win32 6.x (`Too many positional arguments`,
`WIN32_ERROR` vs `HRESULT`). Recorded so nobody reaches for it under pressure.

#### What remains open

Everything above was run on an EMULATOR, not a handset. The emulator is a real Android and the
migration is a device-local operation, so this is strong evidence; a phone with a hardware-backed
Keystore is still the last word, and the second hop (10.x to 11.x) has not been attempted and must
rerun these same tests when it is.

<!-- superseded plan, kept for the shape of the test -->

**The original plan, for reference:**

1. install a build with `flutter_secure_storage` **9.2.4** on a real Android device,
2. sign in, and confirm a token is stored,
3. upgrade IN PLACE to this build (10.3.2) -- no uninstall,
4. cold-start, and confirm the session survives without a sign-in,
5. confirm the Tink-encrypted entries are gone from `shared_prefs/FlutterSecureStorage.xml`.

Until step 4 passes on a handset, the two-hop has bought nothing that has been demonstrated.

**To close:** the app builds for Android (done), is installed and runs (done), the eight health checks
pass (done), AND the 9.2.4 to 10.3.2 upgrade-in-place preserves an authenticated session on a real
device (open). Only then is the second hop, 10.x to 11.x, worth taking -- and it reruns the same
authentication and storage tests.

### 95. The cold-start rotation is not single-flight with the interceptor's

**Status:** open · **Raised:** 2026-09-12 · **Pre-existing; found while building the Get Started flow**

`SessionController._restore` calls `refresh()` directly, and `AuthInterceptor._refreshOnce` is the
thing that serialises rotations. They do not share that gate, so the two CAN present the same refresh
token at the same moment.

It is reachable, not theoretical: public routes deliberately render before the session resolves (that
is what keeps `/verify-email?token=…` working from a cold start), so opening the app on a car or a
gallery starts the catalogue's requests while `restore` is still rotating. Every one of those goes
through `onRequest`, which reads the stored token and refreshes it when the access token is stale —
which it always is at launch.

The server's 60-second reuse grace covers most of it, and the `xmin` concurrency check turns the
loser into a 401 rather than a family revocation. But a 401 on the refresh endpoint IS a verdict to
this app, so the customer can be signed out on arrival, on a session that was perfectly valid.

**To close:** route `_restore`'s rotation through the same single-flight the interceptor owns — or
have `onRequest` wait while the session is `unknown`, which is the shorter change and costs the first
request of a cold start nothing it was not already waiting for.

### 96. Upgrading past the install marker signs every device out once

**Status:** open, one-time · **Raised:** 2026-09-12

`khadra.session_owned` replaced `khadra.install_marker` on 2026-09-12 (see `docs/auth-and-sessions.md`).
A device upgrading in place from a build that wrote the OLD key has no `session_owned`, so its
perfectly good refresh token is disowned, discarded, and the app opens on Get Started asking the
customer to sign in again.

That is the safe direction and it is deliberate — the alternative is trusting a marker that was never
written — but it is a real one-time sign-out for every installed tester, and it will look like a bug
to whoever reports it.

**To close:** nothing to fix. Delete this item once the fleet has been through it, or fold the
migration into the same on-device test as item 94, which already installs an old build and upgrades
in place.

### 97. The launcher icon has no themed (monochrome) layer

**Status:** open, needs a DESIGN decision · **Raised:** 2026-09-13

The Android launcher icon is real as of 2026-09-13: an adaptive icon built from
`assets/brand/khadra-logo.png` by `tools/make_launcher_icons.dart` and
`flutter_launcher_icons`, on brand green, sized to Android's 66/108 safe zone.
`test/launcher_icon_test.dart` keeps it from rotting.

What it does NOT have is `adaptive_icon_monochrome`. Android 13 and later let a
customer theme every icon to their wallpaper, and an app with no monochrome layer
is left in full colour among a screen of tinted ones — visible, and visibly the
odd one out.

It was left out rather than derived, because a themed icon is a single-colour
SILHOUETTE and this mark is a filled circular badge: flattening it gives a plain
disc with no car and no wordmark in it, which is less recognisable than the
full-colour icon Android falls back to. Deriving one automatically would have
shipped something worse while looking like the box was ticked.

**To close:** the owner supplies, or approves, a single-colour mark — the car
alone is the obvious candidate, as a path rather than a photograph of a badge.
Then add `adaptive_icon_monochrome` to `flutter_launcher_icons.yaml`, re-run the
two commands, and extend `launcher_icon_test.dart` to require the third layer.

**Also open, and smaller:** the iOS icon set is untouched. `flutter_launcher_icons`
is configured `ios: false` deliberately — nothing in this environment can look at
an iOS build, and a generated icon nobody has seen is worse than a placeholder
somebody knows is a placeholder. The same two commands do iOS the day there is a
device to check it on.

### 98. Arabic is rendered with Latin letter-spacing

**Status:** closed · **Raised:** 2026-09-13 · **Closed:** 2026-09-18 — `KhadraType` answers the
tracking, the theme takes `light({bool arabic})`, and the audit forbids the literal that would go
round either. See the close note at the end of this item.

Fourteen styles in `Khadra.Mobile/lib` set `letterSpacing`, from -0.6 to +0.7: the
theme's own title styles, `KhadraLargeTitle`, `KhadraBadge`, `KhadraSpecGrid`,
`KhadraSectionTitle` and several screens. Flutter inserts that tracking between
every glyph, including between the joined letters of an Arabic word.

The console already ruled on this and says why, in `Khadra.Dashboard/src/styles/_rtl.scss`:

> Arabic is cursive: the letters in a word are joined. `letter-spacing` prises
> those joins apart, so a tracked caption does not render as wide Arabic, it
> renders as broken Arabic.

It zeroes tracking under `:lang(ar)` and brings the emphasis back with weight. The
customer app has no equivalent, so every tracked label on it — the section headings
on Profile, the badges on a booking, the spec grid on a car — is drawn with its
Arabic joins opened up. It is cosmetic, not functional, which is why it was
recorded rather than rushed at the end of an unrelated pass.

`test/rtl_audit_test.dart` deliberately does NOT yet fail on this; the source scan
cannot tell which styles land on Arabic text and which never can.

**Closed as described, with two additions.** `KhadraType.tracking(latin, arabic)` is the answer and
`KhadraType.of(context, latin)` reads it from `Localizations.localeOf(context)` — the LANGUAGE, not
the direction, because they are not the same question even where they agree. `KhadraTheme.light({bool
arabic})` carries it into the styles built before there is a context, and `main.dart` passes the
resolved `isArabicProvider` so the scale follows the app's own language switch rather than the
device's.

First addition: the Arabic scale is untracked WHOLE, not style by style. `_untracked` clears the
tracking on all fifteen text styles, including the ones this app does not override and inherits from
Material with figures of their own — so a screen that starts using `displayMedium` tomorrow cannot
inherit tracking nobody chose. (`TextTheme.apply(letterSpacingFactor: 0)` looks like the one-liner for
this and is not: it asserts on any style whose tracking is already unset, which is most of them.)

Second addition: three sites keep their literal, each with an `rtl-audit: allow` marker on the line —
the booking reference in `bookings_screen`, `booking_detail_screen` and `request_booking_screen`. A
reference is Latin in both languages and is opened out deliberately, because somebody reads it aloud
over a phone. Zeroing those would have been the rule applied past its reason.

`test/rtl_audit_test.dart` now fails on a `letterSpacing:` literal outside `khadra_theme.dart`, and
`test/typography_test.dart` asserts the English scale keeps every figure the design asks for while the
Arabic scale carries none, on the theme and on the three widgets that name their own.

### 99. The seat filter is a list typed into the screen

**Status:** closed · **Raised:** 2026-09-13 · **Closed:** 2026-09-18 — the chips are now the
facets of the bookable catalogue. See the close note at the end of this item.

`Khadra.Mobile/lib/features/catalogue/filter_sheet.dart` builds its "minimum seats"
chips from `const [2, 4, 5, 7]`. Every other group in that sheet is served by the
platform — cities and car types from the lookup endpoints, transmissions from
`/app-config`'s vocabularies — and this one is four numbers somebody chose.

It is the standing rule broken in miniature: a list a customer sees that no API
sent. It is not dangerous the way an invented price would be, but it is the same
class, and the day the platform lists a nine-seat van the filter cannot find it.

**Closed differently, and better.** A vocabulary on `/app-config` would have been a second list
somebody chose — accurate only for as long as nobody changed the fleet. `GET /api/v1/vehicles/facets`
(anonymous, `no-store`, the catalogue's own rate-limit policy) returns `{ seats, carTypeIds }` taken
from the bookable-catalogue predicate itself, so the filter offers a seat count exactly when a
customer could book one, and offers the nine-seat van the day it is listed. The category chips on Home
are built from the same call, intersected with the active car-type lookups.

Null is a real answer: on an older API, or a dropped request, the app falls back to every active car
type and offers no seat group at all rather than a list of its own.

`rtl_audit_test.dart` now fails on a literal list of numbers anywhere under `features/catalogue/`,
which is the shape this came back as.

## Console localization, Wave Two (2026-09-18)

The pass that localized the Dealer, Employee and Admin consoles end to end and fixed the platform
commission that printed as "JOD 0−". What it left behind, and why.

### 100. The server still writes every refusal and validation message in English

**Status:** open · **Raised:** 2026-09-18 · **Deliberate, scoped out of Wave Two**

ProblemDetails titles, the 28 FluentValidation `WithMessage` texts and roughly 194 `Error` messages
are English, and always were. The consoles no longer show them in Arabic: a refusal is held as a
`ProblemSnapshot`, known error CODES are worded from the dictionary, and anything unmapped shows the
server's English only in English mode — in Arabic the reader gets "the request was refused" plus the
trace id. So an Arabic screen never shows English prose, but it also cannot say exactly what went
wrong for a code the console has not mapped.

**To close:** localize the API's own messages (resources keyed by error code, `Accept-Language`
forwarded by the BFF), or accept the current behaviour and keep mapping codes as they appear. The
codes are the contract either way; a server-side dictionary would be a third copy to keep in step,
which is why it was not done here.

### 101. The customer app does not yet read the new "account closed" facts

**Status:** open, BOOKINGS HALF CLOSED 2026-09-21 · **Raised:** 2026-09-18

**The bookings half is done.** `Booking` and `BookingListItem` parse `dealerRemoved`, and every
screen that names an office — the bookings list, the booking detail, Home's next-booking card and
the review form — goes through `BookingPresentation.dealerName`, which answers the app's own
`bookingDealerRemoved` string when the flag is set. It was closed with the Booking Details redesign
because that screen now leads with the office name: shipping it still printing "Dealer no longer on
the platform" inside an Arabic card would have been this item in a larger font.

**Still open:** `customerAccountClosed` on a booking, and the three dispute flags
(`openedByAccountClosed`, `authorAccountClosed`, `resolvedByAccountClosed`), none of which the app
parses. The customer never sees their own name as a party, so that one may turn out to be nothing to
do; the dispute screens are not.

Wave Two added a fact beside every name a booking or a dispute carries: `dealerRemoved`,
`customerAccountClosed`, `openedByAccountClosed`, `authorAccountClosed`, `resolvedByAccountClosed`.
The consoles read those flags and word them. `Khadra.Mobile` still prints the name as it arrives,
which for a party that no longer resolves is an English stand-in ("Dealer no longer on the platform")
in the middle of an Arabic screen. Nothing broke — the strings kept their names, types and meanings,
which is why the change was safe to make additively — but the app is a release behind the fact.

**To close:** read the boolean beside each name in `Khadra.Mobile/lib/api/dtos.dart` and word it from
the app's own `AppLocalizations`, as the consoles do.

### 102. A penalty's reason has a code now; the customer app has no vocabulary for it

**Status:** open · **Raised:** 2026-09-18

`PenaltyAssessment` carries `reasonCode` (a `PenaltyReason` name) beside the frozen English
`reason`. The consoles word the code and fall back to the sentence for bookings assessed before
codes existed. The customer app parses `reason` and never displays it, so it is unaffected — but the
day it wants to show why a penalty was assessed, it needs the words.

**To close:** serve a `PenaltyReasons` vocabulary on `/app-config`, built from the same enum, beside
the existing vocabularies. Additive, and the app already knows how to render one.

### 103. Names written into the record in English, at the moment of the action

**Status:** open · **Raised:** 2026-09-18

Some names are not looked up at read time but WRITTEN when something happens, and they were written
in English: notification actor names ("A colleague", "A customer", "The rental office"), audit
`actorName` ("Unknown admin", "Unknown"), `DealerDocumentReview.ReviewedByName` ("Unknown"). Those
records are history and are not rewritten, so they stay English on an Arabic screen.

**To close:** for records written from here on, store a code (or the actor's id) beside the name and
word it at render time, as the penalty reason now does. Historical rows keep what they were given.

### 104. The vehicle wizard offers nine car makes nobody served it

**Status:** open · **Raised:** 2026-09-18 · **Found during the localization sweep**

`Khadra.Dashboard/src/app/features/fleet/vehicle-wizard.component.ts` builds its make suggestions
from `['Toyota', 'Hyundai', 'Kia', …]` — a list typed into the screen. Every other list on that form
is served by the platform: car types and cities from the lookup endpoints, transmissions and fuel
types from the API's own vocabularies. It is the standing rule broken in miniature, and the day a
dealer lists a make that is not in those nine, the suggestions are quietly wrong.

They are brand names, so they read the same in both languages; the localization scanner allowlists
them for that reason, with a pointer to this item. The defect is the list, not the language.

**To close:** serve the makes as a lookup or an `/app-config` vocabulary, or drop the suggestions and
let the field stand alone.

### 105. The car form still pre-fills figures nobody chose

**Status:** open · **Raised:** 2026-09-18 · **Found during the localization sweep**

`car-form.component.ts` starts a new car at `seats: 5`, `dailyRate: 30`, `securityDeposit: 150`,
`transmission: 'Automatic'`, `fuelType: 'Petrol'` and the current year. The vehicle WIZARD had the
same defaults and they were removed, with a comment recording why: a dealer who tabbed past them
published a real car at figures the console invented. The older form was not fixed at the same time.

**To close:** start those fields empty, as the wizard does, and let the dealer state each one.

### 106. The admin dealer list judges its review SLA by the browser's clock alone

**Status:** open · **Raised:** 2026-09-18

Every other clock in the console consults the server's own flag as well as the local one, so a
browser whose time is behind cannot show a broken promise as time remaining: the dispute queue reads
`isOverdue`, the review screen reads `isBreachingSla`. The dealer LIST has no such field on its rows,
so `dealers-list` compares `reviewDueAt` against `Date.now()` and nothing else.

**To close:** add `IsBreachingSla` to `DealerListItem` (the reader already has `now` for the queue
counts) and pass it to `formats.sla(...)`, which takes the flag.

### 107. A resubmitted application cannot say which decision it answered

**Status:** open · **Raised:** 2026-09-18 · **Behaviour deliberately changed in Wave Two**

`Dealer.Resubmit` can follow a clarification request OR a rejection, and it clears the note, so
afterwards the aggregate holds no trace of which one happened. The review timeline used to label
every such step "Clarification requested" — right for one path, wrong for the other, and it was a
guess either way. It now says only that a decision was recorded, which is true.

**To close (only if the owner wants the distinction back):** record the decision that was answered on
the aggregate — a `PreviousDecision` alongside `ReviewedAt` — and word it from that. Until then the
audit log is where an administrator can see which it was.

### 108. Two console strings still come from the server in English

**Status:** open · **Raised:** 2026-09-18

- The platform settings screen prints the SOURCE of its figures ("Configuration") as the API sends
  it — a display word rather than a code, so the console cannot translate it.
- `DisputeAdminReader` sends "—" as a booking reference when a ticket's booking does not resolve,
  which is practically unreachable (bookings are never deleted) but is the reader inventing a value.

**To close:** send a code for the settings source and word it in the console; drop the "—" and let the
reference be null, which the console already knows how to word.

### 109. A resubmitted application does not record what it answered

**Status:** open · **Raised:** 2026-09-18 · **From the architecture review of Wave Two**

Item 107 says the review timeline can no longer name the decision a dealer answered. The reason is
that `Dealer.Resubmit` keeps `ReviewedAt` but clears `ReviewNote` and moves the status back to
`PendingReview`, so nothing on the aggregate says whether the applicant was REJECTED or merely asked
to clarify — or what they were asked to fix. Those are different review postures, and an admin
picking up a resubmission cannot tell them apart without opening the audit log.

**To close:** two fields on `Dealer`, set inside `Resubmit`: the status being resubmitted from, and
the note it answers. Not a history table — a second cycle overwriting the first is fine, because the
timeline shows only the latest decision anyway. Then the timeline words the step from those.

### 110. Every countdown trusts the browser's clock for "how long"

**Status:** open · **Raised:** 2026-09-18 · **From the architecture review of Wave Two**

Whether a deadline has PASSED is the server's answer (`isAwaitingDecision`, `isOverdue`,
`isBreachingSla`) OR the local clock, whichever says so first, so a slow browser clock cannot show a
dead deadline as live. How MUCH time is left, though, is always local arithmetic. A browser running
fast shows "Expired" or "Overdue by 1m" a little early. Nothing is locked: the controls gate on
status and permissions, not on the countdown.

**To close:** where a response already carries the moment it was generated (`AttentionQueue` has
`generatedAt`), anchor the clock to that plus elapsed local time instead of `Date.now()`. Cheap where
the field exists; the rest keep the local clock and the server flag.

## The rental office's customer page (2026-09-18)

Six things this pass left for later, recorded when they were decided rather than when they bite.

### 111. Email acceptance is logged and nothing more

**Status:** open · **Raised:** 2026-09-18

`IEmailSender` now returns an `EmailSendReceipt` — provider, provider message id, accepted-at,
attempts — and `AuthEmailDispatcher` and `BookingEmailDispatcher` each write one Information line per
accepted send, carrying the RECIPIENT DOMAIN only. That is enough to find a message in Brevo's own
transactional log by id, and it is not a record: logs are ephemeral locally and whatever the host keeps
in production, and nothing is queryable.

It also does not prove delivery, and must never be read as if it did. Acceptance means the provider
took the request. Delivered, deferred, bounced and blocked all happen afterwards and only the provider
knows.

**To close:** an append-only `email_delivery_attempts` table written in its own post-commit
transaction, and provider delivery webhooks updating the row. That is a migration, a retention policy
for data that is PII-adjacent, and a failure path of its own — none of which the app needs today, which
is why it is here and not in the last pass.

### 112. Dealer prose is not frozen onto a booking

**Status:** open · **Raised:** 2026-09-18

A booking freezes the rules and the price it was made under (`BookingTerms`, `BookingPricing`), so a
gallery changing its fee never re-prices an existing rental. The office's own words are NOT frozen:
rental conditions, insurance and pickup instructions are read live off the dealer, and an office may
rewrite them the day after a customer books on the strength of them.

For the platform's own terms this does not matter, because those are frozen and are what a dispute is
judged against. It matters for a dispute where the argument is about something the OFFICE promised —
"they said a second driver was included".

**To close:** decide first whether dealer prose is ever evidence. If it is, a version row per change
and the version id frozen onto the booking; if it is not, say so here and close this. Do not build the
versioning until that question has an answer — a history table nobody reads is worse than no history.

### 113. No Admin can read what an office tells customers

**Status:** open · **Raised:** 2026-09-18

Six free-text fields, up to 2,000 characters each, written by dealers and shown to every customer.
Nothing moderates them and no Admin screen shows them. That is the same exposure vehicle descriptions
already have, so it is not new, but it is now six times larger and it is about policy rather than about
a car.

**To close:** a read-only panel on the admin dealer page showing the six sections and which are
hidden. Admin console work, which is out of scope for a customer-app pass.

### 114. The design export has no artboard for the customer page

**Status:** open · **Raised:** 2026-09-18

`docs/design/Dealer Console.dc.html` is the source of truth for how the console looks, and it does not
contain this screen — it did not exist when the project was exported. The screen is built from the
console's existing components and tokens (`sect`, `field`, `note-box`, `dc-split-155`) so it is
consistent by construction, but it is not DESIGNED, and the rules file says to diff against the export
before changing a screen's appearance.

**To close:** add the artboard on the next design export, then diff the Angular against it.

### 115. A platform-rules block on the rental office page, if it is ever wanted

**Status:** open (deliberate omission) · **Raised:** 2026-09-18

The architecture review proposed putting Khadra's own booking rules and the renter's required
documents on the rental office page, fed from `/app-config`. The owner ruled both out for this pass.
They are stated on every booking quote (`QuoteTerms`) and on the booking itself, which is where a
customer meets them at the moment they matter.

Recorded because the reasoning may not survive contact with real customers: somebody comparing three
offices before choosing one cannot see the cancellation window until they have picked a car.

**To close:** an owner decision, not a fix. If they want it, it is a block on the page fed from
`/app-config` — never text a dealer writes, which would be a promise nothing enforces.

### 116. The customer app picks "today" from the device clock

**Status:** open · **Raised:** 2026-09-18

The rental office page collapses opening hours to today's line, and which day that is comes from
`DateTime.now()` converted to Amman. The ZONE is right — it is the office's own day, not the phone's —
but the instant is the phone's, so a device whose clock is a day out shows the wrong row. The whole
week is one tap away and nothing is gated on it, which is why this is a note rather than a fix.

Same class as item 110 in the console: whether a deadline has passed is the server's answer; how long
is left is local arithmetic.

**To close:** carry the server's own "now" on a response the page already makes and anchor the day to
it plus elapsed local time. Cheap wherever a response already says when it was generated; not worth a
field of its own just for this.

### 117. Saving the dealer page wipes the dealership's city and address

**Status:** closed · **Raised:** 2026-09-18 · **Closed:** 2026-09-19 — the request carries the
location and requires it present, the form shows and edits it, and the command can no longer be built
without it. Approved by the owner as a contract change. See the close note at the end of this item.

`PUT /api/v1/dealers/me/profile` takes `BusinessName`, `Latitude`, `Longitude` and `OperatingHours`
and nothing else. It builds `UpdateDealerProfileCommand` with five arguments, so `CityId`,
`AddressArea` and `AddressStreet` fall back to the record's own defaults of null; `ResolveAddress(null,
null)` returns a successful null rather than refusing; and `Dealer.UpdateProfile` assigns `CityId =
cityId; Address = address;` **unconditionally**. So an owner who changes one closing time clears both.

The handler's own comment says the city "now travels with the rest of the form, and is checked against
the lookup exactly as submission checks it". The command grew those parameters; the HTTP request never
did, and neither did the console form.

Two consequences, and the second is the serious one:

- The address line disappears from the rental office page, which is new — customers only started
  seeing it in this pass.
- `CatalogueReader.SearchAsync` filters vehicles by `dealer.CityId`, so the office's **entire fleet
  drops out of every city-filtered search**. Nothing tells anybody: the cars are still listed, still
  bookable by direct link, and simply absent from the results customers actually browse.

`DealerProfileTests` passes because it builds the command the same five-argument way the controller
does. It exercises the wiping path and asserts only that `Description` survived it. A test shaped like
the bug is why this stood.

**To close:** `cityId`, `addressArea` and `addressStreet` on the API request, on the console's
`UpdateProfileRequest`, and on the dealer page form — the dashboard's `DealerProfile` already reads
both back, so the form has the values to seed from. Then a handler test asserting a save keeps them.
**Not** by having the handler quietly forward the stored values when a field is omitted: that makes
"omitted" mean two different things on the same endpoint, which is the ambiguity that produced this.

**Close note (2026-09-19).** Closed the way this item asked, with the owner's rulings on the retired
city and the architecture advisor's review:

- **The request** carries `CityId`, `AddressArea` and `AddressStreet`, named and limited as the
  application form's (100 and 200). Each is `[JsonRequired]`: it must be present, and may be null.
  Null is an answer; leaving one out is a 400, so no client — an old console, a stale tab — can erase a
  location by not knowing about it. The 400 is the framework's model-binding shape, the same as every
  other malformed body today (see item 121).
- **The command** lost its `= null` defaults, so the five-argument construction that did this no
  longer compiles. `DealerProfileTests` had to state a location to build, which is the point.
- **A retired city.** The city an office is already filed under is not re-checked against the lookup,
  so an office under a city an administrator retired later can still save its hours, and is never made
  to move to save anything. A NEW city is checked exactly as submission checks it: a retired or unknown
  one is refused with `dealer.unknown_city`.
- **The form** shows the city, area and street, seeded from `GET /dealers/me`, and sends all three on
  every save. The city is selected per option, not by the select's value, because it is seeded before
  the list arrives. Until the list loads the select is disabled and the city is sent unchanged. A city
  retired since it was filed is pinned as "Your current city (no longer offered)" — the dealer cannot
  fetch its name; see item 124. Once an office has a city the form does not offer "no city", because
  that takes its fleet out of city search; the API still accepts null.
- **Tests at the boundary that broke,** not below it: the real controller's request-to-command mapping
  (`DealerProfileEndpointTests`, which fails if the mapping is removed — checked by re-introducing the
  bug); the handler over the real repositories on SQLite, read back through `GET /dealers/me`'s own
  query; the city-filtered catalogue after an unrelated save, with a control proving it can fail; and
  the retired-city rule, which fails four tests when its guard is removed.

**Deploy ordering.** Ship the API and the console together. An API that requires the location, facing
a console tab opened before the update, answers that tab's profile saves with a 400 until it is
reloaded. That is loud on purpose — the quiet version is the one that erased data — but it is a window.

### 118. An unrecognised hidden-section name is dropped rather than kept

**Status:** open · **Raised:** 2026-09-18 · **From the architecture review of the customer page**

`HiddenSectionsConverter.Read` drops a stored name this build does not know. Making the read total is
right — materialising a dealer must never fail over a stored string — but dropping is not the only way
to be total, and the direction of the failure matters.

Kept, an unknown name hides something the old build cannot render anyway. Dropped, it is gone from the
set, so the next save from that build narrows the column and, after a roll-forward, a section the owner
chose to hide is shown. The whole feature exists to stop exactly that.

It is not a launch blocker: the vocabulary has six names and has never changed, so there is nothing
unrecognised to drop.

**To close, before a seventh section is ever added:** `PublicProfile` carries a private list of
unrecognised names, set only by the converter's read; `Write` appends them after the known ones;
`UpdatePublicProfile` copies them from the outgoing profile onto the incoming one; the comparer
includes them. Test: tamper the column to `About;Prices`, load, hide Insurance, save, assert
`About;Insurance;Prices`.

### 119. The public-profile endpoints have no Security-layer tests

**Status:** open · **Raised:** 2026-09-18 · **From the architecture review of the customer page**

`Khadra.Tests/Security` has nothing touching `me/public-profile` or `galleries/`. The application
layer covers owner-only writing, and the reader covers what a suspended office returns, but neither
exercises the wire: an employee's 403 rests on the `DealerOwner` policy attribute being present, and
"404 with none of the office's prose in the body" is asserted one layer below the serialiser.

**To close:** two `WebApplicationFactory` tests — an employee's `PUT me/public-profile` is 403, and an
anonymous `GET galleries/{id}` for a suspended office is 404 whose body contains none of the six
sections.

### 120. Typed text collapses its own line breaks in the console

**Status:** open (deferred by the owner, 2026-09-19) · **Raised:** 2026-09-18 · **From the
architecture review of the customer page**

`.user-text` marks text somebody typed — an office's customer page, a customer's dispute statement, a
review note — so bidi does not reorder it. It sets no `white-space`, so the line breaks a person typed
collapse into one paragraph wherever the console shows them. It is most visible in the customer page
preview, which says it shows what customers see: an office that lays its rental conditions out as a
list sees them run together.

The fix proposed was `white-space: pre-line` on `.user-text` in `src/styles/`. The owner deferred it
rather than take it inside the customer page work, because `.user-text` is console-wide: it would
change how every typed string on every screen wraps, the night before a walkthrough of every screen,
under a change labelled for one of them.

**To close:** either the global rule, with a before-and-after of the screens that render `.user-text`,
or a preview-only rule the customer page panel opts into. Either way, check how the customer app lays
out the same text, so the preview and the page a customer reads agree.

### 121. A malformed request body is refused without a `code`

**Status:** open · **Raised:** 2026-09-19 · **Pre-existing; from the architecture review of item 117**

Every error this API returns is meant to carry a stable `code` beside its `traceId`. Model-binding
failures do not: invalid JSON, a missing `[JsonRequired]` property, a `[StringLength]` breach. They
become MVC's automatic `ValidationProblemDetails` — a title, an `errors` map, a `traceId`, and no
`code` — because nothing customises `InvalidModelStateResponseFactory`. `ApiSmokeTests` already
accepts that shape, so it is not new; item 117 made one more request depend on it, since a client
that leaves the location out of a dealer page save now gets exactly this.

The console copes (`snapshotProblem` records `code: null`, and English mode shows the title), but no
client can tell "your body was malformed" from any other 400 by code.

**To close:** one `InvalidModelStateResponseFactory` that adds `code: "request.invalid"` for every
endpoint, keeping the `errors` map as it is. A smoke test pins the code.

### 122. Retiring a city drops its offices out of city search, and nobody is told

**Status:** open · **Raised:** 2026-09-19 · **Pre-existing; from the architecture review of item 117**

`SetLookupActiveCommand` retires a city without looking at who is filed under it. From then on the
customer app cannot offer that city as a filter, so every office in it is missing from every
city-filtered search — the same consequence item 117 had, arrived at from the other side. The
administrator who retired the city sees nothing to suggest it.

Item 117 made sure such an office can still save its page and keep its filing; it did not make it
findable.

**To close:** an owner decision first — refuse to retire a city that trading offices are filed under,
or warn with the count and let the administrator proceed. Either way the lookups screen says how many
offices a city holds before it is retired.

### 123. The dealer page needs a version check the day it gains a second writer

**Status:** open (not needed yet) · **Raised:** 2026-09-19 · **From the architecture review of
item 117**

`PUT me/profile` replaces the whole page, location included, from whatever the owner's form loaded.
Today that is safe: after submission the owner is the only writer of the name, pin, hours, city and
address. `Dealer` has an `xmin` concurrency token, but it only compares against the row the handler
loaded inside the request, not the one the console loaded minutes earlier.

The day anything else writes these fields — an administrator correcting a city, an import — an owner
with the page open would silently put back what it had loaded.

**To close, before a second writer exists:** the response carries a version, the console sends it
back as `If-Match`, and a mismatch is a 409 the form explains.

### 124. Nobody but an administrator can name a retired city

**Status:** open · **Raised:** 2026-09-19 · **From item 117**

A dealer can fetch only the offered cities (`GET /cities`, active only). So the dealer page cannot
name an office's city once an administrator has retired it, and shows it as "Your current city (no
longer offered)" instead — honest, and enough to keep it, but the owner cannot see which city it is.
The administrator's dealer review screen reads the same active-only list and names nothing either.

Deliberately NOT fixed by putting the city's name on `DealerProfileDto`: that would copy a renameable
value out of the lookup into a Dealers read model, which `dealer-review.component.ts` already argues
against, and it is built in eight places.

**To close:** a way for a signed-in reader to name a city by id whether or not it is still offered —
`GET /api/v1/cities/{id}`, or an id-list that includes retired entries — used by both screens.

### 125. A newer console could drop a section it forgot to send

**Status:** open · **Raised:** 2026-09-19 · **From the architecture review of the customer page**

The stale-console protection (item 3 of the owner's 2026-09-19 approvals) covers a console OLDER than
its server: a section it has no box for disables Save. The opposite case is not covered.
`customerPageRequest` writes the six text fields out by hand, independently of `SECTIONS`, the map
that decides which boxes the page renders. A release that adds a seventh box to `SECTIONS` and forgets
the request would show the box, accept the typing, and send nothing for it — and the save, being a full
replacement, would clear whatever the section held.

Left out of the approved fix because it is a different case from the one the owner approved.

**To close:** a spec asserting that the fields `SECTIONS` renders are exactly the text keys
`customerPageRequest` sends (every key but `hiddenSections`), so the two cannot drift silently.

### 120. The platform writes English prose into a booking's status history

**Status:** open · **Raised:** 2026-09-21

Five transitions the platform makes on a timer record a sentence and no reason code:
`"Payment window elapsed."`, `"Dealer did not respond."`, `"No-show window elapsed."`,
`"Settlement window elapsed with no dispute."` and `"Dispute resolved."` (`Booking.cs`, around the
`Transition` calls for Expired, NoShow and Completed). A reason a PERSON typed is their own words
and is shown as typed, correctly. These are the platform's words, in English, on a permanent record
that an Arabic-speaking customer reads on their own booking — the same class of problem as the
rejection sentence that was fixed on 2026-09-08 by storing a code beside the text.

The customer app now **omits** them: an entry whose actor is System or Admin and whose `reasonCode`
is null shows its status and time and no sentence. Nothing is lost, because the status name says the
same thing in the reader's language — but the row is still in the database, and the consoles print it.

**To close:** pass a `reasonCode` on those five `Transition` calls (the domain events already carry
the names — `PaymentWindowElapsed`, `DealerDidNotRespond`), publish them in the vocabulary
`/app-config` serves, and word them in both clients. Then the app can stop dropping the line.

### 121. The remaining aggregate-child relationships still carry the mapping that answers 500

**Status:** open · **Raised:** 2026-09-21

`DeleteBehavior.Restrict` on a required child collection is not a stricter setting, it is a broken
one: the child's foreign key cannot be null, so removing the child from its parent's collection
leaves EF nowhere to put it and `SaveChangesAsync` throws *"the association between entity types …
has been severed"*. Two relationships had it and both were live 500s — removing a vehicle photo
(`Vehicle.Images`) and un-saving a car (`CustomerShortlist._entries`). Both were changed to
`ClientCascade` on 2026-09-21, with `RemovingAChildFromAnAggregateTests` pinning them and pinning
that no constraint in the database cascades.

Every other `HasMany` in the model is still `Restrict`, and that is safe only for as long as its
aggregate never removes a child. One is already latent: `Dealer.AttachDocument` (`Dealer.cs`) calls
`_documents.RemoveAll(...)`, and its only caller today runs on a brand-new dealer before `AddAsync`,
so nothing is tracked and nothing throws. A "re-upload after clarification" handler that loads a
tracked dealer would throw on the first save. (`User.AttachDocument` avoids the whole question by
replacing in place — the better pattern where the row can be reused.)

**To close:** either convert every aggregate-to-child collection to `ClientCascade` in one pass —
same DDL, one migration, and the `IAppendOnly` guard in `KhadraDbContext` still refuses deletes on
the append-only children — or add the assertion to `RemovingAChildFromAnAggregateTests` that every
required child collection reachable from an aggregate is mapped `ClientCascade`, so the next one is
caught by a failing test rather than by a customer.

### 122. `SetPrimaryImage` marks a cover photo without moving it

**Status:** open · **Raised:** 2026-09-21

A dealer choosing a cover photo sets `IsPrimary` and leaves `Position` alone, so "the cover" and
"the first photo" are two different facts that the readers have to keep agreeing about. They had
already drifted: the search card picked the primary while the car's own page was ordered by position,
so a dealer who promoted their third photograph saw one photo on the card and a different one when
they tapped through. `CatalogueReader` now orders the car page primary-first (2026-09-21) and
`CatalogueReaderTests` pins it, which makes the two agree — but by having each reader remember,
rather than by the record being unambiguous.

**To close:** decide whether "primary" is a flag or simply position 0. If it is position 0, make
`SetPrimaryImage` move the photograph and reindex, drop the ordering from the readers, and the
question cannot be got wrong again.

### 123. The sandbox payment provider, and everything that must go with it

**Status:** open · **Raised:** 2026-09-21 · **Blocks:** nothing today, blocks the real adapter

`SandboxPaymentProvider` completes a checkout and moves no money. The owner approved it on 2026-09-21
so the booking and payment lifecycle could be clicked through end to end; the three mechanisms that
make it impossible to operate in Production are described under item 76, and
`SandboxPaymentGuardTests` proves each of them fires. This item is not about whether it is safe now.
It is about the fact that the class promises its own deletion, and a promise made in a doc comment is
a promise nobody keeps.

**To close, when a real adapter ships:**

- Delete `SandboxPaymentProvider`, `SandboxEvents`, `SandboxCheckoutEndpoints`, the
  `Payments:SandboxConsoleBaseUrl` setting and the `SANDBOX` entry in `PaymentOptions.KnownProviders`.
  The banner on the three clients can stay: `payments.mode` still answers `Live`, and a mode nobody
  reports costs nothing.
- Wipe or retire every database that ever held a `SANDBOX` payment. The startup guard already refuses
  to serve one with a real provider, so this is not optional — it is the step that guard forces, and
  doing it deliberately beats discovering it at a deploy.

**And one hole the boot-time guard cannot close.** It runs once, at startup. Two processes starting
against one empty database — one sandbox, one real — both pass, and afterwards both write. Only the
database can refuse that: an INSERT trigger on `payments` rejecting a row whose class (SANDBOX versus
not) differs from any existing row's, in the same idiom as the trigger `audit_entries` already has.
It needs a real adapter to be reachable at all, so it is written with that adapter, in the same
migration.

### 124. The customer app's bookings list never leaves its loading state

**Status:** the spinner is FIXED (2026-09-22); the request COUNT is unexplained and stays open ·
**Raised:** 2026-09-21 · **Owner decision:** deferred to the realtime/refresh batch

Found during the manual sandbox payment lifecycle. Opening the customer app's bookings list shows a
spinner that never resolves, while the API answers correctly and quickly.

**Exactly what was observed, on a local build against a local API:**

- The bookings list remains in its loading state indefinitely.
- Requests repeat rather than settling.
- **16 `GET /api/v1/bookings?tab=…` and 16 `GET /api/v1/bookings/tab-counts`** were counted in one
  API process from **two** openings of the screen.
- The API itself returns **200** for both, in roughly 200 ms.

So this is a client-side refresh loop, not a server fault. `bookings_screen.dart` matches
`AsyncLoading()` before its data arm and falls through to `_ => const KhadraLoading()`, so a provider
that is perpetually re-fetching renders as a permanent spinner rather than as stale data or an error.

**Not caused by the sandbox payment work.** Nothing in that batch touches this screen or its
providers, and the booking DETAIL screen — which the payment flow uses — works throughout: it renders
the deposit, the countdown, the Pay button and the confirmed timeline.

**To close:** it belongs with the next batch's realtime refresh and request deduplication. Whatever
fixes the loop should also make this screen unable to present a loading state forever — a list that
has data must show it, and a list that has failed must say so.

**Root-caused 2026-09-22, and the diagnosis above is wrong in one place.** There were TWO independent
defects, either of which alone produces the reported screen:

1. **The token rotation deadlocked.** `AuthInterceptor` was a `QueuedInterceptor`: all requests share
   one queue, `onRequest` held its turn while awaiting a rotation, and the rotation was itself a
   request waiting for a turn that could not free. From roughly four minutes after each sign-in — the
   access token's life less the sixty-second stale margin — the app made **no HTTP requests at all**.
   Measured on the emulator against the real API, inside the stale window, bookings screen open, 30
   seconds: **0 list, 0 tab-counts, 0 refreshes, 15 requests total across the whole session.** So
   "requests repeat" was the opposite of what was happening. Fixed by dropping the queue and keeping
   single-flight in the existing completer; `test/token_rotation_test.dart` reproduces the deadlock
   against a real loopback socket and hangs without the fix.
2. **A rotation blanked the list.** The note above says `AsyncLoading()` catches "a provider that is
   perpetually re-fetching". That is not what Riverpod does. A pull-to-refresh leaves `AsyncData`
   with `isLoading` set and never hit that arm; a re-run caused by a DEPENDENCY changing gives
   `AsyncLoading` still carrying the previous value, and that arm swallowed it. The dependency is
   `sessionProvider`, which `MyBookingsNotifier` watches — so every rotation, about every four
   minutes, replaced the list with a full-screen spinner even when nothing was wrong. Fixed by
   matching on the value; `test/bookings_refresh_test.dart` fails without it.

**What is NOT explained, and is why this stays open.** The 16-and-16 count has not been reproduced.
Measured with this fix alone: opening the screen costs **1 list + 1 tab-counts**; a rotation costs
**1 more of each**; leaving the tab and returning costs **nothing**. Sixteen is exactly twice the
eight tabs, so "eight per open" was the obvious shape and it is ruled out — nothing watches more than
the selected tab. Item 125 then changed two of those figures on purpose: a rotation now costs
nothing, and a return re-reads once the twenty-second floor has passed. `bookings_refresh_test.dart`
pins all three as they stand. Re-measure on a device before closing this: if it does not recur, say
so here and close it; if it does, it is a third defect and the first two did not cause it.

### 125. A tab the customer returns to shows what it read the last time

**Status:** FIXED 2026-09-22 — see `docs/refresh-policy.md` · **Raised:** 2026-09-22

Found while measuring item 124. Leaving the Bookings tab and coming back re-reads **nothing** — not
the list, not the tab counts. The tab shell keeps the screen mounted, so the `autoDispose` providers
never dispose and never rebuild. `test/bookings_refresh_test.dart` records this as a measurement
rather than as correct behaviour.

That is the opposite failure from the one reported, and the more serious one. A customer who opens
Bookings, goes to browse for ten minutes and comes back is looking at bookings read ten minutes ago,
with nothing on screen saying so. A gallery can approve or reject inside that window, and the whole
point of the landing surface (see `nextBookingProvider`) is that a customer finds out their booking
was approved by opening the app — they have two hours to pay.

Nothing here was made worse by the item 124 fix; the screen simply never re-read on re-entry.

**Closed by** one refresh policy for both clients, settled by the owner on 2026-09-22 and written
down in `docs/refresh-policy.md`: three tiers, four triggers, a twenty-second floor on re-entry, a
poll only for the surface actually in front of somebody, and a full stop when the app is backgrounded
or the console has been idle ten minutes. `live_refresh_test.dart` and `live-refresh.service.spec.ts`
hold the numbers; `bookings_refresh_test.dart` holds this case specifically — returning inside the
floor costs nothing, returning after it re-reads.

The dealer half went with it: the console's queue now learns about a new booking from
`GET /dealers/me/pulse` rather than from somebody pressing F5.

**Two things this turned up on the way, both now fixed and both worth remembering.** The console's
three `NavigationEnd` re-runs bumped a params signal rather than calling `reload()`, and Angular keeps
a resource's value only while the request object is the same REFERENCE — so every screen change
blanked the dealer's permissions, the rail and the bell for the length of a round trip. And a
customer's token rotation was re-reading the bookings list, the counts and the landing card every few
minutes, because the notifier watched the whole `SessionState` and that record has no value equality.
Both are the same shape as item 124: a value quietly thrown away and fetched again.

### 126. A failed Keystore write leaves a consumed refresh token on disk

**Status:** open · **Raised:** 2026-09-22 (Fable advisor, during the item 124 review)

`SessionStore.saveRefreshToken` goes through `_bounded`, which swallows a failure or a five-second
silence by design, and `SessionController._install` proceeds regardless. If that write fails, disk
still holds the token that was just CONSUMED by the rotation. The next rotation — roughly fourteen
minutes later on a 15-minute access token, and on the next cold start — presents it, well outside
the server's 60-second grace, and `RefreshTokensHandler` revokes the family. The customer is signed
out of this device mid-task with no message, and nothing in the app knows why.

**To close:** hold the current refresh token in memory in `SessionStore` alongside the access token,
and have `readRefreshToken` prefer memory, reading disk only when memory is empty (a cold start).
The disk-first WRITE ordering stays exactly as it is — it is what makes every crash window fail safe.
As a second benefit, every authenticated request currently pays a bounded platform-channel read just
to learn whether a refresh token exists; memory answers that for free.

### 127. A document upload that meets a 401 tells the customer they are offline

**Status:** open · **Raised:** 2026-09-22 (Fable advisor, during the item 124 review)

`FormData.finalize()` throws `StateError` on a second send, so a multipart body cannot be retried.
`AuthInterceptor` retries any 401 once. An upload has a 120-second send timeout against a
60-second stale margin, so a token that passes the proactive check and expires mid-transfer produces
401 → rotate → retry → `StateError` → wrapped as `DioExceptionType.unknown` → `ApiFailure.offline`.
The customer is told their connection failed, for an upload the server actually refused, while
holding a perfectly good network.

**To close:** when `request.data is FormData`, rotate but do not retry — `handler.next(error)`, so
the screen re-sends with the new token and says something true if that fails too. Needs a test with
a real multipart body, because the defect is in `finalize`, not in the interceptor's logic.

### 128. A refresh that times out waits for the next request instead of retrying

**Status:** open · **Raised:** 2026-09-22 (Fable advisor, during the item 124 review) · low priority

`SessionController.refresh` correctly treats a transport failure as "not a verdict" and returns
false without ending the session. But the rotation may well have REACHED the server and had its
response lost, in which case the token is already consumed. `RefreshTokensHandler` is designed for
exactly this — "its client retries and, inside the grace above, gets the winner's replacement" — and
the client does not retry. It waits for the next stale request, and if the app is backgrounded in
between, the 60-second grace lapses and the next rotation is a replay that revokes the family.

**To close:** on a timeout from `/auth/refresh` specifically, one immediate retry of the same token,
inside the grace. Not on any other failure, and not more than once.

### 129. Console polling keeps an abandoned session alive past its idle timeout

**Status:** handled in the client, open as a server question · **Raised:** 2026-09-22 (Fable advisor)

`DistributedCacheTicketStore.RenewAsync` writes the BFF's session ticket to Redis with
`SlidingExpiration = SessionIdleMinutes`, and `RetrieveAsync` reads it through `cache.GetAsync` on
every cookie-authenticated request — and a Redis read SLIDES a sliding entry. `SlidingExpiration =
false` on the cookie only stops the cookie being re-issued; it does not touch the ticket.

So any background polling from the console keeps the session alive indefinitely, up to the eight-hour
absolute cap. Before this work nothing polled and an unattended console died after thirty quiet
minutes. That is a security property, and it would have been changed by accident.

`LiveRefreshService` handles it from the client: polling stops after ten minutes without a
`pointerdown`, `keydown` or `wheel`, comfortably under the thirty-minute timeout, and the first input
resumes it. `live-refresh.service.spec.ts` pins it.

**To close:** decide whether the server should also stop relying on "nobody polls" for its idle
timeout. `IDistributedCache.Get` cannot be told not to slide, so it needs either a separate
last-seen stamp that only real navigation updates, or an endpoint the poll can use that does not
carry the session cookie at all. Client-side politeness is the right fix for today and the wrong
thing to depend on for ever.

### 130. The dealer pulse watches three things, and only three

**Status:** open by design · **Raised:** 2026-09-22

`DealerQueueSignature` derives its token from booking status, whether each booking has lapsed, and
the live-dispute flag. It is computed at read time, so there is no stored version for a writer to
forget to bump — but it only sees what it looks at.

Anything that starts changing a dealer's queue row WITHOUT changing one of those three will leave the
console stale and certain it is not, which is the only failure mode of a pulse that matters. The list
is written down in the record's own documentation for exactly that reason.

**To close:** when the push channel lands, drive it from domain events after commit — that is a
complete change source and makes the derived signature unnecessary. Until then, anyone adding a
column the dealer's list renders adds it to `QueueSignatureAsync` in the same change.

### 131. Drop the seven retired legacy text columns

**Status:** deferred on purpose · **Raised:** 2026-09-22

`dealers.description`, `rental_conditions`, `insurance_summary`, `pickup_instructions`,
`delivery_notes`, `customer_notes` and `vehicles.description` are retained after
`BilingualDealerContent` for migration and audit only. Nothing reads or writes them, so from the
first save after the migration they drift from what customers see — by design — and every day they
stay they are a copy somebody may take for the current text.

**To close:** a SEPARATE migration, reviewed on its own, once production has run on the new columns
long enough to be trusted: re-run the verbatim check of item 132 against the rows nobody has saved
since, take a backup, then drop. Never folded into another migration.

### 132. The bilingual migration must be deployed stop-the-world: no old and new API at once

**Status:** open until production has been migrated this way · **Raised:** 2026-09-22

`20260922012458_BilingualDealerContent` copies each office's text out of seven legacy columns into
fourteen new ones (`*_ar` / `*_en`) and from then on the new binary reads and writes ONLY the new
ones. The legacy columns are kept, and commented as retired, for migration and audit.

That makes an overlap dangerous in one direction only, and silently: an OLD API process still
running after the migration keeps writing the legacy columns, and nothing reads them any more. An
office that saves its page through an old process during a rolling deploy "saves", gets a 200, and
the page customers see does not change. (An old CONSOLE TAB is not the risk — its save body is
refused with 400 by the new API, and nothing is written; `CustomerPagePayloadTests` pins it.)

**Procedure** — the owner's seven steps, in the owner's order, with one preparation ahead of them.

**Before step 1:** build and sign the Production APK at the version the API's
`MobileApp:MinimumSupportedVersion` names, 1.1.0 (`MobileAppMinimumVersionTests` fails the suite if
`pubspec.yaml` is below it), upload it wherever customers install from, and set `MobileApp:UpdateUrl`
in production configuration to that address. From step 5 the API refuses every installed 1.0.0 app
on every call (item 133), so every minute between step 5 and step 7 is a minute in which those
customers have nothing that works and nothing newer to install. With the APK already built and
uploaded, step 7 is a release, not a build.

Releasing it BEFORE step 1 closes that gap entirely, and is safe: a 1.1.0 build talking to the
current API calls no endpoint that API lacks, finds no minimum in its `/app-config`, and simply shows
no office prose until step 5, because `ResolvedText.maybe` reads a plain string as nothing. The order
below is the owner's; moving the release ahead of step 1 is a recommendation for the owner to take
or leave.

1. Stop every API instance. The console and the app can stay up; they fail their calls for the
   duration and retry.
2. Take a database backup.
3. Apply `20260922012458_BilingualDealerContent` (`dotnet ef database update …`, or the bundle). It
   aborts, changing nothing, if its script detector misreads Arabic, presentation forms or English
   in its self-test, and if any legacy value would land on both sides, neither side, or altered.
4. Run the verification query, after the migration and BEFORE the new API starts (the first save
   changes the new columns, and the check no longer describes the copy):

   ```sql
   with pairs(v, a, e) as (
     select description, description_ar, description_en from dealers
     union all select rental_conditions,   rental_conditions_ar,   rental_conditions_en   from dealers
     union all select insurance_summary,   insurance_summary_ar,   insurance_summary_en   from dealers
     union all select pickup_instructions, pickup_instructions_ar, pickup_instructions_en from dealers
     union all select delivery_notes,      delivery_notes_ar,      delivery_notes_en      from dealers
     union all select customer_notes,      customer_notes_ar,      customer_notes_en      from dealers
     union all select description,         description_ar,         description_en         from vehicles)
   select count(*) filter (where v is not null) as legacy,
          count(*) filter (where v is not null and (a is null) <> (e is null)
                             and coalesce(a, e) = v) as verbatim_on_one_side,
          count(*) filter (where v is null and (a is not null or e is not null)) as invented
   from pairs;
   ```

   It passes when `legacy = verbatim_on_one_side` and `invented = 0`. On the e2e database: 10, 10,
   0. On the rehearsal copy with Arabic, English, mixed-script, presentation-form and digits-only
   values: 16, 16, 0. A legacy value that is blank copies as blank and counts as verbatim; the
   read side treats a stored blank as nothing written (`CatalogueReaderTests`).
5. Start the new API. It ships `MobileApp:MinimumSupportedVersion` 1.1.0, so from its first
   request an installed 1.0.0 app is answered `426 app.update_required` (item 133). Its boot log
   says so in a warning beginning `Customer app: builds older than 1.1.0 are REFUSED`; read that
   line before going on.
6. Deploy the new console. A tab still open on the old one is refused with 400 on a customer-page
   save and writes nothing.
7. Build and distribute the new Production APK — the release of the build prepared before step 1.

**The exact commands for Render and Supabase**, with a check and a way back at every step, are in
[releases/2026-09-bilingual-and-app-gate.md](releases/2026-09-bilingual-and-app-gate.md). Two things
found while preparing it. The script that production applies,
`docs/sql/2026-09-22-bilingual-dealer-content.sql`, carries **two** migrations: the last regeneration
of `khadra-schema.sql` stopped before `ChildCollectionsDeleteTheirOrphans`, so production may never
have had it. It is idempotent and applies whichever is missing. And Render deploys are zero-downtime
by default, so "stop every API instance" means suspending the service — with the console suspended
first and left down until the new API is live, because the console is the only thing that writes an
office's texts, which makes an old API process harmless if one runs.

**To close:** production migrated this way, and step 4 recorded.

### 133. Installed copies of the customer app cannot read the new gallery and car text

**Status:** closed · **Closed:** 2026-09-22 — the API refuses any customer-app build older than the
minimum it publishes, and that minimum is 1.1.0.

The customer-facing reads now return an office's text as `{ "text", "language" }` where they used
to return a plain string: a car's `description`, and the six `sections` of a gallery page. A 1.0.0
build casts each field `as String?`, and a Dart cast of a map to a string throws — for every
written section and, after the backfill, for every car. The owner chose to refuse old builds rather
than keep a second copy of each field for them, and to build the refusal as a general mechanism for
every future breaking change rather than a one-off for this one.

**The server half: `MobileAppVersionGate`.** It sits after CORS and rate limiting and BEFORE
authentication, so a refused build never reaches a token check, a session or a handler. A request is
the customer app when it carries `X-Khadra-App-Version` (sent by 1.1.0 onwards on every call to the
API's own origin) or, without that header, a User-Agent beginning `Khadra (` — what the app has sent
since d6fddef, and so what every build older than the header sends. An app request below
`MobileApp:MinimumSupportedVersion`, or with a version that does not parse, or with no version at
all, is answered 426 with the usual ProblemDetails plus `code: app.update_required`,
`minimumSupportedVersion` and `updateUrl`, a title and detail in Arabic and English, and
`Cache-Control: no-store`. Never gated: anything outside `/api` (health, OpenAPI, Scalar),
`/api/v1/app-config` (so a refused build can still read the minimum), and any request not
identified as the app — the console through the BFF, a browser, server-to-server calls. With no
minimum configured nothing is gated at all. `MobileAppVersionGateTests` pins every one of those.

**The app half.** `/app-config` publishes `mobileApp: { minimumSupportedVersion, updateUrl }`. The
app compares its installed version (`package_info_plus`) with that minimum at launch, and a 426
carrying the code on any call mid-session does the same. Either one puts `UpdateRequiredScreen` in
place of the router, not over it, so no route and no deep link can reach past it; it stays up for
the life of the process. Arabic and English, with the language menu on it. It never ends the session
and never reads a 426 as bad credentials: `SessionController.refresh` treats it like a network
failure, and the stored token survives it (`update_gate_test.dart`, `update_gate_http_test.dart`).

Both halves order versions by Semantic Versioning 2.0.0 precedence — numbers as numbers, a
prerelease below its release, build metadata ignored — and both run their tests against ONE vector
file, `docs/contracts/app-version-vectors.json`, so they cannot come to disagree about which build is
newer. `MobileAppMinimumVersionTests` fails the suite if a tracked `appsettings` file sets a minimum
above `pubspec.yaml`'s version, or if the shipped minimum would let 1.0.0 back in.

**What an installed 1.0.0 build does.** Checked on the emulator with a build of b30fcdb against the
gated API. Every call is refused, and its error panels show the server's title, «حدّث تطبيق خضرا
للمتابعة · Update the Khadra app to continue». Its sign-in screen shows the same, by the same path:
that build shows the server's title for any code it does not know. One thing it does wrong that the server cannot fix: its
launch-time token refresh is refused, and that build treats ANY refused refresh as a verdict, so it
clears its own stored session. The server revokes nothing, but the customer signs in once after
updating. 1.1.0 onwards does not do this. (The uninstall 1.0.0 now needs, below, loses that session
anyway.)

**Still not gated:**

- Builds from before d6fddef (2026-09-21) send Dart's default User-Agent, which every Dart program
  sends, so nothing tells them apart from other traffic. They break as this item first described.
- A web build cannot set its own User-Agent, so an OLD web build is never identified. A new one sends
  the header and is gated like a phone.

**1.0.0 cannot be updated in place at all.** 1.1.0 is the first build signed with Khadra's release
key rather than a laptop's debug key (item 134), and Android refuses an update signed by a different
key. So a phone with 1.0.0 must uninstall it before installing 1.1.0, which loses its sign-in either
way, and the release notes must say so.

**To raise the minimum** for a future breaking change, follow CLAUDE.md, "The customer app's
contract", and `docs/contracts/README.md`: in the repository, one change set raises `pubspec.yaml`'s
version and `MobileApp:MinimumSupportedVersion` together; in production, that build is published
before the API carrying the minimum is deployed. Raised first, it refuses every customer with nothing
to update to.

### 134. The release signing key exists only on one laptop

**Status:** open · **Raised:** 2026-09-22

From 1.1.0 the customer app is signed with Khadra's release key, `.keys/khadra-release.jks`, alias
`khadra`, certificate SHA-256:

`AD:62:F2:E9:3B:AB:9E:55:5E:B2:D6:5D:44:70:31:0A:68:42:1A:D5:D6:B7:46:37:7D:9E:2D:31:AF:28:EF:C4`

Its password is in `Khadra.Mobile/android/key.properties`. Both are gitignored, and both exist only
on the owner's machine — inside the OneDrive-synced folder, which is a copy, not a deliberate backup,
and exposes the key to anyone who reaches that account.

Android installs an update only when it is signed with the same key as the copy already installed,
and the app is on no store that could re-sign it. Lose the key and no update can ever install over a
customer's app: each would have to uninstall, losing their sign-in, and install what Android treats
as a different app. It is the one credential in this system with no recovery path.

Until 1.1.0, every build was signed with that laptop's Android debug key, which is why this item
exists now: the switch was made on 2026-09-22, while only 1.0.0 test installs have to pay for it with
one uninstall.

**To close:** two copies of the keystore and its password away from this machine and OneDrive — the
password in a password manager, the file on offline storage — and a restore tested once: a release
built on another machine from those copies passes `apksigner verify --print-certs` with the
fingerprint above.

### 135. After a failed payment attempt the app cannot say why it failed

**Status:** open · **Raised:** 2026-09-23

The customer app's in-app checkout (`CheckoutScreen`) learns an attempt's outcome by re-reading the
booking. `PaymentAvailabilityDto.LiveAttempt` names only the attempt still in flight, so once an
attempt is declined, lapses or fails at the provider the booking read carries `liveAttempt: null` and
no `failureCode`. The app therefore says only what it knows — "that payment attempt ended without
confirming your booking" — and never "declined", "expired" or "try another card".

That is honest and it is enough for the sandbox. It is not enough for real cards: a customer whose
bank declined them and a customer whose session lapsed need different next steps.

**To close:** an additive, optional field on the customer's booking read (for example `lastAttempt`
with its `status` and `failureCode`, owner approval needed as it is the Payments read model), and the
app mapping those codes through `api_failure_messages.dart` — before the first real provider goes live.

### 136. Reminders are only as punctual as the API is awake

**Status:** open · **Raised:** 2026-09-23

Pickup, return and payment reminders are sent by the settlement pass, which runs inside the API.
On a free Render instance the API sleeps after fifteen idle minutes, and while it sleeps nothing is
sent: a reminder is sent late if the moment is still ahead when it wakes, and never once the moment
has passed. The settlement pass (expiries, no-shows, completion) has the same exposure.

**To close:** an always-on instance for the production API, or an external scheduler that wakes it.

### 137. Handover verification is built but not yet required

**Status:** open · **Raised:** 2026-09-23

`Handover:RequireVerification` is false, so a dealer can still record a pickup or return with no code
and no reason, exactly as before — because an installed 1.1.0 app cannot show a code.

**To close:** publish the 1.2.0 APK, then set `Handover__RequireVerification=true` and
`MobileApp__MinimumSupportedVersion=1.2.0` together (docs/production.md, "The handover code").

### 138. Push notifications are Android only

**Status:** open · **Raised:** 2026-09-23

The server sends APNs-compatible messages, but the iOS app is not registered with Firebase (no
`GoogleService-Info.plist`, no APNs key) and `PushMessaging` initialises only on Android. Close it
with the first iOS release.

---

## Customer website (`Khadra.Web`, branch `feature/customer-website`, 2026-09-23)

Numbered from 140 so they cannot collide with 135–138, which live on the unmerged
`feature/push-reminders-handover` branch.

### 140. The public website needs always-on, privately networked hosting — LAUNCH BLOCKER

**Status:** open · **Raised:** 2026-09-23

The website is three services on one path: the customer BFF (`Khadra.Bff` with
`BffSecurity__Deployment=customer-web`), the renderer (`Khadra.Web`, Node), and the API. On Render's
free plan each sleeps after about fifteen minutes and takes up to a minute to wake, so the first
visitor — and every crawler visit to a cold site — waits on up to three cold starts in a row, and a
crawler that times out drops the page. A free service also cannot receive private traffic, so the
renderer reaches the API over the public internet and the API sees ONE address for every visitor:
the per-address rate limit (`Public`, 1200/min, capped by the 600/min global limit) becomes one
bucket shared by all of them, which a crawl alone can exhaust.

**To close:** the customer BFF, the renderer and the API on always-on plans in one region, with the
renderer calling the API over the private network (`KHADRA_API_URL=http://khadra:8080`), the
renderer's address in the API's `KnownProxies`, and `KHADRA_EDGE_SECRET` set on the renderer to the
customer BFF's `BffSecurity__FrontendSharedSecret`. Owner decision (2026-09-23): not before launch;
production hosting is not to be changed without asking.

### 141. Car photos are served at full size on every page

**Status:** open · **Raised:** 2026-09-23

There is one size of every vehicle and gallery image — the file the office uploaded — and a results
page of twelve cards downloads twelve originals. It is acceptable at today's handful of cars and it
will not be at launch: it is the largest item in a page's weight and the usual cause of a poor
Largest Contentful Paint. Owner decision (2026-09-23): keep as a pre-launch optimisation.

**To close:** width variants made at upload (or an image-resizing edge in front of the public host),
exposed as an ADDED `thumbnailUrl` beside `coverImageUrl` so installed apps are unaffected, and
`srcset` on the website's cards and gallery.

### 142. The customer email links still point at the console until `App:CustomerAppBaseUrl` is set

**Status:** open · **Raised:** 2026-09-23

`AuthEmailComposer` now sends a customer's verification and reset links, and `BookingEmailComposer`
its booking links, to `App:CustomerAppBaseUrl` when it is set; empty, customers keep getting the
console's `/verify-email` and `/reset-password`, exactly as before. The website redirects a
language-less `/verify-email?token=…`, `/reset-password?token=…` and `/bookings/{id}` to the
reader's language, so one host serves the emails and, later, the app links of item 91.

**To close, in this order:** deploy the customer BFF and the renderer on staging; confirm
`/verify-email` and `/reset-password` redeem a real token there; only then set
`App__CustomerAppBaseUrl` on the staging API. Production waits for its own customer domain. Never set
it to the console.

### 143. The payment return address must follow the website on staging

**Status:** open · **Raised:** 2026-09-23

A provider sends the customer back to `{Payments:ReturnUrlBase or App:ClientBaseUrl}/bookings/{id}`.
The website's `/bookings/{id}` re-reads the booking and never trusts the redirect, so it is ready to
be that address. Owner decision (2026-09-23): point staging's `Payments__ReturnUrlBase` at the staging
website once it is live; production stays on `Payments:Provider=None` and its return address is set
with the real provider. The sandbox checkout has not been driven end to end through the website
(the shared development database is on `None`, and moving it to `Sandbox` would pin it there for good).

### 144. The website's handover codes have not been verified by an office end to end

**Status:** open · **Raised:** 2026-09-23 · **Updated:** 2026-09-24

`feature/push-reminders-handover` (5d34fef) is merged into `feature/customer-website`. The booking
page shows "Show my pickup code" while Confirmed and "Show my return code" while PickedUp; the panel
takes `POST /bookings/{id}/handover-code`, shows the six digits and a QR of `qrPayload`, counts
down to the server's `expiresAt`, greys out when it passes, and "Get a new code" replaces it (the
server supersedes the old one). While it is open the page re-reads the booking every five seconds
and closes the panel with "Handover recorded." when the status moves on. Each recorded handover
shows how it was proved (`verification`: Code / Unverified with the office's reason / NotRequired).
Notifications word every customer kind the platform sends and open the booking, or — for a dispute
update — the new read-only `/disputes/{id}` page.

**To close:** an office verifies a pickup code and a return code shown by the website (and one
unverified handover with a reason) on staging, with the customer page watching.

### 145. A mistyped car or office URL is corrected with a canonical tag, not a 301

**Status:** open · **Raised:** 2026-09-23

`/cars/{words}-{id}` is found by its id whatever the words say; the page renders with a canonical tag
naming the current words and the browser replaces the address. A crawler therefore sees a 200 with a
canonical rather than a permanent redirect. Search engines honour the canonical, but a 301 is the
stronger signal.

**To close:** a server-side check in the renderer that answers 301 when the words differ, once the
renderer can make that one extra API read cheaply (item 140).

### 146. Disputes have no pages on the website

**Status:** open · **Raised:** 2026-09-23

The booking page says when a dispute is open, and the notifications list shows dispute updates, but a
customer opens, reads and answers a dispute only in the app for now. A dispute notification on the
website stays on the notifications page instead of linking to a page that does not exist.

### 147. The website has not been driven through dealer approval, payment and handover

**Status:** closed · **Closed:** 2026-09-24 — driven end to end through the screens, with an Al-Nadeem employee and owner in the console.

On a copy of the development database, with Sandbox payments: approval (by an employee), the payment
countdown, a declined then a captured Sandbox payment, Confirmed, a paid cancellation inside the free
window, the pickup code and QR, pickup verified by PIN (employee), an unverified pickup with its
reason (employee), return verified by the scanned QR payload (owner), return by PIN (owner), and the
refusals: wrong code, a QR naming another booking, a superseded code inside its lifetime, an expired
code, five wrong guesses locking the code, no code and no reason, a reason under ten characters, and
the per-booking limit on new codes. Every page updated itself after each handover, and every step's
notification arrived and opened its booking. English and Arabic, desktop and phone. Five website bugs
found on the way were fixed in the same pass.

### 148. Two public catalogue reads load more rows than they return

**Status:** open · **Raised:** 2026-09-23

`CatalogueReader.FacetsAsync` reads every bookable car's make into memory to merge spellings, and
`ListGalleriesAsync` reads every trading office (twice: to order them, then to build the page's cards),
because the business name sits behind a converter SQL cannot sort by. Both are correct and bounded by
the platform's size; neither will stay cheap if the catalogue grows by orders of magnitude.

**To close:** group makes in SQL (`GroupBy(make.ToLower())` with a count), and order offices by a
column EF can translate, once either shows up in a profile.

### 149. A page whose API call fails during server rendering shows its loading state

**Status:** closed · **Closed:** 2026-09-24 — a failed read renders its error panel on the server, and a page whose primary read failed answers 503.

When the renderer's own call failed (seen against staging, whose API has no `GET /galleries` yet),
the server sent the section's skeleton with a 200. The cause was Angular's `resource.value()`, which
THROWS once a resource has failed: a computed or template expression reading it aborted the render
half-way. Every page now reads through `httpData` (`Khadra.Web/src/app/core/http/http-data.ts`), whose
`value()` answers undefined on failure, so the error panel is what gets drawn. The home page (its
cars), `/cars` (any failure except a 400 the API explained in words) and `/dealers` set 503, as the
car and office pages already did for their record; a secondary section that fails (the home page's
offices on today's staging) shows its own error panel inside a 200 page. Verified against an
unreachable API, against staging and against a healthy local API.

### 150. The website needs this branch's API on staging before it can be deployed there

**Status:** open · **Raised:** 2026-09-24

Probed read-only on 2026-09-24: staging's API has no `GET /api/v1/galleries` (answers 401) and its
facets carry no makes, fuel types or years. Staging's `payments.mode` is Sandbox and its app minimum
is 1.1.0. **To close, in this order** (`docs/deployment.md`):

1. Deploy this branch's API to staging. It carries the four migrations of the push work
   (`PushDevicesAndPreferredLanguage`, `NotificationDeliveryOutbox`, `BookingReminders`,
   `HandoverCodes`), `GET /api/v1/galleries`, the catalogue's make / fuel / year filters, sort and
   facets, the handover-code endpoint, and email links that open the website.
2. Set on the staging API: `App__CustomerAppBaseUrl` and `Payments__ReturnUrlBase` to the website's
   staging address (only once it exists), and `Payments__SandboxConsoleBaseUrl` to the staging API's
   own https address. `Handover__RequireVerification` is the owner's decision (item 154).
3. Deploy the customer BFF (`BffSecurity__Deployment=customer-web`, its own Redis realm) and the
   renderer (`KHADRA_API_URL`, `KHADRA_PUBLIC_BASE_URL`, `KHADRA_EDGE_SECRET` shared with the BFF).

### 151. A customer's dispute payload carries more than a customer should hold

**Status:** open · **Raised:** 2026-09-24

`GET /api/v1/disputes/{id}` answers a customer with the admin-facing `DisputeDto`: the assigned
administrator's id and name, `OpenedByUserId`, each statement's `AuthorUserId` and `AuthorName` (for a
dealer statement, a staff member's personal name — the dealership's internal business), and the
platform's own split (`RetainedByPlatform`, `TransferredToDealer`). The website's dispute page renders
none of these, but they reach the browser. It is the installed app's existing contract, so nothing is
removed. **To close:** an additive redaction in the dispute view for non-admin callers — the gallery's
name in `AuthorName` for a dealer statement, null admin ids and names, and no platform split — with a
test per party.

**Who sees which dispute share, decided 2026-09-26** (`docs/payments-programme.md`): the customer only their
own share; the rental office its own share and any charge assessed to it; the administrator every share.
The booking's financial views follow it from payments Phase 4. The dispute pages do not yet: the
customer's payload carries the office's and the platform's shares (above), and the office's dispute
page shows the customer's refund and the platform's share. Closing this item brings both into line.

**The office's half is done, 2026-09-27 (payments Phase 4b; owner, 2026-09-27).** Its copies of the
BOOKING are projected: `BookingDto.ForDealer` drops the refund list (a dispute decision's row is on it),
the refunded and outstanding totals, the deposit's refund and every figure that carries the processing
fee — on `GET /bookings/{id}`, on the answers to approve, reject, pickup and return, and on the booking
inside the office's dispute view. So is its copy of a DECISION: `DisputeResolutionDto.ForDealer` keeps
the basis the decision split, the office's own share and any charge assessed to it, and sends
`refundToCustomer`, `retainedByPlatform` and `waivesEverything` (true only when the platform kept
nothing, so it would tell the office the customer's share) as null — on `GET /disputes/{id}` and the
answers to open, add a statement and withdraw. The office's dispute page shows its part only, and the
administrator's resolve form says the office sees only its share, so the note should not name the
others. `DisputeUseCaseTests` pins each party's copy, the office's JSON included, and the composer
takes its reader as a required argument, so a path that forgets it cannot fall through to the
administrator's copy. Not a gap: the customer-reputation panel an office sees on a LIVE booking counts
disputes the customer won outright (`CustomerReputationReader`), which is the same "the platform kept
nothing" fact the office's copy withholds — aggregated across the customer's bookings and never on the
booking the dispute was about. That is the panel's own design (2026-09-11), not a leak to close here.

**What remains, deferred by the owner on 2026-09-27:** the customer's half. The customer's copy still
carries every share (`transferredToDealer`, `retainedByPlatform`) and every other field above; changing
it changes what installed builds are sent, so it is checked against what they parse and, if any of them
reads those fields, ships under the raised-minimum rule. The non-money parts of the original "To close"
— administrator ids and names for non-admin callers, the gallery's name for a dealer statement — are
untouched for both parties.

### 152. A free cancellation says "costs you nothing" while the deposit stays held

**Status:** closed · **Closed:** 2026-09-24 — the paid free cancellation now refunds the deposit, and every screen says so.

The owner decided (item 77) that the deposit goes back in full. The cancel sheet on the website and in
the app now says, when the server's `cancellation.willRefundDeposit` is true: "Free cancellation. Your
deposit will be refunded in full to your original payment method. We will initiate the refund
immediately, but your bank may take additional time to show it." (and the owner's Arabic). After the
cancellation the booking shows the refund's own state — initiated, refunded, or delayed and still owed —
instead of "Deposit paid", on the website, the app, the dealer console and the admin console
(`BookingDto.depositRefund`). An unpaid free cancellation keeps "Cancelling now costs you nothing",
which is true.

### 153. The console's handover refusals are easy to miss on a narrow screen

**Status:** open · **Raised:** 2026-09-24

A wrong, expired or locked code is refused with a clear sentence, but as a toast, which on a narrow
console window sits behind the still-open handover dialog. The dialog's note also says the code is
"in their Khadra app"; website customers have it on the website too. **To close:** show the refusal
inside the dialog, and word the note for both.

### 154. Whether staging and production require the handover code

**Status:** open · **Raised:** 2026-09-24 · **Owner decision**

`Handover:RequireVerification` is false in tracked settings, so a handover without a code is recorded
as NotRequired and the unverified path (reason, audit, the customer's "recorded without your code")
never runs. The full path was tested locally with it on. **To close:** the owner decides per
environment, and the setting is set to match.

### 155. A failed cities read makes a city search look like "Any city"

**Status:** open · **Raised:** 2026-09-24 (Fable advisor review)

When `/api/v1/cities` fails, `LookupsService.cityName()` answers an empty string, so a city-filtered
search is headed "Any city" and the city selects offer only "Any city", with no error shown. The
search itself still sends the city, so results are right; only the wording misleads. **To close:**
show the city filter as unavailable while the lookup has failed, with a test.

### 156. A gallery or admin cancelling a PAID booking inside the free window refunds nothing

**Status:** closed · **Closed:** 2026-09-26 — the owner's Phase 3 decisions.

An administrator's cancellation of a paid booking before pickup now returns the whole payment, deposit
included, at once (`PlatformCancellation`). A gallery's cancellation returns everything above the
deposit at once, and the deposit when the window closes with no claim on it — the penalty such a
cancellation assesses is against the office and never holds the customer's money.

`AssessCancellation` treats the free window as free for EITHER party, so a gallery that cancels a paid
booking within that hour is assessed nothing — and, because the owner's refund rule names the
customer, the customer's deposit stays held with no refund. The customer is worse off than if they had
cancelled themselves. **To close:** the owner decides whether any cancellation of a paid booking inside
the window (by the gallery, or by an admin) also refunds in full; if so, widen
`Booking.ReturnsDepositOnCancellation` and its tests, and nothing else changes.

### 157. A refund the provider keeps refusing is re-sent every minute, for ever

**Status:** open · **Raised:** 2026-09-24

The payment sweep re-sends every Failed refund on every tick and logs an error each time. That predates
the free-cancellation refund, but now every paid free cancellation produces a refund, so one a provider
permanently refuses (a closed card, a provider rule) becomes a log flood and an unbounded retry.
**To close:** back off (a growing interval, a cap), surface refunds refused more than N times on the
admin's payments screen for a human, and test both, before a real provider is connected.

**Progress, 2026-09-27 (payments Phase 4b):** the human half is built. The refunds queue
(`/payments/refunds`) lists refused refunds first, each with the provider's code; the dashboard's work
queue carries one `RefundFailed` row for all of them, and the bell rings for it; the money panel counts
them (`refundsFailed`). It surfaces EVERY refused refund, not those refused more than N times, because
the count cannot be read: `Refund` stores only `FailedAt`, which `MarkFailed` overwrites on every
refusal. What remains — the back-off and the log flood — needs that field first: a refusal counter on
the refund (a migration), the growing interval and the cap read from it, and their tests.

### 158. The gallery's screens still show commission and payout on a free-cancelled paid booking

**Status:** closed · **Closed:** 2026-09-27 — the office's money section words commission from the server's state.

The office's booking page reads its money from the financial state's office projection (payments
Phase 4b). Commission is worded from its `commission.state` (projected, expected, earned, not earned,
undecided), and the payout line is left out whenever the platform keeps nothing (`NotEarned`,
`NotApplicable`), so a free-cancelled paid booking says the commission was not earned and shows no
payout. `office-money.presenter.spec.ts` pins both cases.

The dealer console's money panel lists the frozen platform commission and the net payout for every
booking, including a cancelled one whose deposit a free cancellation refunded, where the platform
takes nothing and pays nothing. The deposit row now says the deposit went back to the customer; the two
rows below it are still the booking's frozen figures. **To close:** hide or zero commission and payout
for a cancelled booking (the Payments ledger decides what a cancelled booking actually earned), with a
test per status.

### 159. A real adapter's refund events must name the refund they settle

**Status:** closed · **Closed:** 2026-09-26 — `ProviderEvent.RefundReference`; the sandbox names every refund.

The webhook settles or refuses exactly the refund the event names (`Payment.RefundWithProviderReference`).
An event that names none is accepted only when exactly one refund is out with the provider for exactly
its amount; anything else is recorded as `Unmatched` and logged at Error, never guessed. The sandbox's
refund reference is derived from the refund's id under the webhook secret, so a re-send keeps it, as a
real provider's idempotency does. What remains for the real adapter is item 163.

`ProviderEvent` carries no refund reference, so `RefundSettled` / `RefundFailed` settle "the first Sent
refund" of the payment. That is safe today — at most one refund of a payment is ever Sent, which the
free cancellation's guards and `DepositHeldFor` reading zero make true — but a real provider, a future
partial dispute refund and a re-sent one together could settle the wrong row. **To close, with the
real adapter:** the adapter puts the refund's provider reference on the event, and the handler matches
on it, with a test that two refunds on one payment settle independently.

### 160. The refund promise on the sheet can be a minute older than the tap

**Status:** closed · **Closed:** 2026-09-26 — `expectedRefund` (owner decision 2).

The cancel sheet on the website and in the app sends the refund it showed (`cancellation.refundAmount`);
when it no longer matches, nothing is cancelled and the answer is 409 `booking.refund_changed` with the
current figure, which both clients show before asking again. An installed app that does not send it is
answered as before.

`cancellation.willRefundDeposit` is computed when the booking is read. A customer who opens the sheet
inside the free window and taps after it closes is assessed the penalty and refunded nothing, against
a sheet that promised the deposit back; the response then shows the truth. The penalty preview has
always had the same race. **To close, if the owner wants it:** the client sends what it was promised
(for example `expectFree: true`) and the server answers 409 when `CancellationWouldReturnDeposit` is
no longer true, so the customer is asked again rather than surprised.

### 161. An office's name has no Arabic form

**Status:** open · **Raised:** 2026-09-24 · **Owner decision**

`BusinessName` is one string, entered once at registration, so every Arabic page — the directory, the
home page's office cards, a car's office line — shows an office by the name it typed, usually in Latin
letters. The directory's new name search (`GET /galleries?text=`) matches that one string, so an
Arabic speaker searching in Arabic finds an office only if it registered its name in Arabic. Everything
else an office writes for customers is already bilingual (`ResolvedText`); its name is the exception.
**To close, if the owner wants it:** a second, optional Arabic name on the dealer, entered in the
dealer console, resolved per `Accept-Language` like the office's texts, and searched alongside the
first. It is a schema change and a contract addition, so it is decided before it is built.

### 162. From the one-day commission on, the platform holds office money nothing records

**Status:** closed · **Raised:** 2026-09-24 · **Closed:** 2026-09-30 (payments Phase 8, `b2818ea`) — the office payables ledger records what Khadra owes each office, booking by booking, and an administrator settles it by hand.

Commission is 20% of one day's rental (frozen per booking since `FrozenCommission`), the deposit 20%
of the whole rental. On any booking longer than a day the deposit exceeds the commission, and the
difference belongs to the office — and a full online payment makes it the whole rental. The owner
accepted holding it on 2026-09-24 with a manual office-payable ledger: payable at `Completed`, never
marked settled while a refund or dispute on the booking is open, paid by hand, marked paid by an
administrator with an audit entry. **To close:** that ledger (payments phase 8). Until then, no real
money may move — which item 76 already guarantees, since there is no merchant account.

**Commission, decided 2026-09-26 for Phase 4** (`docs/payments-programme.md`): earned at `Completed`
when no dispute or refund affects settlement; not earned when the whole payment is refunded; and two
cases are left **Undecided** for this ledger to settle — a booking that completed through a dispute
resolution, and a deposit held for a customer penalty (Phase 4 takes no commission from it).

**How it closed.** Every paid booking whose outcome is final — a completed rental once a finality margin has
passed, a cancellation or a no-show once its dispute window has closed, never while a dispute is live — is given
ONE `OfficePayable`, recorded by the settlement pass from the one calculator: the office's money, Khadra's
commission and any charge a dispute put on the office, each a line, and the net as one signed figure. Its figures
are frozen (a database trigger refuses any change to them), and a pass checks each unsettled one against its
booking's records again, holding it back if they no longer agree (item 209). An administrator settles an office's
due balance by hand from `/payouts` — the day it was paid, a reference, a note — for exactly the figure the screen
showed; the settlement is numbered (`SET-2026-000001`, `TEST-SET-…` for sandbox money), audited in the same
transaction, and voidable with a reason, which reopens what it covered. Nothing is due while a refund or a live
dispute on the booking is open (item 207 says which refunds count) or while an administrator holds it, and a
settlement pays the whole due balance (item 206). The two cases this item left Undecided are decided: commission is
the frozen figure capped at the office's money (owner, 2026-09-29), and the held deposit is item 164. The office reads
its own payables and settlements at `/dealer/payouts`. Real money still waits for item 76.

### 163. A refund event can arrive before the platform has recorded the refund as sent

**Status:** open · **Raised:** 2026-09-26 (Fable advisor review) · **Belongs with item 76**

The payment sweep sends each refund, marks it Sent in memory, and saves once after the loop. A provider
fast enough to post `refund_settled` inside that window names a reference the row does not carry yet:
the event is recorded `Unmatched`, answered 2xx, and not retried, so the refund reads Sent for ever. The
sandbox cannot produce it (a tester settles by hand). **To close, with the real adapter:** match on the
platform's own refund id where the provider echoes the idempotency key, or save after each send, or
reconcile Sent refunds against the provider — with a test of the race.

Since payments Phase 4b (2026-09-27) the event is at least VISIBLE: a payment's page (`/payments/{id}`)
lists every provider event tied to it, by id or by reference, with its outcome, so an `Unmatched`
event reads "No refund matched" beside the refund still showing Sent. The race itself is unchanged.

### 164. A deposit held for a penalty against the customer has no way out once the window closes

**Status:** closed · **Raised:** 2026-09-26 (Fable advisor review) · **Closed:** 2026-09-30 (payments Phase 8, `b2818ea`) — owner decision of 2026-09-29: the deposit is kept as the penalty, for the office less Khadra's commission.

A late cancellation or a self-pickup no-show assesses a penalty against the customer on the deposit.
Phase 3 correctly does not release that deposit when the window closes. But money moves only through a
dispute ticket, and none can be opened after the window, so the deposit then sits on the platform with
no path to the office or back to the customer. The dealer console says "Held pending settlement" for it,
which is true and open-ended. **To close:** the owner decides what a closed window with an assessed,
undisputed customer penalty means for the money — it belongs with the office payables ledger (item
162, payments Phase 8).

**What the customer reads meanwhile, decided 2026-09-26:** factual and neutral, promising nothing to
either side — "Your deposit remains held because a customer penalty was assessed and no dispute was
opened. Final settlement is still pending." / "لا يزال عربونك محتجزًا لأنّ غرامةً قُدِّرت على العميل
ولم يُفتح أيّ نزاع. التسوية النهائية لا تزال معلّقة." Phase 4 shows it; the outcome is still this item's.

**Progress, 2026-09-27 (payments Phase 4b):** each such deposit is on the administrator's work queue, one
`DepositAwaitingDecision` row per booking — found by `ListHeldForCustomerPenaltyAsync` and kept only
where the calculator reads `HeldUnresolved` (`HeldDepositFinder`), so the calculator stays the one
definition. It is WATCHED, not work: the dashboard lists it, the bell does not, because nothing an
administrator can do clears it before Phase 8. **The candidate set is unbounded and re-read often:**
every such booking since launch, loaded with its payments and tickets, each time the queue is read — and
every administrator's console polls it every 60 seconds while open, on every screen, for the bell. Harmless at today's volumes. **Cap it** (list the oldest few with a total, or keep a watermark)
before the set passes a few hundred bookings, or retire the query when Phase 8 gives these deposits an
exit and the set stops growing.

**How it closed.** The owner decided on 2026-09-29: when a paid booking that never ran ends with a penalty on the
customer, and its dispute window closes with no dispute, the penalty is final — the deposit it was assessed on is
kept, and it is owed to the office less Khadra's frozen commission. It amends spec 3.3 for this one case; every
other assessment (the office's, a range, one on nothing) still needs a ticket, so
`PenaltyAssessment.RequiresTicketToEnforce` is now false for a customer's fixed penalty alone. The ledger records
the booking's payable as `PenaltyKept`, and from then — from the recorded payable, never from the clock — the
calculator reads the deposit as `KeptAsPenalty` and the penalty as `KeptFromDeposit`. Only the whole deposit can be
kept, so the penalty is locked at 100% until a partial path exists (item 205); the customer's sentences for it, on
the website, in app 1.3.0 and on the booking statement, are drafts awaiting the owner, and the installed apps' old
promise is a launch gate (item 208). The `DepositAwaitingDecision` work-queue row is retired with the unbounded
read behind it: these deposits now have an exit, and a booking the ledger cannot record is a `PayablesOnHold` row
instead. `HeldDepositFinder` itself is unused and waits for the owner's word to be deleted (item 214).

### 165. Bookings that ended before Phase 3 are settled by today's rule when it deploys

**Status:** open · **Raised:** 2026-09-26

On its first pass the settlement sweep releases the deposit of every earlier paid booking whose window
closed cleanly (a gallery's cancellation, for instance), and the payment sweep logs — but does not
refund — a payment in full whose booking ended before pickup, or an administrator's cancellation,
without the refund Phase 3 would now record. Only sandbox money exists on staging, so nothing real
moves. **To close:** before a real provider, look at the `2316` lines on staging and decide by hand
what each logged payment is owed; there is no administrator rail to record such a refund yet.

### 166. The consoles decide "deposit held" from the list of refunds

**Status:** closed · **Closed:** 2026-09-27 — both consoles render the server's `deposit.state`.

The office's and the administrator's booking pages read the booking's financial state
(`GET /bookings/{id}/financials`, payments Phases 4a and 4b) and word the deposit from its
`deposit.state` instead of deriving it from the refund list. A dispute resolved with nothing to the
customer is `DecidedByDispute`, not held; a deposit held for an assessed customer penalty is
`HeldForAssessedPenalty` and then `HeldUnresolved` (item 164).

The dealer console says a deposit is "held pending settlement" when it was paid and no refund has
returned or decided it. A dispute resolved with nothing to the customer records no refund, so such a
deposit still reads "held". **To close:** the booking's financial state from the server (payments
Phase 4), which the consoles render instead of deriving.

### 167. The website's Arabic penalty line reads "على أنت"

**Status:** open · **Raised:** 2026-09-26 (found in the payments Phase 3 browser run; not part of Phase 3)

On a booking the customer cancelled late, the website's Arabic page says "قُدِّر مبلغ … على أنت":
`booking.penalty` and `booking.penaltyRange` in `Khadra.Web/src/app/core/i18n/ar.ts` put the party's
word after "على", and the customer's word (`booking.party.Customer`) is "أنت", which Arabic does not
attach that way — it is "عليك". The customer app already solved this with its own on-you sentences
(`bookingPenaltyAssessedOnYou`, `bookingPenaltyRangeOnYou`). **To close:** the same pair on the
website, chosen when the penalty is attributed to the customer, with an Arabic test.

### 168. The office's approve dialog says free cancellation starts at approval

**Status:** open · **Raised:** 2026-09-26 (found in the payments Phase 3 browser run) · **Fix before release**

The dealer console's approve confirmation (`dealerDecide.approve.body`, English and Arabic) ends
"The customer's free-cancellation window starts now." Since 2026-09-25 the window starts when the
PAYMENT is confirmed ("within 1 hour after payment", `Booking.FreeCancellationDeadline`), not at
approval, so the office is told the wrong moment. **To close:** reword both languages to say the
window opens when the customer pays, with a dictionary test.

### 169. A resolved dispute does not stop a second ticket splitting the same deposit

**Status:** closed · **Raised:** 2026-09-26 (found in the payments Phase 3 browser run; older than Phase 3) · **Settled:** 2026-09-26, owner · **Built:** 2026-09-26

The owner's rule: a second dispute may still be OPENED inside the window, but it can only split the
part of the deposit earlier disputes left unresolved, never the original deposit again, and the
backend is the only source of that figure. Built as:

- **One calculator**, `DisputedDeposit.For` (Application/Disputes): the deposit the booking holds for
  disputes (`BookingDisputeSettlement.DepositHeldFor`, which already reads zero once the whole payment
  went back or the window released it) less every leg — refund, platform, office — of the booking's
  RESOLVED tickets opened BEFORE the one asked about (`IDisputeTicketRepository.ListResolvedForBookingAsync`).
  A withdrawn ticket decided nothing. A resolved ticket keeps the basis it was decided against.
- **The resolve handler** validates against it: the split must still add up to exactly the basis
  (`dispute.disposition_unbalanced`), so after a resolution that decided the whole 18.000 a second
  ticket can close only with an all-zero split and a note. Earlier decisions that already exceed the
  deposit are refused (`dispute.deposit_over_allocated`, 409), never floored at zero.
- **The office charge** is bounded across the booking's disputes too: every charge together stays
  inside the booking's assessed range, the minimum binds only the first, and an office charged the
  maximum cannot be charged again (`DisputeResolution.Create`, `alreadyChargedToDealer`).
- **The view** states all three figures from the same calculator — `depositHeld` (what this ticket can
  split), `depositOnBooking`, `decidedByEarlierTickets` — additively (docs/contracts). The admin form,
  the office's dispute page, the customer website's dispute page and app 1.3.0 show them with an EN/AR
  notice on a live ticket; no client subtracts. The app's resolution card also stopped calling the
  dispute deposit the car's "Security deposit".

No schema change and no migration. A second ticket is reachable only on a cancelled or no-show
booking: resolving a dispute on a returned booking completes it, and a completed booking cannot be
disputed. The owner's second example — a first dispute that resolves only 5.000 of 18.000 — is honoured
by the calculator and the handler (a later ticket then splits 13.000), but no resolution can decide
only PART of its basis today; whether one may is item 170.

### 170. A dispute resolution must decide the whole deposit it can split

**Status:** closed · **Raised:** 2026-09-26 (while closing item 169) · **Settled:** 2026-09-26, owner — kept as it is

`DepositDisposition.Create` insists the three legs add up to EXACTLY the basis, so a resolution always
decides all of what its ticket can split, and after one resolution every later ticket's basis is zero.
The question was whether a resolution may decide only PART of it and leave the rest held for later.

**Decision (owner, 2026-09-26):** no. Every resolution fully allocates the amount currently available
to its dispute — customer share + office share + platform share = the whole available held amount —
and how it is split stays the administrator's choice: for an 18.000 deposit, for example, 13.000 back
to the customer and 5.000 to the office. A partial, unresolved decision is not needed and is not built.
Were it ever wanted, it would need an explicit held leg and a rule for where the remainder goes when
the window closes, because an undecided remainder would otherwise be stranded exactly as in item 164.
The calculator and the resolve handler already treat an earlier partial decision correctly (tests pin
it), so nothing here would have to change first.

### 171. A later dispute's basis leans on ticket opening times, and dispute_tickets has no plain booking index

**Status:** closed · **Raised:** 2026-09-26 (Fable advisor review of item 169) · **Closed:** 2026-09-30 (payments Phase 8, `011d064`; the index in `b2818ea`)

`DisputedDeposit.For` counts the resolved tickets opened BEFORE the one asked about. That is exact
while one API node writes `OpenedAt` (only one ticket per booking can be live, so a ticket always opens
after the previous one closed), but a tie or a clock skew between nodes inside an open-resolve-open
sequence would make neither ticket "earlier", and both would be offered the full deposit. Not reachable
through a console at human speed. **To close:** make it timestamp-free — for a live ticket count every
other resolved ticket; for a resolved one read what earlier disputes decided as its stored basis
subtracted from the deposit on the booking — and add a plain index on `dispute_tickets.booking_id`
(the only one today is the partial index for live rows), which `ListResolvedForBookingAsync` and
`HasClaimOnDepositAsync` both scan without. The index needs a migration; the table is small.

**How it closed.** Exactly as proposed. `DisputedDeposit.For` compares no timestamps: a live ticket counts every
other resolved ticket of its booking, and a resolved one reads what earlier disputes decided as the deposit less the
basis it was itself decided against, which its resolution stores. Two tests pin both whatever the tickets' clocks
say. The plain index, `ix_dispute_tickets_booking`, ships in the Phase 8 migration, which needed one anyway.

### 172. Invoices and receipts must reach the customer — the required scope of payments Phases 5–7

**Status:** open · **Raised:** 2026-09-26 (owner) · **Required scope — may not be dropped or narrowed without the owner**

Issued financial documents are not a backend-only feature. The owner recorded the customer
experience Phases 5–7 must deliver (`docs/payments-programme.md`, "Required scope for Phases 5–7"):
a dedicated **Invoices / الفواتير** area in the Flutter app; every earlier invoice and receipt available
permanently from the customer's account; the related receipt or invoice reachable from Booking
Details; immutable payment receipts, refund receipts and booking statements; PDF viewing and download;
email delivery of each issued document; English and Arabic document and email content; refunds as
linked records that never overwrite the original payment receipt; and email delivery status and
history the administrator can see (queued, sent, failed). **To close:** all of it built and verified
end to end on both customer clients, in both languages — not the backend alone.

**Progress.** Phase 5 built the documents, the customer's area on the website and in the app, Booking
Details' documents and the linked refunds (2026-09-27 to 29). Phase 6 (2026-09-29) built PDF viewing and
download in English and Arabic on the website, in the app (1.3.0, unreleased) and in the console. Its
follow-up the same day (`506fef7`) settled the one default left: a VOIDED document stays in the customer's
history and its PDF is a voided copy — stamped VOID / «ملغى» on every page, saying it is no longer valid and
naming its correction — while the original stays the administrator's (`docs/payments-programme.md`, Phase 6,
3). Phase 7 (2026-09-29) built the last of it: every payment and refund receipt, and every correction, is emailed
to its customer with its PDF — in the language they chose, or Arabic and English for a customer who never chose,
never to an address they have not verified — and the administrator sees each email's state (queued, sent,
skipped, failed), who asked for it, where and in which languages it went, and every attempt, and can email a
receipt again; English and Arabic throughout (`docs/payments-programme.md`, Phase 7; `730a9c5`, verified live through
Mailpit on 2026-09-29). What stands between this item
and closing is its own test: the whole of it verified end to end on both customer clients — the app's open flow on
a phone is the one part no stack here has reached (Phase 6).

### 173. The penalty notice still says "Nothing has been charged" after a dispute settled the penalty

**Status:** closed · **Raised:** 2026-09-26 (found in the payments Phase 4a browser run) · **Closed:** 2026-09-26 — the notice words the server's `penalty.state` in the owner's two sentences.

A booking with an assessed penalty carries a notice on the website (`booking.penaltyNotCharged` in
`booking-detail.component.html`, whenever the penalty is not "nothing owed") and in the app
(`bookingPenaltyNotCharged`, whenever `penalty.requiresTicketToEnforce`): "Nothing has been charged.
An assessment only becomes money if a dispute is opened and Khadra settles it." Both say it for as long
as the penalty exists — also after a dispute has settled it. On KH-NY8AHLNK, where a dispute split the
18.000 deposit 9.000 to the customer and 9.000 to the office, the notice sits above the Payments
section stating that decision and contradicts it. **To close:** once a dispute has decided the deposit
(the financial state's `DecidedByDispute`), the notice says so instead, in wording the owner approves,
on the website and in the app, in both languages, with tests.

**How it closed.** The owner gave the two sentences (`docs/payments-programme.md`, "the penalty
notice"). The booking answer now carries `penalty.state` — `Assessed` until a dispute on the booking
is RESOLVED, then `ResolvedByDispute` — read by the server from the booking's own dispute tickets
(`BookingContext.HasResolvedDispute`); a withdrawn ticket decided nothing and an open one has not
decided yet. The website and app 1.3.0 word only those two states and leave any other unsaid. The app's
dispute screen also ended its resolution card with the old sentence, written for the time before
Payments existed; that line is gone, as the website never had it. Installed 1.1.0/1.2.x keep the old
sentence until they update: production takes no payments, so no dispute there can have settled a
penalty before then.

## Audit subjects in the Arabic console (2026-09-27)

The dashboard's activity strip read "Azzeldeen Al-Arabiat حسم النزاع Dispute on KH-NY8AHLNK": a
dispute's audit label was the English sentence "Dispute on KH-…", in a table that refuses UPDATE.
Fixed the same day: the feed and the audit log now carry `entityId` and `bookingReference` beside
the label, read through the ticket, and the console words a dispute and a customer from those facts,
old rows included (`AuditRows`, `audit-subject.ts`). New dispute rows store the bare reference. The
feed is one whole sentence per action, passive in Arabic. These three were found while doing it and
left for later.

### 174. The audit log's Change column prints stored English on the Arabic screen

**Status:** open · **Raised:** 2026-09-27 (while wording the audit subjects)

`previous_value` and `new_value` are printed as stored, in the row and in its expanded detail. Most
are enum names — "Open", "Suspended", "PendingReview", "Admin", and since payments Phase 8 a payable hold's
reason ("Manual") — which the console could word the
way it words every other status, but nothing maps them yet. Some are English composed on the server
and can only be shown as they are: the dispute resolution summary ("Resolved: of 18.000 JOD held,
refund …", item 50), the handover line ("PickedUp (Pickup, Code)"), the code lock ("Pickup code
locked after 5 wrong tries") and a lookup's "Amman / عمّان · Offered". **To close:** word the enum
values through `statusLabel`/`enumLabel` by the entry's type; store parts rather than sentences for
new rows (item 50 for disputes; the handover and lookup writers too); show what old rows hold in an
`.ltr` run, as a penalty assessed before reason codes does.

### 175. "System" is English on the Arabic console

**Status:** open · **Raised:** 2026-09-27

An entry recorded with no actor (the bootstrap administrator's invitation, and any recorder that
finds no signed-in admin) carries `AuditEntry.SystemActorName`, "System", as its actor name. The
audit log prints it in the Who column (its role line is translated); the activity strip prints it in
the sentence, "دُعي المشرف … من قِبل System". The feed cannot tell that entry from a person named
System, because it does not carry `actorUserId`. **To close:** add `actorUserId` to the feed entry
(additive; only the console reads it) and word the system actor from a key on both screens.

### 176. A city or car type is recorded in the audit trail by its English name only

**Status:** open · **Raised:** 2026-09-27 · **Owner decision**

`LookupUseCases` writes `NameEn` as the subject label, so the Arabic strip reads "أُضيفت المدينة
Madaba من قِبل …". The label is a snapshot on purpose: the entry has to say what the city was called
when it was changed, and reading today's Arabic name would restate a renamed entry's history. Related
to item 161 (an office's name has no Arabic form). **To close, if the owner wants it:** snapshot the
Arabic name as well for new rows and word the subject in the reader's language; old rows keep their
English name.

## Issued financial documents (payments Phase 5a, 2026-09-27)

The backend of the receipts and statements in `docs/payments-phase5-plan.md`, and what it knowingly
leaves for later.

### 177. A customer's name is frozen into financial documents that nothing can erase

**Status:** open · **Raised:** 2026-09-27 (plan §3.13) · **Owner decision** · **Before real customers**

Every issued document freezes the customer's name into `financial_documents.snapshot`, which the
database refuses to update, delete or truncate (the same `khadra_table_is_append_only()` guard as the
audit trail). That is right for a financial record — a receipt must say whom it was issued to — and it
is also personal data with no erasure path: `User.Delete` is a soft delete and never touches a document.
The snapshot carries the minimum on purpose (the owner, 2026-09-27: the name only — no email, no phone;
addresses and delivery attempts belong to the Phase 7 delivery history). **Widened by payments Phase 6
(2026-09-29):** every document is now also stored as a PDF in each language, in private document storage
(`financial-documents/{documentId}/…`), with the same name printed on it, and `financial_document_renditions`
is append-only too; the retention decision and any erasure path cover those files as well. **To close:** the
owner states the legal basis for keeping issued documents — and their PDFs — and for how long (Jordan's
record-keeping rules for the business), and the privacy notice says so, before the first real customer's
document is issued.

### 178. Khadra's legal identity must be configured before any real document is issued

**Status:** open · **Raised:** 2026-09-27 (owner decision 1 of Phase 5) · **Before real money**

No permanent financial document is issued with placeholder or incomplete issuer information (owner,
2026-09-27). Until `FinancialDocuments:Issuer` holds Khadra's real legal name (English and Arabic),
commercial registration, address (English and Arabic), support email and phone, every document owed
waits on hold (`IssuerNotConfigured`), the boot log says FINANCIAL DOCUMENTS ARE NOT ISSUED, and the
administrator's work queue lists them. A half-configured identity refuses to start and names what is
missing. A clearly marked TEST identity (`TestIdentity` true) exists for local sandbox testing only:
`Program.cs` refuses it in any environment but Development and with any payment provider but
`SANDBOX` — Staging included — and the issuer never lets it sign real money. **To close:** the owner
gives the identity; it is set in Production's environment with `TestIdentity` false, and the boot log
reads "Financial documents issued as …". **Since payments Phase 6 (owner, 2026-09-29),** Khadra's commercial
registration is captured in every snapshot but not printed in a PDF's body, nor — since the Phase 6 follow-up —
shown on the website's or the app's document pages (the console keeps it in its proof of issue); the legal check before the first
real document confirms that a receipt may leave it out — and if it may not, a compact legal line in the page
footer is the recorded place for it, never the transaction details.

### 179. Two known bounds of statement issuing

**Status:** open · **Raised:** 2026-09-27 (architecture review of payments Phase 5a)

Two cases the issuing sweep does not handle, both unreachable today, recorded so they are not rediscovered.
**A booking whose captured payments name two providers** is held (`SnapshotFailed`) and gets no statement:
a statement cannot say which kind of money it is about. Today one database holds one kind of money
(`PaymentsStartupCheck`), but the day a real adapter replaces the sandbox (item 76), a booking that paid its
deposit under one provider and the rest under the other would be held for good. **Clock skew between API
instances** larger than `FinancialDocuments:LateCommitMarginMinutes` could let a fact committed just after a
statement, and stamped earlier than it, go unnoticed until the booking's next checkpoint; it belongs with the
single-instance assumption of the settlement pass. **To close:** decide how a statement names a booking paid
under two providers before a real adapter lands; keep the instances' clocks synchronised (or run the pass on
one instance) when the API scales out.

## Issued financial documents — the clients (payments Phase 5b, 2026-09-28)

What the review of the website, the app and the console (`docs/payments-phase5b-plan.md` §18) found
beyond 5b's own follow-ups, R1–R7, which were fixed in 5b itself (`1315fd5`, 2026-09-28).

### 180. The administrator's document reads, and a booking's financials, are sent without `no-store`

**Status:** closed · **Raised:** 2026-09-28 (review of payments Phase 5b) · **Closed:** 2026-09-28 (`ec176c4`), verified live 2026-09-29 — every administrator document endpoint (the list, the vocabulary, the holds, a document, a booking's documents and the void) and both financials endpoints, the parties' and the administrator's, answer `Cache-Control: no-store, private`, refusals included.

Decision D6 put `Cache-Control: no-store, private` on the three CUSTOMER document endpoints only. The
administrator's reads (`AdminFinancialDocumentsController`: the list, a document with its void's reason, a
booking's documents, and the holds, whose last error is free text) and `GET /bookings/{id}/financials`
(Phase 4: paid, refunded, the payment history) answer with no cache header, so whether a body survives on a
shared machine is left to the browser and to whatever sits in front of the BFF. Browsers do not
heuristically cache such JSON and Cloudflare does not cache JSON by default, so the exposure is narrow, and
the helper that closes it already exists. **To close:** the owner extends D6; `KeepOutOfCaches()` in those
actions, with the same header assertions in `FinancialDocumentEndpointTests`.

### 181. Voiding a receipt leaves the booking's current statement naming it

**Status:** closed · **Raised:** 2026-09-28 (review of payments Phase 5b) · **Closed:** 2026-09-28 (`07a7284`), verified live 2026-09-29 — the owner decided that a receipt's correction is a sixth statement checkpoint, `ReceiptCorrected` ("Receipt corrected" / «تصحيح إيصال»): the settlement pass issues the booking's statement one new version within a pass, never inside the void's transaction, listing the correction and never the voided receipt, and the console's void dialog on a receipt says so. KH-P6UW4FB9's TEST-STM-2026-000020, version 4, lists TEST-PAY-2026-000014 and not TEST-PAY-2026-000013.

A void is not one of the checkpoints that issue a statement (`StatementCheckpoints`, the closed list the
owner approved on 2026-09-27), so after a receipt is voided and corrected the booking's current statement
still lists the voided number in its Documents section — KH-P6UW4FB9's TEST-STM-2026-000014 names
TEST-PAY-2026-000013. Neither page is wrong (the voided receipt says so and links its correction), but the
statement a customer reads last is out of date. Today's remedy: void the statement too; its correction lists
the receipts as they now stand. **To close:** the owner decides whether a receipt's correction also corrects
the booking's current statement, in the same transaction; until then the console's void dialog could say so.

### 182. The financial documents' concurrency proofs run only when PostgreSQL is opted in

**Status:** closed · **Raised:** 2026-09-28 (review of payments Phase 5b) · **Closed:** 2026-09-29 — the void race is forced by a test-only synchronization (`1e7f720`), so the loser can be refused only through the race branch, and the owner's PostgreSQL proof against disposable scratch databases at `07a7284` passed 23 of 23, the forced void race among them, none skipped. This repository has no CI able to run the opt-in suite; automating it once CI exists is a separate infrastructure follow-up, not a Phase 5b blocker (owner, 2026-09-29).

Two administrators voiding one document at once (one void, one correction), the number series' row lock,
and a rolled-back number coming back are proven only by `[PostgresFact]` tests, which skip unless
`KHADRA_TEST_POSTGRES` is set. On SQLite the unique-violation translation never happens, so the void
handler's race branch is not reached by the default `dotnet test`; the browser run exercised the stale-tab
pre-check, not a true race. **To close:** run the PostgreSQL suite once against a scratch database before
`feature/payments-receipts` merges (the owner holds the connection string), and set the variable in CI.

### 183. The shared document fixture does not carry every wording branch

**Status:** open · **Raised:** 2026-09-28 (review of payments Phase 5b) · **Non-blocking** (owner, 2026-09-28)

`docs/contracts/financial-documents-v1.json` holds every shape of the version-1 grammar, and all three
clients' tests read it — since `07a7284` a `ReceiptCorrected` statement too — but not every WORDING branch
the composer can write: a `BookingEnded` statement (a
cancellation's version), a delivery fee, a refund that failed, a penalty stated as a range, a balance not
yet due. The readers are covered in full; what is lost is that a change to those branches' words does not
show as a fixture diff in review, as the contracts README promises. **To close:** add those cases to
`FinancialDocumentFixtureTests.Generate()`, and `BookingEnded` to its assertion of causes.

## Found during the payments Phase 5b browser run (2026-09-28)

Behaviour met while verifying Phase 5b in the browser that predates it. None is a 5b regression; each is its
own change.

### 184. Typed text in the console inherits the page's direction

**Status:** open · **Raised:** 2026-09-28

`.user-text { unicode-bidi: isolate; }` (`Khadra.Dashboard/src/styles/_rtl.scss`) isolates text somebody
typed but still inherits the page's direction, where its comment promises that the browser decides per
value — that needs `plaintext` or `dir="auto"`. On an Arabic page a Latin reason beginning "§13 …" rendered
as "… 13§"; `b252c70` fixed the void panel alone. **To close:** change the shared rule or add `dir="auto"`
where it is used, and check every screen that uses it in both languages.

### 185. Two sentences in the office console state rules that have changed

**Status:** open · **Raised:** 2026-09-28 · **Before launch**

The approval dialog tells an office that the customer's free-cancellation window "starts now"
(`dealerDecide.approve.body`); it starts when the customer pays. The notification bell says a request expires
when its rental date arrives unanswered (`notifications.oldestAndExpiry`); it expires when the answer window
ends. **To close:** reword both from the current rules, in English and Arabic, with no business number
written into them.

### 186. The office's feed words a customer's cancellation as "updated"

**Status:** open · **Raised:** 2026-09-28

`notifications.service.ts` has no wording for `BookingCancelledByCustomer`, so an employee's Notifications
screen shows "A customer updated KH-…" for a cancellation, and the employee screen's comment still says a
customer's request and cancellation have no producer, which is no longer true. **To close:** word every
notification kind the server raises for office staff, in both languages, and correct the comment.

### 187. An email retry can deliver a second copy

**Status:** open · **Raised:** 2026-09-28 · **Before real users**

KH-6RLYEMBC's approval email arrived twice, four seconds apart. The API log says it was "accepted by Smtp …
attempt 2": the first attempt counted as failed after the server had accepted the message, and the retry sent
it again. The same retry policy covers every email and every transport. **To close:** a retry must not
re-send a message that may already have been accepted — no retry once the SMTP data phase completed, and one
message id or idempotency key across attempts for the HTTP providers — with the total wait still bounded by
`Email:TimeoutSeconds`.

### 188. The customer website never shows why the platform cancelled a booking

**Status:** open · **Raised:** 2026-09-28 · **Owner decision**

An administrator's cancellation requires a reason, and the server's own validation message says it "is
shown to both parties". The API sends it to the customer (`cancellationReason`), but the website words only
a coded reason, so the page says "Cancelled by Khadra" and nothing more. **To close:** decide whether the
customer sees the administrator's reason; then show it (and check the app), or correct the server's message.

### 189. The office is not told when the platform cancels one of its bookings

**Status:** open · **Raised:** 2026-09-28 · **Owner decision**

An administrator's cancellation notifies the customer only (`AdminBookingCommandHandlers`); on a confirmed
booking the office can go on preparing a car for a rental that no longer exists. **To close:** decide, and if
so notify the office's team as the customer's own actions already do.

## Issued financial documents — after Phase 5b (2026-09-29)

Items 190–193 are the iOS readiness work's, on its own branch (`ios/readiness`).

### 194. A refund receipt issued after the booking's current statement does not bring that statement up to date

**Status:** open · **Raised:** 2026-09-28 (the design of item 181) · **Non-blocking** (owner, 2026-09-29)

A statement lists the receipts issued when it is composed, and within a settlement pass receipts are issued
before statements. So a refund receipt normally arrives with, or before, the statement version its refund's
settlement brings. It can arrive after: when the receipt is held (a composition defect), when its payment's
own receipt has not been issued yet, or when the refund settles while a pass is already running, after the
pass has listed its work. The booking's current statement then states the refund correctly — its figures
come from the refund itself — but does not list the receipt in its Documents section, and nothing re-issues
it when the receipt arrives: an original receipt's issuance is not a checkpoint
(`docs/payments-phase5-plan.md` §3.3); only a receipt's correction is (item 181). It stays that way until
the booking's next checkpoint. A payment receipt held past its booking's first statement is the same case.
**To close:** let the later receipt bring the statement current — a new version that lists it, or another
way the owner prefers — with tests for a held receipt and for a refund settled during a pass. Not to be
implemented before the owner asks.

## Issued financial documents — PDFs (payments Phase 6, 2026-09-29)

Drawing each issued document as a PDF in English and Arabic, storing it privately and handing it out
through short-lived links (`docs/payments-phase5-plan.md` §9 and §22), and what that knowingly leaves for
later.

### 195. QuestPDF's Community licence rests on an eligibility that must be re-checked as revenue grows

**Status:** open, standing · **Raised:** 2026-09-29 (the owner confirmed eligibility) · **Every financial year**

The PDFs are drawn with QuestPDF 2026.9.1 under its Community licence (v3.0, effective 6 July 2026), which
the owner confirmed on 2026-09-29 that Khadra qualifies for: consolidated annual gross revenue under
USD 1,000,000 across the entities under common control (a first fiscal year on a good-faith annualised
estimate), not a public-sector body, not publicly traded. The code states it
(`QuestPdfFinancialDocumentRenderer`, `Settings.License = Community`) and the boot log says it on every
start ("Financial document PDFs are drawn with QuestPDF … under the QuestPDF Community licence"). The licence
is only as true as the revenue figure: eligibility lapses the year revenue crosses the threshold, and the
licence then allows a limited period to move to a commercial licence. **To close:** never, by design — at
each financial year end the owner re-confirms eligibility against that year's revenue, and a commercial
licence is bought, and the setting changed, before the grace period ends if it no longer holds.

### 196. The fonts' licence text is not shipped beside the embedded fonts

**Status:** closed · **Raised:** 2026-09-29 · **Closed:** 2026-09-29 (`506fef7`) — each family's `OFL.txt`, downloaded with the owner's approval from its official repository and unchanged, sits beside the font files in the API and in the app, and the font files and the texts are proven to correspond.

The PDF renderer embeds Manrope and Noto Kufi Arabic in the API assembly
(`Khadra.Infrastructure/FinancialDocuments/Fonts/`, copied from `Khadra.Mobile/assets/fonts`), and every
PDF carries subsets of them. Both are under the SIL Open Font License 1.1, which lets them be bundled and
embedded. The PDFs themselves need nothing: the licence lets subsets of a font be embedded in documents
without its text. What wants `OFL.txt` is every place the font FILES are bundled — the TTFs in the repository
and in the API assembly, and the app's assets — which carry their copyright notice and the licence's address in
their own metadata, but not the licence text; no copy of `OFL.txt` is in the repository. Nothing was
downloaded to fix it: that needs the owner's approval. **To close:** add each family's `OFL.txt` (from the
fonts' own repositories) beside the TTFs in both places, and list the fonts in the app's licence page.

**How it closed.** `OFL-Manrope.txt` (from googlefonts/manrope) and `OFL-NotoKufiArabic.txt` (from
notofonts/arabic, the same text Google Fonts ships) sit beside the TTFs in
`Khadra.Infrastructure/FinancialDocuments/Fonts` and `Khadra.Mobile/assets/fonts`, each folder with a
`FONTS.md` naming every font file with its family and style, version, the copyright notice the file itself
carries (it differs from the one heading the licence text: the year, and the repository or holder named), the
licence it names and its SHA-256. `FontLicenceTests` holds them together: every TTF is in its notice with its
hash; the notice's name, version, copyright and licence read back from the font's own `name` table; every
font names the SIL Open Font License 1.1, whose text is beside it for its family; both places carry the same
bytes; and the API's build copies the texts and the notice beside the assembly that embeds the fonts. The app
bundles both texts as assets and adds them to Flutter's licence registry (`registerFontLicences`). The second
half of the old "to close" — a licence page in the app — is not built: the app has no screen that opens
Flutter's licence page at all, for the fonts or for its packages. That is recorded as item 201, not dropped.

### 197. A PDF that cannot be drawn is visible only in the log

**Status:** open · **Raised:** 2026-09-29 (architecture review of payments Phase 6)

A document whose snapshot no longer matches its hash, cannot be read for print, or makes the renderer throw
is logged once at Error and left alone until the API restarts; drawing is not tried again every minute. The
customer's page keeps saying the PDF is being prepared, and the administrator's page shows the rendition
missing, with no reason on either. Issuing has holds with reasons on the work queue for exactly this; drawing
does not yet. A full pass in which nothing could be drawn stops drawing until restart and says so at Error
— that case is covered. **To close:** a durable record of an undrawable PDF with its reason — a hold row or
an attention-queue kind — shown to the administrator, as issuing's holds are.

### 198. Stored PDF files that no row points at are never removed

**Status:** open · **Raised:** 2026-09-29 · **Housekeeping**

The bytes are stored before the row that records them commits, under a fresh key per attempt, so a crash
between the two, a write cancelled by a shutdown, or a failed record that may have committed without saying
so (whose bytes are KEPT on purpose — removing them could leave a recorded PDF every download fails on)
leaves a private file nothing points at. Only a lost race removes its own copy. Such files are unreachable —
a download needs the row's key — and small, but nothing ever sweeps them. **To close:** a periodic sweep that
lists `financial-documents/` keys older than a day with no rendition row, confirms again, and deletes them.

### 199. On Supabase storage, the stored-size check proves nothing

**Status:** open · **Raised:** 2026-09-29 (architecture review of payments Phase 6)

Before it records a PDF, the renderer compares the size storage reports with the bytes it drew, and refuses
to record a mismatch. `LocalDocumentStorage` reports the file it wrote; `SupabaseDocumentStorage` reports the
length of the payload it sent, not anything the store confirmed, so the check is always satisfied there.
The PDF's SHA-256 is still recorded from the drawn bytes, so a later download can be verified against it.
**To close:** have the Supabase store report what it holds (a HEAD after the upload, or the size the store's
answer carries), or record here that the check is local-only by design.

### 200. The PDF work query walks every document on every pass

**Status:** open · **Raised:** 2026-09-29 (final review of payments Phase 6) · **Scaling note**

The facts are the queue, by design: each settlement pass asks which issued documents have no PDF in English
or Arabic, oldest issue first — and, since the Phase 6 follow-up, which voided documents have no voided copy.
Once everything is drawn — the normal state — the answer is empty, but finding that out walks the documents in
issue order with five correlated `EXISTS` per row (a PDF as issued in each language, whether it is voided, and
its voided copy in each language), served by the renditions' unique index and the voids' key. Trivial at launch (tens of thousands of documents answer in well under a second); it grows
with history, not with the backlog. **To close:** when the documents table is large enough for the pass to
notice, bound the question — only documents issued since the oldest one still without a PDF, or a partial
index of documents without both renditions.

### 201. The customer app has no screen that shows its open-source licences

**Status:** closed · **Raised:** 2026-09-29 (architecture review of the payments Phase 6 follow-up) · **Closed:** 2026-09-29 (owner's cleanup before Phase 7) — a "Licences" / «التراخيص» row under About on the Profile tab opens Flutter's licence page.

The app bundles the fonts' licence texts and registers them with Flutter's licence registry beside every
package's own (item 196), but nothing in the app opens `showLicensePage` or an About dialog, so no customer can
read any of them. The fonts' texts travelling inside the app as files satisfies the font licence; many package
licences (MIT, BSD, Apache) also ask that their notices be available to the people the app is distributed to,
and the standard way to do that in Flutter is its licence page. This is new UI, so it was left for the owner
rather than built inside the follow-up. **To close:** a row in the app — Profile, beside the language — reading
"Licences" / «التراخيص» that opens Flutter's licence page, with its two strings and a widget test, or the owner's
word that it is not needed.

**How it closed.** The owner asked for it (2026-09-29): a row under About on the Profile tab — shown to a guest
as well, since About is not an account's — opens `/profile/licences`, Flutter's own licence page
(`LicencesScreen`), which lists every open-source package the build bundles, collected by the build itself,
beside the fonts' licences registered at start, and opens each one's full text. It carries the app's name and
the installed version, and on an English page reads Material's British strings ("Licences", matching the row);
Arabic is «التراخيص». `licences_test.dart` opens it as a guest in both languages and reads Manrope's licence
text from it.

## Issued financial documents — emails (payments Phase 7, 2026-09-29)

Emailing every payment and refund receipt, and their corrections, to its customer with its PDF, and the delivery
history the administrator reads (`docs/payments-programme.md`, Phase 7; `docs/payments-phase5-plan.md` §10 and
§23), and what that knowingly leaves for later. Booking statements are not emailed (owner, 2026-09-29), and no
receipt issued before the Phase 7 migration is emailed on its own: an administrator sends one with "Email it to
the customer" when it is needed.

### 202. Receipts emailed through Brevo carry no idempotency key: a process stopped mid-send can email one twice

**Status:** open · **Raised:** 2026-09-29 (architecture review of payments Phase 7) · **Owner decision (2026-09-29): accepted for local and Staging only · Before Production on Brevo**

A receipt's email spends its send attempt before the transport is called and records the outcome after, so a
process that stops in between — a deploy or a crash in the few seconds a send is in flight — leaves the email to
be sent again once its lease runs out. The retry is the same message under the same key, and what that key buys
depends on the transport: Resend drops the repeat (its `Idempotency-Key`, kept for a day), and SMTP carries the key
as the Message-ID, which the big mailboxes collapse. Brevo's HTTPS API is sent no key, so on Brevo the customer
receives the receipt twice. Brevo does document an `idempotencyKey` inside a request's `headers` object — a UUID,
kept 30 minutes, a repeat refused with `duplicate_parameter` and not processed — but only in its batch-sending
examples. Its documentation does not say whether a single send honours it, what status the refusal carries, or
whether a key used by a request Brevo REJECTED is remembered: if it is, a retry after a genuine rejection would be
refused as a duplicate and could be recorded as sent, which is worse than a second email. The boot log warns on
every start with Brevo (event 2714). **Owner, 2026-09-29:** acceptable for local and Staging only, and not approved
as a Production risk.

**The hard stop (owner, 2026-09-29).** A Production host whose mail provider is Brevo — by its API
(`Email:Provider` `Brevo`) or by its SMTP relay (`Smtp` to a `brevo.com` or `sendinblue.com` host), which is the same
provider — sends no financial-document email until this item is closed, and nothing else about the API stops:
`FinancialDocumentEmailSettings.DeliveryDisabledReason`. The receipt email service does not start there, and says why
at boot (event 2715, "FINANCIAL-DOCUMENT EMAILS ARE NOT SENT", with the reason); the last step of a send refuses
too, should anything reach it; an administrator's "Email it again" is refused with **409
`financial_documents.email_delivery_disabled`**, nothing queued and nothing audited, and the refusal is logged (event
2705). Receipts are still issued and still owed their emails, which wait in the queue — the work queue lists them
once they have waited longer than `StaleAfterMinutes` — and the console's document page says emails are switched off
on this server rather than that one is on its way. Local and Staging may use Brevo under the TEST allowlist; Resend
and every other transport are unaffected. Pinned by `FinancialDocumentEmailConfigurationTests` (which environments
and hosts), `FinancialDocumentEmailServiceTests` (the service never starts a pass) and `FinancialDocumentEmailTests`
(nothing sent, the request refused, the email still owed and sent once the server may send again).

**To close, before Production on Brevo:** either verify Brevo's single-send idempotency against a real Brevo test
account — a repeated send, and a repeat of a rejected send — and implement it safely (a UUID derived from the email's
own key; `duplicate_parameter` read as "already accepted" only once that is proven), then lift the hard stop; or send
receipts through a provider whose idempotency is proven, such as Resend, which the hard stop already lets through.
Either way, the emails that waited in the queue go out when the server may send again.

### 203. The address each receipt email went to is kept with no retention rule and no erasure path

**Status:** open · **Raised:** 2026-09-29 (payments Phase 7) · **Owner decision (2026-09-29): keep it for now as delivery evidence** · **Before real customers**

`financial_document_deliveries.recipient_address` holds the verified address each receipt email was sent to — the
evidence of delivery the administrator reads, and the answer to "where did it go?" should the customer's address
ever change. It is personal data with no retention rule: `User.Delete` is a soft delete and touches no delivery. The
address is on the MUTABLE delivery row on purpose — the append-only attempts and the audit entries carry no address,
name or message body — so an erasure can blank it without touching any record that must never change. No automatic
deletion is added (owner, 2026-09-29). **To close:** the owner decides how long a delivery keeps its address and
what an erasure request does to it, the privacy notice says so, and that behaviour is built with its tests —
beside items 18 and 177, which cover the documents themselves.

### 204. The notification outbox never checks at send time that its claim is still its own

**Status:** open · **Raised:** 2026-09-29 (architecture review of payments Phase 7) · **Owner decision (2026-09-29): fix before Production**

`DeliverNotificationsHandler` claims push and reminder rows with `NotificationDeliveryRepository.ClaimDueAsync`
under a lease, but nothing at send time checks that the claim is still its own. A process whose batch outlives its
lease — a push provider that stalls, a slow database — can reach a row another process has claimed since, and both
send the same push or reminder. `NotificationDeliveryOptions.LeaseOutlastsBatch` keeps the arithmetic honest for
the worst case it assumes, not for every case. Payments Phase 7 closed exactly this gap for receipt emails, and the
owner decided the outbox gets the same treatment before Production. **To close:** the claim returns each row with
the claim count it left; the sender works a row only while the count is still that one; the count is a concurrency
token on the outbox row, so a claim taken over between the read and the send is refused by the database; tests force
the takeover on SQLite and on PostgreSQL — `FinancialDocumentEmailTests` and `PostgresFinancialDocumentEmailTests`
show the pattern.

## The office payables ledger (payments Phase 8, 2026-09-30)

What Khadra owes each rental office, booking by booking, and the settlements an administrator records by hand
(`docs/payments-programme.md`, Phase 8; `docs/payments-phase5-plan.md` §24), and what that knowingly leaves for
later. Nothing here moves money: a settlement records a payment made outside the platform, and until item 76 the
only money any of it describes is sandbox money.

### 205. Only the whole deposit can be kept as a customer's penalty, so the penalty percent is locked at 100

**Status:** open · **Raised:** 2026-09-29 (architecture review of payments Phase 8) · **Owner decision (2026-09-30): keep the lock at 100% for now** · **Before the penalty percent changes**

Since item 164 closed, a customer's uncontested penalty is kept from the deposit when the dispute window closes. The
ledger can keep only the WHOLE deposit: it records `PenaltyKept` when the penalty is fixed and equals what the
deposit holds. Anything less would leave part of the deposit owed back to the customer with no way to send it — the
clean-close release returns the whole deposit or nothing, and nothing while a penalty stands on the customer — so
such a booking would be held (`PenaltyNotWholeDeposit`) and never recorded. The API therefore refuses to start with
`BusinessRules:CustomerCancellationPenaltyPercent` set to anything but 100 (`DependencyInjection`, pinned by
`BusinessRulesConfigurationTests`); a self-pickup no-show already assesses the whole deposit. The server's own word
follows the same line: `requiresTicketToEnforce` is false only for a customer's penalty of the WHOLE deposit, and the
ledger keeps nothing the assessment says a dispute must decide. **To close, before the percent changes:** a partial
path — the rest of the deposit refunded when the window closes, with its receipt — the ledger keeping only the
penalty, and the lock lifted. The customer clients must change with it: the website's and the app's cancel sheets
choose their sentence by whether the penalty is a range, not by `requiresTicketToEnforce`, so today a penalty below
the whole deposit would be told "kept from your deposit". None can be assessed while the lock holds, and none was
frozen before it unless an environment overrode the tracked setting, which has said 100 since the first commit.

### 206. A settlement pays an office's whole due balance, or nothing

**Status:** closed · **Raised:** 2026-09-29 (payments Phase 8) · **Closed:** 2026-09-30 by the owner's decision — no partial settlements in Phase 8; a settlement records the whole currently-settleable balance.

An administrator settles every due payable of one office, in one currency and one kind of money, at once, and for
exactly the balance the screen showed (`expectedAmount`; a different figure is refused with `409
payables.balance_changed` and the balance due now). A payable is left out only by holding it, which is audited.
Paying part of a balance — 100 of 150 — is not offered. That keeps "a payable is settled by one settlement" true, and
an office that both owes and is owed is never split in a way nobody can reconstruct later. **To close:** the owner
says whether partial payments are needed. If they are, a settlement covers an explicit set of payables the
administrator chooses — never part of one payable.

**How it closed.** The owner decided on 2026-09-30: no partial settlements in Phase 8. A settlement records the
whole currently-settleable balance, exactly as built; a payable is kept out of one only by holding it. Should partial
payments ever be wanted, the shape above — an explicit set of payables, never part of one — is where to start.

### 207. Any refund not yet settled blocks an office's payable

**Status:** closed · **Raised:** 2026-09-29 (payments Phase 8) · **Closed:** 2026-09-30 by the owner's decision — keep the safe rule: any refund that is not Settled blocks the office payout.

The owner's rule (2026-09-24) is that a payable is never marked settled while a relevant refund or dispute on its
booking is open. The ledger reads it cautiously: ANY refund on ANY payment of the booking that is not `Settled` —
being sent, delayed, or refused for good — and any live dispute ticket. The payable then reads Blocked and names
what blocks it (`RefundOutstanding`, with the refund, or `DisputeLive`), read live on every screen, never stored.
The cost of the caution: a refund refused for good blocks the office's payable until that refund itself is settled,
and nothing in the ledger can lift it. **To close:** the owner confirms this reading or narrows it — for example to
the refunds that change what the office is owed — and the block follows.

**How it closed.** The owner decided on 2026-09-30 to keep the safe rule for now: any refund that is not `Settled`
blocks the office payout, because an office is never paid while a customer refund obligation remains unresolved.
That is the rule as built; the cost above is accepted, and a refund refused for good is resolved as a refund first.

### 208. Installed apps still tell a cancelling customer that nothing is charged without a dispute

**Status:** open · **Raised:** 2026-09-29 (payments Phase 8) · **A precondition of closing item 76 — a launch gate, not a follow-up**

Since item 164 closed, a customer's uncontested penalty is kept from the deposit when the window closes. The apps
already installed (1.1.0, 1.2.x) say otherwise, in words built into them: their cancel sheet reads "Cancelling now
assesses {amount} against you. Nothing is charged unless a dispute is opened and settled.", and their booking terms
"… Nothing is taken without a dispute being opened and settled." The server does what it can without breaking them:
`requiresTicketToEnforce` is now false for a customer's penalty of the whole deposit, which hides the extra "Nothing
has been charged" line those builds add under it; but the two sentences above are shown unconditionally. The website
and app 1.3.0 (unreleased) carry replacement sentences in English and Arabic. What a customer reads once the penalty
is kept — once, in the booking page's penalty notice and in the statement's Penalty section, while the deposit line
states the amount — is the owner's approved sentence (2026-09-30): "The dispute window ended without a dispute. The assessed deposit penalty has now been
finalized and applied according to the booking’s cancellation terms." / «انتهت مهلة النزاع دون فتح نزاع. تم تثبيت
حسم العربون وتطبيقه وفق شروط إلغاء الحجز.» The two sentences a customer reads BEFORE cancelling — the cancel
sheet's and the booking terms' "It is kept from your deposit when the dispute window closes, unless a dispute decides
otherwise." — are still drafts awaiting the owner. So are two the app has always had, which the kept penalty made
untrue for a booking with a customer's penalty of the whole deposit, installed builds included: withdrawing a dispute
asks "Withdraw this? The rental office will be told, and nothing will be charged to anyone." and then says "Withdrawn.
Nothing has been charged." — yet the booking then settles as if no dispute had been raised, which keeps that penalty
(found in the Phase 8 live check, 2026-09-30, where the consoles' own versions were corrected). The owner approved their
replacement the same day and app 1.3.0 carries it (`2843956`): "The dispute will be withdrawn, and the booking will
settle according to its existing cancellation and penalty rules." / «سيتم سحب النزاع، وسيُسوّى الحجز وفق قواعد الإلغاء
والغرامات المطبقة عليه.», and, once done, the same with its first clause in the past tense. The installed builds keep
the old sentences until a build carrying these is published and required. No real deposit can be kept today —
Production has no provider (item 76). But the rule is not only for windows that close from now on: the ledger's work
query has no lower bound on when a booking ended, so the FIRST pass after the Phase 8 migration records every paid
cancellation and no-show already past its window, and every such deposit with a customer's penalty on it is kept —
on the local database and on Staging, sandbox money cancelled under the old words. The owner accepted that for the two
such local bookings, KH-X73CZSRH and KH-EVUYXLJD, for local verification only (2026-09-30): their historical wording is
no precedent for Production. **To close, before a real
provider — a precondition of closing item 76:** the owner signs the wording off in both languages; a build carrying
it is published; and `MobileApp:MinimumSupportedVersion` is raised to that build, publish first as the contract rule
requires, so that no customer can cancel a real booking under the old promise.

### 209. A payable its records later contradict cannot be corrected

**Status:** open · **Raised:** 2026-09-29 (architecture review of payments Phase 8)

A recorded payable is frozen — a database trigger refuses any change to its figures — and every pass checks the
unsettled ones against their bookings' records again. A difference (a refund or a dispute decision recorded after
the booking was final, or a corrected record) opens a `Contradicted` hold: the payable cannot be settled, and it is
a row on the administrator's work queue. The hold lifts itself if the records come back into agreement, but when
they do not, nothing can replace the payable: one per booking, and no adjustment line. **To close:** a supersede —
the contradicted payable voided and a new one recorded from today's records, both audited, and for a settled one a
correcting settlement — designed with the owner.

### 210. The ledger looks for ended bookings by today's dispute window, not the one each booking froze

**Status:** open · **Raised:** 2026-09-29 (architecture review of payments Phase 8) · **Low priority**

The payables pass looks for cancelled and no-show bookings whose end is older than the finality margin plus TODAY's
`PostReturnSettlementHours`, because the window each booking froze is not a column a query can read. Each booking is
then judged by its own frozen window, so nothing is ever recorded early; but a booking whose window is shorter than
today's is found late, and one whose window is longer — or whose dispute is still live — is looked at and turned
away on every pass until it is final, taking a place in the pass's batch (`Payables:MaxBookingsPerPass`). Completed
rentals are looked at first, so they are never crowded out. **To close:** store the window's end on the booking
(written when the booking ends) and query it.

### 211. Two of the ledger's reads grow with the whole history

**Status:** open · **Raised:** 2026-09-29 (architecture review of payments Phase 8) · **Low priority**

Finance's "kept by Khadra from disputes" loads every resolved dispute ticket and filters by when it was decided in
memory, because the decision is one stored JSON document; and the office balances read every settlement to find
each office's latest. Both are cheap at today's volume. **To close, before either reaches a few thousand rows:** keep
the decision time where SQL can filter it, and pick each office's latest settlement in SQL.

### 212. A kept penalty does not bring the booking's statement a new version

**Status:** closed · **Raised:** 2026-09-29 (payments Phase 8) · **Closed:** 2026-09-30 (owner's decision; `a49a4c9`) — a seventh checkpoint: a penalty kept brings the statement a new version.

When the ledger records a penalty as kept, the booking's financial state changes — the deposit reads `KeptAsPenalty`
and the penalty `KeptFromDeposit` — but none of the six checkpoints that issue a booking statement version fires,
because none of them is "the window closed with the penalty kept". The customer's latest statement goes on saying the
deposit is held. The checkpoints are a closed list the owner set (2026-09-27, amended once for item 181), so the
ledger does not add one on its own. **To close:** the owner approves a seventh checkpoint — the recorded `PenaltyKept`
payable — with its words in both languages, and the settlement pass issues the version as it does for the other six.
Recommended.

**How it closed.** The owner approved it on 2026-09-30: when a penalty becomes kept, a new booking statement version
is issued so the current statement reflects the outcome. The checkpoint is the ledger's RECORD — the `PenaltyKept`
payable, by its id and the instant it was recorded (`StatementCheckpoints`, cause `PenaltyKept`, worded "Deposit
penalty finalized" / «تثبيت حسم العربون» from the owner's own sentence) — never the window's closing, so it is the
same row the deposit's `KeptAsPenalty` is read from, and the statement cannot state one without the other. The work
query finds a booking whose payable was recorded after its latest statement; the documents step, which the settlement
pass runs right after the payables step, issues the version in the same pass. Its Penalty section carries the owner's
approved sentence, once; its Deposit section, the amount and where it went. No schema change: a cause is stored by name.

### 213. A dispute's charge on the office goes to Khadra

**Status:** closed · **Raised:** 2026-09-29 (payments Phase 8) · **Closed:** 2026-09-30 by the owner's decision — an amount an office owes Khadra remains an office debit, netted against what Khadra owes that office.

A dispute resolution may put a charge on the office (`DisputeResolution.DealerCharge`, the non-delivery penalty
among them). The ledger takes every such charge off the office's net (a `DisputeCharge` line), so the office pays it
by being paid less, or by paying Khadra when it is owed less than the charge (owner, 2026-09-29: netted). Where the
money then goes is not decided: nothing passes it on to the customer, whose share of a dispute is only what the
resolution returns of the deposit, so Khadra keeps it, and Finance counts it among the office charges. **To close:**
the owner says whether a charge on the office compensates the customer, stays with Khadra, or depends on the kind of
charge, and the ledger and the customer's refunds follow.

**How it closed.** The owner decided on 2026-09-30: an amount an office owes Khadra remains an office debit and is
netted against what Khadra owes that office — exactly the `DisputeCharge` line as built — with no separate, unrelated
manual debt path for it. The customer's money from a dispute stays what the resolution returns of the deposit.

### 214. `HeldDepositFinder` is unused

**Status:** closed · **Raised:** 2026-09-30 (payments Phase 8) · **Closed:** 2026-09-30 (`efb8b00`) — deleted with the owner's approval, once proven unused.

It fed the `DepositAwaitingDecision` work-queue row, which Phase 8 retired (item 164). The finder,
`IBookingRepository.ListHeldForCustomerPenaltyAsync` with its two implementations, and the tests that exercise them
are still in the tree, the finder still registered, because deleting files waits for the owner's word. **To close:**
delete them.

**How it closed.** The owner approved the deletion on 2026-09-30, on condition of proof that nothing still depended on
it. Nothing did: no handler, service or reader resolved the finder — its registration was its only runtime trace —
and nothing but the finder called the query. The finder, its registration, the query in `IBookingRepository` with
its two implementations, and the two tests that exercised only them (one on SQLite, one on PostgreSQL) are gone; the
solution builds and every other test passes without them.

### 215. The commission on a cancelled booking a dispute decided follows the owner's rule for rentals

**Status:** closed · **Raised:** 2026-09-29 (payments Phase 8) · **Closed:** 2026-09-30 by the owner's confirmation — Khadra's earned commission is always capped at the office's final money from the booking, cancellations and dispute outcomes included.

The owner decided that the commission on a rental completed through a dispute is earned and capped at the office's
money on the booking, and that a kept penalty goes to the office less the commission (2026-09-29). The ledger applies
the same rule to every final booking, including a cancellation or a no-show whose deposit a dispute decided: the
office's share, less the frozen commission capped at that share, and no commission where the office's share is
nothing. **To close:** the owner confirms it, or says what a dispute-decided cancellation should earn Khadra instead.

**How it closed.** Confirmed by the owner on 2026-09-30: Khadra's earned commission is always capped at the office's
final money from the booking, cancellations and dispute outcomes included — the rule as built.

### 216. Khadra never invoices an office for its commission

**Status:** open · **Raised:** 2026-09-30 (payments Phase 8) · **Legal and accounting decision, before real money** · **Owner (2026-09-30): stays a pre-launch decision; no invoicing scheme is built yet**

The ledger records Khadra's commission on every final booking, each settlement records what was paid or received, and
the office reads both on its payouts page. Neither is an issued document: nothing numbered, frozen and given to the
office states the commission as a charge, and nothing is called a tax invoice. Whether Khadra must invoice each office
for its commission — a tax invoice, and Jordan's national e-invoicing system (JoFotara) — is a legal and accounting
question, left open since the Phase 5 plan (`docs/payments-phase5-plan.md` §11) and not decided by Phase 8. **To
close:** the owner's legal and tax advisers decide; if an invoice is required, it is an issued financial document of
its own, built as the Phase 5 documents are, from the settlement or the payable.

### 217. Three console sentences say an office is paid by bank transfer, which nothing has decided

**Status:** closed · **Raised:** 2026-09-30 (payments Phase 8 live check) · **Closed:** 2026-10-01 (`651ef89`) — the owner's instruction: neutral settlement wording while the payout method is not decided.

The owner's architecture (2026-09-24) keeps office settlement manual, with no payout rail and no real money movement
"until the provider/acquiring arrangement says how office settlement works". Three sentences in the console name a
method all the same, in both languages: the administrator's note on an office's payouts ("Pay or collect by bank
transfer first, then record the settlement here …", `payouts.byHandNote`), the office's payouts page ("Khadra pays
what it owes you by bank transfer …", `dealerPayouts.howItWorks`) and the office's reports ("Khadra pays what it owes
you by bank transfer, booking by booking once each outcome is final …", `dealerReports.payoutsAreNotLive`). The last
also reads as one payment per booking, where a settlement pays the whole due balance at once (item 206), and its key
still says payouts are not live. The office's booking page, reworded in the same check (`437f1e0`), names no method.
**To close:** the owner says how an office is paid; the three sentences say that, or say only that the payment is
made outside the platform and recorded here.

**How it closed.** The owner asked on 2026-10-01 for neutral settlement wording, the payout method not being
finalized. The three sentences now say a settlement records a payment made outside the platform; the settlement's
reference is a "Payment reference"; the dealer settings card that offered "Bank details for payouts", under a note
that payouts were not built, is "Payout details" and says Khadra does not collect them here, pointing to the Payouts
page; the two keys whose names said otherwise are renamed (`dealerReports.howPayoutsWork`,
`dealerSettings.payoutDetails`), and the reports' sentence no longer reads as one payment per booking; the domain's
and the API's doc comments say "payment" too. A test keeps every one of these sentences free of a bank or a transfer
in both languages. Once the acquiring arrangement settles how an office is paid, these sentences may name it.

### 218. The customer website drops «أنت» into two more Arabic sentences

**Status:** closed · **Raised:** 2026-09-30 (payments Phase 8 live check) · **Closed:** 2026-10-01 (`d88ad0a` website, `9dbc5f0` app 1.3.0, `f1e2f7c` console) — the owner asked for natural Arabic wording.

The website words who did something by dropping a party word into a sentence. In English that reads well ("by
you"); in Arabic the pronoun attaches to what precedes it, so a free-standing «أنت» is wrong. The customer's own
penalty read «قُدِّر مبلغ … على أنت» and was fixed on its own (`023d53d`), with a sentence of its own for the reader.
Two strings of the same pattern remain: `booking.by` («بواسطة أنت») and `dispute.openedBy` («فتحه أنت»). **To close:**
give the reader a sentence of their own in each, as the penalty now has, with a test that refuses the free-standing
pronoun.

**How it closed.** On 2026-10-01. On the website the history reads «· من قِبلك» and a dispute «فُتح من قِبلك في …»
(`d88ad0a`), the form the site already used for a cancellation («أُلغي من قِبلك»). The same pattern was found in
two more places and fixed with it. The app's label for the customer was «عليك», "against you", since its first
commit, so its history read «· بواسطة عليك» and a dispute's decision labelled the share refunded TO the customer
«عليك»; the label is «أنت» and the history says «من قِبلك» (`9dbc5f0`, app 1.3.0 — installed builds keep their
words). The office's notifications put «أنت» in the actor's place, «قبل أنت KH-…»; what the reader did now reads
«أنت من قبِل KH-…», the console's own form («أنت من يحددها») (`f1e2f7c`). Each is tested in Arabic on the words a
reader sees: the website's page test now strips the direction isolates before it searches, without which its earlier
negative check could not have failed. A colleague named in those notifications is item 220.

### 219. A system hold's detail is shown as the calculator's codes

**Status:** closed · **Raised:** 2026-09-30 (payments Phase 8 live check) · **Closed:** 2026-10-01 (`ed48341`) — at the owner's request.

When the payables pass holds a booking because its records need review, the hold's detail is the calculator's issue
codes joined by commas (`PayablesPassCommands`), and both payouts screens print it as a code in a Latin run — the
local relic KH-95JGHJQZ reads "EndingRefundMissing", on the English screen and the Arabic one alike. The console
already words every one of those codes (`financialIssue.*`, as a booking's Money section shows them). **To close:**
word a system hold's detail through the same dictionary, the codes it does not know shown as they are, and keep an
administrator's typed reason as typed.

**How it closed.** On 2026-10-01. One helper, `issueLines`, words the codes through the console's existing
`financialIssue` sentences, one per line, and spells out a code this build has no word for. It serves both kinds of
hold that carry them: a payable held because its booking's records need review (both payouts screens and the
administrator's booking page), and a financial document held for the same reason, whose "last error" was the same
codes (the document holds screen). A hold's detail now says who wrote it: worded issues; an administrator's reason,
shown as typed; or what the server composed in English with figures — what a payable no longer matches, a penalty
that is not the whole deposit — still shown as it is, left to right, as item 174 records for the audit log.

### 220. The office's notifications give every colleague a masculine verb

**Status:** open · **Raised:** 2026-10-01 (while closing item 218) · **Presentation only**

The office's notifications name a colleague after the verb — «قبل {who} {what}», «رفض», «سجّل … استلام» and
«… إرجاع», «حدّث», «أعاد … تفعيل», and the employee's «منحك {who}» and «ألغى {who}» — and the verb is always
masculine. Arabic agrees a verb with a feminine subject («قبلت سارة …»), and the platform does not know, and should
not ask, anyone's gender. What the reader did themselves is already worded for anyone (item 218). **To close:** a
construction that agrees with anyone — the passive with «من قِبل {who}», or the colleague named first as a label —
with a nominative fallback for "a booking", since «حجزاً» is written as an object.

### 221. The startup checks crash the API when the database refuses the connection

**Status:** open · **Raised:** 2026-10-01 (running the tests for items 217–219; confirmed by the architecture review) · **Owner decision: a payments guard**

`PaymentsStartupCheck` and `FinancialDocumentsStartupCheck` read the database at boot and catch `DbException`, so that
a table they cannot read does not stop the platform: with no provider configured, "nothing this process can do moves
money, so the platform still starts". A refused connection never arrives as one. EF's execution strategy wraps a
transient failure — a refused connection, a socket error, a timeout — in an `InvalidOperationException` neither catch
sees, so the host crashes at boot instead. A failed login is not transient and is caught, which is why the backend
suite passes whenever any Postgres answers on localhost:5432 and about 94 host-starting tests fail when nothing does.
Severity is low: on the sandbox the guard is fatal on an unreadable table by design, so only the message changes; with
no provider (Production) the intended "log and start" is already unreachable while `Admin:Bootstrap:Email` is set,
because `AdminBootstrapper` reads `users` first and fails the same way. Either way a host booting during a database
outage crash-loops until the database is back, instead of starting and serving `/health/live`. **To close, with the
owner's approval:** in both checks, catch an exception whose inner chain holds a `DbException`, keeping the sandbox's
rethrow; tests with a refused connection (`None` starts and logs, `Sandbox` refuses with the guard's own message); and
decide `AdminBootstrapper` in the same change — or record that a database outage at boot is fatal by design.
