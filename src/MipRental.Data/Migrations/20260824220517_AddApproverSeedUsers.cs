using System;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Migrations;
using MipRental.Domain.Entities;

#nullable disable

namespace MipRental.Data.Migrations
{
    /// <summary>
    /// Adım 7: onay akışını uçtan uca yürütebilmek için iki MIP onaylayıcısı.
    /// Varsayılan akışın adımlarına karşılık gelirler: 1. adım SUPERVISOR,
    /// 2. adım DEPT_HEAD. FirmId = null oldukları için MIP personelidir —
    /// firma izolasyon filtresi (kural 7) onlara tüm firmaları gösterir.
    ///
    /// InsertData ile SABİT UserId yazılMAZ: bu tablo IDENTITY'dir ve geliştirme
    /// veritabanlarında elle/duman testiyle eklenmiş kayıtlar araya girmiş olabilir
    /// (sabit id çakışıp migration'ı kilitler). Bunun yerine kullanıcı adı üzerinden
    /// "yoksa ekle" mantığı kullanılır; migration her veritabanında tekrar çalışabilir.
    /// </summary>
    public partial class AddApproverSeedUsers : Migration
    {
        // RoleConfiguration.HasData ile sabit: 2 = SUPERVISOR, 3 = DEPT_HEAD.
        private const int SupervisorRoleId = 2;
        private const int DeptHeadRoleId = 3;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var hasher = new PasswordHasher<User>();

            // CLAUDE.md: şifreler PasswordHasher ile hashlenir, düz metin veritabanına yazılmaz.
            SeedApprover(migrationBuilder, "supervisor", "Saha Amiri", "Amir",
                hasher.HashPassword(new User(), "Amir!2345"), SupervisorRoleId);

            SeedApprover(migrationBuilder, "depthead", "Departman Müdürü", "Müdür",
                hasher.HashPassword(new User(), "Mudur!2345"), DeptHeadRoleId);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var userName in new[] { "supervisor", "depthead" })
            {
                migrationBuilder.Sql($@"
DELETE FROM UserRoles WHERE UserId IN (SELECT UserId FROM Users WHERE UserName = N'{userName}');
DELETE FROM Users WHERE UserName = N'{userName}';");
            }
        }

        private static void SeedApprover(
            MigrationBuilder migrationBuilder, string userName, string fullName, string position, string passwordHash, int roleId)
        {
            // Hash base64'tür, tırnak içermez; yine de sabit metinler tek yerde tutuluyor.
            migrationBuilder.Sql($@"
IF NOT EXISTS (SELECT 1 FROM Users WHERE UserName = N'{userName}')
BEGIN
    INSERT INTO Users (UserName, FullName, Position, FirmId, IsFirmAdmin, PasswordHash, IsActive, CreatedAt)
    VALUES (N'{userName}', N'{fullName}', N'{position}', NULL, 0, N'{passwordHash}', 1, SYSUTCDATETIME());
END;

IF NOT EXISTS (
    SELECT 1 FROM UserRoles ur
    INNER JOIN Users u ON u.UserId = ur.UserId
    WHERE u.UserName = N'{userName}' AND ur.RoleId = {roleId})
BEGIN
    INSERT INTO UserRoles (UserId, RoleId, DepartmentId)
    SELECT UserId, {roleId}, NULL FROM Users WHERE UserName = N'{userName}';
END;");
        }
    }
}
