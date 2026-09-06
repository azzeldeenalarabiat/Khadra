using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DealerSetsItsOwnDeliveryFee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "delivery_fee_amount",
                table: "dealers",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "delivery_fee_currency",
                table: "dealers",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            // Every dealership already offering delivery keeps charging exactly what it was charging.
            //
            // This figure is not invented. Until this migration ran, `BusinessRules:DeliveryFeeJod`
            // was 10 JOD and the platform applied it to every delivery booking on every gallery's
            // behalf. Copying it onto the galleries that had delivery switched on records what was
            // true a moment ago, and each of them is free to change it from their Delivery page.
            //
            // The alternative — leaving the fee null — would leave those rows in a state the domain
            // forbids: `DeliverySettings.Enabled` requires a fee, so an enabled row without one is
            // something the factory could never have produced. Rows with delivery OFF are left
            // alone, because a gallery that does not deliver is not quoting a price.
            migrationBuilder.Sql(
                """
                UPDATE dealers
                SET delivery_fee_amount = 10.000, delivery_fee_currency = 'JOD'
                WHERE delivery_enabled = true AND delivery_fee_amount IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "delivery_fee_amount",
                table: "dealers");

            migrationBuilder.DropColumn(
                name: "delivery_fee_currency",
                table: "dealers");
        }
    }
}
