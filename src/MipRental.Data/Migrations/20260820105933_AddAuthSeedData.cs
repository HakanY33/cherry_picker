using System;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Migrations;
using MipRental.Domain.Entities;

#nullable disable

namespace MipRental.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthSeedData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var hasher = new PasswordHasher<User>();
            var seedDate = DateTime.UtcNow;

            // CLAUDE.md: şifreler PasswordHasher ile hashlenir, düz metin veritabanına yazılmaz.
            var adminPasswordHash = hasher.HashPassword(new User(), "Admin!2345");
            var testVincPasswordHash = hasher.HashPassword(new User(), "Firma!2345");

            migrationBuilder.InsertData(
                table: "Firms",
                columns: new[] { "FirmId", "Code", "Title", "IsActive", "CreatedAt", "CreatedBy" },
                values: new object[] { 1, "TESTVINC", "Test Vinç Ltd. Şti.", true, seedDate, null });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[]
                {
                    "UserId", "UserName", "FullName", "Email", "Phone", "Position", "DepartmentId",
                    "FirmId", "IsFirmAdmin", "ExternalId", "PasswordHash", "IsActive", "LastLoginAt", "CreatedAt"
                },
                values: new object[,]
                {
                    { 1, "admin", "Sistem Yöneticisi", null, null, null, null, null, false, null, adminPasswordHash, true, null, seedDate },
                    { 2, "testvinc", "Test Vinç Kullanıcısı", null, null, null, null, 1, true, null, testVincPasswordHash, true, null, seedDate }
                });

            migrationBuilder.InsertData(
                table: "UserRoles",
                columns: new[] { "UserId", "RoleId", "DepartmentId" },
                values: new object[,]
                {
                    { 1, 7, null }, // ADMIN
                    { 2, 6, null }  // FIRM_USER
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "UserRoles",
                keyColumns: new[] { "UserId", "RoleId" },
                keyValues: new object[,]
                {
                    { 1, 7 },
                    { 2, 6 }
                });

            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "UserId",
                keyValues: new object[] { 1, 2 });

            migrationBuilder.DeleteData(
                table: "Firms",
                keyColumn: "FirmId",
                keyValue: 1);
        }
    }
}
