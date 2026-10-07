START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006235151_PaymentCaptureIncidentsAndRefundBackoff') THEN
    ALTER TABLE payments ADD provider_capture_reference character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006235151_PaymentCaptureIncidentsAndRefundBackoff') THEN
    ALTER TABLE payment_refunds ADD next_attempt_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006235151_PaymentCaptureIncidentsAndRefundBackoff') THEN
    ALTER TABLE payment_refunds ADD refusal_count integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006235151_PaymentCaptureIncidentsAndRefundBackoff') THEN
    UPDATE payment_refunds SET refusal_count = 1 WHERE status = 'Failed' AND refusal_count = 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006235151_PaymentCaptureIncidentsAndRefundBackoff') THEN
    ALTER TABLE payment_provider_events ADD capture_reference character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006235151_PaymentCaptureIncidentsAndRefundBackoff') THEN
    CREATE TABLE payment_incidents (
        id uuid NOT NULL,
        kind character varying(30) NOT NULL,
        payment_id uuid NOT NULL,
        receipt_id uuid NOT NULL,
        provider character varying(30) NOT NULL,
        capture_reference character varying(200),
        reported_amount numeric(18,3) NOT NULL,
        reported_currency character varying(3) NOT NULL,
        expected_amount numeric(18,3) NOT NULL,
        expected_currency character varying(3) NOT NULL,
        other_payment_id uuid,
        detected_at timestamp with time zone NOT NULL,
        handled_at timestamp with time zone,
        handled_by_admin_id uuid,
        handled_note character varying(500),
        updated_at timestamp with time zone,
        CONSTRAINT pk_payment_incidents PRIMARY KEY (id),
        CONSTRAINT fk_payment_incidents_payment_provider_events_receipt_id FOREIGN KEY (receipt_id) REFERENCES payment_provider_events (id) ON DELETE RESTRICT,
        CONSTRAINT fk_payment_incidents_payments_payment_id FOREIGN KEY (payment_id) REFERENCES payments (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006235151_PaymentCaptureIncidentsAndRefundBackoff') THEN
    CREATE UNIQUE INDEX ux_payments_provider_capture_reference ON payments (provider, provider_capture_reference) WHERE provider_capture_reference IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006235151_PaymentCaptureIncidentsAndRefundBackoff') THEN
    CREATE INDEX ix_payment_incidents_detected_at ON payment_incidents (detected_at) WHERE handled_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006235151_PaymentCaptureIncidentsAndRefundBackoff') THEN
    CREATE INDEX ix_payment_incidents_payment_id ON payment_incidents (payment_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006235151_PaymentCaptureIncidentsAndRefundBackoff') THEN
    CREATE UNIQUE INDEX ix_payment_incidents_receipt_id ON payment_incidents (receipt_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261006235151_PaymentCaptureIncidentsAndRefundBackoff') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261006235151_PaymentCaptureIncidentsAndRefundBackoff', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007001707_BookingDisputeWindowEnd') THEN
    ALTER TABLE bookings ADD dispute_window_ends_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007001707_BookingDisputeWindowEnd') THEN
    CREATE INDEX ix_bookings_status_dispute_window_ends_at ON bookings (status, dispute_window_ends_at) WHERE dispute_window_ends_at IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007001707_BookingDisputeWindowEnd') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261007001707_BookingDisputeWindowEnd', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007025537_LegalConsents') THEN
    CREATE TABLE legal_consents (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        document_version_id uuid NOT NULL,
        occurred_at timestamp with time zone NOT NULL,
        channel character varying(20) NOT NULL,
        language character varying(2) NOT NULL,
        action character varying(20) NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT pk_legal_consents PRIMARY KEY (id),
        CONSTRAINT ck_legal_consents_action CHECK (action IN ('Accepted')),
        CONSTRAINT ck_legal_consents_channel CHECK (channel IN ('Website', 'App', 'Console')),
        CONSTRAINT ck_legal_consents_language CHECK (language IN ('ar', 'en')),
        CONSTRAINT fk_legal_consents_legal_document_versions_document_version_id FOREIGN KEY (document_version_id) REFERENCES legal_document_versions (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007025537_LegalConsents') THEN
    CREATE INDEX ix_legal_consents_document_version_id ON legal_consents (document_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007025537_LegalConsents') THEN
    CREATE INDEX ix_legal_consents_user_id_document_version_id ON legal_consents (user_id, document_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007025537_LegalConsents') THEN
    CREATE INDEX ix_legal_consents_user_id_occurred_at ON legal_consents (user_id, occurred_at DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007025537_LegalConsents') THEN

    CREATE TRIGGER legal_consents_append_only
    BEFORE UPDATE OR DELETE ON legal_consents
    FOR EACH ROW EXECUTE FUNCTION khadra_table_is_append_only();

    CREATE TRIGGER legal_consents_no_truncate
    BEFORE TRUNCATE ON legal_consents
    FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007025537_LegalConsents') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261007025537_LegalConsents', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007032513_AdminDocumentAccess') THEN
    ALTER TABLE document_access_entries ALTER COLUMN dealer_id DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007032513_AdminDocumentAccess') THEN
    ALTER TABLE document_access_entries ALTER COLUMN booking_id DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007032513_AdminDocumentAccess') THEN
    ALTER TABLE document_access_entries ADD CONSTRAINT ck_document_access_entries_scope CHECK ((dealer_id IS NOT NULL AND booking_id IS NOT NULL) OR actor_role = 'Admin');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261007032513_AdminDocumentAccess') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261007032513_AdminDocumentAccess', '10.0.11');
    END IF;
END $EF$;
COMMIT;

