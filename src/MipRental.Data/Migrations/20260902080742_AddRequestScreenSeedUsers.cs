using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Migrations;
using MipRental.Domain.Entities;

#nullable disable

namespace MipRental.Data.Migrations
{
    /// <summary>
    /// Adım 11 — talep ekranlarının sunumu ve duman testi için üç kullanıcı:
    /// talep açan, Ekipman Müdürlüğü yöneticisi, firma yetkilisi.
    ///
    /// Mevcut "yoksa ekle" deseni birebir korunur (AddApproverSeedUsers /
    /// AddBudgetSeedUser): sabit UserId YAZILMAZ — Users tablosu IDENTITY'dir ve
    /// geliştirme veritabanlarına elle eklenmiş kayıtlar araya girmiş olabilir.
    /// Kullanıcı adı üzerinden kontrol edildiği için migration her veritabanında
    /// tekrar çalışabilir.
    ///
    /// talep1'in bir DEPARTMANI olmak ZORUNDA: talep açan kişinin departmanı
    /// oturumdan (Users.DepartmentId -> DepartmentId claim'i) okunur ve
    /// RequestsController departmansız kullanıcıya talep açtırmaz. Şu ana kadar
    /// hiç departman seed'lenmemişti; "Operasyon" departmanı da burada eklenir.
    /// </summary>
    public partial class AddRequestScreenSeedUsers : Migration
    {
        // RoleConfiguration.HasData ile sabit id'ler.
        private const int RequesterRoleId = 1;
        private const int EquipmentManagerRoleId = 2;
        private const int FirmManagerRoleId = 9;

        // AddAuthSeedData ile eklenen Test Vinç Ltd. Şti.
        private const int TestFirmId = 1;

        private const string DepartmentCode = "OPS";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
IF NOT EXISTS (SELECT 1 FROM Departments WHERE Code = N'{DepartmentCode}')
BEGIN
    INSERT INTO Departments (Code, Name, ParentDepartmentId, IsActive)
    VALUES (N'{DepartmentCode}', N'Operasyon', NULL, 1);
END;");

            var hasher = new PasswordHasher<User>();

            // CLAUDE.md: şifreler PasswordHasher ile hashlenir, düz metin yazılmaz.
            SeedUser(migrationBuilder, "talep1", "Talep Eden Kullanıcı", "Saha Sorumlusu",
                hasher.HashPassword(new User(), "Talep!2345"), RequesterRoleId, firmId: null, withDepartment: true);

            SeedUser(migrationBuilder, "ekipman1", "Ekipman Müdürlüğü Yöneticisi", "Ekipman Müdürü",
                hasher.HashPassword(new User(), "Ekipman!2345"), EquipmentManagerRoleId, firmId: null, withDepartment: false);

            SeedUser(migrationBuilder, "firma1", "Test Vinç Yetkilisi", "Firma Yetkilisi",
                hasher.HashPassword(new User(), "Firma!2345"), FirmManagerRoleId, firmId: TestFirmId, withDepartment: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var userName in new[] { "talep1", "ekipman1", "firma1" })
            {
                migrationBuilder.Sql($@"
DELETE FROM UserRoles WHERE UserId IN (SELECT UserId FROM Users WHERE UserName = N'{userName}');
DELETE FROM Users WHERE UserName = N'{userName}';");
            }

            // Departman, talebi olan bir kayda bağlı olabilir; Requests.DepartmentId
            // üzerinde Restrict var. Silmeye kalkmak Down'ı kilitler — bilinçli
            // olarak bırakılıyor, boş bir departman zarar vermez.
        }

        private static void SeedUser(
            MigrationBuilder migrationBuilder, string userName, string fullName, string position,
            string passwordHash, int roleId, int? firmId, bool withDepartment)
        {
            var firmValue = firmId is null ? "NULL" : firmId.Value.ToString();
            var departmentValue = withDepartment
                ? $"(SELECT TOP 1 DepartmentId FROM Departments WHERE Code = N'{DepartmentCode}')"
                : "NULL";

            migrationBuilder.Sql($@"
IF NOT EXISTS (SELECT 1 FROM Users WHERE UserName = N'{userName}')
BEGIN
    INSERT INTO Users (UserName, FullName, Position, DepartmentId, FirmId, IsFirmAdmin, PasswordHash, IsActive, CreatedAt)
    VALUES (N'{userName}', N'{fullName}', N'{position}', {departmentValue}, {firmValue}, 0, N'{passwordHash}', 1, SYSUTCDATETIME());
END
ELSE
BEGIN
    -- Kullanıcı önceki bir denemeden kalmış olabilir; departman/firma bağı
    -- eksikse tamamlanır, aksi hâlde talep ekranı departmansız kullanıcıyla açılmaz.
    UPDATE Users
       SET DepartmentId = COALESCE(DepartmentId, {departmentValue}),
           FirmId = COALESCE(FirmId, {firmValue}),
           Position = COALESCE(Position, N'{position}')
     WHERE UserName = N'{userName}';
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
