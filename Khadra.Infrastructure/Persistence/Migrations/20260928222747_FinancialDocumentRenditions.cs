using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The PDF renditions of issued financial documents (payments Phase 6): one additive table, append-only
    /// like the documents it renders — the same database triggers refuse UPDATE, DELETE and TRUNCATE.
    /// </summary>
    /// <remarks>
    /// Its rollback is guarded as <c>20260926230609_FinancialDocuments</c>'s is. A rendition is derived from
    /// the record and could be drawn again, but the row is also the only proof of WHICH bytes a customer was
    /// handed, and DROP TABLE is the one thing the append-only triggers cannot stop — so Down refuses once any
    /// rendition belongs to a real (non-test) document, and from then on this migration is fixed forward. Over
    /// test documents it runs, and the stored bytes the rows pointed at are left in document storage.
    /// </remarks>
    public partial class FinancialDocumentRenditions : Migration
    {
        private const string RefuseRollbackOverRealRenditions = @"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM financial_document_renditions rendition
        JOIN financial_documents document ON document.id = rendition.document_id
        WHERE document.document_number NOT LIKE 'TEST-%') THEN
        RAISE EXCEPTION 'financial_document_renditions records PDFs of real documents: this migration is fixed forward, never reverted';
    END IF;
END $$;";

        private const string CreateAppendOnlyGuards = @"
CREATE TRIGGER financial_document_renditions_append_only
BEFORE UPDATE OR DELETE ON financial_document_renditions
FOR EACH ROW EXECUTE FUNCTION khadra_table_is_append_only();

CREATE TRIGGER financial_document_renditions_no_truncate
BEFORE TRUNCATE ON financial_document_renditions
FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();";

        private const string DropAppendOnlyGuards = @"
DROP TRIGGER IF EXISTS financial_document_renditions_no_truncate ON financial_document_renditions;
DROP TRIGGER IF EXISTS financial_document_renditions_append_only ON financial_document_renditions;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "financial_document_renditions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    language = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    template_version = table.Column<int>(type: "integer", nullable: false),
                    renderer_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    content_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    snapshot_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    rendered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financial_document_renditions", x => x.id);
                    table.CheckConstraint("ck_financial_document_renditions_size_bytes", "size_bytes > 0");
                    table.CheckConstraint("ck_financial_document_renditions_template_version", "template_version >= 1");
                    table.ForeignKey(
                        name: "fk_financial_document_renditions_financial_documents_document_",
                        column: x => x.document_id,
                        principalTable: "financial_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_financial_document_renditions_document_id_language_format_t",
                table: "financial_document_renditions",
                columns: new[] { "document_id", "language", "format", "template_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_financial_document_renditions_storage_key",
                table: "financial_document_renditions",
                column: "storage_key",
                unique: true);

            migrationBuilder.Sql(CreateAppendOnlyGuards);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only while no real (non-test) document has a rendition: see the remarks on this class.
            migrationBuilder.Sql(RefuseRollbackOverRealRenditions);
            migrationBuilder.Sql(DropAppendOnlyGuards);
            migrationBuilder.DropTable(
                name: "financial_document_renditions");
        }
    }
}
