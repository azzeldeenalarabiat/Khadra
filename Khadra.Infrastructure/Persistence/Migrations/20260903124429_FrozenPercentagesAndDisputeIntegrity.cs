using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FrozenPercentagesAndDisputeIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_dispute_tickets_booking_id",
                table: "dispute_tickets");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "dispute_tickets",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "bookings",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateIndex(
                name: "ix_dispute_tickets_booking_id",
                table: "dispute_tickets",
                column: "booking_id",
                unique: true,
                filter: "status IN ('Open', 'UnderReview')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_dispute_tickets_booking_id",
                table: "dispute_tickets");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "dispute_tickets");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "bookings");

            migrationBuilder.CreateIndex(
                name: "ix_dispute_tickets_booking_id",
                table: "dispute_tickets",
                column: "booking_id");
        }
    }
}
