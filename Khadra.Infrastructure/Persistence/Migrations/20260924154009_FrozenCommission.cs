using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Freezes Khadra's commission onto every booking that already exists, under the rule it was made by.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Until 2026-09-24 the commission was never stored: every screen recomputed it as the frozen
    /// percent of the frozen rental total. From that date it is 20% of ONE day, frozen as an amount
    /// (<c>pricing.CommissionAmount</c>) with the rule that produced it (<c>terms.CommissionBasis</c>).
    /// A booking made before must keep the figure it always showed, so this writes, for each of them,
    /// exactly what the old computation produced: <c>RentalTotal × CommissionPercent / 100</c>, rounded
    /// to three decimals HALF TO EVEN, as <c>Money</c> rounds every amount. PostgreSQL's
    /// <c>round(numeric)</c> rounds half AWAY from zero, so the half case is written out: a result
    /// exactly half way lands on the even thousandth.
    /// </para>
    /// <para>
    /// Not optional. The pricing is a JSON document mapped to get-only value objects; a booking without
    /// the key materialises a null <c>CommissionAmount</c> and the first read of its amount throws.
    /// There is deliberately no runtime fallback: two ways of reading one number is what freezing it
    /// is meant to end.
    /// </para>
    /// <para>
    /// Schema-neutral (two JSON documents gain a key each), idempotent (a row that already has the key
    /// is left alone), and additive: an older API build ignores both keys.
    /// </para>
    /// </remarks>
    public partial class FrozenCommission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE bookings
                SET pricing = jsonb_set(
                      pricing,
                      '{CommissionAmount}',
                      jsonb_build_object(
                        'Amount',
                        (SELECT (CASE
                                  WHEN (v * 1000) - trunc(v * 1000) = 0.5 AND mod(trunc(v * 1000), 2) = 0
                                    THEN trunc(v * 1000)
                                  ELSE round(v * 1000)
                                END / 1000)::numeric(18, 3)
                           FROM (SELECT (pricing -> 'RentalTotal' ->> 'Amount')::numeric
                                        * (terms -> 'CommissionPercent' ->> 'Value')::numeric / 100 AS v) AS calc),
                        'CurrencyCode',
                        pricing -> 'RentalTotal' ->> 'CurrencyCode'))
                WHERE NOT (pricing ? 'CommissionAmount');
                """);

            migrationBuilder.Sql(
                """
                UPDATE bookings
                SET terms = jsonb_set(terms, '{CommissionBasis}', '"RentalTotal"')
                WHERE NOT (terms ? 'CommissionBasis');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only the rows this migration wrote. A booking made AFTER it (rules version 2) was priced
            // under the one-day rule and has no other record of its commission, so it keeps its keys.
            migrationBuilder.Sql(
                """
                UPDATE bookings
                SET pricing = pricing - 'CommissionAmount',
                    terms = terms - 'CommissionBasis'
                WHERE (terms ->> 'RulesVersion')::int = 1;
                """);
        }
    }
}
