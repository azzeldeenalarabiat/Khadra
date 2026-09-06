using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OneDealerPerOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_dealers_owner_user_id",
                table: "dealers");

            migrationBuilder.CreateIndex(
                name: "ix_dealers_owner_user_id",
                table: "dealers",
                column: "owner_user_id",
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_dealers_owner_user_id",
                table: "dealers");

            migrationBuilder.CreateIndex(
                name: "ix_dealers_owner_user_id",
                table: "dealers",
                column: "owner_user_id");
        }
    }
}
