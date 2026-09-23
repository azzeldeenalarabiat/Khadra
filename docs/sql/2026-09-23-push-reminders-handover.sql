-- Push notifications, reminders and handover codes (2026-09-23). Four migrations, all ADDITIVE:
--
--   20260923010807_PushDevicesAndPreferredLanguage  push_devices; users.preferred_language (nullable)
--   20260923012528_NotificationDeliveryOutbox       notification_deliveries; notifications.due_at (nullable)
--   20260923013832_BookingReminders                 booking_reminders
--   20260923015142_HandoverCodes                    handover_codes; booking_handovers.verification,
--                                                   .handover_code_id, .unverified_reason (all nullable)
--
-- Idempotent (generated with dotnet ef migrations script --idempotent): each migration runs only if
-- __EFMigrationsHistory does not already list it, so running this twice is harmless. Nothing is
-- dropped or rewritten, and no existing row changes. Apply from the Supabase SQL editor BEFORE the
-- API that needs it is deployed; the running API ignores the new tables and columns.
--
-- Staging first. Never copy staging data into production: this file carries schema only.

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

