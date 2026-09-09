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
Creates 22 tables plus that history table, the `btree_gist` extension, and the
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
         tables with RLS: 23/23
```

Run 1 then 2, in the Supabase SQL editor.
