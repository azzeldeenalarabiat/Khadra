using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AggregateChildrenDeleteTheirOrphans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_booking_handovers_bookings_booking_id",
                table: "booking_handovers");

            migrationBuilder.DropForeignKey(
                name: "fk_booking_status_changes_bookings_booking_id",
                table: "booking_status_changes");

            migrationBuilder.DropForeignKey(
                name: "fk_customer_documents_users_user_id",
                table: "customer_documents");

            migrationBuilder.DropForeignKey(
                name: "fk_dealer_documents_dealers_dealer_id",
                table: "dealer_documents");

            migrationBuilder.DropForeignKey(
                name: "fk_dealer_employees_dealers_dealer_id",
                table: "dealer_employees");

            migrationBuilder.DropForeignKey(
                name: "fk_dispute_statements_dispute_tickets_ticket_id",
                table: "dispute_statements");

            migrationBuilder.DropForeignKey(
                name: "fk_renter_document_reviews_bookings_booking_id",
                table: "renter_document_reviews");

            migrationBuilder.AddForeignKey(
                name: "fk_booking_handovers_bookings_booking_id",
                table: "booking_handovers",
                column: "booking_id",
                principalTable: "bookings",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_booking_status_changes_bookings_booking_id",
                table: "booking_status_changes",
                column: "booking_id",
                principalTable: "bookings",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_customer_documents_users_user_id",
                table: "customer_documents",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_dealer_documents_dealers_dealer_id",
                table: "dealer_documents",
                column: "dealer_id",
                principalTable: "dealers",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_dealer_employees_dealers_dealer_id",
                table: "dealer_employees",
                column: "dealer_id",
                principalTable: "dealers",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_dispute_statements_dispute_tickets_ticket_id",
                table: "dispute_statements",
                column: "ticket_id",
                principalTable: "dispute_tickets",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_renter_document_reviews_bookings_booking_id",
                table: "renter_document_reviews",
                column: "booking_id",
                principalTable: "bookings",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_booking_handovers_bookings_booking_id",
                table: "booking_handovers");

            migrationBuilder.DropForeignKey(
                name: "fk_booking_status_changes_bookings_booking_id",
                table: "booking_status_changes");

            migrationBuilder.DropForeignKey(
                name: "fk_customer_documents_users_user_id",
                table: "customer_documents");

            migrationBuilder.DropForeignKey(
                name: "fk_dealer_documents_dealers_dealer_id",
                table: "dealer_documents");

            migrationBuilder.DropForeignKey(
                name: "fk_dealer_employees_dealers_dealer_id",
                table: "dealer_employees");

            migrationBuilder.DropForeignKey(
                name: "fk_dispute_statements_dispute_tickets_ticket_id",
                table: "dispute_statements");

            migrationBuilder.DropForeignKey(
                name: "fk_renter_document_reviews_bookings_booking_id",
                table: "renter_document_reviews");

            migrationBuilder.AddForeignKey(
                name: "fk_booking_handovers_bookings_booking_id",
                table: "booking_handovers",
                column: "booking_id",
                principalTable: "bookings",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_booking_status_changes_bookings_booking_id",
                table: "booking_status_changes",
                column: "booking_id",
                principalTable: "bookings",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_customer_documents_users_user_id",
                table: "customer_documents",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_dealer_documents_dealers_dealer_id",
                table: "dealer_documents",
                column: "dealer_id",
                principalTable: "dealers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_dealer_employees_dealers_dealer_id",
                table: "dealer_employees",
                column: "dealer_id",
                principalTable: "dealers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_dispute_statements_dispute_tickets_ticket_id",
                table: "dispute_statements",
                column: "ticket_id",
                principalTable: "dispute_tickets",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_renter_document_reviews_bookings_booking_id",
                table: "renter_document_reviews",
                column: "booking_id",
                principalTable: "bookings",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
