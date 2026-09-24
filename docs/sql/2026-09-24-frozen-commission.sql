-- Frozen commission (2026-09-24). One migration, SCHEMA-NEUTRAL, writes existing rows:
--
--   20260924154009_FrozenCommission   bookings.pricing gains CommissionAmount; bookings.terms gains
--                                     CommissionBasis = "RentalTotal" — on every booking that lacks them.
--
-- WHAT IT DOES TO DATA. Until now Khadra's commission was never stored: every screen recomputed it as
-- the booking's frozen CommissionPercent of its RentalTotal. From this release it is 20% of ONE day's
-- rental price, frozen as an amount on each new booking. Existing bookings must keep the figure they
-- always showed, so this writes exactly that old computation onto each of them — RentalTotal ×
-- CommissionPercent / 100, rounded to 3 decimals HALF TO EVEN (as Money rounds every amount; the half
-- case is spelled out because PostgreSQL's round() rounds half away from zero). No column, table or
-- index changes; no existing value is altered; a row that already has the keys is left alone.
--
-- WHY IT MUST RUN. The new API maps these keys to get-only value objects: a booking without them
-- cannot be loaded. There is deliberately no runtime fallback.
--
-- ORDER (Staging first; production is not in scope):
--   1. Run PART 1 below from the Supabase SQL editor. The API that is live now ignores both keys.
--   2. Deploy the API carrying 20260924154009_FrozenCommission.
--   3. Run PART 2 (catch-up) once more: it freezes any booking the OLD API created between steps 1
--      and 2, which would otherwise be unloadable. It is idempotent and safe to run again at any time.
--
-- Reversible: the migration's Down removes the keys from rules-version-1 bookings only.

-- ─── PART 1 ─────────────────────────────────────────────────────────────────────────────────────
START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260924154009_FrozenCommission') THEN
    UPDATE bookings
    SET pricing = jsonb_set(
          pricing,
          '{CommissionAmount}',
          jsonb_build_object(
            'Amount',
            (SELECT (CASE
                      WHEN (v * 1000) - trunc(v * 1000) = 0.5 AND mod(trunc(v * 1000), 2) = 0
                        THEN trunc(v * 1000)
                      ELSE round(v * 1000)
                    END / 1000)::numeric(18, 3)
               FROM (SELECT (pricing -> 'RentalTotal' ->> 'Amount')::numeric
                            * (terms -> 'CommissionPercent' ->> 'Value')::numeric / 100 AS v) AS calc),
            'CurrencyCode',
            pricing -> 'RentalTotal' ->> 'CurrencyCode'))
    WHERE NOT (pricing ? 'CommissionAmount');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260924154009_FrozenCommission') THEN
    UPDATE bookings
    SET terms = jsonb_set(terms, '{CommissionBasis}', '"RentalTotal"')
    WHERE NOT (terms ? 'CommissionBasis');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260924154009_FrozenCommission') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260924154009_FrozenCommission', '10.0.11');
    END IF;
END $EF$;
COMMIT;


-- ─── PART 2 — catch-up, run again AFTER the new API is live ─────────────────────────────────────
-- The same two statements without the history guard. Touches only bookings still missing the keys.
START TRANSACTION;

UPDATE bookings
SET pricing = jsonb_set(
      pricing,
      '{CommissionAmount}',
      jsonb_build_object(
        'Amount',
        (SELECT (CASE
                  WHEN (v * 1000) - trunc(v * 1000) = 0.5 AND mod(trunc(v * 1000), 2) = 0
                    THEN trunc(v * 1000)
                  ELSE round(v * 1000)
                END / 1000)::numeric(18, 3)
           FROM (SELECT (pricing -> 'RentalTotal' ->> 'Amount')::numeric
                        * (terms -> 'CommissionPercent' ->> 'Value')::numeric / 100 AS v) AS calc),
        'CurrencyCode',
        pricing -> 'RentalTotal' ->> 'CurrencyCode'))
WHERE NOT (pricing ? 'CommissionAmount');

UPDATE bookings
SET terms = jsonb_set(terms, '{CommissionBasis}', '"RentalTotal"')
WHERE NOT (terms ? 'CommissionBasis');

COMMIT;

-- Check (both must be 0):
-- SELECT count(*) FILTER (WHERE NOT (pricing ? 'CommissionAmount')) AS missing_amount,
--        count(*) FILTER (WHERE NOT (terms ? 'CommissionBasis'))    AS missing_basis
--   FROM bookings;
