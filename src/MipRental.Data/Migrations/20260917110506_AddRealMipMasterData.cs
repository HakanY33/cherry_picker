using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MipRental.Data.Migrations
{
    /// <summary>
    /// ADIM 18 — MIP'ten gelen GERÇEK ana veri. Şema DEĞİŞMEZ, yalnızca veri.
    ///
    /// Üç blok:
    ///   1. Varyantlar: mevcut ikisinin adı Excel formundaki yazıma çevrilir
    ///      (HasData ile eşleşen UpdateData), eksik dört varyant eklenir.
    ///   2. Lokasyonlar: Adım 17'nin UYDURMA demo ağacı (Rıhtım 1/2/3, A Blok...)
    ///      gerçek liman lokasyonlarıyla değiştirilir.
    ///   3. Departmanlar: MIP'in sekiz müdürlüğü eklenir; demo "Operasyon"
    ///      departmanı pasife çekilir.
    ///
    /// SABİT id YAZILMAZ (AddDemoSeedData ile aynı gerekçe): tablolar IDENTITY ve
    /// geliştirme veritabanlarına ekrandan satır eklenmiş olabilir — ServiceVariants
    /// id 3 fiilen dolu çıktı. "Yoksa ekle" deseni her veritabanında tekrar çalışır.
    ///
    /// SİLME HER ZAMAN KOŞULLUDUR: bir talebe/çalışma kaydına bağlanmış demo satırı
    /// SİLİNMEZ, olduğu yerde kalır. Restrict FK'ları migration'ı kilitlerdi ve
    /// üzerine girilmiş gerçek veriyi silmek bu migration'ın işi değildir (kural 1).
    /// </summary>
    public partial class AddRealMipMasterData : Migration
    {
        /// <summary>
        /// Lokasyon ağacı: (kod, ad, üst kod). FullPath, LocationsController'ın
        /// ürettiğiyle AYNI kuralla kurulur (" > " ayracı, üst yol + ad) — ekranlar
        /// ve belgeler bu alanı okur, ağacı yeniden yürümez.
        ///
        /// Grup düğümleri (Rıhtımlar / Sahalar / Kapılar / Atölyeler) YALNIZCA
        /// gruplamak içindir; MIP'in listesindeki 24 adın hiçbiri değişmez, hepsi
        /// yaprak olarak birebir bu yazımla durur.
        /// </summary>
        private static readonly (string Code, string Name, string ParentCode)[] Locations =
        [
            ("MIP-RIHTIM", "Rıhtımlar", null),
            ("RIH-11",     "11 Nolu Rıhtım",    "MIP-RIHTIM"),
            ("RIH-12",     "12 Nolu Rıhtım",    "MIP-RIHTIM"),
            ("RIH-12-13",  "12-13 Nolu Rıhtım", "MIP-RIHTIM"),
            ("RIH-14-15",  "14-15 Nolu Rıhtım", "MIP-RIHTIM"),
            ("RIH-16",     "16 Nolu Rıhtım",    "MIP-RIHTIM"),
            ("RIH-19",     "19 Nolu Rıhtım",    "MIP-RIHTIM"),
            ("RIH-21",     "21 Nolu Rıhtım",    "MIP-RIHTIM"),
            ("RIH-EMH2",   "EMH 2 Rıhtım",      "MIP-RIHTIM"),

            ("MIP-SAHA",   "Sahalar", null),
            ("SAHA-A",       "A Sahası",     "MIP-SAHA"),
            ("SAHA-B",       "B Sahası",     "MIP-SAHA"),
            ("SAHA-P",       "P Sahası",     "MIP-SAHA"),
            ("SAHA-V",       "V Sahası",     "MIP-SAHA"),
            ("SAHA-CFS",     "CFS Sahası",   "MIP-SAHA"),
            ("SAHA-LIMAN",   "Liman Sahası", "MIP-SAHA"),
            ("SAHA-TERM2",   "Terminal-2",   "MIP-SAHA"),
            ("SAHA-AMBAR06", "Ambar06",      "MIP-SAHA"),

            ("MIP-KAPI",   "Kapılar", null),
            ("KAPI-A-REEFER", "A Kapı Reefer Saha Arası", "MIP-KAPI"),
            ("KAPI-B",        "B Kapı",                   "MIP-KAPI"),
            ("KAPI-B-XRAY",   "B Kapı X-RAY Alanı",       "MIP-KAPI"),
            ("KAPI-D",        "D Kapı",                   "MIP-KAPI"),

            ("MIP-ATOLYE", "Atölyeler ve Bakım Alanları", null),
            ("ATL-EKIPMAN", "Ekipman Bakım Kaynak Atölyesi", "MIP-ATOLYE"),
            ("ATL-KARGO",   "Genel Kargo Bakım Atölyesi",    "MIP-ATOLYE"),
            ("ATL-RSES",    "RS-ES Bakım Alanı",             "MIP-ATOLYE"),
            ("ATL-INSAAT",  "İnşaat Altyapı Ekipman Alanı",  "MIP-ATOLYE")
        ];

        /// <summary>Adım 17 demo ağacı. Çocuktan köke sıralı — Restrict FK bunu şart koşar.</summary>
        private static readonly string[] DemoLocationCodes =
            ["KNT-A", "KNT-B", "RIH-1", "RIH-2", "RIH-3", "KNT", "AMBAR", "ATOLYE", "LIMAN"];

        private static readonly (string Code, string Name)[] Departments =
        [
            ("EKIPMAN_BAKIM",     "Ekipman Bakım Müdürlüğü"),
            ("EKIPMAN_VARDIYA",   "Ekipman Vardiya Servis Müdürlüğü"),
            ("INSAAT_ALTYAPI",    "İnşaat Altyapı Müdürlüğü"),
            ("GUVENLIK",          "Güvenlik Müdürlüğü"),
            ("OTOMASYON",         "Otomasyon Müdürlüğü"),
            ("BILGI_ISLEM",       "Bilgi İşlem Müdürlüğü"),
            ("IDARI_ISLER",       "İdari İşler Müdürlüğü"),
            ("YARDIMCI_TESISLER", "Yardımcı Tesisler Müdürlüğü")
        ];

        /// <summary>
        /// Eksik varyantlar. "Kancalı" SEPETLİ VİNÇ DEĞİLDİR — kancalı mobil
        /// vinçtir; hizmet kategorisinin adı (CHERRY_PICKER kodlu "Mobil Vinç")
        /// ikisini de kapsıyor, bu yüzden kategori adına dokunulmaz.
        ///
        /// Bu dört varyantın SÖZLEŞME FİYAT SATIRI YOKTUR ve bilerek eklenmemiştir:
        /// uydurma birim fiyat, "fiyatı tanımlı değil" hatasından kötüdür. Bu
        /// varyantlarla açılan talep çalışma kaydına dönerken ContractLineResolver
        /// hata verir; fiyatı MIP verdiğinde ContractLines'a satır açılacak.
        /// </summary>
        private static readonly (string Code, string Name)[] Variants =
        [
            ("30T_KANCALI",  "30 Ton / Kancalı"),
            ("60T_KANCALI",  "60 Ton / Kancalı"),
            ("120T_KANCALI", "120 Ton / Kancalı"),
            ("200T",         "200 Ton")
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // HasData ile eşleşen ad güncellemesi — sabit id'ler ServiceVariantConfiguration'dan.
            migrationBuilder.UpdateData(
                table: "ServiceVariants", keyColumn: "VariantId", keyValue: 1,
                column: "Name", value: "30 Ton / Sepetli");

            migrationBuilder.UpdateData(
                table: "ServiceVariants", keyColumn: "VariantId", keyValue: 2,
                column: "Name", value: "60 Ton / Sepetli");

            AddVariants(migrationBuilder);
            ReplaceLocations(migrationBuilder);
            AddDepartments(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "ServiceVariants", keyColumn: "VariantId", keyValue: 1,
                column: "Name", value: "30 Ton Sepetli");

            migrationBuilder.UpdateData(
                table: "ServiceVariants", keyColumn: "VariantId", keyValue: 2,
                column: "Name", value: "60 Ton Sepetli");

            // Yalnızca BU migration'ın eklediği ve hiçbir harekete bağlanmamış
            // satırlar geri alınır. Demo lokasyonları GERİ GELMEZ: silinmiş olmaları
            // bu migration'ın amacıydı, geri yüklemek onları ikinci kez uydurmak olurdu.
            var variantCodes = string.Join(", ", Variants.Select(v => $"N'{v.Code}'"));
            var departmentCodes = string.Join(", ", Departments.Select(d => $"N'{d.Code}'"));

            // Lokasyonlar yapraktan köke: grup düğümü, çocukları gidene kadar silinemez.
            var locationCodes = string.Join(", ", Locations.Reverse().Select(l => $"N'{l.Code}'"));

            migrationBuilder.Sql($@"
UPDATE Users SET DepartmentId = NULL
 WHERE UserName IN (N'ekipman1', N'ekipman2', N'talep1');

UPDATE Departments SET IsActive = 1 WHERE Code = N'OPS';

DELETE FROM Departments
 WHERE Code IN ({departmentCodes})
   AND DepartmentId NOT IN (SELECT DepartmentId FROM Users WHERE DepartmentId IS NOT NULL)
   AND DepartmentId NOT IN (SELECT DepartmentId FROM UserRoles WHERE DepartmentId IS NOT NULL)
   AND DepartmentId NOT IN (SELECT DepartmentId FROM Requests)
   AND DepartmentId NOT IN (SELECT DepartmentId FROM WorkRecords WHERE DepartmentId IS NOT NULL);

DELETE FROM ServiceVariants
 WHERE Code IN ({variantCodes})
   AND VariantId NOT IN (SELECT VariantId FROM ContractLines WHERE VariantId IS NOT NULL)
   AND VariantId NOT IN (SELECT VariantId FROM RequestLines WHERE VariantId IS NOT NULL)
   AND VariantId NOT IN (SELECT VariantId FROM WorkRecordLines WHERE VariantId IS NOT NULL);

DELETE FROM Locations
 WHERE Code IN ({locationCodes})
   AND LocationId NOT IN (SELECT LocationId FROM Requests WHERE LocationId IS NOT NULL)
   AND LocationId NOT IN (SELECT LocationId FROM WorkRecords WHERE LocationId IS NOT NULL)
   AND LocationId NOT IN (SELECT ParentLocationId FROM Locations WHERE ParentLocationId IS NOT NULL);");
        }

        private static void AddVariants(MigrationBuilder migrationBuilder)
        {
            foreach (var (code, name) in Variants)
            {
                migrationBuilder.Sql($@"
IF NOT EXISTS (
    SELECT 1 FROM ServiceVariants v
    INNER JOIN ServiceCategories s ON s.ServiceId = v.ServiceId
    WHERE s.Code = N'CHERRY_PICKER' AND v.Code = N'{code}')
BEGIN
    INSERT INTO ServiceVariants (ServiceId, Code, Name, IsActive)
    SELECT ServiceId, N'{code}', N'{name}', 1 FROM ServiceCategories WHERE Code = N'CHERRY_PICKER';
END;");
            }
        }

        private static void ReplaceLocations(MigrationBuilder migrationBuilder)
        {
            foreach (var (code, name, parentCode) in Locations)
            {
                var parent = parentCode is null
                    ? "NULL"
                    : $"(SELECT TOP 1 LocationId FROM Locations WHERE Code = N'{parentCode}')";

                var fullPath = parentCode is null
                    ? name
                    : $"{Locations.First(l => l.Code == parentCode).Name} > {name}";

                migrationBuilder.Sql($@"
IF NOT EXISTS (SELECT 1 FROM Locations WHERE Code = N'{code}')
BEGIN
    INSERT INTO Locations (Code, Name, ParentLocationId, FullPath, IsActive)
    VALUES (N'{code}', N'{name}', {parent}, N'{fullPath}', 1);
END;");
            }

            // Demo lokasyonları YAPRAKTAN KÖKE silinir. Biri bir talebe bağlıysa o
            // satır (ve varsa üstü) kalır — ekranda görünmeye devam eder; bilinçli:
            // ona bağlı geçmiş kaydın lokasyonu kaybolmamalı.
            foreach (var code in DemoLocationCodes)
            {
                migrationBuilder.Sql($@"
DELETE FROM Locations
 WHERE Code = N'{code}'
   AND LocationId NOT IN (SELECT LocationId FROM Requests WHERE LocationId IS NOT NULL)
   AND LocationId NOT IN (SELECT LocationId FROM WorkRecords WHERE LocationId IS NOT NULL)
   AND LocationId NOT IN (SELECT ParentLocationId FROM Locations WHERE ParentLocationId IS NOT NULL);");
            }
        }

        private static void AddDepartments(MigrationBuilder migrationBuilder)
        {
            foreach (var (code, name) in Departments)
            {
                migrationBuilder.Sql($@"
IF NOT EXISTS (SELECT 1 FROM Departments WHERE Code = N'{code}')
BEGIN
    INSERT INTO Departments (Code, Name, ParentDepartmentId, IsActive)
    VALUES (N'{code}', N'{name}', NULL, 1);
END;");
            }

            // Demo kullanıcıları gerçek müdürlüklere bağlanır. Kozmetik değil:
            // operasyon tablosunun üst bloğu oturumdaki kullanıcının DEPARTMANINI
            // yazar, "Talep Eden Müdürlük" sütunu da talebi açanın departmanından
            // gelir — departmansız kullanıcıyla iki alan da boş çıkardı.
            migrationBuilder.Sql(@"
UPDATE Users
   SET DepartmentId = (SELECT TOP 1 DepartmentId FROM Departments WHERE Code = N'EKIPMAN_BAKIM')
 WHERE UserName IN (N'ekipman1', N'ekipman2') AND DepartmentId IS NULL;

UPDATE Users
   SET DepartmentId = (SELECT TOP 1 DepartmentId FROM Departments WHERE Code = N'EKIPMAN_VARDIYA')
 WHERE UserName = N'talep1';

-- Demo 'Operasyon' departmanı SİLİNMEZ, pasife çekilir: geçmiş talepler ona FK
-- ile bağlı olabilir. Pasif departman ekranlardaki listelerde çıkmaz.
UPDATE Departments SET IsActive = 0 WHERE Code = N'OPS' AND IsActive = 1;");
        }
    }
}
