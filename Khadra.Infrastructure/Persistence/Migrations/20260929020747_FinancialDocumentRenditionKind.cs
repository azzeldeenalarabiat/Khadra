using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Which PDF of a document a rendition is (payments Phase 6 follow-up, owner 2026-09-29): the document AS ISSUED,
    /// or the VOIDED COPY its customer is given once it is voided — stamped VOID / ملغى and naming its correction — so
    /// the original bytes are never touched and never handed to the customer. Additive: one column, and the unique
    /// index widened by it so a document can hold both kinds in each language.
    /// </summary>
    /// <remarks>
    /// Every row already here is a PDF of a document as issued — nothing else was ever drawn — so the column arrives
    /// holding <c>AsIssued</c>, and the default that put it there is dropped at once: from then on every row says
    /// which kind it is. Adding a column with a constant default rewrites no row and fires no trigger, so the
    /// append-only guards on the table stand untouched throughout.
    ///
    /// Down refuses once any voided copy exists. Dropping the column would leave two rows for one document,
    /// language and template, which the narrower index refuses — and would make a voided copy indistinguishable from
    /// the document as issued, the one thing this column exists to keep apart. The rows cannot be deleted (the table
    /// is append-only), so from the first voided copy on, this migration is fixed forward.
    /// </remarks>
    public partial class FinancialDocumentRenditionKind : Migration
    {
        private const string DropKindDefault = @"
ALTER TABLE financial_document_renditions ALTER COLUMN kind DROP DEFAULT;";

        private const string RefuseRollbackOverVoidedCopies = @"
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM financial_document_renditions WHERE kind <> 'AsIssued') THEN
        RAISE EXCEPTION 'financial_document_renditions holds voided copies: this migration is fixed forward, never reverted';
    END IF;
END $$;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_financial_document_renditions_document_id_language_format_t",
                table: "financial_document_renditions");

            // Every existing row is a PDF of the document as issued; the default fills them and is then dropped.
            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "financial_document_renditions",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "AsIssued");

            migrationBuilder.Sql(DropKindDefault);

            migrationBuilder.CreateIndex(
                name: "ix_financial_document_renditions_document_id_language_format_k",
                table: "financial_document_renditions",
                columns: new[] { "document_id", "language", "format", "kind", "template_version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only while no voided copy exists: see the remarks on this class.
            migrationBuilder.Sql(RefuseRollbackOverVoidedCopies);

            migrationBuilder.DropIndex(
                name: "ix_financial_document_renditions_document_id_language_format_k",
                table: "financial_document_renditions");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "financial_document_renditions");

            migrationBuilder.CreateIndex(
                name: "ix_financial_document_renditions_document_id_language_format_t",
                table: "financial_document_renditions",
                columns: new[] { "document_id", "language", "format", "template_version" },
                unique: true);
        }
    }
}
