using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerShortlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_shortlists",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_shortlists", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shortlist_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shortlist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    saved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shortlist_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_shortlist_entries_customer_shortlists_shortlist_id",
                        column: x => x.shortlist_id,
                        principalTable: "customer_shortlists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shortlist_entries_shortlist_id_saved_at",
                table: "shortlist_entries",
                columns: new[] { "shortlist_id", "saved_at" });

            migrationBuilder.CreateIndex(
                name: "ix_shortlist_entries_shortlist_id_vehicle_id",
                table: "shortlist_entries",
                columns: new[] { "shortlist_id", "vehicle_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shortlist_entries");

            migrationBuilder.DropTable(
                name: "customer_shortlists");
        }
    }
}
