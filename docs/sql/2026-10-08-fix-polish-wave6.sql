-- Fix & Polish Wave 6: six migrations, for a database at 20261007142545_NotificationDeliveryClaimToken (the end of
-- Wave 4; Wave 5 added none). Generated with
--   dotnet ef migrations script 20261007142545_NotificationDeliveryClaimToken \
--     20261008122550_ArabicAuditSubjectAndNotificationStandIn --idempotent \
--     --project Khadra.Infrastructure --startup-project Khadra.WebAPI
-- and preceded by one READ-ONLY check. See docs/sql/README.md, section 10.

-- ── Pre-check (read-only; pre-launch item 52) ───────────────────────────────────────────────────────────────────────
-- OfferedLookupNamesAreUnique refuses to apply, and rolls the whole transaction back, while two OFFERED cities or two
-- offered car types share a name — compared as the new indexes compare: case-folded, with Arabic diacritics and
-- tatweel removed. Run this first; every row it returns is a pair an administrator must retire or rename on /cities
-- or /car-types before applying. No rows: apply the script.
SELECT 'cities' AS list, 'name_en' AS name_column, lower(regexp_replace(name_en, '[\u064B-\u0652\u0640]', '', 'g')) AS compared_as,
       string_agg(name_en, ' | ' ORDER BY name_en) AS names, count(*) AS offered
FROM cities WHERE is_active GROUP BY 3 HAVING count(*) > 1
UNION ALL
SELECT 'cities', 'name_ar', lower(regexp_replace(name_ar, '[\u064B-\u0652\u0640]', '', 'g')),
       string_agg(name_ar, ' | ' ORDER BY name_ar), count(*)
FROM cities WHERE is_active GROUP BY 3 HAVING count(*) > 1
UNION ALL
SELECT 'car_types', 'name_en', lower(regexp_replace(name_en, '[\u064B-\u0652\u0640]', '', 'g')),
       string_agg(name_en, ' | ' ORDER BY name_en), count(*)
FROM car_types WHERE is_active GROUP BY 3 HAVING count(*) > 1
UNION ALL
SELECT 'car_types', 'name_ar', lower(regexp_replace(name_ar, '[\u064B-\u0652\u0640]', '', 'g')),
       string_agg(name_ar, ' | ' ORDER BY name_ar), count(*)
FROM car_types WHERE is_active GROUP BY 3 HAVING count(*) > 1;

