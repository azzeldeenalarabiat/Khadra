using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The office payables ledger (payments Phase 8; owner, 2026-09-24 and 2026-09-29): six additive tables — each final
    /// paid booking's payable and its lines, the settlements an administrator records by hand with their lines and
    /// voids, and the holds — and the index every ticket of a booking is read by (pre-launch item 171).
    /// </summary>
    /// <remarks>
    /// Hand-edited beyond what EF generates, for what EF does not model:
    /// <list type="number">
    /// <item>PostgreSQL-only CHECKs on money (not in the EF model: SQLite, which the unit tests run on, compares
    /// decimals as floating point): a payable's figures are non-negative, its commission never more than the office's
    /// money, and its net exactly that money less the commission and the charges; a line is positive; a settlement's
    /// sign is its direction.</item>
    /// <item>A payable is FROZEN: a row trigger refuses any UPDATE that touches anything but its settlement (and the
    /// shadow <c>updated_at</c>), moving it from one settlement straight to another, rewriting when it was settled,
    /// and every DELETE; a statement trigger refuses TRUNCATE. Lines, settlements, settlement lines and voids are append-only with the shared
    /// function. Holds change — they are opened, checked and released — but are never deleted.</item>
    /// <item>Down REFUSES to run once any payable or settlement holds real (non-SANDBOX) money: the triggers stop
    /// UPDATE, DELETE and TRUNCATE, but not DROP TABLE. Over test money it runs, and drops its own functions — never
    /// the shared one.</item>
    /// </list>
    /// </remarks>
    public partial class OfficePayables : Migration
    {
        private const string CreateMoneyChecks = @"
ALTER TABLE office_payables ADD CONSTRAINT ck_office_payables_figures
    CHECK (office_money >= 0 AND commission >= 0 AND office_charges >= 0
           AND commission <= office_money
           AND net = office_money - commission - office_charges);

ALTER TABLE office_payable_lines ADD CONSTRAINT ck_office_payable_lines_amount
    CHECK (amount > 0);

ALTER TABLE office_settlements ADD CONSTRAINT ck_office_settlements_direction
    CHECK ((direction = 'Payout' AND amount > 0)
        OR (direction = 'Received' AND amount < 0)
        OR (direction = 'Netted' AND amount = 0));";

        private const string CreateGuards = @"
CREATE FUNCTION khadra_office_payable_is_frozen()
RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'office_payables is a ledger: DELETE is not permitted';
    END IF;
    IF (to_jsonb(NEW) - 'settlement_id' - 'settled_at' - 'updated_at')
       IS DISTINCT FROM (to_jsonb(OLD) - 'settlement_id' - 'settled_at' - 'updated_at') THEN
        RAISE EXCEPTION 'office_payables: a payable''s figures are frozen; only its settlement may change';
    END IF;
    IF OLD.settlement_id IS NOT NULL AND NEW.settlement_id IS NOT NULL AND OLD.settlement_id <> NEW.settlement_id THEN
        RAISE EXCEPTION 'office_payables: a payable leaves a settlement only when that settlement is voided';
    END IF;
    IF OLD.settlement_id IS NOT DISTINCT FROM NEW.settlement_id AND OLD.settled_at IS DISTINCT FROM NEW.settled_at THEN
        RAISE EXCEPTION 'office_payables: when a payable was settled is part of its settlement, and never rewritten';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE FUNCTION khadra_office_payable_hold_is_kept()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'office_payable_holds keeps every hold, released or not: % is not permitted', TG_OP;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER office_payables_frozen
BEFORE UPDATE OR DELETE ON office_payables
FOR EACH ROW EXECUTE FUNCTION khadra_office_payable_is_frozen();

CREATE TRIGGER office_payables_no_truncate
BEFORE TRUNCATE ON office_payables
FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();

CREATE TRIGGER office_payable_lines_append_only
BEFORE UPDATE OR DELETE ON office_payable_lines
FOR EACH ROW EXECUTE FUNCTION khadra_table_is_append_only();

CREATE TRIGGER office_payable_lines_no_truncate
BEFORE TRUNCATE ON office_payable_lines
FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();

CREATE TRIGGER office_settlements_append_only
BEFORE UPDATE OR DELETE ON office_settlements
FOR EACH ROW EXECUTE FUNCTION khadra_table_is_append_only();

CREATE TRIGGER office_settlements_no_truncate
BEFORE TRUNCATE ON office_settlements
FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();

CREATE TRIGGER office_settlement_lines_append_only
BEFORE UPDATE OR DELETE ON office_settlement_lines
FOR EACH ROW EXECUTE FUNCTION khadra_table_is_append_only();

CREATE TRIGGER office_settlement_lines_no_truncate
BEFORE TRUNCATE ON office_settlement_lines
FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();

CREATE TRIGGER office_settlement_voids_append_only
BEFORE UPDATE OR DELETE ON office_settlement_voids
FOR EACH ROW EXECUTE FUNCTION khadra_table_is_append_only();

CREATE TRIGGER office_settlement_voids_no_truncate
BEFORE TRUNCATE ON office_settlement_voids
FOR EACH STATEMENT EXECUTE FUNCTION khadra_table_is_append_only();

CREATE TRIGGER office_payable_holds_kept
BEFORE DELETE ON office_payable_holds
FOR EACH ROW EXECUTE FUNCTION khadra_office_payable_hold_is_kept();

CREATE TRIGGER office_payable_holds_no_truncate
BEFORE TRUNCATE ON office_payable_holds
FOR EACH STATEMENT EXECUTE FUNCTION khadra_office_payable_hold_is_kept();";

        private const string RefuseRollbackOverRealMoney = @"
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM office_payables WHERE provider <> 'SANDBOX')
       OR EXISTS (SELECT 1 FROM office_settlements WHERE provider <> 'SANDBOX') THEN
        RAISE EXCEPTION 'office_payables records real money owed to or by offices: this migration is fixed forward, never reverted';
    END IF;
