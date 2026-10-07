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

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260911000801_AddCustomerShortlist') THEN
    CREATE TABLE customer_shortlists (
        id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT pk_customer_shortlists PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260911000801_AddCustomerShortlist') THEN
    CREATE TABLE shortlist_entries (
        id uuid NOT NULL,
        shortlist_id uuid NOT NULL,
        vehicle_id uuid NOT NULL,
        saved_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_shortlist_entries PRIMARY KEY (id),
        CONSTRAINT fk_shortlist_entries_customer_shortlists_shortlist_id FOREIGN KEY (shortlist_id) REFERENCES customer_shortlists (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260911000801_AddCustomerShortlist') THEN
    CREATE INDEX ix_shortlist_entries_shortlist_id_saved_at ON shortlist_entries (shortlist_id, saved_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260911000801_AddCustomerShortlist') THEN
    CREATE UNIQUE INDEX ix_shortlist_entries_shortlist_id_vehicle_id ON shortlist_entries (shortlist_id, vehicle_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260911000801_AddCustomerShortlist') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260911000801_AddCustomerShortlist', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260917002143_DealerPublicProfile') THEN
    ALTER TABLE dealers ADD customer_notes character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260917002143_DealerPublicProfile') THEN
    ALTER TABLE dealers ADD delivery_notes character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260917002143_DealerPublicProfile') THEN
    ALTER TABLE dealers ADD hidden_profile_sections character varying(200) NOT NULL DEFAULT ('');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260917002143_DealerPublicProfile') THEN
    ALTER TABLE dealers ADD insurance_summary character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260917002143_DealerPublicProfile') THEN
    ALTER TABLE dealers ADD pickup_instructions character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260917002143_DealerPublicProfile') THEN
    ALTER TABLE dealers ADD rental_conditions character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260917002143_DealerPublicProfile') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260917002143_DealerPublicProfile', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260918201534_PenaltyReasonCode') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260918201534_PenaltyReasonCode', '10.0.11');
    END IF;
END $EF$;
COMMIT;

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

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260923010807_PushDevicesAndPreferredLanguage') THEN
    ALTER TABLE users ADD preferred_language character varying(2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260923010807_PushDevicesAndPreferredLanguage') THEN
    CREATE TABLE push_devices (
        id uuid NOT NULL,
        token character varying(1024) NOT NULL,
        platform character varying(10) NOT NULL,
        user_id uuid NOT NULL,
        session_family_id uuid,
        language character varying(2) NOT NULL,
        app_version character varying(32),
        created_at timestamp with time zone NOT NULL,
        last_seen_at timestamp with time zone NOT NULL,
        revoked_at timestamp with time zone,
        updated_at timestamp with time zone,
        CONSTRAINT pk_push_devices PRIMARY KEY (id),
        CONSTRAINT fk_push_devices_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260923010807_PushDevicesAndPreferredLanguage') THEN
    CREATE INDEX ix_push_devices_session_family_id ON push_devices (session_family_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260923010807_PushDevicesAndPreferredLanguage') THEN
    CREATE UNIQUE INDEX ix_push_devices_token ON push_devices (token);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260923010807_PushDevicesAndPreferredLanguage') THEN
    CREATE INDEX ix_push_devices_user_id ON push_devices (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260923010807_PushDevicesAndPreferredLanguage') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260923010807_PushDevicesAndPreferredLanguage', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260923012528_NotificationDeliveryOutbox') THEN
    ALTER TABLE notifications ADD due_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260923012528_NotificationDeliveryOutbox') THEN
    CREATE TABLE notification_deliveries (
        id uuid NOT NULL,
        notification_id uuid NOT NULL,
        channel character varying(10) NOT NULL,
        state character varying(10) NOT NULL,
        attempts integer NOT NULL,
        next_attempt_at timestamp with time zone NOT NULL,
        created_at timestamp with time zone NOT NULL,
        completed_at timestamp with time zone,
        last_error character varying(300),
        updated_at timestamp with time zone,
        CONSTRAINT pk_notification_deliveries PRIMARY KEY (id),
        CONSTRAINT fk_notification_deliveries_notifications_notification_id FOREIGN KEY (notification_id) REFERENCES notifications (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260923012528_NotificationDeliveryOutbox') THEN
    CREATE UNIQUE INDEX ix_notification_deliveries_notification_id_channel ON notification_deliveries (notification_id, channel);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260923012528_NotificationDeliveryOutbox') THEN
    CREATE INDEX ix_notification_deliveries_pending_due ON notification_deliveries (next_attempt_at) WHERE state = 'Pending';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260923012528_NotificationDeliveryOutbox') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260923012528_NotificationDeliveryOutbox', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260923013832_BookingReminders') THEN
    CREATE TABLE booking_reminders (
        id uuid NOT NULL,
        booking_id uuid NOT NULL,
        kind character varying(10) NOT NULL,
        anchor_at timestamp with time zone NOT NULL,
        sent_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT pk_booking_reminders PRIMARY KEY (id),
        CONSTRAINT fk_booking_reminders_bookings_booking_id FOREIGN KEY (booking_id) REFERENCES bookings (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260923013832_BookingReminders') THEN
    CREATE UNIQUE INDEX ix_booking_reminders_booking_id_kind_anchor_at ON booking_reminders (booking_id, kind, anchor_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260923013832_BookingReminders') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260923013832_BookingReminders', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260923015142_HandoverCodes') THEN
    ALTER TABLE booking_handovers ADD handover_code_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260923015142_HandoverCodes') THEN
    ALTER TABLE booking_handovers ADD unverified_reason character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260923015142_HandoverCodes') THEN
    ALTER TABLE booking_handovers ADD verification character varying(20);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260923015142_HandoverCodes') THEN
    CREATE TABLE handover_codes (
        id uuid NOT NULL,
        booking_id uuid NOT NULL,
        type character varying(10) NOT NULL,
        code_hash character(64) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        failed_attempts integer NOT NULL,
        used_at timestamp with time zone,
        used_by_user_id uuid,
        superseded_at timestamp with time zone,
        updated_at timestamp with time zone,
        CONSTRAINT pk_handover_codes PRIMARY KEY (id),
        CONSTRAINT fk_handover_codes_bookings_booking_id FOREIGN KEY (booking_id) REFERENCES bookings (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260923015142_HandoverCodes') THEN
    CREATE INDEX ix_handover_codes_booking_id_type_created_at ON handover_codes (booking_id, type, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260923015142_HandoverCodes') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260923015142_HandoverCodes', '10.0.11');
    END IF;
END $EF$;
COMMIT;

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

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260924155530_PaymentPurposeAndOnlinePaid') THEN
    ALTER TABLE payments ADD processing_fee numeric(18,3) NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260924155530_PaymentPurposeAndOnlinePaid') THEN
    ALTER TABLE payments ADD purpose character varying(20) NOT NULL DEFAULT ('Deposit');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260924155530_PaymentPurposeAndOnlinePaid') THEN
    ALTER TABLE bookings ADD online_paid numeric(18,3) NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260924155530_PaymentPurposeAndOnlinePaid') THEN
    UPDATE bookings
    SET online_paid = (pricing -> 'DepositAmount' ->> 'Amount')::numeric(18, 3)
    WHERE deposit_payment_id IS NOT NULL AND online_paid = 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260924155530_PaymentPurposeAndOnlinePaid') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260924155530_PaymentPurposeAndOnlinePaid', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260924162513_PaymentFeeRefundable') THEN
    ALTER TABLE payments ADD fee_refundable boolean NOT NULL DEFAULT TRUE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260924162513_PaymentFeeRefundable') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260924162513_PaymentFeeRefundable', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926230609_FinancialDocuments') THEN
    ALTER TABLE payment_refunds ADD booking_part numeric(18,3);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926230609_FinancialDocuments') THEN
    ALTER TABLE payment_refunds ADD fee_part numeric(18,3);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926230609_FinancialDocuments') THEN

    UPDATE payment_refunds AS r
    SET fee_part = CASE
            WHEN p.processing_fee = 0 OR r.currency <> p.currency THEN 0
            WHEN r.reason = 'OrphanedCapture' THEN LEAST(p.processing_fee, r.amount)
            WHEN r.reason IN ('FreeCancellation', 'PlatformCancellation', 'EndedBeforePickup')
                 AND p.fee_refundable THEN LEAST(p.processing_fee, r.amount)
            ELSE 0
        END
    FROM payments AS p
    WHERE p.id = r.payment_id;

    UPDATE payment_refunds SET booking_part = amount - fee_part;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926230609_FinancialDocuments') THEN
    ALTER TABLE payment_refunds ALTER COLUMN booking_part SET NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926230609_FinancialDocuments') THEN
    ALTER TABLE payment_refunds ALTER COLUMN fee_part SET NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926230609_FinancialDocuments') THEN

    ALTER TABLE payment_refunds ADD CONSTRAINT ck_payment_refunds_split
        CHECK (fee_part >= 0 AND booking_part >= 0 AND booking_part + fee_part = amount);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926230609_FinancialDocuments') THEN
    CREATE TABLE financial_document_issuance_holds (
        id uuid NOT NULL,
        document_type character varying(20) NOT NULL,
        subject_id uuid NOT NULL,
        booking_id uuid NOT NULL,
        reason character varying(30) NOT NULL,
        attempts integer NOT NULL,
        first_failed_at timestamp with time zone NOT NULL,
        last_failed_at timestamp with time zone NOT NULL,
        next_attempt_at timestamp with time zone NOT NULL,
        last_error character varying(300),
        resolved_at timestamp with time zone,
        updated_at timestamp with time zone,
        CONSTRAINT pk_financial_document_issuance_holds PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926230609_FinancialDocuments') THEN
    CREATE TABLE financial_document_series (
        series_key character varying(24) NOT NULL,
        last_number bigint NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_financial_document_series PRIMARY KEY (series_key),
        CONSTRAINT ck_financial_document_series_last_number CHECK (last_number >= 1)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926230609_FinancialDocuments') THEN
    CREATE TABLE financial_documents (
        id uuid NOT NULL,
        document_type character varying(20) NOT NULL,
        document_number character varying(32) NOT NULL,
        subject_id uuid NOT NULL,
        version integer NOT NULL,
        previous_version_id uuid,
        related_document_id uuid,
        booking_id uuid NOT NULL,
        booking_reference character varying(20) NOT NULL,
        customer_id uuid NOT NULL,
        dealer_id uuid NOT NULL,
        payment_id uuid,
        refund_id uuid,
        cause character varying(20) NOT NULL,
        occurred_at timestamp with time zone NOT NULL,
        issued_at timestamp with time zone NOT NULL,
        covers_through timestamp with time zone,
        checkpoint_fingerprint character(64),
        headline_amount numeric(18,3) NOT NULL,
        currency character varying(3) NOT NULL,
        provider character varying(30) NOT NULL,
        calculator_version integer NOT NULL,
        snapshot_schema_version integer NOT NULL,
        snapshot json NOT NULL,
        content_sha256 character(64) NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT pk_financial_documents PRIMARY KEY (id),
        CONSTRAINT ck_financial_documents_previous CHECK ((version = 1) = (previous_version_id IS NULL)),
        CONSTRAINT ck_financial_documents_statement_coverage CHECK ((document_type = 'BookingStatement') = (covers_through IS NOT NULL AND checkpoint_fingerprint IS NOT NULL)),
        CONSTRAINT ck_financial_documents_subject CHECK ((document_type = 'PaymentReceipt' AND payment_id IS NOT NULL AND refund_id IS NULL AND subject_id = payment_id) OR (document_type = 'RefundReceipt' AND payment_id IS NOT NULL AND refund_id IS NOT NULL AND subject_id = refund_id) OR (document_type = 'BookingStatement' AND refund_id IS NULL AND subject_id = booking_id)),
        CONSTRAINT ck_financial_documents_version CHECK (version >= 1),
        CONSTRAINT fk_financial_documents_financial_documents_previous_version_id FOREIGN KEY (previous_version_id) REFERENCES financial_documents (id) ON DELETE RESTRICT,
        CONSTRAINT fk_financial_documents_financial_documents_related_document_id FOREIGN KEY (related_document_id) REFERENCES financial_documents (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926230609_FinancialDocuments') THEN
    CREATE TABLE financial_document_voids (
        document_id uuid NOT NULL,
        voided_at timestamp with time zone NOT NULL,
        voided_by_admin_id uuid NOT NULL,
        reason character varying(500) NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT pk_financial_document_voids PRIMARY KEY (document_id),
        CONSTRAINT fk_financial_document_voids_financial_documents_document_id FOREIGN KEY (document_id) REFERENCES financial_documents (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926230609_FinancialDocuments') THEN
    CREATE UNIQUE INDEX ix_financial_document_issuance_holds_document_type_subject_id ON financial_document_issuance_holds (document_type, subject_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926230609_FinancialDocuments') THEN
    CREATE INDEX ix_financial_document_issuance_holds_next_attempt_at ON financial_document_issuance_holds (next_attempt_at) WHERE resolved_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926230609_FinancialDocuments') THEN
    CREATE INDEX ix_financial_documents_booking_id_issued_at_id ON financial_documents (booking_id, issued_at DESC, id DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926230609_FinancialDocuments') THEN
    CREATE INDEX ix_financial_documents_customer_id_issued_at_id ON financial_documents (customer_id, issued_at DESC, id DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926230609_FinancialDocuments') THEN
    CREATE UNIQUE INDEX ix_financial_documents_document_number ON financial_documents (document_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926230609_FinancialDocuments') THEN
    CREATE UNIQUE INDEX ix_financial_documents_document_type_subject_id_version ON financial_documents (document_type, subject_id, version);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926230609_FinancialDocuments') THEN
    CREATE INDEX ix_financial_documents_issued_at_id ON financial_documents (issued_at DESC, id DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926230609_FinancialDocuments') THEN
    CREATE INDEX ix_financial_documents_payment_id ON financial_documents (payment_id) WHERE payment_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926230609_FinancialDocuments') THEN
    CREATE INDEX ix_financial_documents_previous_version_id ON financial_documents (previous_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926230609_FinancialDocuments') THEN
    CREATE INDEX ix_financial_documents_related_document_id ON financial_documents (related_document_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926230609_FinancialDocuments') THEN

    CREATE TRIGGER financial_documents_append_only
    BEFORE UPDATE OR DELETE ON financial_documents
    FOR EACH ROW EXECUTE FUNCTION khadra_table_is_append_only();

    CREATE TRIGGER financial_documents_no_truncate
    BEFORE TRUNCATE ON financial_documents
    FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();

    CREATE TRIGGER financial_document_voids_append_only
    BEFORE UPDATE OR DELETE ON financial_document_voids
    FOR EACH ROW EXECUTE FUNCTION khadra_table_is_append_only();

    CREATE TRIGGER financial_document_voids_no_truncate
    BEFORE TRUNCATE ON financial_document_voids
    FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260926230609_FinancialDocuments') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260926230609_FinancialDocuments', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260928222747_FinancialDocumentRenditions') THEN
    CREATE TABLE financial_document_renditions (
        id uuid NOT NULL,
        document_id uuid NOT NULL,
        language character varying(2) NOT NULL,
        format character varying(10) NOT NULL,
        template_version integer NOT NULL,
        renderer_version character varying(64) NOT NULL,
        storage_key character varying(300) NOT NULL,
        content_sha256 character(64) NOT NULL,
        size_bytes bigint NOT NULL,
        snapshot_sha256 character(64) NOT NULL,
        rendered_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT pk_financial_document_renditions PRIMARY KEY (id),
        CONSTRAINT ck_financial_document_renditions_size_bytes CHECK (size_bytes > 0),
        CONSTRAINT ck_financial_document_renditions_template_version CHECK (template_version >= 1),
        CONSTRAINT fk_financial_document_renditions_financial_documents_document_ FOREIGN KEY (document_id) REFERENCES financial_documents (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260928222747_FinancialDocumentRenditions') THEN
    CREATE UNIQUE INDEX ix_financial_document_renditions_document_id_language_format_t ON financial_document_renditions (document_id, language, format, template_version);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260928222747_FinancialDocumentRenditions') THEN
    CREATE UNIQUE INDEX ix_financial_document_renditions_storage_key ON financial_document_renditions (storage_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260928222747_FinancialDocumentRenditions') THEN

    CREATE TRIGGER financial_document_renditions_append_only
    BEFORE UPDATE OR DELETE ON financial_document_renditions
    FOR EACH ROW EXECUTE FUNCTION khadra_table_is_append_only();

    CREATE TRIGGER financial_document_renditions_no_truncate
    BEFORE TRUNCATE ON financial_document_renditions
    FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260928222747_FinancialDocumentRenditions') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260928222747_FinancialDocumentRenditions', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929020747_FinancialDocumentRenditionKind') THEN
    DROP INDEX ix_financial_document_renditions_document_id_language_format_t;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929020747_FinancialDocumentRenditionKind') THEN
    ALTER TABLE financial_document_renditions ADD kind character varying(10) NOT NULL DEFAULT 'AsIssued';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929020747_FinancialDocumentRenditionKind') THEN

    ALTER TABLE financial_document_renditions ALTER COLUMN kind DROP DEFAULT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929020747_FinancialDocumentRenditionKind') THEN
    CREATE UNIQUE INDEX ix_financial_document_renditions_document_id_language_format_k ON financial_document_renditions (document_id, language, format, kind, template_version);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929020747_FinancialDocumentRenditionKind') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260929020747_FinancialDocumentRenditionKind', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929164711_FinancialDocumentDeliveries') THEN
    CREATE TABLE financial_document_deliveries (
        id uuid NOT NULL,
        document_id uuid NOT NULL,
        channel character varying(10) NOT NULL,
        state character varying(10) NOT NULL,
        requested_by_admin_id uuid,
        queued_at timestamp with time zone NOT NULL,
        next_attempt_at timestamp with time zone NOT NULL,
        claims integer NOT NULL,
        send_attempts integer NOT NULL,
        completed_at timestamp with time zone,
        recipient_address character varying(320),
        languages character varying(10),
        waiting_reason character varying(20),
        waiting_since timestamp with time zone,
        last_error character varying(300),
        updated_at timestamp with time zone,
        CONSTRAINT pk_financial_document_deliveries PRIMARY KEY (id),
        CONSTRAINT ck_financial_document_deliveries_completed CHECK ((state = 'Queued') = (completed_at IS NULL)),
        CONSTRAINT ck_financial_document_deliveries_counts CHECK (claims >= 0 AND send_attempts >= 0),
        CONSTRAINT ck_financial_document_deliveries_waiting CHECK ((waiting_reason IS NULL) = (waiting_since IS NULL) AND (state = 'Queued' OR waiting_reason IS NULL)),
        CONSTRAINT fk_financial_document_deliveries_document FOREIGN KEY (document_id) REFERENCES financial_documents (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929164711_FinancialDocumentDeliveries') THEN
    CREATE TABLE financial_document_delivery_attempts (
        id uuid NOT NULL,
        delivery_id uuid NOT NULL,
        number integer NOT NULL,
        outcome character varying(10) NOT NULL,
        attempted_at timestamp with time zone NOT NULL,
        error character varying(300),
        provider character varying(20),
        provider_message_id character varying(200),
        english_rendition_id uuid,
        arabic_rendition_id uuid,
        CONSTRAINT pk_financial_document_delivery_attempts PRIMARY KEY (id),
        CONSTRAINT ck_financial_document_delivery_attempts_number CHECK (number >= 1),
        CONSTRAINT fk_financial_document_delivery_attempts_arabic_rendition FOREIGN KEY (arabic_rendition_id) REFERENCES financial_document_renditions (id) ON DELETE RESTRICT,
        CONSTRAINT fk_financial_document_delivery_attempts_delivery FOREIGN KEY (delivery_id) REFERENCES financial_document_deliveries (id) ON DELETE RESTRICT,
        CONSTRAINT fk_financial_document_delivery_attempts_english_rendition FOREIGN KEY (english_rendition_id) REFERENCES financial_document_renditions (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929164711_FinancialDocumentDeliveries') THEN
    CREATE INDEX ix_financial_document_deliveries_document_id_queued_at ON financial_document_deliveries (document_id, queued_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929164711_FinancialDocumentDeliveries') THEN
    CREATE INDEX ix_financial_document_deliveries_failed ON financial_document_deliveries (queued_at) WHERE state = 'Failed';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929164711_FinancialDocumentDeliveries') THEN
    CREATE UNIQUE INDEX ix_financial_document_deliveries_one_queued ON financial_document_deliveries (document_id) WHERE state = 'Queued';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929164711_FinancialDocumentDeliveries') THEN
    CREATE INDEX ix_financial_document_deliveries_queued_due ON financial_document_deliveries (next_attempt_at) WHERE state = 'Queued';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929164711_FinancialDocumentDeliveries') THEN
    CREATE INDEX ix_financial_document_delivery_attempts_arabic_rendition_id ON financial_document_delivery_attempts (arabic_rendition_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929164711_FinancialDocumentDeliveries') THEN
    CREATE UNIQUE INDEX ix_financial_document_delivery_attempts_delivery_id_number ON financial_document_delivery_attempts (delivery_id, number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929164711_FinancialDocumentDeliveries') THEN
    CREATE INDEX ix_financial_document_delivery_attempts_english_rendition_id ON financial_document_delivery_attempts (english_rendition_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929164711_FinancialDocumentDeliveries') THEN

    CREATE TRIGGER financial_document_delivery_attempts_append_only
    BEFORE UPDATE OR DELETE ON financial_document_delivery_attempts
    FOR EACH ROW EXECUTE FUNCTION khadra_table_is_append_only();

    CREATE TRIGGER financial_document_delivery_attempts_no_truncate
    BEFORE TRUNCATE ON financial_document_delivery_attempts
    FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929164711_FinancialDocumentDeliveries') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260929164711_FinancialDocumentDeliveries', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929195432_OfficePayables') THEN
    CREATE TABLE office_settlements (
        id uuid NOT NULL,
        settlement_number character varying(32) NOT NULL,
        dealer_id uuid NOT NULL,
        currency character varying(3) NOT NULL,
        provider character varying(30) NOT NULL,
        direction character varying(10) NOT NULL,
        amount numeric(18,3) NOT NULL,
        paid_on date NOT NULL,
        reference character varying(100),
        note character varying(500),
        recorded_by_admin_id uuid NOT NULL,
        recorded_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT pk_office_settlements PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929195432_OfficePayables') THEN
    CREATE TABLE office_payables (
        id uuid NOT NULL,
        booking_id uuid NOT NULL,
        dealer_id uuid NOT NULL,
        booking_reference character varying(20) NOT NULL,
        currency character varying(3) NOT NULL,
        provider character varying(30) NOT NULL,
        outcome character varying(30) NOT NULL,
        final_at timestamp with time zone NOT NULL,
        recorded_at timestamp with time zone NOT NULL,
        office_money numeric(18,3) NOT NULL,
        commission numeric(18,3) NOT NULL,
        office_charges numeric(18,3) NOT NULL,
        net numeric(18,3) NOT NULL,
        calculator_version integer NOT NULL,
        settlement_id uuid,
        settled_at timestamp with time zone,
        updated_at timestamp with time zone,
        CONSTRAINT pk_office_payables PRIMARY KEY (id),
        CONSTRAINT ck_office_payables_calculator_version CHECK (calculator_version >= 1),
        CONSTRAINT ck_office_payables_settled CHECK ((settlement_id IS NULL) = (settled_at IS NULL)),
        CONSTRAINT fk_office_payables_settlement FOREIGN KEY (settlement_id) REFERENCES office_settlements (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929195432_OfficePayables') THEN
    CREATE TABLE office_settlement_voids (
        settlement_id uuid NOT NULL,
        voided_at timestamp with time zone NOT NULL,
        voided_by_admin_id uuid NOT NULL,
        reason character varying(500) NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT pk_office_settlement_voids PRIMARY KEY (settlement_id),
        CONSTRAINT fk_office_settlement_voids_settlement FOREIGN KEY (settlement_id) REFERENCES office_settlements (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929195432_OfficePayables') THEN
    CREATE TABLE office_payable_holds (
        id uuid NOT NULL,
        booking_id uuid NOT NULL,
        dealer_id uuid NOT NULL,
        payable_id uuid,
        reason character varying(30) NOT NULL,
        detail character varying(500),
        opened_at timestamp with time zone NOT NULL,
        opened_by_admin_id uuid,
        checks integer NOT NULL,
        last_checked_at timestamp with time zone NOT NULL,
        next_check_at timestamp with time zone,
        released_at timestamp with time zone,
        released_by_admin_id uuid,
        release_note character varying(500),
        updated_at timestamp with time zone,
        CONSTRAINT pk_office_payable_holds PRIMARY KEY (id),
        CONSTRAINT ck_office_payable_holds_checks CHECK (checks >= 0),
        CONSTRAINT ck_office_payable_holds_manual CHECK ((reason = 'Manual') = (opened_by_admin_id IS NOT NULL) AND (released_by_admin_id IS NULL OR reason = 'Manual')),
        CONSTRAINT ck_office_payable_holds_payable CHECK ((payable_id IS NULL) = (reason IN ('NeedsReview', 'PenaltyNotWholeDeposit'))),
        CONSTRAINT fk_office_payable_holds_payable FOREIGN KEY (payable_id) REFERENCES office_payables (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929195432_OfficePayables') THEN
    CREATE TABLE office_payable_lines (
        id uuid NOT NULL,
        payable_id uuid NOT NULL,
        position integer NOT NULL,
        kind character varying(20) NOT NULL,
        amount numeric(18,3) NOT NULL,
        source_id uuid,
        CONSTRAINT pk_office_payable_lines PRIMARY KEY (id),
        CONSTRAINT ck_office_payable_lines_position CHECK (position >= 1),
        CONSTRAINT fk_office_payable_lines_payable FOREIGN KEY (payable_id) REFERENCES office_payables (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929195432_OfficePayables') THEN
    CREATE TABLE office_settlement_lines (
        id uuid NOT NULL,
        settlement_id uuid NOT NULL,
        payable_id uuid NOT NULL,
        net numeric(18,3) NOT NULL,
        CONSTRAINT pk_office_settlement_lines PRIMARY KEY (id),
        CONSTRAINT fk_office_settlement_lines_payable FOREIGN KEY (payable_id) REFERENCES office_payables (id) ON DELETE RESTRICT,
        CONSTRAINT fk_office_settlement_lines_settlement FOREIGN KEY (settlement_id) REFERENCES office_settlements (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929195432_OfficePayables') THEN
    CREATE INDEX ix_dispute_tickets_booking ON dispute_tickets (booking_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929195432_OfficePayables') THEN
    CREATE UNIQUE INDEX ix_office_payable_holds_one_open ON office_payable_holds (booking_id, reason) WHERE released_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929195432_OfficePayables') THEN
    CREATE INDEX ix_office_payable_holds_open_dealer ON office_payable_holds (dealer_id) WHERE released_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929195432_OfficePayables') THEN
    CREATE INDEX ix_office_payable_holds_open_next_check ON office_payable_holds (next_check_at) WHERE released_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929195432_OfficePayables') THEN
    CREATE INDEX ix_office_payable_holds_payable_id ON office_payable_holds (payable_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929195432_OfficePayables') THEN
    CREATE UNIQUE INDEX ix_office_payable_lines_payable_id_position ON office_payable_lines (payable_id, position);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929195432_OfficePayables') THEN
    CREATE UNIQUE INDEX ix_office_payables_booking ON office_payables (booking_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929195432_OfficePayables') THEN
    CREATE INDEX ix_office_payables_dealer_final ON office_payables (dealer_id, final_at DESC, id DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929195432_OfficePayables') THEN
    CREATE INDEX ix_office_payables_final ON office_payables (final_at DESC, id DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929195432_OfficePayables') THEN
    CREATE INDEX ix_office_payables_open ON office_payables (dealer_id, currency, provider) WHERE settlement_id IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929195432_OfficePayables') THEN
    CREATE INDEX ix_office_payables_settlement ON office_payables (settlement_id) WHERE settlement_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929195432_OfficePayables') THEN
    CREATE INDEX ix_office_settlement_lines_payable_id ON office_settlement_lines (payable_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929195432_OfficePayables') THEN
    CREATE UNIQUE INDEX ix_office_settlement_lines_settlement_id_payable_id ON office_settlement_lines (settlement_id, payable_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929195432_OfficePayables') THEN
    CREATE INDEX ix_office_settlements_dealer_recorded ON office_settlements (dealer_id, recorded_at DESC, id DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929195432_OfficePayables') THEN
    CREATE INDEX ix_office_settlements_paid_on ON office_settlements (paid_on);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929195432_OfficePayables') THEN
    CREATE UNIQUE INDEX ix_office_settlements_settlement_number ON office_settlements (settlement_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929195432_OfficePayables') THEN

    ALTER TABLE office_payables ADD CONSTRAINT ck_office_payables_figures
        CHECK (office_money >= 0 AND commission >= 0 AND office_charges >= 0
               AND commission <= office_money
               AND net = office_money - commission - office_charges);

    ALTER TABLE office_payable_lines ADD CONSTRAINT ck_office_payable_lines_amount
        CHECK (amount > 0);

    ALTER TABLE office_settlements ADD CONSTRAINT ck_office_settlements_direction
        CHECK ((direction = 'Payout' AND amount > 0)
            OR (direction = 'Received' AND amount < 0)
            OR (direction = 'Netted' AND amount = 0));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929195432_OfficePayables') THEN

    CREATE FUNCTION khadra_office_payable_is_frozen()
    RETURNS TRIGGER AS $$
    BEGIN
        IF TG_OP = 'DELETE' THEN
            RAISE EXCEPTION 'office_payables is a ledger: DELETE is not permitted';
        END IF;
        IF (to_jsonb(NEW) - 'settlement_id' - 'settled_at' - 'updated_at')
           IS DISTINCT FROM (to_jsonb(OLD) - 'settlement_id' - 'settled_at' - 'updated_at') THEN
            RAISE EXCEPTION 'office_payables: a payable''s figures are frozen; only its settlement may change';
        END IF;
        IF OLD.settlement_id IS NOT NULL AND NEW.settlement_id IS NOT NULL AND OLD.settlement_id <> NEW.settlement_id THEN
            RAISE EXCEPTION 'office_payables: a payable leaves a settlement only when that settlement is voided';
        END IF;
        IF OLD.settlement_id IS NOT DISTINCT FROM NEW.settlement_id AND OLD.settled_at IS DISTINCT FROM NEW.settled_at THEN
            RAISE EXCEPTION 'office_payables: when a payable was settled is part of its settlement, and never rewritten';
        END IF;
        RETURN NEW;
    END;
    $$ LANGUAGE plpgsql;

    CREATE FUNCTION khadra_office_payable_hold_is_kept()
    RETURNS TRIGGER AS $$
    BEGIN
        RAISE EXCEPTION 'office_payable_holds keeps every hold, released or not: % is not permitted', TG_OP;
    END;
    $$ LANGUAGE plpgsql;

    CREATE TRIGGER office_payables_frozen
    BEFORE UPDATE OR DELETE ON office_payables
    FOR EACH ROW EXECUTE FUNCTION khadra_office_payable_is_frozen();

    CREATE TRIGGER office_payables_no_truncate
    BEFORE TRUNCATE ON office_payables
    FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();

    CREATE TRIGGER office_payable_lines_append_only
    BEFORE UPDATE OR DELETE ON office_payable_lines
    FOR EACH ROW EXECUTE FUNCTION khadra_table_is_append_only();

    CREATE TRIGGER office_payable_lines_no_truncate
    BEFORE TRUNCATE ON office_payable_lines
    FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();

    CREATE TRIGGER office_settlements_append_only
    BEFORE UPDATE OR DELETE ON office_settlements
    FOR EACH ROW EXECUTE FUNCTION khadra_table_is_append_only();

    CREATE TRIGGER office_settlements_no_truncate
    BEFORE TRUNCATE ON office_settlements
    FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();

    CREATE TRIGGER office_settlement_lines_append_only
    BEFORE UPDATE OR DELETE ON office_settlement_lines
    FOR EACH ROW EXECUTE FUNCTION khadra_table_is_append_only();

    CREATE TRIGGER office_settlement_lines_no_truncate
    BEFORE TRUNCATE ON office_settlement_lines
    FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();

    CREATE TRIGGER office_settlement_voids_append_only
    BEFORE UPDATE OR DELETE ON office_settlement_voids
    FOR EACH ROW EXECUTE FUNCTION khadra_table_is_append_only();

    CREATE TRIGGER office_settlement_voids_no_truncate
    BEFORE TRUNCATE ON office_settlement_voids
    FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();

    CREATE TRIGGER office_payable_holds_kept
    BEFORE DELETE ON office_payable_holds
    FOR EACH ROW EXECUTE FUNCTION khadra_office_payable_hold_is_kept();

    CREATE TRIGGER office_payable_holds_no_truncate
    BEFORE TRUNCATE ON office_payable_holds
    FOR EACH STATEMENT EXECUTE FUNCTION khadra_office_payable_hold_is_kept();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260929195432_OfficePayables') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260929195432_OfficePayables', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005062940_LegalDocumentVersions') THEN
    CREATE TABLE legal_document_versions (
        id uuid NOT NULL,
        kind character varying(30) NOT NULL,
        version_label character varying(40) NOT NULL,
        effective_from timestamp with time zone NOT NULL,
        published_at timestamp with time zone NOT NULL,
        published_by_admin_id uuid NOT NULL,
        body_en text NOT NULL,
        body_ar text NOT NULL,
        body_en_sha256 character(64) NOT NULL,
        body_ar_sha256 character(64) NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT pk_legal_document_versions PRIMARY KEY (id),
        CONSTRAINT ck_legal_document_versions_bodies CHECK (length(body_en) BETWEEN 1 AND 200000 AND length(body_ar) BETWEEN 1 AND 200000),
        CONSTRAINT ck_legal_document_versions_effective_from CHECK (effective_from >= published_at),
        CONSTRAINT ck_legal_document_versions_hashes CHECK (length(body_en_sha256) = 64 AND length(body_ar_sha256) = 64),
        CONSTRAINT ck_legal_document_versions_kind CHECK (kind IN ('Terms', 'Privacy')),
        CONSTRAINT ck_legal_document_versions_version_label CHECK (length(version_label) BETWEEN 1 AND 40 AND version_label = trim(version_label))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005062940_LegalDocumentVersions') THEN
    CREATE UNIQUE INDEX ux_legal_document_versions_kind_effective_from ON legal_document_versions (kind, effective_from);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005062940_LegalDocumentVersions') THEN
    CREATE UNIQUE INDEX ux_legal_document_versions_kind_version_label ON legal_document_versions (kind, version_label);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005062940_LegalDocumentVersions') THEN

    CREATE TRIGGER legal_document_versions_append_only
    BEFORE UPDATE OR DELETE ON legal_document_versions
    FOR EACH ROW EXECUTE FUNCTION khadra_table_is_append_only();

    CREATE TRIGGER legal_document_versions_no_truncate
    BEFORE TRUNCATE ON legal_document_versions
    FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261005062940_LegalDocumentVersions') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261005062940_LegalDocumentVersions', '10.0.11');
    END IF;
END $EF$;
COMMIT;

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

