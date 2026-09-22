START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260920233804_ChildCollectionsDeleteTheirOrphans') THEN
    ALTER TABLE shortlist_entries DROP CONSTRAINT fk_shortlist_entries_customer_shortlists_shortlist_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260920233804_ChildCollectionsDeleteTheirOrphans') THEN
    ALTER TABLE vehicle_images DROP CONSTRAINT fk_vehicle_images_vehicles_vehicle_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260920233804_ChildCollectionsDeleteTheirOrphans') THEN
    ALTER TABLE shortlist_entries ADD CONSTRAINT fk_shortlist_entries_customer_shortlists_shortlist_id FOREIGN KEY (shortlist_id) REFERENCES customer_shortlists (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260920233804_ChildCollectionsDeleteTheirOrphans') THEN
    ALTER TABLE vehicle_images ADD CONSTRAINT fk_vehicle_images_vehicles_vehicle_id FOREIGN KEY (vehicle_id) REFERENCES vehicles (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260920233804_ChildCollectionsDeleteTheirOrphans') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260920233804_ChildCollectionsDeleteTheirOrphans', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    ALTER TABLE dealers ADD description_ar character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    ALTER TABLE dealers ADD description_en character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    ALTER TABLE dealers ADD rental_conditions_ar character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    ALTER TABLE dealers ADD rental_conditions_en character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    ALTER TABLE dealers ADD insurance_summary_ar character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    ALTER TABLE dealers ADD insurance_summary_en character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    ALTER TABLE dealers ADD pickup_instructions_ar character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    ALTER TABLE dealers ADD pickup_instructions_en character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    ALTER TABLE dealers ADD delivery_notes_ar character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    ALTER TABLE dealers ADD delivery_notes_en character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    ALTER TABLE dealers ADD customer_notes_ar character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    ALTER TABLE dealers ADD customer_notes_en character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    ALTER TABLE vehicles ADD description_ar character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    ALTER TABLE vehicles ADD description_en character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    CREATE FUNCTION pg_temp.khadra_mostly_arabic(t text) RETURNS boolean
    LANGUAGE sql IMMUTABLE AS $fn$
      SELECT length(regexp_replace(t, '[^؀-ۿݐ-ݿࢠ-ࣿﭐ-﷿ﹰ-﻿]', '', 'g'))
           > length(regexp_replace(t, '[^A-Za-z]', '', 'g'))
    $fn$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    DO $selftest$
    BEGIN
      IF NOT pg_temp.khadra_mostly_arabic('ممنوع التدخين') THEN
        RAISE EXCEPTION 'BilingualDealerContent: the script detector does not recognise Arabic. The character ranges in this migration are corrupt; every value would have been filed as English. Nothing has been committed.';
      END IF;
      IF pg_temp.khadra_mostly_arabic('No smoking in the vehicle') THEN
        RAISE EXCEPTION 'BilingualDealerContent: the script detector calls English Arabic. Nothing has been committed.';
      END IF;
      IF NOT pg_temp.khadra_mostly_arabic('ﻣﻤﻨﻮﻉ ﺍﻟﺘﺪﺧﻴﻦ') THEN
        RAISE EXCEPTION 'BilingualDealerContent: the script detector misses Arabic presentation forms, which is how text pasted from a PDF arrives. Nothing has been committed.';
      END IF;
    END
    $selftest$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    UPDATE dealers SET
      description_ar = CASE WHEN pg_temp.khadra_mostly_arabic(description) THEN description END,
      description_en = CASE WHEN pg_temp.khadra_mostly_arabic(description) THEN NULL ELSE description END
    WHERE description IS NOT NULL
      AND description_ar IS NULL
      AND description_en IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    DO $guard$
    DECLARE wrong integer;
    BEGIN
      SELECT count(*) INTO wrong FROM dealers
       WHERE description IS NOT NULL
         AND (coalesce(description_ar, description_en) IS DISTINCT FROM description
              OR (description_ar IS NOT NULL AND description_en IS NOT NULL));
      IF wrong > 0 THEN
        RAISE EXCEPTION
          'BilingualDealerContent: % rows of dealers.description were not copied verbatim to exactly one language. Nothing has been committed.', wrong;
      END IF;
    END
    $guard$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    COMMENT ON COLUMN dealers.description IS
      'Retired by BilingualDealerContent (2026-09-22). Copied verbatim to description_ar / description_en by script detection. Retained legacy source for migration/audit only; customer and dealer reads use the new bilingual columns. Unmapped by EF. Dropped by pre-launch item 131.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    UPDATE dealers SET
      rental_conditions_ar = CASE WHEN pg_temp.khadra_mostly_arabic(rental_conditions) THEN rental_conditions END,
      rental_conditions_en = CASE WHEN pg_temp.khadra_mostly_arabic(rental_conditions) THEN NULL ELSE rental_conditions END
    WHERE rental_conditions IS NOT NULL
      AND rental_conditions_ar IS NULL
      AND rental_conditions_en IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    DO $guard$
    DECLARE wrong integer;
    BEGIN
      SELECT count(*) INTO wrong FROM dealers
       WHERE rental_conditions IS NOT NULL
         AND (coalesce(rental_conditions_ar, rental_conditions_en) IS DISTINCT FROM rental_conditions
              OR (rental_conditions_ar IS NOT NULL AND rental_conditions_en IS NOT NULL));
      IF wrong > 0 THEN
        RAISE EXCEPTION
          'BilingualDealerContent: % rows of dealers.rental_conditions were not copied verbatim to exactly one language. Nothing has been committed.', wrong;
      END IF;
    END
    $guard$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    COMMENT ON COLUMN dealers.rental_conditions IS
      'Retired by BilingualDealerContent (2026-09-22). Copied verbatim to rental_conditions_ar / rental_conditions_en by script detection. Retained legacy source for migration/audit only; customer and dealer reads use the new bilingual columns. Unmapped by EF. Dropped by pre-launch item 131.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    UPDATE dealers SET
      insurance_summary_ar = CASE WHEN pg_temp.khadra_mostly_arabic(insurance_summary) THEN insurance_summary END,
      insurance_summary_en = CASE WHEN pg_temp.khadra_mostly_arabic(insurance_summary) THEN NULL ELSE insurance_summary END
    WHERE insurance_summary IS NOT NULL
      AND insurance_summary_ar IS NULL
      AND insurance_summary_en IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    DO $guard$
    DECLARE wrong integer;
    BEGIN
      SELECT count(*) INTO wrong FROM dealers
       WHERE insurance_summary IS NOT NULL
         AND (coalesce(insurance_summary_ar, insurance_summary_en) IS DISTINCT FROM insurance_summary
              OR (insurance_summary_ar IS NOT NULL AND insurance_summary_en IS NOT NULL));
      IF wrong > 0 THEN
        RAISE EXCEPTION
          'BilingualDealerContent: % rows of dealers.insurance_summary were not copied verbatim to exactly one language. Nothing has been committed.', wrong;
      END IF;
    END
    $guard$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    COMMENT ON COLUMN dealers.insurance_summary IS
      'Retired by BilingualDealerContent (2026-09-22). Copied verbatim to insurance_summary_ar / insurance_summary_en by script detection. Retained legacy source for migration/audit only; customer and dealer reads use the new bilingual columns. Unmapped by EF. Dropped by pre-launch item 131.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    UPDATE dealers SET
      pickup_instructions_ar = CASE WHEN pg_temp.khadra_mostly_arabic(pickup_instructions) THEN pickup_instructions END,
      pickup_instructions_en = CASE WHEN pg_temp.khadra_mostly_arabic(pickup_instructions) THEN NULL ELSE pickup_instructions END
    WHERE pickup_instructions IS NOT NULL
      AND pickup_instructions_ar IS NULL
      AND pickup_instructions_en IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    DO $guard$
    DECLARE wrong integer;
    BEGIN
      SELECT count(*) INTO wrong FROM dealers
       WHERE pickup_instructions IS NOT NULL
         AND (coalesce(pickup_instructions_ar, pickup_instructions_en) IS DISTINCT FROM pickup_instructions
              OR (pickup_instructions_ar IS NOT NULL AND pickup_instructions_en IS NOT NULL));
      IF wrong > 0 THEN
        RAISE EXCEPTION
          'BilingualDealerContent: % rows of dealers.pickup_instructions were not copied verbatim to exactly one language. Nothing has been committed.', wrong;
      END IF;
    END
    $guard$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    COMMENT ON COLUMN dealers.pickup_instructions IS
      'Retired by BilingualDealerContent (2026-09-22). Copied verbatim to pickup_instructions_ar / pickup_instructions_en by script detection. Retained legacy source for migration/audit only; customer and dealer reads use the new bilingual columns. Unmapped by EF. Dropped by pre-launch item 131.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    UPDATE dealers SET
      delivery_notes_ar = CASE WHEN pg_temp.khadra_mostly_arabic(delivery_notes) THEN delivery_notes END,
      delivery_notes_en = CASE WHEN pg_temp.khadra_mostly_arabic(delivery_notes) THEN NULL ELSE delivery_notes END
    WHERE delivery_notes IS NOT NULL
      AND delivery_notes_ar IS NULL
      AND delivery_notes_en IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    DO $guard$
    DECLARE wrong integer;
    BEGIN
      SELECT count(*) INTO wrong FROM dealers
       WHERE delivery_notes IS NOT NULL
         AND (coalesce(delivery_notes_ar, delivery_notes_en) IS DISTINCT FROM delivery_notes
              OR (delivery_notes_ar IS NOT NULL AND delivery_notes_en IS NOT NULL));
      IF wrong > 0 THEN
        RAISE EXCEPTION
          'BilingualDealerContent: % rows of dealers.delivery_notes were not copied verbatim to exactly one language. Nothing has been committed.', wrong;
      END IF;
    END
    $guard$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    COMMENT ON COLUMN dealers.delivery_notes IS
      'Retired by BilingualDealerContent (2026-09-22). Copied verbatim to delivery_notes_ar / delivery_notes_en by script detection. Retained legacy source for migration/audit only; customer and dealer reads use the new bilingual columns. Unmapped by EF. Dropped by pre-launch item 131.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    UPDATE dealers SET
      customer_notes_ar = CASE WHEN pg_temp.khadra_mostly_arabic(customer_notes) THEN customer_notes END,
      customer_notes_en = CASE WHEN pg_temp.khadra_mostly_arabic(customer_notes) THEN NULL ELSE customer_notes END
    WHERE customer_notes IS NOT NULL
      AND customer_notes_ar IS NULL
      AND customer_notes_en IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    DO $guard$
    DECLARE wrong integer;
    BEGIN
      SELECT count(*) INTO wrong FROM dealers
       WHERE customer_notes IS NOT NULL
         AND (coalesce(customer_notes_ar, customer_notes_en) IS DISTINCT FROM customer_notes
              OR (customer_notes_ar IS NOT NULL AND customer_notes_en IS NOT NULL));
      IF wrong > 0 THEN
        RAISE EXCEPTION
          'BilingualDealerContent: % rows of dealers.customer_notes were not copied verbatim to exactly one language. Nothing has been committed.', wrong;
      END IF;
    END
    $guard$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    COMMENT ON COLUMN dealers.customer_notes IS
      'Retired by BilingualDealerContent (2026-09-22). Copied verbatim to customer_notes_ar / customer_notes_en by script detection. Retained legacy source for migration/audit only; customer and dealer reads use the new bilingual columns. Unmapped by EF. Dropped by pre-launch item 131.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    UPDATE vehicles SET
      description_ar = CASE WHEN pg_temp.khadra_mostly_arabic(description) THEN description END,
      description_en = CASE WHEN pg_temp.khadra_mostly_arabic(description) THEN NULL ELSE description END
    WHERE description IS NOT NULL
      AND description_ar IS NULL
      AND description_en IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    DO $guard$
    DECLARE wrong integer;
    BEGIN
      SELECT count(*) INTO wrong FROM vehicles
       WHERE description IS NOT NULL
         AND (coalesce(description_ar, description_en) IS DISTINCT FROM description
              OR (description_ar IS NOT NULL AND description_en IS NOT NULL));
      IF wrong > 0 THEN
        RAISE EXCEPTION
          'BilingualDealerContent: % rows of vehicles.description were not copied verbatim to exactly one language. Nothing has been committed.', wrong;
      END IF;
    END
    $guard$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    COMMENT ON COLUMN vehicles.description IS
      'Retired by BilingualDealerContent (2026-09-22). Copied verbatim to description_ar / description_en by script detection. Retained legacy source for migration/audit only; customer and dealer reads use the new bilingual columns. Unmapped by EF. Dropped by pre-launch item 131.';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260922012458_BilingualDealerContent') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260922012458_BilingualDealerContent', '10.0.11');
    END IF;
END $EF$;
COMMIT;

