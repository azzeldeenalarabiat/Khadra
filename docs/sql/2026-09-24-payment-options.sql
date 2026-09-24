-- Payment purpose, processing fee and amount paid online (2026-09-24). Two migrations, both ADDITIVE:
--
--   20260924155530_PaymentPurposeAndOnlinePaid
--       payments.purpose         varchar(20)   NOT NULL DEFAULT 'Deposit'
--       payments.processing_fee  numeric(18,3) NOT NULL DEFAULT 0
--       bookings.online_paid     numeric(18,3) NOT NULL DEFAULT 0
--   20260924162513_PaymentFeeRefundable
--       payments.fee_refundable  boolean       NOT NULL DEFAULT TRUE
--
-- fee_refundable freezes, per payment, whether its processing fee goes back with a refund. TRUE is
-- what every existing row has always meant: no payment has carried a fee, and every refund so far
-- returned the whole capture.
--
-- The defaults describe every existing row truthfully: each payment so far was a deposit with no fee.
-- The one write sets bookings.online_paid on bookings a payment has already confirmed — until this
-- release the only way to confirm was the deposit, so each was paid exactly its frozen DepositAmount.
--
-- Idempotent (dotnet ef migrations script --idempotent): runs only if __EFMigrationsHistory does not
-- list it. Nothing is dropped. An older API ignores all three columns, so apply it BEFORE deploying the
-- API that needs it. Requires 2026-09-24-frozen-commission.sql first.
--
-- Staging first; production is not in scope. No table is added, so supabase-lockdown.sql need not re-run.

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


-- ─── Catch-up — run once more AFTER the new API is live ──────────────────────────────────────────
-- A booking the OLD API confirmed between applying the migration above and the new API going live has
-- online_paid = 0. Its balance still reads correctly (zero paid falls back to the frozen balance), but
-- the financial statement reads online_paid, so bring it in line. Idempotent; safe to re-run.
UPDATE bookings
SET online_paid = (pricing -> 'DepositAmount' ->> 'Amount')::numeric(18, 3)
WHERE deposit_payment_id IS NOT NULL AND online_paid = 0;
