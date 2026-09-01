using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Migrations;
using MipRental.Domain.Entities;

#nullable disable

namespace MipRental.Data.Migrations
{
    /// <summary>
    /// Sunum için BUDGET rolünde ayrı bir kullanıcı: "butce".
    ///
    /// Neden ayrı kullanıcı: BÜTÇE rolü admin hesabından KALDIRILDI ve geri
    /// verilmiyor. Rol ayrımının sunumda görünür olması için bütçe yetkisi
    /// kendi hesabında duruyor — tek hesapta toplanırsa yetkilendirme
    /// gösterilemez hâle gelir.
    ///
    /// FirmId = null olduğu için MIP personelidir; firma izolasyon filtresi
    /// (CLAUDE.md kural 7) ona tüm firmaları gösterir.
    ///
    /// InsertData ile SABİT UserId yazılMAZ: Users tablosu IDENTITY'dir ve
    /// geliştirme veritabanlarına elle/duman testiyle eklenmiş kayıtlar araya
    /// girmiş olabilir (sabit id çakışıp migration'ı kilitler). Bunun yerine
    /// kullanıcı adı üzerinden "yoksa ekle" mantığı kullanılır; migration her
    /// veritabanında tekrar çalışabilir (AddApproverSeedUsers ile aynı desen).
    /// </summary>
    public partial class AddBudgetSeedUser : Migration
    {
        // RoleConfiguration.HasData ile sabit: 4 = BUDGET.
        private const int BudgetRoleId = 4;

        private const string UserName = "butce";
        private const string FullName = "Bütçe Uzmanı";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CLAUDE.md: şifreler PasswordHasher ile hashlenir, düz metin veritabanına yazılmaz.
            var passwordHash = new PasswordHasher<User>().HashPassword(new User(), "Butce!2345");

            // Hash base64'tür, tırnak içermez; yine de sabit metinler tek yerde tutuluyor.
            migrationBuilder.Sql($@"
IF NOT EXISTS (SELECT 1 FROM Users WHERE UserName = N'{UserName}')
BEGIN
    INSERT INTO Users (UserName, FullName, FirmId, IsFirmAdmin, PasswordHash, IsActive, CreatedAt)
    VALUES (N'{UserName}', N'{FullName}', NULL, 0, N'{passwordHash}', 1, SYSUTCDATETIME());
END;

IF NOT EXISTS (
    SELECT 1 FROM UserRoles ur
    INNER JOIN Users u ON u.UserId = ur.UserId
    WHERE u.UserName = N'{UserName}' AND ur.RoleId = {BudgetRoleId})
BEGIN
    INSERT INTO UserRoles (UserId, RoleId, DepartmentId)
    SELECT UserId, {BudgetRoleId}, NULL FROM Users WHERE UserName = N'{UserName}';
END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
DELETE FROM UserRoles WHERE UserId IN (SELECT UserId FROM Users WHERE UserName = N'{UserName}');
DELETE FROM Users WHERE UserName = N'{UserName}';");
        }
    }
}
