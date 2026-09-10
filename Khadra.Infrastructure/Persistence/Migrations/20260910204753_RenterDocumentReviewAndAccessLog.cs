using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenterDocumentReviewAndAccessLog : Migration
    {
        // The disclosure log is append-only in the database as well as in the application
        // (KhadraDbContext refuses to persist a modified or deleted IAppendOnly record). A record
        // the application can quietly rewrite is not a record, and neither is one a migration script
        // or a psql prompt can.
        //
        // Its own function rather than reusing khadra_audit_entries_are_append_only, whose NAME
        // asserts a table: this one is generic and the next append-only table can share it.
        //
        // The TRUNCATE guard is here too, which item 1 records as missing on `audit_entries`. Row
        // triggers do not fire on TRUNCATE, so without it anyone holding table privileges could erase
        // the whole log in one statement -- precisely the record that has to survive an argument about
        // who saw a customer's passport. It is added HERE and not on `audit_entries` because that one
        // has its own owner decision attached (the reseed workflow item 1 describes); this table is
        // new, nothing truncates it, and there is nothing to weigh.
        private const string CreateGuards = @"
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
FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();";

        private const string DropGuards = @"
DROP TRIGGER IF EXISTS document_access_entries_no_truncate ON document_access_entries;
DROP TRIGGER IF EXISTS document_access_entries_append_only ON document_access_entries;
DROP FUNCTION IF EXISTS khadra_table_is_append_only();";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_access_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    actor_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    dealer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    document_uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_access_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "renter_document_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    document_uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_renter_document_reviews", x => x.id);
                    table.ForeignKey(
                        name: "fk_renter_document_reviews_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_access_entries_booking_id_occurred_at",
                table: "document_access_entries",
                columns: new[] { "booking_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_document_access_entries_dealer_id_occurred_at_id",
                table: "document_access_entries",
                columns: new[] { "dealer_id", "occurred_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_document_access_entries_subject_user_id_occurred_at_id",
                table: "document_access_entries",
                columns: new[] { "subject_user_id", "occurred_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_renter_document_reviews_booking_id_document_id_document_upl",
                table: "renter_document_reviews",
                columns: new[] { "booking_id", "document_id", "document_uploaded_at" },
                unique: true);

            migrationBuilder.Sql(CreateGuards);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DropGuards);

            migrationBuilder.DropTable(
                name: "document_access_entries");

            migrationBuilder.DropTable(
                name: "renter_document_reviews");
        }
    }
}
