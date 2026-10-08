using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Two nullable columns, each filled only for rows written from now on (Fix &amp; Polish Wave 6): a city's or car
    /// type's Arabic name beside an audit entry's English subject (pre-launch item 176), and the code of a notification
    /// actor that is a stand-in rather than a name (item 103). Adding a nullable column without a default changes only
    /// the catalogue in PostgreSQL: no existing row is rewritten, so the append-only audit trail's row triggers never
    /// fire and every audit record keeps exactly what it held.
    /// </summary>
    public partial class ArabicAuditSubjectAndNotificationStandIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "actor_stand_in",
                table: "notifications",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "subject_label_ar",
                table: "audit_entries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "actor_stand_in",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "subject_label_ar",
                table: "audit_entries");
        }
    }
}
