using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Payments Phase 5 (owner, 2026-09-27): issued financial documents — payment receipts, refund
    /// receipts and versioned booking statements — and each refund's stored split into booking money and
    /// processing fee (decision 4, 2026-09-26).
    /// </summary>
    /// <remarks>
    /// Hand-edited beyond what EF generates, for three things EF does not model:
    /// <list type="number">
    /// <item>The refund split is added NULLABLE, filled for every existing refund by the rule
    /// <c>Payment.FeeFor</c> applies (written in SQL below), and only then made NOT NULL — EF's own
    /// version added it NOT NULL DEFAULT 0, which would have stamped every existing refund as carrying no
    /// fee. A PostgreSQL-only CHECK then holds the parts to the amount (not in the EF model: SQLite, which
    /// the unit tests run on, compares decimals as floating point).</item>
    /// <item>The documents and their voids are append-only in the database too, with the function the
    /// document access log already uses: a row trigger refuses UPDATE and DELETE, a statement trigger
    /// refuses TRUNCATE (row triggers do not fire on it).</item>
    /// <item>Down drops those triggers and the CHECK explicitly — never the shared function, which
    /// <c>document_access_entries</c> also uses — and REFUSES to run once a real (non-test) document
    /// exists: the triggers stop UPDATE, DELETE and TRUNCATE, but not DROP TABLE, so without that guard
    /// a rollback would destroy issued receipts. From then on this migration is fixed forward.</item>
    /// </list>
    /// </remarks>
    public partial class FinancialDocuments : Migration
    {
        // The rule Payment.FeeFor applies when a refund is recorded, written in SQL for the refunds
        // recorded before the split was stored: over a refund r and its payment p, and pure over columns
        // that never change once written — the payment's frozen fee and whether it was refundable, the
        // refund's reason, amount and currency. Internal so PostgresRefundSplitBackfillTests can run this
        // exact expression against refunds recorded through the aggregate and prove the two agree.
        internal const string RefundFeeRule = @"CASE
        WHEN p.processing_fee = 0 OR r.currency <> p.currency THEN 0
        WHEN r.reason = 'OrphanedCapture' THEN LEAST(p.processing_fee, r.amount)
        WHEN r.reason IN ('FreeCancellation', 'PlatformCancellation', 'EndedBeforePickup')
             AND p.fee_refundable THEN LEAST(p.processing_fee, r.amount)
        ELSE 0
    END";

        private const string FillRefundSplit = @"
UPDATE payment_refunds AS r
SET fee_part = " + RefundFeeRule + @"
FROM payments AS p
WHERE p.id = r.payment_id;

UPDATE payment_refunds SET booking_part = amount - fee_part;";

        private const string CheckRefundSplit = @"
ALTER TABLE payment_refunds ADD CONSTRAINT ck_payment_refunds_split
    CHECK (fee_part >= 0 AND booking_part >= 0 AND booking_part + fee_part = amount);";

        private const string CreateAppendOnlyGuards = @"
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
FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();";

        // The one rollback this migration refuses. A real document — anything not numbered TEST- — is a
        // record given to a customer, and DROP TABLE is the one thing the append-only triggers cannot stop.
        private const string RefuseRollbackOverRealDocuments = @"
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM financial_documents WHERE document_number NOT LIKE 'TEST-%') THEN
        RAISE EXCEPTION 'financial_documents holds real documents: this migration is fixed forward, never reverted';
    END IF;
END $$;";

        private const string DropAppendOnlyGuards = @"
