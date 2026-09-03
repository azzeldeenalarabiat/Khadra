using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DealersBookingsDisputesAndAuditing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    actor_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    previous_value = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    new_value = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dealer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    pickup_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    delivery_latitude = table.Column<double>(type: "double precision", nullable: true),
                    delivery_longitude = table.Column<double>(type: "double precision", nullable: true),
                    payment_option = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    deposit_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_by = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    extended_from_booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    payment_deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    free_cancellation_deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    picked_up_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    returned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    penalty = table.Column<string>(type: "jsonb", nullable: true),
                    pricing = table.Column<string>(type: "jsonb", nullable: false),
                    terms = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dealers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    commercial_registration = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    city_id = table.Column<Guid>(type: "uuid", nullable: true),
                    operating_hours = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    verification_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    review_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    reviewed_by_admin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    review_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivery_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    delivery_radius_km = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    logo_storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cover_storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_suspended = table.Column<bool>(type: "boolean", nullable: false),
                    suspension_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dealers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dispute_tickets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_by_party = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sla_deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    assigned_admin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispute_tickets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "booking_handovers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    recorded_by = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    odometer_km = table.Column<int>(type: "integer", nullable: true),
                    fuel_level = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    cash_collected_amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    cash_collected_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    photo_storage_keys = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_handovers", x => x.id);
                    table.ForeignKey(
                        name: "fk_booking_handovers_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "booking_status_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    to = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_party = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_status_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_booking_status_changes_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dealer_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dealer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dealer_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_dealer_documents_dealers_dealer_id",
                        column: x => x.dealer_id,
                        principalTable: "dealers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dealer_employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dealer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    can_view_reports = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dealer_employees", x => x.id);
                    table.ForeignKey(
                        name: "fk_dealer_employees_dealers_dealer_id",
                        column: x => x.dealer_id,
                        principalTable: "dealers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dispute_statements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    party = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    evidence_storage_keys = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispute_statements", x => x.id);
                    table.ForeignKey(
                        name: "fk_dispute_statements_dispute_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "dispute_tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_actor_user_id",
                table: "audit_entries",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_entity_type_entity_id",
                table: "audit_entries",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_occurred_at",
                table: "audit_entries",
                column: "occurred_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_booking_handovers_booking_id_type",
                table: "booking_handovers",
                columns: new[] { "booking_id", "type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_booking_status_changes_booking_id_occurred_at",
                table: "booking_status_changes",
                columns: new[] { "booking_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_created_at",
                table: "bookings",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_bookings_customer_id",
                table: "bookings",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_dealer_id",
                table: "bookings",
                column: "dealer_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_reference",
                table: "bookings",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bookings_status",
                table: "bookings",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_dealer_documents_dealer_id_type",
                table: "dealer_documents",
                columns: new[] { "dealer_id", "type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dealer_employees_dealer_id_user_id",
                table: "dealer_employees",
                columns: new[] { "dealer_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dealers_commercial_registration",
                table: "dealers",
                column: "commercial_registration",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dealers_owner_user_id",
                table: "dealers",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_dealers_review_due_at",
                table: "dealers",
                column: "review_due_at");

            migrationBuilder.CreateIndex(
                name: "ix_dealers_verification_status",
                table: "dealers",
                column: "verification_status");

            migrationBuilder.CreateIndex(
                name: "ix_dispute_statements_ticket_id_created_at",
                table: "dispute_statements",
                columns: new[] { "ticket_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_dispute_tickets_booking_id",
                table: "dispute_tickets",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_dispute_tickets_sla_deadline",
                table: "dispute_tickets",
                column: "sla_deadline");

            migrationBuilder.CreateIndex(
                name: "ix_dispute_tickets_status",
                table: "dispute_tickets",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entries");

            migrationBuilder.DropTable(
                name: "booking_handovers");

            migrationBuilder.DropTable(
                name: "booking_status_changes");

            migrationBuilder.DropTable(
                name: "dealer_documents");

            migrationBuilder.DropTable(
                name: "dealer_employees");

            migrationBuilder.DropTable(
                name: "dispute_statements");

            migrationBuilder.DropTable(
                name: "bookings");

            migrationBuilder.DropTable(
                name: "dealers");

            migrationBuilder.DropTable(
                name: "dispute_tickets");
        }
    }
}
