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
