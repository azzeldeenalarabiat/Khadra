using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReviewVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reviews_subject_id_direction_created_at",
                table: "reviews");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "visible_from",
                table: "reviews",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Every review written before the blind window existed was PUBLIC the moment it was
            // written, so its reveal instant is its creation. The column default of 0001-01-01 would
            // give the same visibility -- everything is already visible -- but it would record a lie
            // about WHEN, on rows an investigation would later read as fact.
            migrationBuilder.Sql("UPDATE reviews SET visible_from = created_at;");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_subject_id_direction_visible_from",
                table: "reviews",
                columns: new[] { "subject_id", "direction", "visible_from" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reviews_subject_id_direction_visible_from",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "visible_from",
                table: "reviews");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_subject_id_direction_created_at",
                table: "reviews",
                columns: new[] { "subject_id", "direction", "created_at" });
        }
    }
}
