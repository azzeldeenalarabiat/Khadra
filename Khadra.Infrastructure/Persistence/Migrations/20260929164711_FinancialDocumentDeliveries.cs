using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The emails of issued receipts (payments Phase 7, owner, 2026-09-29): two additive tables. The deliveries are the
    /// outbox rows the email service works — mutable, moving from Queued to where they ended — and their attempts are
    /// the history the administrator reads, append-only like the documents: the same database triggers refuse
    /// UPDATE, DELETE and TRUNCATE. The recipient's address is a column of the delivery only, never of an attempt.
    /// </summary>
    /// <remarks>
    /// Its rollback is guarded as <c>20260928222747_FinancialDocumentRenditions</c>'s is. Dropping a table is the one
    /// thing the append-only triggers cannot stop, and an email's history is the platform's only record of what a
    /// customer was sent — so Down refuses once any email belongs to a real (non-test) document, and from then on this
    /// migration is fixed forward. Over test documents it runs.
    /// </remarks>
    public partial class FinancialDocumentDeliveries : Migration
    {
        private const string RefuseRollbackOverRealEmails = @"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM financial_document_deliveries delivery
        JOIN financial_documents document ON document.id = delivery.document_id
        WHERE document.document_number NOT LIKE 'TEST-%') THEN
        RAISE EXCEPTION 'financial_document_deliveries records emails of real documents: this migration is fixed forward, never reverted';
    END IF;
END $$;";

        private const string CreateAppendOnlyGuards = @"
CREATE TRIGGER financial_document_delivery_attempts_append_only
BEFORE UPDATE OR DELETE ON financial_document_delivery_attempts
FOR EACH ROW EXECUTE FUNCTION khadra_table_is_append_only();

CREATE TRIGGER financial_document_delivery_attempts_no_truncate
BEFORE TRUNCATE ON financial_document_delivery_attempts
FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();";

        private const string DropAppendOnlyGuards = @"
DROP TRIGGER IF EXISTS financial_document_delivery_attempts_no_truncate ON financial_document_delivery_attempts;
DROP TRIGGER IF EXISTS financial_document_delivery_attempts_append_only ON financial_document_delivery_attempts;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "financial_document_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    state = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    requested_by_admin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    queued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    claims = table.Column<int>(type: "integer", nullable: false),
                    send_attempts = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recipient_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    languages = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    waiting_reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    waiting_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financial_document_deliveries", x => x.id);
                    table.CheckConstraint("ck_financial_document_deliveries_completed", "(state = 'Queued') = (completed_at IS NULL)");
                    table.CheckConstraint("ck_financial_document_deliveries_counts", "claims >= 0 AND send_attempts >= 0");
                    table.CheckConstraint("ck_financial_document_deliveries_waiting", "(waiting_reason IS NULL) = (waiting_since IS NULL) AND (state = 'Queued' OR waiting_reason IS NULL)");
                    table.ForeignKey(
                        name: "fk_financial_document_deliveries_document",
                        column: x => x.document_id,
                        principalTable: "financial_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "financial_document_delivery_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    provider_message_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    english_rendition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    arabic_rendition_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financial_document_delivery_attempts", x => x.id);
                    table.CheckConstraint("ck_financial_document_delivery_attempts_number", "number >= 1");
                    table.ForeignKey(
                        name: "fk_financial_document_delivery_attempts_arabic_rendition",
                        column: x => x.arabic_rendition_id,
                        principalTable: "financial_document_renditions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_financial_document_delivery_attempts_delivery",
                        column: x => x.delivery_id,
                        principalTable: "financial_document_deliveries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_financial_document_delivery_attempts_english_rendition",
                        column: x => x.english_rendition_id,
                        principalTable: "financial_document_renditions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_financial_document_deliveries_document_id_queued_at",
                table: "financial_document_deliveries",
                columns: new[] { "document_id", "queued_at" });

            migrationBuilder.CreateIndex(
                name: "ix_financial_document_deliveries_failed",
                table: "financial_document_deliveries",
                column: "queued_at",
                filter: "state = 'Failed'");

            migrationBuilder.CreateIndex(
                name: "ix_financial_document_deliveries_one_queued",
                table: "financial_document_deliveries",
                column: "document_id",
                unique: true,
                filter: "state = 'Queued'");

            migrationBuilder.CreateIndex(
                name: "ix_financial_document_deliveries_queued_due",
                table: "financial_document_deliveries",
                column: "next_attempt_at",
                filter: "state = 'Queued'");

            migrationBuilder.CreateIndex(
                name: "ix_financial_document_delivery_attempts_arabic_rendition_id",
                table: "financial_document_delivery_attempts",
                column: "arabic_rendition_id");

            migrationBuilder.CreateIndex(
                name: "ix_financial_document_delivery_attempts_delivery_id_number",
                table: "financial_document_delivery_attempts",
                columns: new[] { "delivery_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_financial_document_delivery_attempts_english_rendition_id",
                table: "financial_document_delivery_attempts",
                column: "english_rendition_id");

            migrationBuilder.Sql(CreateAppendOnlyGuards);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only while no real (non-test) document has an email: see the remarks on this class.
            migrationBuilder.Sql(RefuseRollbackOverRealEmails);
            migrationBuilder.Sql(DropAppendOnlyGuards);
            migrationBuilder.DropTable(
                name: "financial_document_delivery_attempts");

            migrationBuilder.DropTable(
                name: "financial_document_deliveries");
        }
    }
}
