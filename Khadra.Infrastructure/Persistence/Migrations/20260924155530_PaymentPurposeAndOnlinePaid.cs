using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// What a payment is for and the fee inside it; what a booking has been paid online (2026-09-24).
    /// </summary>
    /// <remarks>
    /// Additive: three columns whose defaults describe every existing row truthfully — each payment so
    /// far was a deposit with no fee. The one write is <c>online_paid</c> on bookings a payment has
    /// already confirmed: until this release the only way to confirm was the deposit, so each of them
    /// was paid exactly its frozen <c>DepositAmount</c>. An older API ignores all three columns.
    /// </remarks>
    public partial class PaymentPurposeAndOnlinePaid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "processing_fee",
                table: "payments",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "purpose",
                table: "payments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValueSql: "'Deposit'");

            migrationBuilder.AddColumn<decimal>(
                name: "online_paid",
                table: "bookings",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            // Every booking confirmed so far was confirmed by its deposit, and by nothing else.
            migrationBuilder.Sql(
                """
                UPDATE bookings
                SET online_paid = (pricing -> 'DepositAmount' ->> 'Amount')::numeric(18, 3)
                WHERE deposit_payment_id IS NOT NULL AND online_paid = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "processing_fee",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "purpose",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "online_paid",
                table: "bookings");
        }
    }
}
