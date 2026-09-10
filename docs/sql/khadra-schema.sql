CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902213733_InitialIdentityAccess') THEN
    CREATE TABLE users (
        id uuid NOT NULL,
        email character varying(256) NOT NULL,
        phone character varying(20) NOT NULL,
        name character varying(150) NOT NULL,
        password_hash character varying(100) NOT NULL,
        role character varying(20) NOT NULL,
        status character varying(20) NOT NULL,
        is_email_verified boolean NOT NULL,
        email_verified_at timestamp with time zone,
        must_change_password boolean NOT NULL,
        security_stamp uuid NOT NULL,
        last_login_at timestamp with time zone,
        password_changed_at timestamp with time zone,
        suspension_reason character varying(500),
        created_at timestamp with time zone NOT NULL,
        is_deleted boolean NOT NULL,
        deleted_at timestamp with time zone,
        updated_at timestamp with time zone,
        CONSTRAINT pk_users PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902213733_InitialIdentityAccess') THEN
    CREATE TABLE refresh_tokens (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        family_id uuid NOT NULL,
        token_hash character varying(64) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        family_expires_at timestamp with time zone NOT NULL,
        revoked_at timestamp with time zone,
        replaced_by_token_id uuid,
        created_by_ip character varying(45),
        user_agent character varying(256),
        updated_at timestamp with time zone,
        CONSTRAINT pk_refresh_tokens PRIMARY KEY (id),
        CONSTRAINT fk_refresh_tokens_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902213733_InitialIdentityAccess') THEN
    CREATE TABLE verification_tokens (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        purpose character varying(30) NOT NULL,
        token_hash character varying(64) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        consumed_at timestamp with time zone,
        updated_at timestamp with time zone,
        CONSTRAINT pk_verification_tokens PRIMARY KEY (id),
        CONSTRAINT fk_verification_tokens_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902213733_InitialIdentityAccess') THEN
    CREATE INDEX ix_refresh_tokens_family_id ON refresh_tokens (family_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902213733_InitialIdentityAccess') THEN
    CREATE UNIQUE INDEX ix_refresh_tokens_token_hash ON refresh_tokens (token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902213733_InitialIdentityAccess') THEN
    CREATE INDEX ix_refresh_tokens_user_id ON refresh_tokens (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902213733_InitialIdentityAccess') THEN
    CREATE UNIQUE INDEX ix_users_email ON users (email);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902213733_InitialIdentityAccess') THEN
    CREATE UNIQUE INDEX ix_users_phone ON users (phone);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902213733_InitialIdentityAccess') THEN
    CREATE INDEX ix_users_role ON users (role);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902213733_InitialIdentityAccess') THEN
    CREATE UNIQUE INDEX ix_verification_tokens_token_hash ON verification_tokens (token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902213733_InitialIdentityAccess') THEN
    CREATE INDEX ix_verification_tokens_user_id_purpose ON verification_tokens (user_id, purpose);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260902213733_InitialIdentityAccess') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260902213733_InitialIdentityAccess', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE TABLE audit_entries (
        id uuid NOT NULL,
        occurred_at timestamp with time zone NOT NULL,
        actor_user_id uuid,
        actor_name character varying(200) NOT NULL,
        actor_role character varying(20),
        action character varying(40) NOT NULL,
        entity_type character varying(20) NOT NULL,
        entity_id uuid,
        subject_label character varying(200) NOT NULL,
        previous_value character varying(400),
        new_value character varying(400),
        reason character varying(1000),
        correlation_id character varying(64),
        updated_at timestamp with time zone,
        CONSTRAINT pk_audit_entries PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE TABLE bookings (
        id uuid NOT NULL,
        reference character varying(20) NOT NULL,
        customer_id uuid NOT NULL,
        dealer_id uuid NOT NULL,
        vehicle_id uuid NOT NULL,
        period_start timestamp with time zone NOT NULL,
        period_end timestamp with time zone NOT NULL,
        pickup_method character varying(20) NOT NULL,
        delivery_latitude double precision,
        delivery_longitude double precision,
        payment_option character varying(20) NOT NULL,
        status character varying(20) NOT NULL,
        deposit_payment_id uuid,
        acted_by_user_id uuid,
        cancelled_by character varying(20),
        cancellation_reason character varying(1000),
        extended_from_booking_id uuid,
        created_at timestamp with time zone NOT NULL,
        payment_deadline timestamp with time zone NOT NULL,
        requested_at timestamp with time zone,
        approved_at timestamp with time zone,
        free_cancellation_deadline timestamp with time zone,
        picked_up_at timestamp with time zone,
        returned_at timestamp with time zone,
        finished_at timestamp with time zone,
        updated_at timestamp with time zone,
        penalty jsonb,
        pricing jsonb NOT NULL,
        terms jsonb NOT NULL,
        CONSTRAINT pk_bookings PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE TABLE dealers (
        id uuid NOT NULL,
        owner_user_id uuid NOT NULL,
        business_name character varying(150) NOT NULL,
        description character varying(2000),
        commercial_registration character varying(20) NOT NULL,
        latitude double precision NOT NULL,
        longitude double precision NOT NULL,
        city_id uuid,
        operating_hours character varying(160) NOT NULL,
        verification_status character varying(30) NOT NULL,
        review_note character varying(1000),
        reviewed_by_admin_id uuid,
        reviewed_at timestamp with time zone,
        submitted_at timestamp with time zone NOT NULL,
        review_due_at timestamp with time zone NOT NULL,
        delivery_enabled boolean NOT NULL,
        delivery_radius_km numeric(6,2) NOT NULL,
        logo_storage_key character varying(500),
        cover_storage_key character varying(500),
        is_suspended boolean NOT NULL,
        suspension_reason character varying(1000),
        created_at timestamp with time zone NOT NULL,
        is_deleted boolean NOT NULL,
        deleted_at timestamp with time zone,
        updated_at timestamp with time zone,
        CONSTRAINT pk_dealers PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE TABLE dispute_tickets (
        id uuid NOT NULL,
        booking_id uuid NOT NULL,
        opened_by_user_id uuid NOT NULL,
        opened_by_party character varying(20) NOT NULL,
        reason character varying(2000) NOT NULL,
        status character varying(20) NOT NULL,
        opened_at timestamp with time zone NOT NULL,
        sla_deadline timestamp with time zone NOT NULL,
        assigned_admin_id uuid,
        closed_at timestamp with time zone,
        updated_at timestamp with time zone,
        resolution jsonb,
        CONSTRAINT pk_dispute_tickets PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE TABLE booking_handovers (
        id uuid NOT NULL,
        booking_id uuid NOT NULL,
        type character varying(20) NOT NULL,
        recorded_by character varying(20) NOT NULL,
        recorded_by_user_id uuid NOT NULL,
        odometer_km integer,
        fuel_level numeric(4,3),
        notes character varying(2000),
        cash_collected_amount numeric(18,3),
        cash_collected_currency character varying(3),
        recorded_at timestamp with time zone NOT NULL,
        photo_storage_keys text[] NOT NULL,
        CONSTRAINT pk_booking_handovers PRIMARY KEY (id),
        CONSTRAINT fk_booking_handovers_bookings_booking_id FOREIGN KEY (booking_id) REFERENCES bookings (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE TABLE booking_status_changes (
        id uuid NOT NULL,
        booking_id uuid NOT NULL,
        "from" character varying(20),
        "to" character varying(20) NOT NULL,
        actor_party character varying(20) NOT NULL,
        actor_user_id uuid,
        reason character varying(1000),
        occurred_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_booking_status_changes PRIMARY KEY (id),
        CONSTRAINT fk_booking_status_changes_bookings_booking_id FOREIGN KEY (booking_id) REFERENCES bookings (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE TABLE dealer_documents (
        id uuid NOT NULL,
        dealer_id uuid NOT NULL,
        type character varying(30) NOT NULL,
        storage_key character varying(500) NOT NULL,
        uploaded_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_dealer_documents PRIMARY KEY (id),
        CONSTRAINT fk_dealer_documents_dealers_dealer_id FOREIGN KEY (dealer_id) REFERENCES dealers (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE TABLE dealer_employees (
        id uuid NOT NULL,
        dealer_id uuid NOT NULL,
        user_id uuid NOT NULL,
        can_view_reports boolean NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        deactivated_at timestamp with time zone,
        CONSTRAINT pk_dealer_employees PRIMARY KEY (id),
        CONSTRAINT fk_dealer_employees_dealers_dealer_id FOREIGN KEY (dealer_id) REFERENCES dealers (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE TABLE dispute_statements (
        id uuid NOT NULL,
        ticket_id uuid NOT NULL,
        party character varying(20) NOT NULL,
        author_user_id uuid NOT NULL,
        body character varying(4000) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        evidence_storage_keys text[] NOT NULL,
        CONSTRAINT pk_dispute_statements PRIMARY KEY (id),
        CONSTRAINT fk_dispute_statements_dispute_tickets_ticket_id FOREIGN KEY (ticket_id) REFERENCES dispute_tickets (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE INDEX ix_audit_entries_actor_user_id ON audit_entries (actor_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE INDEX ix_audit_entries_entity_type_entity_id ON audit_entries (entity_type, entity_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE INDEX ix_audit_entries_occurred_at ON audit_entries (occurred_at DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE UNIQUE INDEX ix_booking_handovers_booking_id_type ON booking_handovers (booking_id, type);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE INDEX ix_booking_status_changes_booking_id_occurred_at ON booking_status_changes (booking_id, occurred_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE INDEX ix_bookings_created_at ON bookings (created_at DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE INDEX ix_bookings_customer_id ON bookings (customer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE INDEX ix_bookings_dealer_id ON bookings (dealer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE UNIQUE INDEX ix_bookings_reference ON bookings (reference);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE INDEX ix_bookings_status ON bookings (status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE UNIQUE INDEX ix_dealer_documents_dealer_id_type ON dealer_documents (dealer_id, type);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE UNIQUE INDEX ix_dealer_employees_dealer_id_user_id ON dealer_employees (dealer_id, user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE UNIQUE INDEX ix_dealers_commercial_registration ON dealers (commercial_registration);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE INDEX ix_dealers_owner_user_id ON dealers (owner_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE INDEX ix_dealers_review_due_at ON dealers (review_due_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE INDEX ix_dealers_verification_status ON dealers (verification_status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE INDEX ix_dispute_statements_ticket_id_created_at ON dispute_statements (ticket_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE INDEX ix_dispute_tickets_booking_id ON dispute_tickets (booking_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE INDEX ix_dispute_tickets_sla_deadline ON dispute_tickets (sla_deadline);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    CREATE INDEX ix_dispute_tickets_status ON dispute_tickets (status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903081830_DealersBookingsDisputesAndAuditing') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260903081830_DealersBookingsDisputesAndAuditing', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903082022_AuditTrailImmutability') THEN

    CREATE OR REPLACE FUNCTION khadra_audit_entries_are_append_only()
    RETURNS TRIGGER AS $$
    BEGIN
        RAISE EXCEPTION 'audit_entries is append-only: % is not permitted', TG_OP;
    END;
    $$ LANGUAGE plpgsql;

    CREATE TRIGGER audit_entries_append_only
    BEFORE UPDATE OR DELETE ON audit_entries
    FOR EACH ROW EXECUTE FUNCTION khadra_audit_entries_are_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903082022_AuditTrailImmutability') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260903082022_AuditTrailImmutability', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903095711_CustomerDocumentsAndRenterAge') THEN
    ALTER TABLE users ADD date_of_birth date;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903095711_CustomerDocumentsAndRenterAge') THEN
    ALTER TABLE users ADD is_foreign_national boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903095711_CustomerDocumentsAndRenterAge') THEN
    CREATE TABLE customer_documents (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        type character varying(30) NOT NULL,
        status character varying(20) NOT NULL,
        storage_key character varying(500) NOT NULL,
        content_type character varying(100) NOT NULL,
        size_bytes bigint NOT NULL,
        uploaded_at timestamp with time zone NOT NULL,
        review_note character varying(500),
        CONSTRAINT pk_customer_documents PRIMARY KEY (id),
        CONSTRAINT fk_customer_documents_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903095711_CustomerDocumentsAndRenterAge') THEN
    CREATE UNIQUE INDEX ix_customer_documents_user_id_type ON customer_documents (user_id, type);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903095711_CustomerDocumentsAndRenterAge') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260903095711_CustomerDocumentsAndRenterAge', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903113240_FleetVehicles') THEN
    CREATE TABLE vehicles (
        id uuid NOT NULL,
        dealer_id uuid NOT NULL,
        car_type_id uuid NOT NULL,
        make character varying(60) NOT NULL,
        model character varying(60) NOT NULL,
        year integer NOT NULL,
        color character varying(40),
        seats integer NOT NULL,
        transmission character varying(20) NOT NULL,
        fuel_type character varying(20) NOT NULL,
        description character varying(2000),
        plate_number character varying(10) NOT NULL,
        daily_rate numeric(18,3) NOT NULL,
        daily_rate_currency character varying(3) NOT NULL,
        security_deposit numeric(18,3) NOT NULL,
        security_deposit_currency character varying(3) NOT NULL,
        is_delivery_eligible boolean NOT NULL,
        mileage_unlimited boolean NOT NULL,
        mileage_daily_limit_km integer,
        mileage_excess_fee numeric(18,3),
        mileage_excess_currency character varying(3),
        fuel_policy character varying(20) NOT NULL,
        status character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        is_deleted boolean NOT NULL,
        deleted_at timestamp with time zone,
        updated_at timestamp with time zone,
        CONSTRAINT pk_vehicles PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903113240_FleetVehicles') THEN
    CREATE TABLE vehicle_images (
        id uuid NOT NULL,
        vehicle_id uuid NOT NULL,
        storage_key character varying(500) NOT NULL,
        position integer NOT NULL,
        is_primary boolean NOT NULL,
        uploaded_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_vehicle_images PRIMARY KEY (id),
        CONSTRAINT fk_vehicle_images_vehicles_vehicle_id FOREIGN KEY (vehicle_id) REFERENCES vehicles (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903113240_FleetVehicles') THEN
    CREATE INDEX ix_vehicle_images_vehicle_id_position ON vehicle_images (vehicle_id, position);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903113240_FleetVehicles') THEN
    CREATE INDEX ix_vehicles_dealer_id ON vehicles (dealer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903113240_FleetVehicles') THEN
    CREATE UNIQUE INDEX ix_vehicles_plate_number ON vehicles (plate_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903113240_FleetVehicles') THEN
    CREATE INDEX ix_vehicles_status ON vehicles (status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903113240_FleetVehicles') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260903113240_FleetVehicles', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903124429_FrozenPercentagesAndDisputeIntegrity') THEN
    DROP INDEX ix_dispute_tickets_booking_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903124429_FrozenPercentagesAndDisputeIntegrity') THEN
    CREATE UNIQUE INDEX ix_dispute_tickets_booking_id ON dispute_tickets (booking_id) WHERE status IN ('Open', 'UnderReview');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903124429_FrozenPercentagesAndDisputeIntegrity') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260903124429_FrozenPercentagesAndDisputeIntegrity', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903142352_FrozenTermsFullyPersisted') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260903142352_FrozenTermsFullyPersisted', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260903164245_DealerConcurrencyToken') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260903164245_DealerConcurrencyToken', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904013652_AuditLogIndexes') THEN
    DROP INDEX ix_audit_entries_actor_user_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904013652_AuditLogIndexes') THEN
    DROP INDEX ix_audit_entries_occurred_at;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904013652_AuditLogIndexes') THEN
    CREATE INDEX ix_audit_entries_action_occurred_at_id ON audit_entries (action, occurred_at DESC, id DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904013652_AuditLogIndexes') THEN
    CREATE INDEX ix_audit_entries_actor_user_id_occurred_at_id ON audit_entries (actor_user_id, occurred_at DESC, id DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904013652_AuditLogIndexes') THEN
    CREATE INDEX ix_audit_entries_occurred_at_id ON audit_entries (occurred_at DESC, id DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904013652_AuditLogIndexes') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260904013652_AuditLogIndexes', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904143411_AddLookupTables') THEN
    CREATE TABLE car_types (
        id uuid NOT NULL,
        updated_at timestamp with time zone,
        name_en character varying(100) NOT NULL,
        name_ar character varying(100) NOT NULL,
        is_active boolean NOT NULL,
        display_order integer NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_car_types PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904143411_AddLookupTables') THEN
    CREATE TABLE cities (
        id uuid NOT NULL,
        centre_latitude double precision,
        centre_longitude double precision,
        updated_at timestamp with time zone,
        name_en character varying(100) NOT NULL,
        name_ar character varying(100) NOT NULL,
        is_active boolean NOT NULL,
        display_order integer NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_cities PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904143411_AddLookupTables') THEN
    CREATE INDEX ix_car_types_display_order_name_en ON car_types (display_order, name_en);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904143411_AddLookupTables') THEN
    CREATE INDEX ix_cities_display_order_name_en ON cities (display_order, name_en);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260904143411_AddLookupTables') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260904143411_AddLookupTables', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905164859_OneDealerPerOwner') THEN
    DROP INDEX ix_dealers_owner_user_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905164859_OneDealerPerOwner') THEN
    CREATE UNIQUE INDEX ix_dealers_owner_user_id ON dealers (owner_user_id) WHERE is_deleted = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905164859_OneDealerPerOwner') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260905164859_OneDealerPerOwner', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905214714_DealerSetsItsOwnDeliveryFee') THEN
    ALTER TABLE dealers ADD delivery_fee_amount numeric(18,3);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905214714_DealerSetsItsOwnDeliveryFee') THEN
    ALTER TABLE dealers ADD delivery_fee_currency character varying(3);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905214714_DealerSetsItsOwnDeliveryFee') THEN
    UPDATE dealers
    SET delivery_fee_amount = 10.000, delivery_fee_currency = 'JOD'
    WHERE delivery_enabled = true AND delivery_fee_amount IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260905214714_DealerSetsItsOwnDeliveryFee') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260905214714_DealerSetsItsOwnDeliveryFee', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260906024702_Notifications') THEN
    CREATE TABLE notifications (
        id uuid NOT NULL,
        recipient_user_id uuid NOT NULL,
        kind character varying(40) NOT NULL,
        subject_id uuid,
        subject_reference character varying(50),
        actor_user_id uuid,
        actor_name character varying(150) NOT NULL,
        occurred_at timestamp with time zone NOT NULL,
        read_at timestamp with time zone,
        is_deleted boolean NOT NULL,
        deleted_at timestamp with time zone,
        updated_at timestamp with time zone,
        CONSTRAINT pk_notifications PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260906024702_Notifications') THEN
    CREATE INDEX ix_notifications_recipient_user_id_occurred_at_id ON notifications (recipient_user_id, occurred_at DESC, id DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260906024702_Notifications') THEN
    CREATE INDEX ix_notifications_recipient_user_id_read_at ON notifications (recipient_user_id, read_at) WHERE read_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260906024702_Notifications') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260906024702_Notifications', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260907021642_CalendarDaysAndVehicleHolds') THEN
    ALTER TABLE bookings ADD hold_start timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260907021642_CalendarDaysAndVehicleHolds') THEN

    UPDATE bookings SET hold_start = period_start WHERE hold_start IS NULL;
    ALTER TABLE bookings ALTER COLUMN hold_start SET NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260907021642_CalendarDaysAndVehicleHolds') THEN
    CREATE INDEX ix_bookings_vehicle_id_hold_start ON bookings (vehicle_id, hold_start);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260907021642_CalendarDaysAndVehicleHolds') THEN

    CREATE EXTENSION IF NOT EXISTS btree_gist;

    ALTER TABLE bookings ADD CONSTRAINT bookings_one_hold_per_vehicle
      EXCLUDE USING gist (
        vehicle_id WITH =,
        tstzrange(hold_start, period_end, '[)') WITH &&
      )
      WHERE (status IN ('PendingPayment', 'Requested', 'Approved', 'PickedUp'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260907021642_CalendarDaysAndVehicleHolds') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260907021642_CalendarDaysAndVehicleHolds', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260907121340_ReserveNowPayAfterApproval') THEN

    DO $$
    DECLARE stranded int; paid_requests int;
    BEGIN
      SELECT count(*) INTO stranded FROM bookings WHERE status = 'PendingPayment';
      IF stranded > 0 THEN
        RAISE EXCEPTION
          'Cannot apply ReserveNowPayAfterApproval: % booking(s) are still PendingPayment, a status this migration removes. Decide what each one is -- expire it (status Expired, with a penalty assessment) or let the dealer answer it (status Requested) -- then re-run.', stranded;
      END IF;

      SELECT count(*) INTO paid_requests FROM bookings WHERE status = 'Requested' AND deposit_payment_id IS NOT NULL;
      IF paid_requests > 0 THEN
        RAISE EXCEPTION
          'Cannot apply ReserveNowPayAfterApproval: % request(s) already carry a deposit, which the new order cannot express -- Requested now means unpaid. Decide each by hand (approve and confirm it, or refund and expire it) then re-run.', paid_requests;
      END IF;
    END $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260907121340_ReserveNowPayAfterApproval') THEN
    ALTER TABLE bookings ALTER COLUMN payment_deadline DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260907121340_ReserveNowPayAfterApproval') THEN
    ALTER TABLE bookings ADD decision_deadline timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260907121340_ReserveNowPayAfterApproval') THEN

    UPDATE bookings SET decision_deadline = period_start WHERE decision_deadline IS NULL;
    ALTER TABLE bookings ALTER COLUMN decision_deadline SET NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260907121340_ReserveNowPayAfterApproval') THEN

    ALTER TABLE bookings DROP CONSTRAINT IF EXISTS bookings_one_hold_per_vehicle;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260907121340_ReserveNowPayAfterApproval') THEN

    UPDATE bookings SET status = 'Confirmed'
    WHERE status = 'Approved' AND deposit_payment_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260907121340_ReserveNowPayAfterApproval') THEN

    ALTER TABLE bookings ADD CONSTRAINT bookings_one_hold_per_vehicle
      EXCLUDE USING gist (
        vehicle_id WITH =,
        tstzrange(hold_start, period_end, '[)') WITH &&
      )
      WHERE (status IN ('Requested', 'Approved', 'Confirmed', 'PickedUp'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260907121340_ReserveNowPayAfterApproval') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260907121340_ReserveNowPayAfterApproval', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260907212741_ReviewsAndCustomerCancellation') THEN
    ALTER TABLE bookings ADD cancellation_reason_code character varying(40);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260907212741_ReviewsAndCustomerCancellation') THEN
    ALTER TABLE booking_status_changes ADD reason_code character varying(40);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260907212741_ReviewsAndCustomerCancellation') THEN
    CREATE TABLE reviews (
        id uuid NOT NULL,
        booking_id uuid NOT NULL,
        direction character varying(30) NOT NULL,
        reviewer_user_id uuid NOT NULL,
        subject_id uuid NOT NULL,
        rating integer NOT NULL,
        comment character varying(2000),
        is_hidden boolean NOT NULL,
        hidden_reason character varying(500),
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT pk_reviews PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260907212741_ReviewsAndCustomerCancellation') THEN
    CREATE UNIQUE INDEX ix_reviews_booking_id_direction ON reviews (booking_id, direction);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260907212741_ReviewsAndCustomerCancellation') THEN
    CREATE INDEX ix_reviews_subject_id_direction_created_at ON reviews (subject_id, direction, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260907212741_ReviewsAndCustomerCancellation') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260907212741_ReviewsAndCustomerCancellation', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908153448_Payments') THEN
    CREATE TABLE payment_provider_events (
        id uuid NOT NULL,
        provider character varying(30) NOT NULL,
        provider_event_id character varying(200) NOT NULL,
        provider_reference character varying(200),
        kind character varying(40) NOT NULL,
        payment_id uuid,
        outcome character varying(20) NOT NULL,
        amount numeric(18,3),
        currency_code character varying(3),
        received_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT pk_payment_provider_events PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908153448_Payments') THEN
    CREATE TABLE payments (
        id uuid NOT NULL,
        booking_id uuid NOT NULL,
        customer_id uuid NOT NULL,
        amount numeric(18,3) NOT NULL,
        currency character varying(3) NOT NULL,
        status character varying(20) NOT NULL,
        provider character varying(30) NOT NULL,
        provider_reference character varying(200),
        checkout_url character varying(2000),
        expires_at timestamp with time zone NOT NULL,
        amount_captured numeric(18,3),
        captured_currency character varying(3),
        captured_at timestamp with time zone,
        applied_at timestamp with time zone,
        orphaned_at timestamp with time zone,
        failed_at timestamp with time zone,
        failure_code character varying(100),
        orphan_reason character varying(100),
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT pk_payments PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908153448_Payments') THEN
    CREATE TABLE payment_refunds (
        id uuid NOT NULL,
        payment_id uuid NOT NULL,
        amount numeric(18,3) NOT NULL,
        currency character varying(3) NOT NULL,
        reason character varying(30) NOT NULL,
        dispute_ticket_id uuid,
        status character varying(20) NOT NULL,
        provider_reference character varying(200),
        failure_code character varying(100),
        requested_at timestamp with time zone NOT NULL,
        sent_at timestamp with time zone,
        settled_at timestamp with time zone,
        failed_at timestamp with time zone,
        updated_at timestamp with time zone,
        CONSTRAINT pk_payment_refunds PRIMARY KEY (id),
        CONSTRAINT fk_payment_refunds_payments_payment_id FOREIGN KEY (payment_id) REFERENCES payments (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908153448_Payments') THEN
    CREATE INDEX ix_payment_provider_events_payment_id ON payment_provider_events (payment_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908153448_Payments') THEN
    CREATE UNIQUE INDEX ix_payment_provider_events_provider_provider_event_id ON payment_provider_events (provider, provider_event_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908153448_Payments') THEN
    CREATE INDEX ix_payment_refunds_payment_id ON payment_refunds (payment_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908153448_Payments') THEN
    CREATE INDEX ix_payment_refunds_status_requested_at ON payment_refunds (status, requested_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908153448_Payments') THEN
    CREATE UNIQUE INDEX ix_payments_provider_provider_reference ON payments (provider, provider_reference) WHERE provider_reference IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908153448_Payments') THEN
    CREATE INDEX ix_payments_status_expires_at ON payments (status, expires_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908153448_Payments') THEN
    CREATE UNIQUE INDEX ux_payments_one_live_attempt_per_booking ON payments (booking_id) WHERE status IN ('Initiated', 'Pending');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908153448_Payments') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260908153448_Payments', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908161331_ReviewVisibility') THEN
    DROP INDEX ix_reviews_subject_id_direction_created_at;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908161331_ReviewVisibility') THEN
    ALTER TABLE reviews ADD visible_from timestamp with time zone NOT NULL DEFAULT TIMESTAMPTZ '-infinity';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908161331_ReviewVisibility') THEN
    UPDATE reviews SET visible_from = created_at;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908161331_ReviewVisibility') THEN
    CREATE INDEX ix_reviews_subject_id_direction_visible_from ON reviews (subject_id, direction, visible_from);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260908161331_ReviewVisibility') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260908161331_ReviewVisibility', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260910023659_DealerAddress') THEN
    ALTER TABLE dealers ADD address_area character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260910023659_DealerAddress') THEN
    ALTER TABLE dealers ADD address_street character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260910023659_DealerAddress') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260910023659_DealerAddress', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260910204753_RenterDocumentReviewAndAccessLog') THEN
    CREATE TABLE document_access_entries (
        id uuid NOT NULL,
        occurred_at timestamp with time zone NOT NULL,
        actor_user_id uuid NOT NULL,
        actor_name character varying(200) NOT NULL,
        actor_role character varying(20) NOT NULL,
        dealer_id uuid NOT NULL,
        booking_id uuid NOT NULL,
        subject_user_id uuid NOT NULL,
        document_id uuid NOT NULL,
        document_type character varying(30) NOT NULL,
        document_uploaded_at timestamp with time zone NOT NULL,
        action character varying(20) NOT NULL,
        correlation_id character varying(64),
        updated_at timestamp with time zone,
        CONSTRAINT pk_document_access_entries PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260910204753_RenterDocumentReviewAndAccessLog') THEN
    CREATE TABLE renter_document_reviews (
        id uuid NOT NULL,
        booking_id uuid NOT NULL,
        document_id uuid NOT NULL,
        document_type character varying(30) NOT NULL,
        document_uploaded_at timestamp with time zone NOT NULL,
        reviewed_by_user_id uuid NOT NULL,
        reviewed_by_name character varying(200) NOT NULL,
        reviewed_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_renter_document_reviews PRIMARY KEY (id),
        CONSTRAINT fk_renter_document_reviews_bookings_booking_id FOREIGN KEY (booking_id) REFERENCES bookings (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260910204753_RenterDocumentReviewAndAccessLog') THEN
    CREATE INDEX ix_document_access_entries_booking_id_occurred_at ON document_access_entries (booking_id, occurred_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260910204753_RenterDocumentReviewAndAccessLog') THEN
    CREATE INDEX ix_document_access_entries_dealer_id_occurred_at_id ON document_access_entries (dealer_id, occurred_at DESC, id DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260910204753_RenterDocumentReviewAndAccessLog') THEN
    CREATE INDEX ix_document_access_entries_subject_user_id_occurred_at_id ON document_access_entries (subject_user_id, occurred_at DESC, id DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260910204753_RenterDocumentReviewAndAccessLog') THEN
    CREATE UNIQUE INDEX ix_renter_document_reviews_booking_id_document_id_document_upl ON renter_document_reviews (booking_id, document_id, document_uploaded_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260910204753_RenterDocumentReviewAndAccessLog') THEN

    CREATE OR REPLACE FUNCTION khadra_table_is_append_only()
    RETURNS TRIGGER AS $$
    BEGIN
        RAISE EXCEPTION '%.% is append-only: % is not permitted', TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP;
    END;
    $$ LANGUAGE plpgsql;

    CREATE TRIGGER document_access_entries_append_only
    BEFORE UPDATE OR DELETE ON document_access_entries
    FOR EACH ROW EXECUTE FUNCTION khadra_table_is_append_only();

    CREATE TRIGGER document_access_entries_no_truncate
    BEFORE TRUNCATE ON document_access_entries
    FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260910204753_RenterDocumentReviewAndAccessLog') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260910204753_RenterDocumentReviewAndAccessLog', '10.0.11');
    END IF;
END $EF$;
COMMIT;

