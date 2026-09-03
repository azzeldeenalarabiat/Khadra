using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FleetVehicles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vehicles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dealer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    car_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    make = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    model = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    color = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    seats = table.Column<int>(type: "integer", nullable: false),
                    transmission = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fuel_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    plate_number = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    daily_rate = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    daily_rate_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    security_deposit = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    security_deposit_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    is_delivery_eligible = table.Column<bool>(type: "boolean", nullable: false),
                    mileage_unlimited = table.Column<bool>(type: "boolean", nullable: false),
                    mileage_daily_limit_km = table.Column<int>(type: "integer", nullable: true),
                    mileage_excess_fee = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    mileage_excess_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    fuel_policy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vehicle_images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicle_images", x => x.id);
                    table.ForeignKey(
                        name: "fk_vehicle_images_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_images_vehicle_id_position",
                table: "vehicle_images",
                columns: new[] { "vehicle_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_dealer_id",
                table: "vehicles",
                column: "dealer_id");

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_plate_number",
                table: "vehicles",
                column: "plate_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_status",
                table: "vehicles",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vehicle_images");

            migrationBuilder.DropTable(
                name: "vehicles");
        }
    }
}
