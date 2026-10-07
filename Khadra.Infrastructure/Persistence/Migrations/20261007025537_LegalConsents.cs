using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Consents to the published legal texts (Wave 4, W4-8; pre-launch item 224), reviewed by the advisor before it was
    /// written (2026-10-07).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Append-only, in the database as in the application: no UPDATE, no DELETE, no TRUNCATE. A consent is evidence of
    /// what a person agreed to, kept when their account closes (W4-D8, subject to the lawyer's review).
    /// </para>
    /// <para>
    /// <b>Fixed forward, never reverted, once any consent exists.</b> Removing the table is the one thing the triggers
    /// cannot stop, so Down refuses while it holds a row, as the versions it names do.
    /// </para>
    /// <para>
    /// Staging and Production apply schema by hand (docs/sql/README.md): this migration ships in Wave 4's idempotent
    /// script, and the Supabase lockdown script is run again after it, so the new table is not readable through PostgREST.
    /// </para>
    /// </remarks>
    public partial class LegalConsents : Migration
    {
        private const string CreateAppendOnlyGuards = @"
CREATE TRIGGER legal_consents_append_only
BEFORE UPDATE OR DELETE ON legal_consents
FOR EACH ROW EXECUTE FUNCTION khadra_table_is_append_only();

CREATE TRIGGER legal_consents_no_truncate
BEFORE TRUNCATE ON legal_consents
FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();";

        private const string RefuseRollbackOverConsents = @"
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM legal_consents) THEN
        RAISE EXCEPTION 'legal_consents holds consents people gave: this migration is fixed forward, never reverted';
    END IF;
END $$;";

        private const string DropAppendOnlyGuards = @"
DROP TRIGGER IF EXISTS legal_consents_no_truncate ON legal_consents;
DROP TRIGGER IF EXISTS legal_consents_append_only ON legal_consents;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "legal_consents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    language = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legal_consents", x => x.id);
                    table.CheckConstraint("ck_legal_consents_action", "action IN ('Accepted')");
                    table.CheckConstraint("ck_legal_consents_channel", "channel IN ('Website', 'App', 'Console')");
                    table.CheckConstraint("ck_legal_consents_language", "language IN ('ar', 'en')");
                    table.ForeignKey(
                        name: "fk_legal_consents_legal_document_versions_document_version_id",
                        column: x => x.document_version_id,
                        principalTable: "legal_document_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_legal_consents_document_version_id",
                table: "legal_consents",
                column: "document_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_legal_consents_user_id_document_version_id",
                table: "legal_consents",
                columns: new[] { "user_id", "document_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_legal_consents_user_id_occurred_at",
                table: "legal_consents",
                columns: new[] { "user_id", "occurred_at" },
                descending: new[] { false, true });

            // A consent never changes, in the database as in the application.
            migrationBuilder.Sql(CreateAppendOnlyGuards);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only while nobody has consented to anything: see the remarks on this class.
            migrationBuilder.Sql(RefuseRollbackOverConsents);
            migrationBuilder.Sql(DropAppendOnlyGuards);

            migrationBuilder.DropTable(
                name: "legal_consents");
        }
    }
}
