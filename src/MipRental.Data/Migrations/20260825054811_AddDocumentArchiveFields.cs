using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MipRental.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentArchiveFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "GeneratedDocuments",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FirmId",
                table: "GeneratedDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalAmount",
                table: "GeneratedDocuments",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedDocuments_DocumentType_DocumentId_Kind_GeneratedAt",
                table: "GeneratedDocuments",
                columns: new[] { "DocumentType", "DocumentId", "Kind", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedDocuments_FirmId",
                table: "GeneratedDocuments",
                column: "FirmId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedDocuments_VerificationCode",
                table: "GeneratedDocuments",
                column: "VerificationCode",
                unique: true,
                filter: "[VerificationCode] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_GeneratedDocuments_Firms_FirmId",
                table: "GeneratedDocuments",
                column: "FirmId",
                principalTable: "Firms",
                principalColumn: "FirmId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GeneratedDocuments_Firms_FirmId",
                table: "GeneratedDocuments");

            migrationBuilder.DropIndex(
                name: "IX_GeneratedDocuments_DocumentType_DocumentId_Kind_GeneratedAt",
                table: "GeneratedDocuments");

            migrationBuilder.DropIndex(
                name: "IX_GeneratedDocuments_FirmId",
                table: "GeneratedDocuments");

            migrationBuilder.DropIndex(
                name: "IX_GeneratedDocuments_VerificationCode",
                table: "GeneratedDocuments");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "GeneratedDocuments");

            migrationBuilder.DropColumn(
                name: "FirmId",
                table: "GeneratedDocuments");

            migrationBuilder.DropColumn(
                name: "TotalAmount",
                table: "GeneratedDocuments");
        }
    }
}
