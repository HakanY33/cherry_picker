using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Migrations;
using MipRental.Domain.Entities;

#nullable disable

namespace MipRental.Data.Migrations
{
    /// <summary>
    /// ADIM 17 — teslim/demo başlangıç verisi.
    ///
    /// Amaç: veritabanı SIFIRDAN kurulduğunda (dotnet ef database update) demo
    /// akışı hiçbir yerde "veri yok" hatasına düşmeden yürüsün. Şema DEĞİŞMEZ,
    /// yalnızca veri eklenir.
    ///
    /// Sıfırdan kurulumda HasData ile zaten gelenler (burada TEKRARLANMAZ):
    ///   Roller (1-10), varsayılan onay akışı WR-DEFAULT + iki adımı,
    ///   2026 dönemleri, belge serileri, CHERRY_PICKER hizmeti,
    ///   30T_SEPETLI / 60T_SEPETLI varyantları.
    /// Önceki seed migration'larıyla gelenler: TESTVINC firması, OPS departmanı,
    ///   admin / testvinc / supervisor / depthead / butce / talep1 / ekipman1 / firma1.
    ///
    /// Sıfırdan kurulumda EKSİK olan ve demoyu kıranlar — bu migration onları kapatır:
    ///   1. SÖZLEŞME VE FİYAT SATIRI HİÇ YOKTU. Talep tamamlanıp çalışma kaydına
    ///      dönerken ContractLineResolver "... fiyatı tanımlı değil" fırlatıyordu.
    ///      Fiyat satırları VARYANT bazlıdır (l.VariantId == variantId, tam eşleşme):
    ///      talep 30T/60T seçtiği için varyantsız bir satır EŞLEŞMEZ. Bu yüzden
    ///      her iki varyant için ayrı satır açılır.
    ///   2. 2027 dönemleri yoktu — gelecek yıla kayıt girilemiyordu (kural 4).
    ///   3. Lokasyon ağacı boştu — talep formundaki lokasyon listesi boş geliyordu.
    ///   4. FIRM_OPERATOR / EQUIPMENT_VIEWER / ACCOUNTING rollerinde hesap yoktu;
    ///      operatör ekranı ve fiyat gizliliği demosu gösterilemiyordu.
    ///
    /// Desen önceki seed migration'larla aynı: SABİT id YAZILMAZ (tablolar
    /// IDENTITY'dir ve geliştirme veritabanlarına elle eklenmiş satırlar araya
    /// girmiş olabilir), "yoksa ekle" mantığı kullanılır; her veritabanında
    /// tekrar çalışabilir.
    /// </summary>
    public partial class AddDemoSeedData : Migration
    {
        // Firms.Code — AddAuthSeedData ile gelir.
        private const string FirmCode = "TESTVINC";

        // Demo sözleşmesi. Mevcut geliştirme veritabanlarındaki SOZ-2026-001'den
        // BİLEREK ayrı bir numara: demo verisi demo olduğu anlaşılacak şekilde durur
        // ve elle girilmiş sözleşmeyle çakışmaz.
        private const string ContractNo = "SOZ-DEMO-001";

