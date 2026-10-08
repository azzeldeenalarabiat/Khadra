using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SignInThrottle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sign_in_throttles",
                columns: table => new
                {
                    subject_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    window_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    failures = table.Column<int>(type: "integer", nullable: false),
                    blocked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sign_in_throttles", x => x.subject_hash);
                    table.CheckConstraint("ck_sign_in_throttles_failures", "failures >= 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_sign_in_throttles_window_started_at",
                table: "sign_in_throttles",
                column: "window_started_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sign_in_throttles");
        }
    }
}
