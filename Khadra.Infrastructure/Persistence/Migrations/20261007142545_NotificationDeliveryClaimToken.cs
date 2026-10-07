using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Makes <c>notification_deliveries.attempts</c>, the claim count, a concurrency token (pre-launch item 204). The
    /// token lives in the model only — EF adds it to the WHERE clause of every update — so the schema is unchanged and
    /// this migration exists to keep the snapshot true. Applying it writes its history row and nothing else.
    /// </summary>
    public partial class NotificationDeliveryClaimToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
