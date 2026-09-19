using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DealerPublicProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "customer_notes",
                table: "dealers",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "delivery_notes",
                table: "dealers",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hidden_profile_sections",
                table: "dealers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValueSql: "''");

            migrationBuilder.AddColumn<string>(
                name: "insurance_summary",
                table: "dealers",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pickup_instructions",
                table: "dealers",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rental_conditions",
                table: "dealers",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "customer_notes",
                table: "dealers");

            migrationBuilder.DropColumn(
                name: "delivery_notes",
                table: "dealers");

            migrationBuilder.DropColumn(
                name: "hidden_profile_sections",
                table: "dealers");

            migrationBuilder.DropColumn(
                name: "insurance_summary",
                table: "dealers");

            migrationBuilder.DropColumn(
                name: "pickup_instructions",
                table: "dealers");

            migrationBuilder.DropColumn(
                name: "rental_conditions",
                table: "dealers");
        }
    }
}
