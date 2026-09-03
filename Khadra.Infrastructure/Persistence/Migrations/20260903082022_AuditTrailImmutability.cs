using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditTrailImmutability : Migration
    {
        // The application already refuses to modify or delete an audit entry
        // (KhadraDbContext.GuardAuditTrailIsAppendOnly). This makes the database refuse it too, so the
        // guarantee survives a future handler, a migration script, or someone at a psql prompt.
        private const string CreateGuard = @"
CREATE OR REPLACE FUNCTION khadra_audit_entries_are_append_only()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'audit_entries is append-only: % is not permitted', TG_OP;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER audit_entries_append_only
BEFORE UPDATE OR DELETE ON audit_entries
FOR EACH ROW EXECUTE FUNCTION khadra_audit_entries_are_append_only();";

        private const string DropGuard = @"
DROP TRIGGER IF EXISTS audit_entries_append_only ON audit_entries;
DROP FUNCTION IF EXISTS khadra_audit_entries_are_append_only();";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(CreateGuard);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DropGuard);
        }
    }
}
