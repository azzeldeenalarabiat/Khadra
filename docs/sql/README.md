# Applying the schema by hand

Render runs `preDeployCommand` only on paid instance types. On a free instance it
is accepted in the dashboard and silently never runs, which looks exactly like a
migration that failed: the service deploys, reports healthy, and every query dies
with `42P01: relation "bookings" does not exist`.

These two scripts apply the same schema from Supabase's SQL editor instead, with
no Render feature and no connection string leaving the browser.

## 1. `khadra-schema.sql`

Generated with `dotnet ef migrations script --idempotent`, so it is safe to run
again — each migration is wrapped in a check against `__EFMigrationsHistory`.
Creates 26 tables plus that history table, the `btree_gist` extension, and the
`bookings_one_hold_per_vehicle` exclusion constraint that stops two live bookings
overlapping on one vehicle.

The last regeneration (2026-09-22) added two migrations and no tables:
`ChildCollectionsDeleteTheirOrphans`, which re-creates two foreign keys as NO ACTION
instead of RESTRICT, and `BilingualDealerContent`, which adds fourteen `*_ar` / `*_en`
columns (script 5 below). That makes 25. A database built from this file was compared
with one built by EF's own migrations: the schemas are identical, whitespace aside.
The regeneration before (2026-09-20) added `AddCustomerShortlist`, which brought
`customer_shortlists` and `shortlist_entries` and made script 2 mandatory,
`DealerPublicProfile`, and `PenaltyReasonCode`.

**Regenerated on 2026-10-07 (Fix & Polish Wave 4)**: every migration through
`20261007142545_NotificationDeliveryClaimToken`, 43 in all, creating 46 tables plus the history table. The copy before it had stopped at the 2026-09-24 migrations, so
this is also the first time the payments tables, the financial documents, the payables, the legal texts and the
consents appear in it. Re-run script 2 after it.

Regenerate after adding a migration:

```bash
dotnet ef migrations script --idempotent \
  --project Khadra.Infrastructure --startup-project Khadra.WebAPI \
  --output docs/sql/khadra-schema.sql
```

## 2. `supabase-lockdown.sql` — do not skip this one

Supabase publishes every table in `public` through PostgREST, and the anon key
that reaches it is **public by design**: it ships inside client applications. EF
creates Khadra's tables in `public` with no row-level security, so between running
the schema script and this one, `users`, `bookings`, `customer_documents` and
`payments` are readable by anyone with that key. Customer passports and driving
licences are in those tables.

Khadra does not use PostgREST. It connects as `postgres`, which bypasses RLS — so
enabling RLS with no policies denies the API roles everything and changes nothing
for the application. Verified on a database seeded with Supabase's own default
grants:

```
BEFORE   anon reading users: 0            (readable)
AFTER    anon reading users: ERROR: permission denied for table users
         anon reading customer_documents: ERROR: permission denied
         app  reading users: 0            (unaffected)
         tables with RLS: 25/25
```

Run 1 then 2, in the Supabase SQL editor.

**Script 2 must be re-run after every migration that adds a table.** It loops over
`pg_tables`, so it needs no editing — but a table created after the last run has RLS
off and is published through PostgREST until it does. `document_access_entries` is
the table that made this worth stating: it records who opened which customer's
passport, and it is exactly what the anon key must never reach.

## 3. `verify-admin.sql`

Read-only. Answers "is there an administrator, who is it, and can they actually sign in?"

The platform deliberately reveals none of that over HTTP — Forgot Password returns
the same 202 whether or not the account exists, because anything else turns that
form into a way to ask who holds an account. So the database is the only honest
place to look, and this is the query to look with.

Read `is_email_verified` first. An administrator ROW is not the same thing as a
usable login: an invitation that was never accepted leaves the address unverified,
and `CanAuthenticate` refuses it. **A password reset does not fix that** — resetting
changes the password without verifying the address. Only the invitation link does
both, in one step.

It also answers the question to ask *before* setting `Admin__Bootstrap__Email`: is
that address already taken? The unique index on `users.email` covers soft-deleted
rows, so a customer account made while testing the phone app will block it.

