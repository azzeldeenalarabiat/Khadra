using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentCaptureIncidentsAndRefundBackoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "provider_capture_reference",
                table: "payments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_attempt_at",
                table: "payment_refunds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "refusal_count",
                table: "payment_refunds",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // A refund that already reads Failed has been refused at least once (Wave 4, B4; the advisor's D8 review),
            // and "refused 0 times" beside it would be a small lie on the refunds queue. 1 is a FLOOR, not a count:
            // the old sweep re-sent a Failed refund every minute for as long as it stood, so the true number is
            // unknown and at least one. next_attempt_at stays NULL, so it is due on the first tick, as before.
            migrationBuilder.Sql("UPDATE payment_refunds SET refusal_count = 1 WHERE status = 'Failed' AND refusal_count = 0;");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "payment_refunds",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<string>(
                name: "capture_reference",
                table: "payment_provider_events",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "payment_incidents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    capture_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reported_amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    reported_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    expected_amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    expected_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    other_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    handled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    handled_by_admin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    handled_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_incidents", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_incidents_payment_provider_events_receipt_id",
                        column: x => x.receipt_id,
                        principalTable: "payment_provider_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_incidents_payments_payment_id",
                        column: x => x.payment_id,
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_payments_provider_capture_reference",
                table: "payments",
                columns: new[] { "provider", "provider_capture_reference" },
                unique: true,
                filter: "provider_capture_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payment_incidents_detected_at",
                table: "payment_incidents",
                column: "detected_at",
                filter: "handled_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payment_incidents_payment_id",
                table: "payment_incidents",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_incidents_receipt_id",
                table: "payment_incidents",
                column: "receipt_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_incidents");

            migrationBuilder.DropIndex(
                name: "ux_payments_provider_capture_reference",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "provider_capture_reference",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "next_attempt_at",
                table: "payment_refunds");

            migrationBuilder.DropColumn(
                name: "refusal_count",
                table: "payment_refunds");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "payment_refunds");

            migrationBuilder.DropColumn(
                name: "capture_reference",
                table: "payment_provider_events");
        }
    }
}
