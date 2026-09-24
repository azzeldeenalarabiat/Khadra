using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <summary>Whether a payment's processing fee is returned with it, frozen on the row (2026-09-24).</summary>
    /// <remarks>
    /// Existing rows read TRUE: every refund before this migration returned the whole capture, and no
    /// payment has carried a fee yet, so true is what each row has always meant. Additive.
    /// </remarks>
    public partial class PaymentFeeRefundable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "fee_refundable",
                table: "payments",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fee_refundable",
                table: "payments");
        }
    }
}
