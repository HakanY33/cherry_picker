using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MipRental.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalTokens",
                columns: table => new
                {
                    ApprovalTokenId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProgressPaymentId = table.Column<int>(type: "int", nullable: false),
                    IssuedToUserId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "varbinary(32)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UsedFromIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    UsedUserAgent = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalTokens", x => x.ApprovalTokenId);
                    table.ForeignKey(
                        name: "FK_ApprovalTokens_ProgressPayments_ProgressPaymentId",
                        column: x => x.ProgressPaymentId,
                        principalTable: "ProgressPayments",
                        principalColumn: "ProgressPaymentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalTokens_Users_IssuedToUserId",
                        column: x => x.IssuedToUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTokens_IssuedToUserId",
                table: "ApprovalTokens",
                column: "IssuedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTokens_Payment",
                table: "ApprovalTokens",
                column: "ProgressPaymentId");

            migrationBuilder.CreateIndex(
                name: "UQ_ApprovalTokens_Hash",
                table: "ApprovalTokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalTokens");
        }
    }
}
