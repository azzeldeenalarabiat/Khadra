using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CalendarDaysAndVehicleHolds : Migration
    {
        // Pre-launch checklist item 12: nothing stopped two bookings holding the same car on the same
        // dates. The application guard (BookingRepository.HasOverlappingBookingAsync) is a
        // check-then-act, and two customers pressing Book at the same moment both pass it. This is
        // the floor underneath it that cannot be raced.
        //
        // Three details worth stating, because each one was arrived at the hard way:
        //
        // - It excludes on [hold_start, period_end), not on the customer's period. hold_start is the
        //   period opened backwards by the turnaround gap the booking froze, so the gallery's
        //   cleaning time is enforced by the database rather than only by a query.
        // - hold_start is an ordinary column, not GENERATED. Postgres demands an IMMUTABLE expression
        //   for a generated column or an index, and adding an interval to a timestamptz is only
        //   STABLE — the result depends on the session time zone. The aggregate computes it once.
        // - The predicate cannot mention now(), so a PendingPayment booking whose deadline has long
        //   passed still counts here as holding the car. The application's own predicate excludes it,
        //   which means the two disagree until something expires the row. Whatever creates bookings
        //   must expire stale unpaid holds on that vehicle in the same transaction, before it
        //   inserts, or the guard says free and this says taken.
        private const string AddHoldConstraint = @"
CREATE EXTENSION IF NOT EXISTS btree_gist;

ALTER TABLE bookings ADD CONSTRAINT bookings_one_hold_per_vehicle
  EXCLUDE USING gist (
    vehicle_id WITH =,
    tstzrange(hold_start, period_end, '[)') WITH &&
  )
  WHERE (status IN ('PendingPayment', 'Requested', 'Approved', 'PickedUp'));";

        private const string DropHoldConstraint = @"
ALTER TABLE bookings DROP CONSTRAINT IF EXISTS bookings_one_hold_per_vehicle;";

        // Rows that predate the column claim the car from the customer's own start, which is the rule
        // they were actually made under.
        //
        // The column arrives NULLABLE and is tightened afterwards, rather than arriving NOT NULL with
        // a sentinel default that gets rewritten. A sentinel would have to be matched to be replaced,
        // and DateTimeOffset.MinValue does not reach Postgres as '0001-01-01' — it becomes
        // '-infinity'. A backfill written against the wrong spelling silently updates nothing, every
        // held booking for a vehicle then claims it from the beginning of time, and the exclusion
        // constraint below refuses to apply at all. NULL cannot be spelled wrongly.
        private const string BackfillHoldStart = @"
UPDATE bookings SET hold_start = period_start WHERE hold_start IS NULL;
ALTER TABLE bookings ALTER COLUMN hold_start SET NOT NULL;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "hold_start",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(BackfillHoldStart);

            migrationBuilder.CreateIndex(
                name: "ix_bookings_vehicle_id_hold_start",
                table: "bookings",
                columns: new[] { "vehicle_id", "hold_start" });

            // This will REFUSE to apply if the table already holds two bookings that overlap on one
            // vehicle. That is the point: it is a statement that the data has always satisfied this,
            // and a database where it has not must be reconciled by a person, not silently by a
            // migration deciding which booking to discard.
            migrationBuilder.Sql(AddHoldConstraint);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DropHoldConstraint);

            migrationBuilder.DropIndex(
                name: "ix_bookings_vehicle_id_hold_start",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "hold_start",
                table: "bookings");
        }
    }
}
