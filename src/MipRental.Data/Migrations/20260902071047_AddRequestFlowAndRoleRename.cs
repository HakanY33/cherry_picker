using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MipRental.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestFlowAndRoleRename : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ActualEndTime",
                table: "Requests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ActualStartTime",
                table: "Requests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignedLicensePlate",
                table: "Requests",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignedOperatorName",
                table: "Requests",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Requests",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "Requests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EquipmentDecisionAt",
                table: "Requests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirmDecisionAt",
                table: "Requests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "Requests",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "Requests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ApprovalFlowSteps",
                keyColumn: "FlowStepId",
                keyValue: 2,
                column: "Name",
                value: "Bütçe Yöneticisi Onayı");

            // ---------------------------------------------------------------
            // Rol yeniden adlandırma + yeni roller.
            //
            // EF'in ürettiği UpdateData/InsertData çağrıları yerine "yoksa ekle /
            // varsa güncelle" SQL'i yazıldı (AddBudgetSeedUser ile aynı desen):
            // migration her veritabanında tekrar çalışabilmeli ve elle rol
            // eklenmiş bir geliştirme veritabanında Code UNIQUE index'ine
            // takılmamalı.
            //
            // KRİTİK: RoleId DEĞİŞMİYOR. UserRoles ve ApprovalFlowSteps satırları
            // RoleId ile bağlı; sadece Code/Name güncellendiği için SUPERVISOR
            // rolündeki her kullanıcı hiçbir veri taşınmadan EQUIPMENT_MANAGER
            // rolünde kalır. Rol atamalarını "taşıyan" bir UPDATE'e gerek YOKTUR
            // ve yazılmamıştır — taşımak, ilişkiyi bozma riskini boşuna alırdı.
            // ---------------------------------------------------------------
            migrationBuilder.Sql(@"
-- Yeniden adlandırma: yalnızca eski kod hâlâ duruyorsa ve yeni kod başka bir
-- satırda kullanılmıyorsa. İkinci koşul Code UNIQUE index'ini korur.
IF EXISTS (SELECT 1 FROM Roles WHERE RoleId = 2 AND Code = N'SUPERVISOR')
   AND NOT EXISTS (SELECT 1 FROM Roles WHERE Code = N'EQUIPMENT_MANAGER')
BEGIN
    UPDATE Roles SET Code = N'EQUIPMENT_MANAGER', Name = N'Ekipman Müdürlüğü Yöneticisi' WHERE RoleId = 2;
END;

IF EXISTS (SELECT 1 FROM Roles WHERE RoleId = 3 AND Code = N'DEPT_HEAD')
   AND NOT EXISTS (SELECT 1 FROM Roles WHERE Code = N'BUDGET_MANAGER')
BEGIN
    UPDATE Roles SET Code = N'BUDGET_MANAGER', Name = N'Bütçe Yöneticisi' WHERE RoleId = 3;
END;");

            // Yeni roller. Kod zaten varsa adı/kapsamı güncellenir; yoksa sabit
            // RoleId ile eklenir (RoleConfiguration.HasData ile aynı id'ler —
            // model snapshot'ı ile veritabanı ayrışmasın).
            foreach (var (roleId, code, name, scope) in new[]
                     {
                         (8, "EQUIPMENT_VIEWER", "Ekipman Müdürlüğü Kullanıcısı", "INTERNAL"),
                         (9, "FIRM_MANAGER", "Firma Yetkilisi", "EXTERNAL"),
                         (10, "FIRM_OPERATOR", "Firma Operatörü", "EXTERNAL")
                     })
            {
                migrationBuilder.Sql($@"
IF EXISTS (SELECT 1 FROM Roles WHERE Code = N'{code}')
BEGIN
    UPDATE Roles SET Name = N'{name}', Scope = N'{scope}' WHERE Code = N'{code}';
END
ELSE IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleId = {roleId})
BEGIN
    SET IDENTITY_INSERT Roles ON;
    INSERT INTO Roles (RoleId, Code, Name, Scope) VALUES ({roleId}, N'{code}', N'{name}', N'{scope}');
    SET IDENTITY_INSERT Roles OFF;
END;");
            }

            // ---------------------------------------------------------------
            // Talep durumlarının yeni şekli (RequestStatus enum'ı).
            //
            // Şema değişmiyor (Status zaten nvarchar), sadece geçerli DEĞER kümesi
            // değişti. Talep akışı hiç yazılmadığı için canlıda/geliştirmede kayıt
            // beklenmiyor; bu eşleme savunma amaçlıdır — eski bir değer kalırsa
            // enum'a çevrilemez ve okuma anında patlar.
            //
            //   PENDING             -> PENDING_EQUIPMENT (zincirin ilk adımı)
            //   APPROVED            -> SCHEDULED         (onaylanmış = planlanmış)
            //   REJECTED            -> REJECTED_BY_EQUIPMENT
            //   REVISION_REQUESTED  -> DRAFT             (top yeniden talep sahibinde)
            // DRAFT / SUBMITTED / CANCELLED aynı kalır.
            // ---------------------------------------------------------------
            migrationBuilder.Sql(@"
UPDATE Requests SET Status = N'PENDING_EQUIPMENT'     WHERE Status = N'PENDING';
UPDATE Requests SET Status = N'SCHEDULED'             WHERE Status = N'APPROVED';
UPDATE Requests SET Status = N'REJECTED_BY_EQUIPMENT' WHERE Status = N'REJECTED';
UPDATE Requests SET Status = N'DRAFT'                 WHERE Status = N'REVISION_REQUESTED';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Roles",
                keyColumn: "RoleId",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "Roles",
                keyColumn: "RoleId",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "Roles",
                keyColumn: "RoleId",
                keyValue: 10);

            migrationBuilder.DropColumn(
                name: "ActualEndTime",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "ActualStartTime",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "AssignedLicensePlate",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "AssignedOperatorName",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "EquipmentDecisionAt",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "FirmDecisionAt",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "Requests");

            migrationBuilder.UpdateData(
                table: "ApprovalFlowSteps",
                keyColumn: "FlowStepId",
                keyValue: 2,
                column: "Name",
                value: "Departman Müdürü Onayı");

            // Yeniden adlandırmanın geri alınması. Up ile simetrik guard'lar:
            // eski kod başka bir satırda duruyorsa UNIQUE index'e takılmasın.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM Roles WHERE RoleId = 2 AND Code = N'EQUIPMENT_MANAGER')
   AND NOT EXISTS (SELECT 1 FROM Roles WHERE Code = N'SUPERVISOR')
BEGIN
    UPDATE Roles SET Code = N'SUPERVISOR', Name = N'Amir' WHERE RoleId = 2;
END;

IF EXISTS (SELECT 1 FROM Roles WHERE RoleId = 3 AND Code = N'BUDGET_MANAGER')
   AND NOT EXISTS (SELECT 1 FROM Roles WHERE Code = N'DEPT_HEAD')
BEGIN
    UPDATE Roles SET Code = N'DEPT_HEAD', Name = N'Departman Müdürü' WHERE RoleId = 3;
END;

-- Talep durumları KAYIPLI geri döner: PENDING_FIRM ve IN_PROGRESS/COMPLETED
-- eski kümede karşılıksızdır, en yakın eski değere düşürülür. Down yalnızca
-- geliştirme ortamı için bir kaçış kapısıdır; canlı veri üzerinde koşturulmaz.
UPDATE Requests SET Status = N'PENDING'            WHERE Status IN (N'PENDING_EQUIPMENT', N'PENDING_FIRM');
UPDATE Requests SET Status = N'APPROVED'           WHERE Status IN (N'SCHEDULED', N'IN_PROGRESS', N'COMPLETED');
UPDATE Requests SET Status = N'REJECTED'           WHERE Status IN (N'REJECTED_BY_EQUIPMENT', N'REJECTED_BY_FIRM');");
        }
    }
}
