using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MipRental.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestDurationConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CompletedByUserId",
                table: "Requests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmationDecisionAt",
                table: "Requests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisputeReason",
                table: "Requests",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DisputeResolvedAt",
                table: "Requests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Requests_CompletedByUserId",
                table: "Requests",
                column: "CompletedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Requests_Users_CompletedByUserId",
                table: "Requests",
                column: "CompletedByUserId",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Requests_Users_CompletedByUserId",
                table: "Requests");

            migrationBuilder.DropIndex(
                name: "IX_Requests_CompletedByUserId",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "CompletedByUserId",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "ConfirmationDecisionAt",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "DisputeReason",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "DisputeResolvedAt",
                table: "Requests");
        }
    }
}
