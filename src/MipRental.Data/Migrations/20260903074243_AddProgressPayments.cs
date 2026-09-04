using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MipRental.Data.Migrations
{
    /// <summary>
    /// Adım 14 Bölüm A — hakediş (ProgressPayments) ve dondurulmuş kayıt listesi
    /// (ProgressPaymentRecords).
    ///
    /// İki UNIQUE index iki ayrı çift ödeme yolunu kapatır:
    ///   UQ_ProgressPayments_Period_Firm        — bir dönem + firma için tek hakediş
    ///   UQ_ProgressPaymentRecords_WorkRecord   — bir çalışma kaydı tek hakedişe girer
    /// Garantiler veritabanında: uygulama katmanı kontrolü iki paralel istekte de
    /// "yok" görebilir (ADR-027 ile aynı gerekçe).
    /// </summary>
    public partial class AddProgressPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProgressPayments",
                columns: table => new
                {
                    ProgressPaymentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PeriodId = table.Column<int>(type: "int", nullable: false),
                    FirmId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "DRAFT"),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    RecordCount = table.Column<int>(type: "int", nullable: false),
                    PendingRecordCountAtCreation = table.Column<int>(type: "int", nullable: false),
                    BudgetNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    BudgetApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    BudgetApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ManagerApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ManagerApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ManagerNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgressPayments", x => x.ProgressPaymentId);
                    table.ForeignKey(
                        name: "FK_ProgressPayments_Firms_FirmId",
                        column: x => x.FirmId,
                        principalTable: "Firms",
                        principalColumn: "FirmId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProgressPayments_Periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "Periods",
                        principalColumn: "PeriodId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProgressPayments_Users_BudgetApprovedByUserId",
                        column: x => x.BudgetApprovedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProgressPayments_Users_ManagerApprovedByUserId",
                        column: x => x.ManagerApprovedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProgressPaymentRecords",
                columns: table => new
                {
                    ProgressPaymentRecordId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProgressPaymentId = table.Column<int>(type: "int", nullable: false),
                    WorkRecordId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgressPaymentRecords", x => x.ProgressPaymentRecordId);
                    table.ForeignKey(
                        name: "FK_ProgressPaymentRecords_ProgressPayments_ProgressPaymentId",
                        column: x => x.ProgressPaymentId,
                        principalTable: "ProgressPayments",
                        principalColumn: "ProgressPaymentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProgressPaymentRecords_WorkRecords_WorkRecordId",
                        column: x => x.WorkRecordId,
                        principalTable: "WorkRecords",
                        principalColumn: "WorkRecordId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UQ_ProgressPaymentRecords_Record",
                table: "ProgressPaymentRecords",
                columns: new[] { "ProgressPaymentId", "WorkRecordId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_ProgressPaymentRecords_WorkRecord",
                table: "ProgressPaymentRecords",
                column: "WorkRecordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProgressPayments_BudgetApprovedByUserId",
                table: "ProgressPayments",
                column: "BudgetApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProgressPayments_FirmId",
                table: "ProgressPayments",
                column: "FirmId");

            migrationBuilder.CreateIndex(
                name: "IX_ProgressPayments_ManagerApprovedByUserId",
                table: "ProgressPayments",
                column: "ManagerApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "UQ_ProgressPayments_Period_Firm",
                table: "ProgressPayments",
                columns: new[] { "PeriodId", "FirmId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProgressPaymentRecords");

            migrationBuilder.DropTable(
                name: "ProgressPayments");
        }
    }
}
