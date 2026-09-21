using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChildCollectionsDeleteTheirOrphans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_shortlist_entries_customer_shortlists_shortlist_id",
                table: "shortlist_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_vehicle_images_vehicles_vehicle_id",
                table: "vehicle_images");

            migrationBuilder.AddForeignKey(
                name: "fk_shortlist_entries_customer_shortlists_shortlist_id",
                table: "shortlist_entries",
                column: "shortlist_id",
                principalTable: "customer_shortlists",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_vehicle_images_vehicles_vehicle_id",
                table: "vehicle_images",
                column: "vehicle_id",
                principalTable: "vehicles",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_shortlist_entries_customer_shortlists_shortlist_id",
                table: "shortlist_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_vehicle_images_vehicles_vehicle_id",
                table: "vehicle_images");

            migrationBuilder.AddForeignKey(
                name: "fk_shortlist_entries_customer_shortlists_shortlist_id",
                table: "shortlist_entries",
                column: "shortlist_id",
                principalTable: "customer_shortlists",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_vehicle_images_vehicles_vehicle_id",
                table: "vehicle_images",
                column: "vehicle_id",
                principalTable: "vehicles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