END $$;";

        // The tables' own triggers go with them; the two functions this migration created do not. The shared
        // khadra_table_is_append_only() belongs to earlier migrations and stays.
        private const string DropGuards = @"
DROP TRIGGER IF EXISTS office_payable_holds_no_truncate ON office_payable_holds;
DROP TRIGGER IF EXISTS office_payable_holds_kept ON office_payable_holds;
DROP TRIGGER IF EXISTS office_settlement_voids_no_truncate ON office_settlement_voids;
DROP TRIGGER IF EXISTS office_settlement_voids_append_only ON office_settlement_voids;
DROP TRIGGER IF EXISTS office_settlement_lines_no_truncate ON office_settlement_lines;
DROP TRIGGER IF EXISTS office_settlement_lines_append_only ON office_settlement_lines;
DROP TRIGGER IF EXISTS office_settlements_no_truncate ON office_settlements;
DROP TRIGGER IF EXISTS office_settlements_append_only ON office_settlements;
DROP TRIGGER IF EXISTS office_payable_lines_no_truncate ON office_payable_lines;
DROP TRIGGER IF EXISTS office_payable_lines_append_only ON office_payable_lines;
DROP TRIGGER IF EXISTS office_payables_no_truncate ON office_payables;
DROP TRIGGER IF EXISTS office_payables_frozen ON office_payables;";

        private const string DropFunctions = @"