The one destructive statement on the page is commented out and explains itself: it
consumes every live verification link, which is the remedy if messages were written
to the log while `Email:Provider` selected the Logging transport. Each of those log
lines carries a working link — an hour for a reset, a day for verification, seven
days for an invitation. A build from 2026-09-17 on no longer writes them: the Logging
transport logs the subject and the recipient's domain only. So this is the remedy for
lines an earlier build wrote.

## 4. `2026-09-10-dealer-address.sql`

The one migration this schema gained after the first deployment: the two nullable
address columns on `dealers`. Idempotent, like everything here — it checks
`__EFMigrationsHistory` and records itself, so running it twice is a no-op.

Additive and nullable, which is what makes it safe to apply BEFORE the new build
is deployed: EF names its columns explicitly in every query, so the running older
API neither sees nor touches them.

`khadra-schema.sql` above already contains it, for a database being created from
scratch. This file is for the one that already exists.

## 5. `2026-09-22-bilingual-dealer-content.sql`

The two migrations after `PenaltyReasonCode`, for the production database that already
exists, generated with:

```bash
dotnet ef migrations script 20260918201534_PenaltyReasonCode 20260922012458_BilingualDealerContent \
  --idempotent --project Khadra.Infrastructure --startup-project Khadra.WebAPI \
  --output docs/sql/2026-09-22-bilingual-dealer-content.sql
```

- **`ChildCollectionsDeleteTheirOrphans`** has been in the code since 2026-09-20, but
  the last regeneration of script 1 stopped before it, so production may never have
  had it. Harmless either way — two foreign keys go from RESTRICT to NO ACTION, and
  both refuse — and each migration checks `__EFMigrationsHistory`, so this script
  applies whichever of the two is missing and nothing else.
- **`BilingualDealerContent` is NOT safe to apply ahead of the new build.** Unlike
  script 4 it has to be applied with the API stopped: from the moment it runs, an older
  API still writes the legacy columns and nothing reads them. Pre-launch item 132 and
  [releases/2026-09-bilingual-and-app-gate.md](../releases/2026-09-bilingual-and-app-gate.md)
  give the procedure.

It checks itself. It aborts, committing none of `BilingualDealerContent`, if its
script detector misreads Arabic, presentation forms or English, or if any legacy
value would land on both sides, neither side, or altered. Run it **from the file**
(`psql -v ON_ERROR_STOP=1 -f`) rather than pasted into the SQL editor: the detector's
character ranges are literal Arabic, including U+FEFF, and a paste that normalises
them fails the self-test. Safely — but it fails.

Rehearsed on 2026-09-22 against copies of a seeded database in both states production
can be in — before `ChildCollectionsDeleteTheirOrphans` and after it:

```
legacy | verbatim_on_one_side | invented        (item 132's verification query)
  16   |          16          |    0            both states; a second run changes nothing
```

A copy whose detector was deliberately broken stopped with the self-test's own
message, `psql` exit 3, no new columns, all 16 legacy values untouched.

No table is added, so `supabase-lockdown.sql` does not need re-running for it.

## 6. `2026-09-24-frozen-commission.sql`

Schema-neutral, but it **writes existing rows**: every booking gains
`pricing.CommissionAmount` and `terms.CommissionBasis = "RentalTotal"`. The amount is
exactly what every screen used to compute on the fly — the booking's frozen
`CommissionPercent` of its `RentalTotal` — rounded half to even at three decimals,
the way `Money` rounds. From this release new bookings freeze 20% of ONE day
instead, and existing bookings must keep the figure they always showed.

The new API cannot load a booking without these keys, so the order matters:

1. Run **Part 1** (the idempotent migration) before deploying the API that needs it.
   The API that is live ignores both keys.
2. Deploy the API.
3. Run **Part 2** (the unguarded catch-up) once more. It freezes any booking the old
   API created between steps 1 and 2. Idempotent; safe to run again at any time.

Rehearsed on 2026-09-24 against the local development database (80 bookings): every
backfilled amount equalled the old computation, and the half-way cases were checked
against .NET's half-to-even rounding (1.2505 → 1.250, 1.2515 → 1.252).

No table is added, so `supabase-lockdown.sql` does not need re-running for it.

## 7. `2026-09-24-payment-options.sql`

