using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MipRental.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalFlowAndLineObjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsObjected",
                table: "WorkRecordLines",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ObjectedAt",
                table: "WorkRecordLines",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ObjectedByUserId",
                table: "WorkRecordLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ObjectionReason",
                table: "WorkRecordLines",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.InsertData(
                table: "ApprovalFlows",
                columns: new[] { "FlowId", "Code", "DocumentType", "IsActive", "Name", "ServiceId" },
                values: new object[] { 1, "WR-DEFAULT", "WORK_RECORD", true, "Çalışma Kaydı Varsayılan Onay Akışı", null });

            migrationBuilder.InsertData(
                table: "ApprovalFlowSteps",
                columns: new[] { "FlowStepId", "AmountThreshold", "EscalateAfterHours", "FlowId", "IsMandatory", "Name", "ReminderAfterHours", "RoleId", "StepNo" },
                values: new object[,]
                {
                    { 1, null, 48, 1, true, "Amir Onayı", 24, 2, 1 },
                    { 2, null, 48, 1, true, "Departman Müdürü Onayı", 24, 3, 2 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkRecordLines_Objected",
                table: "WorkRecordLines",
                columns: new[] { "WorkRecordId", "IsObjected" },
                filter: "[IsObjected] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_WorkRecordLines_ObjectedByUserId",
                table: "WorkRecordLines",
                column: "ObjectedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkRecordLines_Users_ObjectedByUserId",
                table: "WorkRecordLines",
                column: "ObjectedByUserId",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkRecordLines_Users_ObjectedByUserId",
                table: "WorkRecordLines");

            migrationBuilder.DropIndex(
                name: "IX_WorkRecordLines_Objected",
                table: "WorkRecordLines");

            migrationBuilder.DropIndex(
                name: "IX_WorkRecordLines_ObjectedByUserId",
                table: "WorkRecordLines");

            migrationBuilder.DeleteData(
                table: "ApprovalFlowSteps",
                keyColumn: "FlowStepId",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "ApprovalFlowSteps",
                keyColumn: "FlowStepId",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "ApprovalFlows",
                keyColumn: "FlowId",
                keyValue: 1);

            migrationBuilder.DropColumn(
                name: "IsObjected",
                table: "WorkRecordLines");

            migrationBuilder.DropColumn(
                name: "ObjectedAt",
                table: "WorkRecordLines");

            migrationBuilder.DropColumn(
                name: "ObjectedByUserId",
                table: "WorkRecordLines");

            migrationBuilder.DropColumn(
                name: "ObjectionReason",
                table: "WorkRecordLines");
        }
    }
}
