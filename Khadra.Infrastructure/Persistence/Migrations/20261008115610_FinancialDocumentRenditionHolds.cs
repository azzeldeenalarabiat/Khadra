using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khadra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinancialDocumentRenditionHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "financial_document_rendition_holds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    language = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    first_failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financial_document_rendition_holds", x => x.id);
                    table.CheckConstraint("ck_financial_document_rendition_holds_attempts", "attempts >= 1");
                    table.ForeignKey(
                        name: "fk_financial_document_rendition_holds_financial_documents_docu",
                        column: x => x.document_id,
                        principalTable: "financial_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_financial_document_rendition_holds_document_id_language_kind",
                table: "financial_document_rendition_holds",
                columns: new[] { "document_id", "language", "kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "financial_document_rendition_holds");
        }
    }
}