Additive: `payments.purpose` (default `'Deposit'`), `payments.processing_fee` (default 0) and
`bookings.online_paid` (default 0) and `payments.fee_refundable` (default true, which is what every
existing row has always meant), for the two ways of paying an approved booking — the deposit or
the full amount. The one write sets `online_paid` to the frozen deposit on every booking a payment
already confirmed, since the deposit was the only way to confirm until this release. Apply after
script 6 and before deploying the API; run its catch-up section once more after the deploy.

## 8. `2026-10-05-legal-documents.sql`

The legal texts (Wave 2 G1). It creates one table, `legal_document_versions`, with:

- its five CHECKs;
- the two unique indexes the publish handler recognises by name (`ux_legal_document_versions_kind_effective_from`,
  `ux_legal_document_versions_kind_version_label`);
- its append-only triggers (row UPDATE/DELETE and TRUNCATE, on the existing `khadra_table_is_append_only()`).

Generated with `dotnet ef migrations script 20260929195432_OfficePayables 20261005062940_LegalDocumentVersions
--idempotent`, so it is guarded by `__EFMigrationsHistory` and safe to run twice. It writes no rows: the texts arrive
only through the console's *Legal documents* screen, never by script.

1. Run it before deploying the API that serves `/legal-documents` (an older API ignores the table).
2. **Run script 2 (`supabase-lockdown.sql`) again.** It is a new table in `public`. The lockdown loops over every
   table, and its default-privilege revoke already covers tables created later, but running it again is the step
   that proves it.
3. Deploy the API.

There is no rollback script. The migration's `Down` refuses while any version exists, because a published legal text
is evidence. Before anything is published, dropping the table and its history row is the whole rollback.

Proved on 2026-10-05 against a throwaway PostgreSQL 18 (`PostgresLegalDocumentsTests`): the constraint and index
names, the triggers refusing UPDATE, DELETE and TRUNCATE, each stored hash equal to
`encode(sha256(convert_to(body,'UTF8')),'hex')`, and the refused rollback.

## 9. `2026-10-07-fix-polish-wave4.sql`

Fix & Polish Wave 4's five migrations, for a database at `20261005062940_LegalDocumentVersions` (Staging today),
generated with:

```bash
dotnet ef migrations script 20261005062940_LegalDocumentVersions 20261007142545_NotificationDeliveryClaimToken \
  --idempotent --project Khadra.Infrastructure --startup-project Khadra.WebAPI \
  --output docs/sql/2026-10-07-fix-polish-wave4.sql
```

- **`PaymentCaptureIncidentsAndRefundBackoff`** adds `payment_incidents` (a new table), the capture reference on
  payments and provider events, the `xmin` concurrency token on refunds (a system column, so nothing is added), and the
  refusal schedule on refunds. Its one write gives every refund already `Failed` a `refusal_count` of 1, so the sweep
  tries it again at once and counts it as refused once.
- **`BookingDisputeWindowEnd`** adds the nullable `bookings.dispute_window_ends_at` and its partial index.
- **`LegalConsents`** adds `legal_consents` (a new table), append-only with row and TRUNCATE triggers, its CHECKs and
  its foreign key to `legal_document_versions` (restrict). It writes no rows: consent arrives only from the people
  giving it.
- **`AdminDocumentAccess`** makes `document_access_entries.dealer_id` and `booking_id` nullable under
  `ck_document_access_entries_scope` (null only for an administrator), and adds the `xmin` token on
  `customer_documents` (a system column again: nothing is added).
- **`NotificationDeliveryClaimToken`** changes no schema. It makes `notification_deliveries.attempts` a concurrency
  token in the model (pre-launch item 204), which lives in EF's UPDATE statements only, and writes its history row.

All five are additive for the API that is live, which never reads the new columns and never writes a null into the
loosened ones. So:

1. Run it before deploying the Wave 4 API.
2. **Run script 2 (`supabase-lockdown.sql`) again**: `payment_incidents` and `legal_consents` are new tables in
   `public`, and `legal_consents` records who accepted what.
3. Deploy the API, the BFFs, the console and the website together. Consent is required, and the gate closes, only
   once a legal text is in force.

There is no rollback script. `LegalConsents`' `Down` refuses once anybody has consented, and `AdminDocumentAccess`'
once an administrator has opened a document: both are evidence. Before either has happened, each `Down` runs.