-- ── The migrations (idempotent: each step runs only where its history row is missing) ──────────────────────────────
START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008110814_SignInThrottle') THEN
    CREATE TABLE sign_in_throttles (
        subject_hash character(64) NOT NULL,
        window_started_at timestamp with time zone NOT NULL,
        failures integer NOT NULL,
        blocked_until timestamp with time zone,
        CONSTRAINT pk_sign_in_throttles PRIMARY KEY (subject_hash),
        CONSTRAINT ck_sign_in_throttles_failures CHECK (failures >= 1)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008110814_SignInThrottle') THEN
    CREATE INDEX ix_sign_in_throttles_window_started_at ON sign_in_throttles (window_started_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008110814_SignInThrottle') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261008110814_SignInThrottle', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008111400_AuditTrailRefusesTruncate') THEN

    DROP TRIGGER IF EXISTS audit_entries_no_truncate ON audit_entries;

    CREATE TRIGGER audit_entries_no_truncate
    BEFORE TRUNCATE ON audit_entries
    FOR EACH STATEMENT EXECUTE FUNCTION khadra_audit_entries_are_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008111400_AuditTrailRefusesTruncate') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261008111400_AuditTrailRefusesTruncate', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008113235_AggregateChildrenDeleteTheirOrphans') THEN
    ALTER TABLE booking_handovers DROP CONSTRAINT fk_booking_handovers_bookings_booking_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008113235_AggregateChildrenDeleteTheirOrphans') THEN
    ALTER TABLE booking_status_changes DROP CONSTRAINT fk_booking_status_changes_bookings_booking_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008113235_AggregateChildrenDeleteTheirOrphans') THEN
    ALTER TABLE customer_documents DROP CONSTRAINT fk_customer_documents_users_user_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008113235_AggregateChildrenDeleteTheirOrphans') THEN
    ALTER TABLE dealer_documents DROP CONSTRAINT fk_dealer_documents_dealers_dealer_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008113235_AggregateChildrenDeleteTheirOrphans') THEN
    ALTER TABLE dealer_employees DROP CONSTRAINT fk_dealer_employees_dealers_dealer_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008113235_AggregateChildrenDeleteTheirOrphans') THEN
    ALTER TABLE dispute_statements DROP CONSTRAINT fk_dispute_statements_dispute_tickets_ticket_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008113235_AggregateChildrenDeleteTheirOrphans') THEN
    ALTER TABLE renter_document_reviews DROP CONSTRAINT fk_renter_document_reviews_bookings_booking_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008113235_AggregateChildrenDeleteTheirOrphans') THEN
    ALTER TABLE booking_handovers ADD CONSTRAINT fk_booking_handovers_bookings_booking_id FOREIGN KEY (booking_id) REFERENCES bookings (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008113235_AggregateChildrenDeleteTheirOrphans') THEN
    ALTER TABLE booking_status_changes ADD CONSTRAINT fk_booking_status_changes_bookings_booking_id FOREIGN KEY (booking_id) REFERENCES bookings (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008113235_AggregateChildrenDeleteTheirOrphans') THEN
    ALTER TABLE customer_documents ADD CONSTRAINT fk_customer_documents_users_user_id FOREIGN KEY (user_id) REFERENCES users (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008113235_AggregateChildrenDeleteTheirOrphans') THEN
    ALTER TABLE dealer_documents ADD CONSTRAINT fk_dealer_documents_dealers_dealer_id FOREIGN KEY (dealer_id) REFERENCES dealers (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008113235_AggregateChildrenDeleteTheirOrphans') THEN
    ALTER TABLE dealer_employees ADD CONSTRAINT fk_dealer_employees_dealers_dealer_id FOREIGN KEY (dealer_id) REFERENCES dealers (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008113235_AggregateChildrenDeleteTheirOrphans') THEN
    ALTER TABLE dispute_statements ADD CONSTRAINT fk_dispute_statements_dispute_tickets_ticket_id FOREIGN KEY (ticket_id) REFERENCES dispute_tickets (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008113235_AggregateChildrenDeleteTheirOrphans') THEN
    ALTER TABLE renter_document_reviews ADD CONSTRAINT fk_renter_document_reviews_bookings_booking_id FOREIGN KEY (booking_id) REFERENCES bookings (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008113235_AggregateChildrenDeleteTheirOrphans') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261008113235_AggregateChildrenDeleteTheirOrphans', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008113858_OfferedLookupNamesAreUnique') THEN

    DO $$
    DECLARE found text;
    BEGIN
        SELECT string_agg(duplicate, '; ') INTO found FROM (
        SELECT 'cities.name_en ' || string_agg(name_en, ' = ') AS duplicate
        FROM cities WHERE is_active
        GROUP BY lower(regexp_replace(name_en, '[\u064B-\u0652\u0640]', '', 'g'))
        HAVING count(*) > 1
        UNION ALL 
        SELECT 'cities.name_ar ' || string_agg(name_ar, ' = ') AS duplicate
        FROM cities WHERE is_active
        GROUP BY lower(regexp_replace(name_ar, '[\u064B-\u0652\u0640]', '', 'g'))
        HAVING count(*) > 1
        UNION ALL 
        SELECT 'car_types.name_en ' || string_agg(name_en, ' = ') AS duplicate
        FROM car_types WHERE is_active
        GROUP BY lower(regexp_replace(name_en, '[\u064B-\u0652\u0640]', '', 'g'))
        HAVING count(*) > 1
        UNION ALL 
        SELECT 'car_types.name_ar ' || string_agg(name_ar, ' = ') AS duplicate
        FROM car_types WHERE is_active
        GROUP BY lower(regexp_replace(name_ar, '[\u064B-\u0652\u0640]', '', 'g'))
        HAVING count(*) > 1) duplicates;

        IF found IS NOT NULL THEN
            RAISE EXCEPTION 'Offered lookup entries share a name (%). Retire the duplicates on /cities or /car-types, then apply this migration again.', found;
        END IF;
    END $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008113858_OfferedLookupNamesAreUnique') THEN
    CREATE UNIQUE INDEX ux_cities_offered_name_en ON cities (lower(regexp_replace(name_en, '[\u064B-\u0652\u0640]', '', 'g'))) WHERE is_active;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008113858_OfferedLookupNamesAreUnique') THEN
    CREATE UNIQUE INDEX ux_cities_offered_name_ar ON cities (lower(regexp_replace(name_ar, '[\u064B-\u0652\u0640]', '', 'g'))) WHERE is_active;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008113858_OfferedLookupNamesAreUnique') THEN
    CREATE UNIQUE INDEX ux_car_types_offered_name_en ON car_types (lower(regexp_replace(name_en, '[\u064B-\u0652\u0640]', '', 'g'))) WHERE is_active;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008113858_OfferedLookupNamesAreUnique') THEN
    CREATE UNIQUE INDEX ux_car_types_offered_name_ar ON car_types (lower(regexp_replace(name_ar, '[\u064B-\u0652\u0640]', '', 'g'))) WHERE is_active;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008113858_OfferedLookupNamesAreUnique') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261008113858_OfferedLookupNamesAreUnique', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008115610_FinancialDocumentRenditionHolds') THEN
    CREATE TABLE financial_document_rendition_holds (
        id uuid NOT NULL,
        document_id uuid NOT NULL,
        language character varying(2) NOT NULL,
        kind character varying(10) NOT NULL,
        reason character varying(30) NOT NULL,
        attempts integer NOT NULL,
        first_failed_at timestamp with time zone NOT NULL,
        last_failed_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT pk_financial_document_rendition_holds PRIMARY KEY (id),
        CONSTRAINT ck_financial_document_rendition_holds_attempts CHECK (attempts >= 1),
        CONSTRAINT fk_financial_document_rendition_holds_financial_documents_docu FOREIGN KEY (document_id) REFERENCES financial_documents (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008115610_FinancialDocumentRenditionHolds') THEN
    CREATE UNIQUE INDEX ix_financial_document_rendition_holds_document_id_language_kind ON financial_document_rendition_holds (document_id, language, kind);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008115610_FinancialDocumentRenditionHolds') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261008115610_FinancialDocumentRenditionHolds', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008122550_ArabicAuditSubjectAndNotificationStandIn') THEN
    ALTER TABLE notifications ADD actor_stand_in character varying(20);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008122550_ArabicAuditSubjectAndNotificationStandIn') THEN
    ALTER TABLE audit_entries ADD subject_label_ar character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261008122550_ArabicAuditSubjectAndNotificationStandIn') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261008122550_ArabicAuditSubjectAndNotificationStandIn', '10.0.11');
    END IF;
END $EF$;
COMMIT;