        // RoleConfiguration.HasData ile sabit id'ler.
        private const int AccountingRoleId = 5;
        private const int EquipmentViewerRoleId = 8;
        private const int FirmOperatorRoleId = 10;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            SeedLocations(migrationBuilder);
            SeedPeriods(migrationBuilder, 2027);
            SeedContractAndPrices(migrationBuilder);
            SeedDemoUsers(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sadece BU migration'ın eklediği, hiçbir harekete bağlanmamış satırlar
            // geri alınır. Bir talebe/kayda bağlanmış lokasyon, dönem ya da fiyat
            // satırına DOKUNULMAZ: Restrict FK'ları Down'ı kilitler ve demo
            // verisini geri almak, üzerine girilmiş gerçek veriyi silme gerekçesi
            // değildir (kural 1).
            migrationBuilder.Sql(@"
DELETE FROM UserRoles WHERE UserId IN (SELECT UserId FROM Users WHERE UserName IN (N'operator1', N'ekipman2', N'muhasebe'));
DELETE FROM Users WHERE UserName IN (N'operator1', N'ekipman2', N'muhasebe')
  AND UserId NOT IN (SELECT RequestedByUserId FROM Requests)
  AND UserId NOT IN (SELECT EnteredByUserId FROM WorkRecords);

DELETE FROM ContractLines
 WHERE ContractId IN (SELECT ContractId FROM Contracts WHERE ContractNo = N'SOZ-DEMO-001')
   AND ContractLineId NOT IN (SELECT ContractLineId FROM WorkRecordLines WHERE ContractLineId IS NOT NULL);
DELETE FROM Contracts
 WHERE ContractNo = N'SOZ-DEMO-001'
   AND ContractId NOT IN (SELECT ContractId FROM ContractLines);

DELETE FROM Periods
 WHERE Year = 2027 AND Status = N'OPEN'
   AND PeriodId NOT IN (SELECT PeriodId FROM WorkRecords WHERE PeriodId IS NOT NULL);

DELETE FROM Locations
 WHERE Code IN (N'KNT-A', N'KNT-B', N'RIH-1', N'RIH-2', N'RIH-3', N'AMBAR', N'ATOLYE', N'KNT', N'LIMAN')
   AND LocationId NOT IN (SELECT LocationId FROM Requests WHERE LocationId IS NOT NULL)
   AND LocationId NOT IN (SELECT LocationId FROM WorkRecords WHERE LocationId IS NOT NULL)
   AND LocationId NOT IN (SELECT ParentLocationId FROM Locations WHERE ParentLocationId IS NOT NULL);");
        }

        /// <summary>
        /// Liman lokasyon ağacı. FullPath, LocationsController'ın ürettiğiyle AYNI
        /// biçimdedir (" > " ayracı) — ekranlar ve PDF'ler bu alanı okur, ağacı
        /// yeniden yürümez; burada tutarsız yazılırsa ekranda tutarsız görünür.
        /// </summary>
        private static void SeedLocations(MigrationBuilder migrationBuilder)
        {
            AddLocation(migrationBuilder, "LIMAN", "Liman Sahası", null, "Liman Sahası");
            AddLocation(migrationBuilder, "RIH-1", "Rıhtım 1", "LIMAN", "Liman Sahası > Rıhtım 1");
            AddLocation(migrationBuilder, "RIH-2", "Rıhtım 2", "LIMAN", "Liman Sahası > Rıhtım 2");
            AddLocation(migrationBuilder, "RIH-3", "Rıhtım 3", "LIMAN", "Liman Sahası > Rıhtım 3");
            AddLocation(migrationBuilder, "KNT", "Konteyner Sahası", "LIMAN", "Liman Sahası > Konteyner Sahası");
            AddLocation(migrationBuilder, "KNT-A", "A Blok", "KNT", "Liman Sahası > Konteyner Sahası > A Blok");
            AddLocation(migrationBuilder, "KNT-B", "B Blok", "KNT", "Liman Sahası > Konteyner Sahası > B Blok");
            AddLocation(migrationBuilder, "AMBAR", "Kapalı Ambar", "LIMAN", "Liman Sahası > Kapalı Ambar");
            AddLocation(migrationBuilder, "ATOLYE", "Bakım Atölyesi", "LIMAN", "Liman Sahası > Bakım Atölyesi");
        }

        private static void AddLocation(
            MigrationBuilder migrationBuilder, string code, string name, string parentCode, string fullPath)
        {
            var parent = parentCode is null
                ? "NULL"
                : $"(SELECT TOP 1 LocationId FROM Locations WHERE Code = N'{parentCode}')";

            migrationBuilder.Sql($@"
IF NOT EXISTS (SELECT 1 FROM Locations WHERE Code = N'{code}')
BEGIN
    INSERT INTO Locations (Code, Name, ParentLocationId, FullPath, IsActive)
    VALUES (N'{code}', N'{name}', {parent}, N'{fullPath}', 1);
END;");
        }

        /// <summary>
        /// Bir yılın 12 dönemi, OPEN. Dönem yoksa o tarihe kayıt HİÇ girilemez
        /// (PeriodGuardInterceptor); demo gelecek yıla da kayıt girebilmeli.
        /// </summary>
        private static void SeedPeriods(MigrationBuilder migrationBuilder, int year)
        {
            migrationBuilder.Sql($@"
INSERT INTO Periods (Year, Month, Status)
SELECT {year}, m.Month, N'OPEN'
FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) AS m(Month)
WHERE NOT EXISTS (SELECT 1 FROM Periods p WHERE p.Year = {year} AND p.Month = m.Month);");
        }

        /// <summary>
        /// Demo sözleşmesi ve İKİ varyant için fiyat satırı.
        ///
        /// ValidTo = NULL bilinçlidir: fiyat satırının süresi dolmaz, dolayısıyla
        /// demo hangi tarihte yapılırsa yapılsın "fiyatı tanımlı değil" çıkmaz.
        /// Sınırı sözleşmenin kendi EndDate'i çizer (bu yıl + gelecek yıl).
        ///
        /// Fiyatlandırma parametreleri bilinçli olarak doludur; demo motorun
        /// tamamını göstersin diye: yuvarlama (UP_30), asgari faturalanabilir
        /// miktar, gün eşiği + günlük tarife, sefer başı mobilizasyon bedeli.
        /// </summary>
        private static void SeedContractAndPrices(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
DECLARE @FirmId INT = (SELECT TOP 1 FirmId FROM Firms WHERE Code = N'{FirmCode}');
IF @FirmId IS NULL RETURN;

-- Sözleşme bu yılın başından GELECEK yılın sonuna kadar geçerli.
DECLARE @StartDate DATE = DATEFROMPARTS(YEAR(SYSUTCDATETIME()), 1, 1);
DECLARE @EndDate   DATE = DATEFROMPARTS(YEAR(SYSUTCDATETIME()) + 1, 12, 31);

IF NOT EXISTS (SELECT 1 FROM Contracts WHERE FirmId = @FirmId AND ContractNo = N'{ContractNo}')
BEGIN
    INSERT INTO Contracts (FirmId, ContractNo, Title, StartDate, EndDate, Currency, Status, Notes, CreatedAt)
    VALUES (@FirmId, N'{ContractNo}', N'Mobil Vinç Kiralama Sözleşmesi (Demo)',
            @StartDate, @EndDate, N'TRY', N'ACTIVE',
            N'Adım 17 demo başlangıç verisi. Gerçek sözleşme değildir.', SYSUTCDATETIME());
END;

DECLARE @ContractId INT = (SELECT TOP 1 ContractId FROM Contracts WHERE FirmId = @FirmId AND ContractNo = N'{ContractNo}');
DECLARE @ServiceId  INT = (SELECT TOP 1 ServiceId FROM ServiceCategories WHERE Code = N'CHERRY_PICKER');
IF @ContractId IS NULL OR @ServiceId IS NULL RETURN;

INSERT INTO ContractLines
    (ContractId, ServiceId, VariantId, UnitPrice, Currency, MinBillableQuantity, RoundingRule,
     DayThresholdHours, DailyPrice, MobilizationFee, MaxQuantityPerRecord, ValidFrom, ValidTo, IsActive, CreatedAt)
SELECT @ContractId, @ServiceId, v.VariantId, p.UnitPrice, N'TRY', 2, N'UP_30',
       8, p.DailyPrice, p.MobilizationFee, 24, @StartDate, NULL, 1, SYSUTCDATETIME()
FROM (VALUES
        (N'30T_SEPETLI', CAST( 1250.0000 AS decimal(18,4)), CAST( 8500.0000 AS decimal(18,4)), CAST( 750.0000 AS decimal(18,4))),
        (N'60T_SEPETLI', CAST( 1850.0000 AS decimal(18,4)), CAST(12500.0000 AS decimal(18,4)), CAST(1100.0000 AS decimal(18,4)))
     ) AS p(Code, UnitPrice, DailyPrice, MobilizationFee)
JOIN ServiceVariants v ON v.ServiceId = @ServiceId AND v.Code = p.Code
WHERE NOT EXISTS (
    SELECT 1 FROM ContractLines cl
     WHERE cl.ContractId = @ContractId AND cl.ServiceId = @ServiceId AND cl.VariantId = v.VariantId);");
        }

        /// <summary>
        /// Demo hesapları. Sıfırdan kurulumda FIRM_OPERATOR, EQUIPMENT_VIEWER ve
        /// ACCOUNTING rollerinde HİÇ kullanıcı yoktu; bu üç rol olmadan operatör
        /// ekranı (Adım 12) ve fiyat gizliliği (Adım 9) gösterilemez.
        ///
        /// Şifre, kullanıcı ZATEN VARSA da yazılır. Sebep: operator1 geliştirme
        /// veritabanına ekrandan, üretilmiş bir şifreyle eklenmişti; README'deki
        /// tablo ile veritabanı aksi hâlde uyuşmazdı. Bunlar demo hesaplarıdır ve
        /// migration veritabanı başına BİR KEZ çalışır.
        /// </summary>
        private static void SeedDemoUsers(MigrationBuilder migrationBuilder)
        {
            var hasher = new PasswordHasher<User>();

            // CLAUDE.md: şifreler PasswordHasher ile hashlenir, düz metin yazılmaz.
            SeedUser(migrationBuilder, "operator1", "Test Vinç Operatörü", "Operatör",
                hasher.HashPassword(new User(), "Operator!2345"), FirmOperatorRoleId, withFirm: true);

            SeedUser(migrationBuilder, "ekipman2", "Ekipman Müdürlüğü Kullanıcısı", "Ekipman Sorumlusu",
                hasher.HashPassword(new User(), "Ekipman!2345"), EquipmentViewerRoleId, withFirm: false);

            SeedUser(migrationBuilder, "muhasebe", "Muhasebe Uzmanı", "Muhasebe",
                hasher.HashPassword(new User(), "Muhasebe!2345"), AccountingRoleId, withFirm: false);
        }

        private static void SeedUser(
            MigrationBuilder migrationBuilder, string userName, string fullName, string position,
            string passwordHash, int roleId, bool withFirm)
        {
            var firmValue = withFirm
                ? $"(SELECT TOP 1 FirmId FROM Firms WHERE Code = N'{FirmCode}')"
                : "NULL";

            // Hash base64'tür, tırnak içermez.
            migrationBuilder.Sql($@"
IF NOT EXISTS (SELECT 1 FROM Users WHERE UserName = N'{userName}')
BEGIN
    INSERT INTO Users (UserName, FullName, Position, DepartmentId, FirmId, IsFirmAdmin, PasswordHash, IsActive, CreatedAt)
    VALUES (N'{userName}', N'{fullName}', N'{position}', NULL, {firmValue}, 0, N'{passwordHash}', 1, SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE Users
       SET PasswordHash = N'{passwordHash}',
           FirmId = COALESCE(FirmId, {firmValue}),
           Position = COALESCE(Position, N'{position}'),
           IsActive = 1
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
