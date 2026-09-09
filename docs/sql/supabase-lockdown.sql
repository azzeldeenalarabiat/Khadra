-- Close Supabase's REST API over the Khadra schema.
--
-- WHY THIS IS NOT OPTIONAL
--
-- Supabase publishes every table in `public` through PostgREST, and the anon key
-- that reaches it is PUBLIC by design -- it ships inside client applications and
-- is visible in the project dashboard. Khadra's tables were created by EF in
-- `public` with no row-level security, which means `users`, `bookings`,
-- `customer_documents` and `payments` would be readable, and in places writable,
-- by anyone who has that key. Customer passports and driving licences are in
-- there.
--
-- Khadra does not use PostgREST at all. It connects as `postgres` over the
-- pooler, and that role BYPASSES row-level security -- so enabling RLS with no
-- policies at all denies the API roles everything while the application carries
-- on exactly as before. Nothing to maintain, and no policy to get subtly wrong.
--
-- Run it in the Supabase SQL editor AFTER the schema script. Re-running is safe.

DO $$
DECLARE
    target record;
BEGIN
    FOR target IN
        SELECT tablename
        FROM pg_tables
        WHERE schemaname = 'public'
    LOOP
        -- No policies are created, deliberately. RLS with no policy denies every
        -- role that is subject to it, which is precisely what anon and
        -- authenticated should get here.
        EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY;', target.tablename);
        EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY;', target.tablename);
    END LOOP;
END
$$;

-- Belt and braces: take the grants away as well, so a future table created
-- without RLS is not exposed the moment it appears.
--
-- Guarded on the roles existing. They always do on Supabase; they do not on a
-- plain Postgres, and an unguarded REVOKE would abort the script THERE -- after
-- the RLS above but before it reported, which is the worst place to stop.
DO $$
DECLARE
    api_role text;
BEGIN
    FOREACH api_role IN ARRAY ARRAY['anon', 'authenticated']
    LOOP
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = api_role) THEN
            EXECUTE format('REVOKE ALL ON ALL TABLES IN SCHEMA public FROM %I;', api_role);
            EXECUTE format('REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM %I;', api_role);
            EXECUTE format('REVOKE ALL ON ALL FUNCTIONS IN SCHEMA public FROM %I;', api_role);
            EXECUTE format('REVOKE USAGE ON SCHEMA public FROM %I;', api_role);
            EXECUTE format(
                'ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON TABLES FROM %I;', api_role);
            EXECUTE format(
                'ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON SEQUENCES FROM %I;', api_role);
            RAISE NOTICE 'Revoked public schema access from %', api_role;
        ELSE
            RAISE NOTICE 'Role % does not exist here; nothing to revoke.', api_role;
        END IF;
    END LOOP;
END
$$;

-- What this should print: every table, rowsecurity = true.
SELECT tablename, rowsecurity
FROM pg_tables
WHERE schemaname = 'public'
ORDER BY tablename;
