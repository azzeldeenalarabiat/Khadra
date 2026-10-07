using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BookingDisputeWindowEnd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "dispute_window_ends_at",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_bookings_status_dispute_window_ends_at",
                table: "bookings",
                columns: new[] { "status", "dispute_window_ends_at" },
                filter: "dispute_window_ends_at IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bookings_status_dispute_window_ends_at",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "dispute_window_ends_at",
                table: "bookings");
        }
    }
}