DROP FUNCTION IF EXISTS khadra_office_payable_hold_is_kept();
DROP FUNCTION IF EXISTS khadra_office_payable_is_frozen();";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "office_settlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    settlement_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    dealer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recorded_by_admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_office_settlements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "office_payables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dealer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_reference = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    final_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    office_money = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    commission = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    office_charges = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    net = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    calculator_version = table.Column<int>(type: "integer", nullable: false),
                    settlement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_office_payables", x => x.id);
                    table.CheckConstraint("ck_office_payables_calculator_version", "calculator_version >= 1");
                    table.CheckConstraint("ck_office_payables_settled", "(settlement_id IS NULL) = (settled_at IS NULL)");
                    table.ForeignKey(
                        name: "fk_office_payables_settlement",
                        column: x => x.settlement_id,
                        principalTable: "office_settlements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "office_settlement_voids",
                columns: table => new
                {
                    settlement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    voided_by_admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_office_settlement_voids", x => x.settlement_id);
                    table.ForeignKey(
                        name: "fk_office_settlement_voids_settlement",
                        column: x => x.settlement_id,
                        principalTable: "office_settlements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "office_payable_holds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dealer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payable_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    opened_by_admin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    checks = table.Column<int>(type: "integer", nullable: false),
                    last_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    released_by_admin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    release_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_office_payable_holds", x => x.id);
                    table.CheckConstraint("ck_office_payable_holds_checks", "checks >= 0");
                    table.CheckConstraint("ck_office_payable_holds_manual", "(reason = 'Manual') = (opened_by_admin_id IS NOT NULL) AND (released_by_admin_id IS NULL OR reason = 'Manual')");
                    table.CheckConstraint("ck_office_payable_holds_payable", "(payable_id IS NULL) = (reason IN ('NeedsReview', 'PenaltyNotWholeDeposit'))");
                    table.ForeignKey(
                        name: "fk_office_payable_holds_payable",
                        column: x => x.payable_id,
                        principalTable: "office_payables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "office_payable_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_office_payable_lines", x => x.id);
                    table.CheckConstraint("ck_office_payable_lines_position", "position >= 1");
                    table.ForeignKey(
                        name: "fk_office_payable_lines_payable",
                        column: x => x.payable_id,
                        principalTable: "office_payables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "office_settlement_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    settlement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    net = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_office_settlement_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_office_settlement_lines_payable",
                        column: x => x.payable_id,
                        principalTable: "office_payables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_office_settlement_lines_settlement",
                        column: x => x.settlement_id,
                        principalTable: "office_settlements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dispute_tickets_booking",
                table: "dispute_tickets",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_office_payable_holds_one_open",
                table: "office_payable_holds",
                columns: new[] { "booking_id", "reason" },
                unique: true,
                filter: "released_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_office_payable_holds_open_dealer",
                table: "office_payable_holds",
                column: "dealer_id",
                filter: "released_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_office_payable_holds_open_next_check",
                table: "office_payable_holds",
                column: "next_check_at",
                filter: "released_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_office_payable_holds_payable_id",
                table: "office_payable_holds",
                column: "payable_id");

            migrationBuilder.CreateIndex(
                name: "ix_office_payable_lines_payable_id_position",
                table: "office_payable_lines",
                columns: new[] { "payable_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_office_payables_booking",
                table: "office_payables",
                column: "booking_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_office_payables_dealer_final",
                table: "office_payables",
                columns: new[] { "dealer_id", "final_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_office_payables_final",
                table: "office_payables",
                columns: new[] { "final_at", "id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_office_payables_open",
                table: "office_payables",
                columns: new[] { "dealer_id", "currency", "provider" },
                filter: "settlement_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_office_payables_settlement",
                table: "office_payables",
                column: "settlement_id",
                filter: "settlement_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_office_settlement_lines_payable_id",
                table: "office_settlement_lines",
                column: "payable_id");

            migrationBuilder.CreateIndex(
                name: "ix_office_settlement_lines_settlement_id_payable_id",
                table: "office_settlement_lines",
                columns: new[] { "settlement_id", "payable_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_office_settlements_dealer_recorded",
                table: "office_settlements",
                columns: new[] { "dealer_id", "recorded_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_office_settlements_paid_on",
                table: "office_settlements",
                column: "paid_on");

            migrationBuilder.CreateIndex(
                name: "ix_office_settlements_settlement_number",
                table: "office_settlements",
                column: "settlement_number",
                unique: true);

            migrationBuilder.Sql(CreateMoneyChecks);
            migrationBuilder.Sql(CreateGuards);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only while no real (non-test) money is recorded: see the remarks on this class.
            migrationBuilder.Sql(RefuseRollbackOverRealMoney);
            // The triggers first: a DROP TABLE is not refused by them, but a guard left behind on a table that stays
            // would be; and the functions only once nothing uses them.
            migrationBuilder.Sql(DropGuards);
            migrationBuilder.DropTable(
                name: "office_payable_holds");

            migrationBuilder.DropTable(
                name: "office_payable_lines");

            migrationBuilder.DropTable(
                name: "office_settlement_lines");

            migrationBuilder.DropTable(
                name: "office_settlement_voids");

            migrationBuilder.DropTable(
                name: "office_payables");

            migrationBuilder.DropTable(
                name: "office_settlements");

            migrationBuilder.Sql(DropFunctions);

            migrationBuilder.DropIndex(
                name: "ix_dispute_tickets_booking",
                table: "dispute_tickets");
        }
    }
}
