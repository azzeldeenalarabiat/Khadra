using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// An administrator may open and reject a renter's document (Wave 4, W4-9; checklist 27), reviewed by the advisor
    /// before it was written (2026-10-07).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>document_access_entries</c> names a dealership and a booking for every office view; an administrator's view has
    /// neither, so both columns admit NULL, and only for an administrator (<c>ck_document_access_entries_scope</c>). The
    /// table stays append-only: the triggers refuse rows, not DDL.
    /// </para>
    /// <para>
    /// <c>customer_documents</c> gains the <c>xmin</c> concurrency token, a system column, so the model's
    /// <c>AddColumn</c> is a no-op on PostgreSQL, as Wave 4's refund token was: an administrator rejecting a file while
    /// the customer replaces it is caught on the document's own row.
    /// </para>
    /// <para>
    /// <b>Down refuses once an administrator's view is recorded</b> (the advisor's review): making the columns NOT NULL
    /// again would fail on that row, and the row is a disclosure record that may never be removed.
    /// </para>
    /// </remarks>
    public partial class AdminDocumentAccess : Migration
    {
        private const string RefuseRollbackOverAdminViews = @"
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM document_access_entries WHERE dealer_id IS NULL OR booking_id IS NULL) THEN
        RAISE EXCEPTION 'document_access_entries records an administrator''s view of a document: this migration is fixed forward, never reverted';
    END IF;
END $$;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "dealer_id",
                table: "document_access_entries",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "booking_id",
                table: "document_access_entries",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "customer_documents",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_access_entries_scope",
                table: "document_access_entries",
                sql: "(dealer_id IS NOT NULL AND booking_id IS NOT NULL) OR actor_role = 'Admin'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only while no administrator has opened a document: see the remarks on this class.
            migrationBuilder.Sql(RefuseRollbackOverAdminViews);

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_access_entries_scope",
                table: "document_access_entries");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "customer_documents");

            migrationBuilder.AlterColumn<Guid>(
                name: "dealer_id",
                table: "document_access_entries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "booking_id",
                table: "document_access_entries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