DROP TRIGGER IF EXISTS financial_document_voids_no_truncate ON financial_document_voids;
DROP TRIGGER IF EXISTS financial_document_voids_append_only ON financial_document_voids;
DROP TRIGGER IF EXISTS financial_documents_no_truncate ON financial_documents;
DROP TRIGGER IF EXISTS financial_documents_append_only ON financial_documents;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. The refund split: nullable, filled by the rule, then required and checked.
            migrationBuilder.AddColumn<decimal>(
                name: "booking_part",
                table: "payment_refunds",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "fee_part",
                table: "payment_refunds",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.Sql(FillRefundSplit);

            migrationBuilder.AlterColumn<decimal>(
                name: "booking_part",
                table: "payment_refunds",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,3)",
                oldPrecision: 18,
                oldScale: 3,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "fee_part",
                table: "payment_refunds",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,3)",
                oldPrecision: 18,
                oldScale: 3,
                oldNullable: true);

            migrationBuilder.Sql(CheckRefundSplit);

            // 2. The documents, as EF generates them from the model.

            migrationBuilder.CreateTable(
                name: "financial_document_issuance_holds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    first_failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financial_document_issuance_holds", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "financial_document_series",
                columns: table => new
                {
                    series_key = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    last_number = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financial_document_series", x => x.series_key);
                    table.CheckConstraint("ck_financial_document_series_last_number", "last_number >= 1");
                });

            migrationBuilder.CreateTable(
                name: "financial_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    document_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    previous_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    related_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_reference = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dealer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    refund_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cause = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    covers_through = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    checkpoint_fingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    headline_amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    calculator_version = table.Column<int>(type: "integer", nullable: false),
                    snapshot_schema_version = table.Column<int>(type: "integer", nullable: false),
                    snapshot = table.Column<string>(type: "json", nullable: false),
                    content_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financial_documents", x => x.id);
                    table.CheckConstraint("ck_financial_documents_previous", "(version = 1) = (previous_version_id IS NULL)");
                    table.CheckConstraint("ck_financial_documents_statement_coverage", "(document_type = 'BookingStatement') = (covers_through IS NOT NULL AND checkpoint_fingerprint IS NOT NULL)");
                    table.CheckConstraint("ck_financial_documents_subject", "(document_type = 'PaymentReceipt' AND payment_id IS NOT NULL AND refund_id IS NULL AND subject_id = payment_id) OR (document_type = 'RefundReceipt' AND payment_id IS NOT NULL AND refund_id IS NOT NULL AND subject_id = refund_id) OR (document_type = 'BookingStatement' AND refund_id IS NULL AND subject_id = booking_id)");
                    table.CheckConstraint("ck_financial_documents_version", "version >= 1");
                    table.ForeignKey(
                        name: "fk_financial_documents_financial_documents_previous_version_id",
                        column: x => x.previous_version_id,
                        principalTable: "financial_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_financial_documents_financial_documents_related_document_id",
                        column: x => x.related_document_id,
                        principalTable: "financial_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "financial_document_voids",
                columns: table => new
                {
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    voided_by_admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financial_document_voids", x => x.document_id);
                    table.ForeignKey(
                        name: "fk_financial_document_voids_financial_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "financial_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_financial_document_issuance_holds_document_type_subject_id",
                table: "financial_document_issuance_holds",
                columns: new[] { "document_type", "subject_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_financial_document_issuance_holds_next_attempt_at",
                table: "financial_document_issuance_holds",
                column: "next_attempt_at",
                filter: "resolved_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_financial_documents_booking_id_issued_at_id",
                table: "financial_documents",
                columns: new[] { "booking_id", "issued_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_financial_documents_customer_id_issued_at_id",
                table: "financial_documents",
                columns: new[] { "customer_id", "issued_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_financial_documents_document_number",
                table: "financial_documents",
                column: "document_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_financial_documents_document_type_subject_id_version",
                table: "financial_documents",
                columns: new[] { "document_type", "subject_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_financial_documents_issued_at_id",
                table: "financial_documents",
                columns: new[] { "issued_at", "id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_financial_documents_payment_id",
                table: "financial_documents",
                column: "payment_id",
                filter: "payment_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_financial_documents_previous_version_id",
                table: "financial_documents",
                column: "previous_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_financial_documents_related_document_id",
                table: "financial_documents",
                column: "related_document_id");

            // 3. Issued documents and their voids never change, in the database as in the application.
            migrationBuilder.Sql(CreateAppendOnlyGuards);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only while no real (non-test) document exists: see the remarks on this class.
            migrationBuilder.Sql(RefuseRollbackOverRealDocuments);
            migrationBuilder.Sql(DropAppendOnlyGuards);
            migrationBuilder.Sql("ALTER TABLE payment_refunds DROP CONSTRAINT IF EXISTS ck_payment_refunds_split;");

            migrationBuilder.DropTable(
                name: "financial_document_issuance_holds");

            migrationBuilder.DropTable(
                name: "financial_document_series");

            migrationBuilder.DropTable(
                name: "financial_document_voids");

            migrationBuilder.DropTable(
                name: "financial_documents");

            migrationBuilder.DropColumn(
                name: "booking_part",
                table: "payment_refunds");

            migrationBuilder.DropColumn(
                name: "fee_part",
                table: "payment_refunds");
        }
    }
}
