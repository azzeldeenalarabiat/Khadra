using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditTrailRefusesTruncate : Migration
    {
        // Pre-launch item 1 (owner, 2026-10-08). `audit_entries` has refused UPDATE and DELETE since
        // AuditTrailImmutability, but a row trigger does not fire on TRUNCATE, so anyone holding table
        // privileges could still erase the whole trail in one statement. This is the statement guard every
        // later append-only table was born with (document_access_entries_no_truncate and the rest).
        //
        // It reuses the table's own function, so the refusal reads like the other two:
        // "audit_entries is append-only: TRUNCATE is not permitted".
        //
        // Safe on a database that already holds a trail: creating a trigger reads and writes no row, so no
        // entry is altered or rewritten. The DROP first makes it safe to apply where somebody has already
        // added a trigger of this name by hand. Nothing in the application or the tooling truncates this
        // table; the seeder whose reseed did was deleted on 2026-09-05.
        private const string CreateGuard = @"
DROP TRIGGER IF EXISTS audit_entries_no_truncate ON audit_entries;

CREATE TRIGGER audit_entries_no_truncate
BEFORE TRUNCATE ON audit_entries
FOR EACH STATEMENT EXECUTE FUNCTION khadra_audit_entries_are_append_only();";

        // Removes only what Up added: the row guard and its function belong to AuditTrailImmutability.
        private const string DropGuard = @"
DROP TRIGGER IF EXISTS audit_entries_no_truncate ON audit_entries;";

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
