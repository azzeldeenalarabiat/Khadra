using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The booking lifecycle reordered: a customer reserves first and pays after the dealer approves
    /// (see <c>docs/spec-amendments.md</c>, 2026-09-07).
    /// </summary>
    /// <remarks>
    /// Three things change in the data, and each is here rather than in code because the schema is
    /// the only place they can be stated once for every row:
    ///
    /// - A second deadline. A booking now runs two consecutive clocks: the dealer's window to answer,
    ///   then the customer's window to pay. They are never both live, so <c>payment_deadline</c>
    ///   becomes nullable and stays null until somebody approves.
    /// - <c>PendingPayment</c> is gone and <c>Confirmed</c> exists. The exclusion constraint spells
    ///   the holding statuses out in SQL, so it has to be dropped and re-added.
    /// - An approved booking that was already paid for is a CONFIRMED booking now. Left as it was,
    ///   the expiry job would read it as approved-and-unpaid and cancel a booking whose deposit has
    ///   cleared.
    ///
    /// The frozen terms on existing rows are deliberately NOT rewritten. <c>BookingTerms.AnswerWindow</c>
    /// is new, so older documents lack the key and read as zero — but nothing reads it on a stored
    /// booking. It is used once, when <c>Booking.Create</c> computes <c>decision_deadline</c>, and
    /// that column is backfilled here. Hand-writing a value into a JSON document whose exact format
    /// is the provider's business would risk producing one the application can no longer parse, to
    /// correct a field nobody reads. Zero is also the honest reading: no answer window was ever
    /// promised on those bookings, and the column they ARE judged by is backfilled to say the same.
    /// </remarks>
    public partial class ReserveNowPayAfterApproval : Migration
    {
        // Runs FIRST, before any schema change, so a refusal leaves the database exactly as it was.
        //
        // Two row shapes the new machine cannot carry, and neither may be decided quietly by a
        // schema migration, because both are decisions about a real customer's money:
        //
        // - A PENDINGPAYMENT booking is a checkout that was started and never completed. There is no
        //   state for that any more. Expiring it and pushing it in front of a dealer as a request
        //   are both defensible and they are not the same thing for the customer.
        // - A PAID REQUEST is worse, because it looks migratable and is not. Under the old order
        //   every Requested row had already paid; under the new one Requested means unpaid, and
        //   Approve would open a payment window against a deposit that is already in hand. Nothing
        //   would ever pay it -- ConfirmDepositPaid with the same payment id is an idempotent
        //   no-op -- so the expiry job would eventually expire a booking the customer paid for.
        //
        // In practice this fires for nobody: nothing in the platform can create a booking yet, which
        // is the very slice of work this reordering exists to unblock.
        private const string RefuseUncarryableRows = @"
DO $$
DECLARE stranded int; paid_requests int;
BEGIN
  SELECT count(*) INTO stranded FROM bookings WHERE status = 'PendingPayment';
  IF stranded > 0 THEN
    RAISE EXCEPTION
      'Cannot apply ReserveNowPayAfterApproval: % booking(s) are still PendingPayment, a status this migration removes. Decide what each one is -- expire it (status Expired, with a penalty assessment) or let the dealer answer it (status Requested) -- then re-run.', stranded;
  END IF;

  SELECT count(*) INTO paid_requests FROM bookings WHERE status = 'Requested' AND deposit_payment_id IS NOT NULL;
  IF paid_requests > 0 THEN
    RAISE EXCEPTION
      'Cannot apply ReserveNowPayAfterApproval: % request(s) already carry a deposit, which the new order cannot express -- Requested now means unpaid. Decide each by hand (approve and confirm it, or refund and expire it) then re-run.', paid_requests;
  END IF;
END $$;";

        // Approved AND paid is what Confirmed means. Under the old order approval could only happen
        // after the deposit cleared, so every approved row qualifies. The deposit test is a belt on
        // braces: a row that somehow has no payment is left Approved, which is now a truthful
        // description of it, and the guard above has already refused the shapes that are not.
        private const string PromotePaidApprovalsToConfirmed = @"
UPDATE bookings SET status = 'Confirmed'
WHERE status = 'Approved' AND deposit_payment_id IS NOT NULL;";

        private const string DemoteConfirmedToApproved = @"
UPDATE bookings SET status = 'Approved' WHERE status = 'Confirmed';";

        // Arrives nullable and is tightened once every row has a real value, rather than arriving NOT
        // NULL with a sentinel default. DateTimeOffset.MinValue does not reach Postgres as
        // '0001-01-01' -- it becomes '-infinity' -- so a backfill matching on the wrong spelling
        // updates nothing and every legacy booking ends up with a deadline in the infinite past.
        //
        // The value is period_start, NOT today's 48-hour answer window, because that is the deadline
        // these rows were actually made under: the old ExpireUnanswered refused until the rental
        // began, and their frozen terms carry no answer window at all. Writing 48 hours here would
        // judge yesterday's booking by today's rule -- the thing BookingTerms exists to prevent --
        // and any legacy request older than two days would arrive already expired, its car released
        // and its approval refused, on a booking the customer had paid to hold. Same reasoning, and
        // the same shape, as CalendarDaysAndVehicleHolds backfilling hold_start to period_start.
        private const string BackfillDecisionDeadline = @"
UPDATE bookings SET decision_deadline = period_start WHERE decision_deadline IS NULL;
ALTER TABLE bookings ALTER COLUMN decision_deadline SET NOT NULL;";

        // A booking that never reached approval has no payment deadline to reverse into, so Down
        // gives it the one value that is certainly true of it: its rental start, which every window
        // on a booking is capped at anyway.
        private const string BackfillPaymentDeadlineForDown = @"
UPDATE bookings SET payment_deadline = period_start WHERE payment_deadline IS NULL;";

        // Down refuses for the mirror of the reason Up does. The old code reads Requested and
        // Approved as states a deposit has already cleared into -- old Approved even allowed the
        // keys to be handed over -- so rolling back over an unpaid one would hand a dealer a car
        // against money nobody has taken. Reversing the SCHEMA is a migration's business; deciding
        // what to do with a customer who owes a deposit is not.
        private const string RefuseUnpaidLiveRowsOnDown = @"
DO $$
DECLARE unpaid int;
BEGIN
  SELECT count(*) INTO unpaid FROM bookings
  WHERE status IN ('Requested', 'Approved') AND deposit_payment_id IS NULL;
  IF unpaid > 0 THEN
    RAISE EXCEPTION
      'Cannot revert ReserveNowPayAfterApproval: % live booking(s) have no deposit, and the previous order treats Requested and Approved as already paid. Settle each one first (take the deposit, or cancel it) then re-run.', unpaid;
  END IF;
END $$;";

        private const string DropHoldConstraint = @"
ALTER TABLE bookings DROP CONSTRAINT IF EXISTS bookings_one_hold_per_vehicle;";

        // Identical to the constraint added by CalendarDaysAndVehicleHolds except for the status
        // list: PendingPayment out, Confirmed in. Everything the original said about it still holds --
        // it excludes on [hold_start, period_end) so the gallery's turnaround gap is enforced by the
        // database, and the predicate cannot mention now(), so a stale unpaid hold still counts here
        // until something expires the row.
        private const string AddHoldConstraint = @"
ALTER TABLE bookings ADD CONSTRAINT bookings_one_hold_per_vehicle
  EXCLUDE USING gist (
    vehicle_id WITH =,
    tstzrange(hold_start, period_end, '[)') WITH &&
  )
  WHERE (status IN ('Requested', 'Approved', 'Confirmed', 'PickedUp'));";

        private const string AddPreviousHoldConstraint = @"
ALTER TABLE bookings ADD CONSTRAINT bookings_one_hold_per_vehicle
  EXCLUDE USING gist (
    vehicle_id WITH =,
    tstzrange(hold_start, period_end, '[)') WITH &&
  )
  WHERE (status IN ('PendingPayment', 'Requested', 'Approved', 'PickedUp'));";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RefuseUncarryableRows);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "payment_deadline",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decision_deadline",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(BackfillDecisionDeadline);

            // The constraint has to go before the status changes and come back after: its predicate
            // names statuses, and the rows being renamed are exactly the ones it is watching.
            migrationBuilder.Sql(DropHoldConstraint);
            migrationBuilder.Sql(PromotePaidApprovalsToConfirmed);
            migrationBuilder.Sql(AddHoldConstraint);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RefuseUnpaidLiveRowsOnDown);
            migrationBuilder.Sql(DropHoldConstraint);
            migrationBuilder.Sql(DemoteConfirmedToApproved);

            migrationBuilder.DropColumn(
                name: "decision_deadline",
                table: "bookings");

            migrationBuilder.Sql(BackfillPaymentDeadlineForDown);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "payment_deadline",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.Sql(AddPreviousHoldConstraint);
        }
    }
}
