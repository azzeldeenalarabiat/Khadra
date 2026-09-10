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
Creates 25 tables plus that history table, the `btree_gist` extension, and the
`bookings_one_hold_per_vehicle` exclusion constraint that stops two live bookings
overlapping on one vehicle.

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
days for an invitation.

## 4. `2026-09-10-dealer-address.sql`

The one migration this schema gained after the first deployment: the two nullable
address columns on `dealers`. Idempotent, like everything here — it checks
`__EFMigrationsHistory` and records itself, so running it twice is a no-op.

Additive and nullable, which is what makes it safe to apply BEFORE the new build
is deployed: EF names its columns explicitly in every query, so the running older
API neither sees nor touches them.

`khadra-schema.sql` above already contains it, for a database being created from
scratch. This file is for the one that already exists.
