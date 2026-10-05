using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The published legal texts (Wave 2 G1; pre-launch item 224), reviewed by the advisor before it was written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Append-only, in the database as in the application: no UPDATE, no DELETE, no TRUNCATE. A published version is
    /// evidence of what people were shown, and from Wave 4 of what they agreed to.
    /// </para>
    /// <para>
    /// <b>Fixed forward, never reverted, once any version exists.</b> Removing the table is the one thing the triggers
    /// cannot stop, so Down refuses while it holds a row. There is no TEST- marker here to tell practice from record:
    /// every row counts.
    /// </para>
    /// <para>
    /// Staging and Production apply schema by hand (docs/sql/README.md): this migration ships as an idempotent script,
    /// and the Supabase lockdown script is run again after it, so the new table is not readable through PostgREST.
    /// </para>
    /// </remarks>
    public partial class LegalDocumentVersions : Migration
    {
        private const string CreateAppendOnlyGuards = @"
CREATE TRIGGER legal_document_versions_append_only
BEFORE UPDATE OR DELETE ON legal_document_versions
FOR EACH ROW EXECUTE FUNCTION khadra_table_is_append_only();

CREATE TRIGGER legal_document_versions_no_truncate
BEFORE TRUNCATE ON legal_document_versions
FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();";

        private const string RefuseRollbackOverPublishedVersions = @"
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM legal_document_versions) THEN
        RAISE EXCEPTION 'legal_document_versions holds published legal texts: this migration is fixed forward, never reverted';
    END IF;
END $$;";

        private const string DropAppendOnlyGuards = @"
DROP TRIGGER IF EXISTS legal_document_versions_no_truncate ON legal_document_versions;
DROP TRIGGER IF EXISTS legal_document_versions_append_only ON legal_document_versions;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "legal_document_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    version_label = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_by_admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body_en = table.Column<string>(type: "text", nullable: false),
                    body_ar = table.Column<string>(type: "text", nullable: false),
                    body_en_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    body_ar_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legal_document_versions", x => x.id);
                    table.CheckConstraint("ck_legal_document_versions_bodies", "length(body_en) BETWEEN 1 AND 200000 AND length(body_ar) BETWEEN 1 AND 200000");
                    table.CheckConstraint("ck_legal_document_versions_effective_from", "effective_from >= published_at");
                    table.CheckConstraint("ck_legal_document_versions_hashes", "length(body_en_sha256) = 64 AND length(body_ar_sha256) = 64");
                    table.CheckConstraint("ck_legal_document_versions_kind", "kind IN ('Terms', 'Privacy')");
                    table.CheckConstraint("ck_legal_document_versions_version_label", "length(version_label) BETWEEN 1 AND 40 AND version_label = trim(version_label)");
                });

            migrationBuilder.CreateIndex(
                name: "ux_legal_document_versions_kind_effective_from",
                table: "legal_document_versions",
                columns: new[] { "kind", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_legal_document_versions_kind_version_label",
                table: "legal_document_versions",
                columns: new[] { "kind", "version_label" },
                unique: true);

            // A published version never changes, in the database as in the application.
            migrationBuilder.Sql(CreateAppendOnlyGuards);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only while nothing has been published: see the remarks on this class.
            migrationBuilder.Sql(RefuseRollbackOverPublishedVersions);
            migrationBuilder.Sql(DropAppendOnlyGuards);

            migrationBuilder.DropTable(
                name: "legal_document_versions");
        }
    }
}
