using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditLogIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_audit_entries_actor_user_id",
                table: "audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_audit_entries_occurred_at",
                table: "audit_entries");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_action_occurred_at_id",
                table: "audit_entries",
                columns: new[] { "action", "occurred_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_actor_user_id_occurred_at_id",
                table: "audit_entries",
                columns: new[] { "actor_user_id", "occurred_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_occurred_at_id",
                table: "audit_entries",
                columns: new[] { "occurred_at", "id" },
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_audit_entries_action_occurred_at_id",
                table: "audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_audit_entries_actor_user_id_occurred_at_id",
                table: "audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_audit_entries_occurred_at_id",
                table: "audit_entries");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_actor_user_id",
                table: "audit_entries",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_occurred_at",
                table: "audit_entries",
                column: "occurred_at",
                descending: new bool[0]);
        }
    }
}
