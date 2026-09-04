using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MipRental.Data.Migrations
{
    /// <summary>
    /// Adım 15 — mail gönderim altyapısı.
    ///
    /// Notifications: LastAttemptAt, NextAttemptAt (üstel geri çekilme) ve
    /// LastError (sağlık ekranında MIP IT'nin bakacağı tek yer).
    /// Approvals: EscalationSentAt — eskalasyon da hatırlatma gibi adım başına
    /// BİR KEZ üretilsin diye.
    ///
    /// Durum kolonu string'dir (ADR-009); yeni SENDING ve SKIPPED_EXTERNAL
    /// değerleri için şema değişikliği gerekmez.
    /// </summary>
    public partial class AddEmailDispatchFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastAttemptAt",
                table: "Notifications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "Notifications",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "Notifications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EscalationSentAt",
                table: "Approvals",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_NextAttempt",
                table: "Notifications",
                columns: new[] { "Status", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_NextAttempt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "LastAttemptAt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "EscalationSentAt",
                table: "Approvals");
        }
    }
}
