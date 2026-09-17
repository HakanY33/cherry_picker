using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MipRental.Data.Migrations
{
    /// <summary>
    /// ADIM 18 — Adım 18'de eklenen dört varyantın DEMO fiyat satırları.
    ///
    /// Açık kapatılıyor: bu varyantlarla açılan talep çalışma kaydına dönerken
    /// ContractLineResolver "... fiyatı tanımlı değil" fırlatıyordu; fiyat satırı
    /// VARYANT bazlı tam eşleşmeyle seçildiği için (l.VariantId == variantId)
    /// varyantsız bir satır bu boşluğu kapatmıyor.
    ///
    /// FİYATLAR UYDURMADIR ve sözleşme notunda öyle yazıyor. Tonaj arttıkça artar;
    /// aynı tonajda kancalı, sepetliden bir tık ucuzdur (sepet donanımı ek kalemdir).
    /// Kural seti mevcut iki satırla AYNI: UP_30 yuvarlama, asgari 2 saat,
    /// 8 saat gün eşiği, kayıt başına en çok 24 saat, ValidTo = NULL.
    ///
    /// ValidTo = NULL bilinçlidir (AddDemoSeedData ile aynı gerekçe): fiyat satırının
    /// süresi dolmaz, demo hangi tarihte yapılırsa yapılsın fiyat bulunur. Sınırı
    /// sözleşmenin kendi EndDate'i çizer.
    ///
    /// "Yoksa ekle": aynı sözleşme + hizmet + varyant için satır varsa DOKUNULMAZ.
    /// Migration her veritabanında tekrar çalışabilir ve elle girilmiş gerçek bir
    /// fiyatın üzerine ASLA yazmaz.
    /// </summary>
    public partial class AddDemoPricesForNewVariants : Migration
    {
        private const string ContractNo = "SOZ-DEMO-001";

        private const string DemoPriceNote =
            "Demo amaçlı fiyatlar — gerçek birim fiyatlar MIP'ten alınacak.";

        /// <summary>Notta kesme işareti var; SQL literal'ine girmeden önce ikilenir.</summary>
        private static string NoteSql => DemoPriceNote.Replace("'", "''");

        /// <summary>
        /// Ondalık ayırıcı SQL'de HER ZAMAN noktadır. Sunucunun kültürü tr-TR ise
        /// varsayılan ToString "1100,0000" üretir ve SQL bunu iki argüman sanar.
        /// </summary>
        private static string Sql(decimal value) => value.ToString("0.0000", CultureInfo.InvariantCulture);

        /// <summary>
        /// (varyant kodu, saatlik, günlük, mobilizasyon). Günlük ≈ 6,8 × saatlik,
        /// mobilizasyon ≈ 0,6 × saatlik — mevcut iki satırın oranı budur; yeni
        /// satırlar aynı oranı yuvarlayarak sürdürür.
        ///
        /// Altı satırın tamamı saatlik fiyata göre artan sırada durur:
        /// 1100 &lt; 1250 &lt; 1650 &lt; 1850 &lt; 2800 &lt; 4500.
        /// </summary>
        private static readonly (string Code, decimal UnitPrice, decimal DailyPrice, decimal MobilizationFee)[] Prices =
        [
            ("30T_KANCALI",  1100m,  7500m,  650m),
            ("60T_KANCALI",  1650m, 11250m, 1000m),
            ("120T_KANCALI", 2800m, 19000m, 1700m),
            ("200T",         4500m, 30500m, 2700m)
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var rows = string.Join(",\n        ", Prices.Select(p =>
                $"(N'{p.Code}', CAST({Sql(p.UnitPrice)} AS decimal(18,4)), " +
                $"CAST({Sql(p.DailyPrice)} AS decimal(18,4)), " +
                $"CAST({Sql(p.MobilizationFee)} AS decimal(18,4)))"));

            migrationBuilder.Sql($@"
DECLARE @ContractId INT = (SELECT TOP 1 ContractId FROM Contracts WHERE ContractNo = N'{ContractNo}');
DECLARE @ServiceId  INT = (SELECT TOP 1 ServiceId FROM ServiceCategories WHERE Code = N'CHERRY_PICKER');
IF @ContractId IS NULL OR @ServiceId IS NULL RETURN;

-- ValidFrom sözleşmenin KENDİ başlangıcıdır, yeniden hesaplanmaz: mevcut iki
-- satır da oradan doğdu, ayrı bir tarih ikinci bir fiyat dönemi yaratırdı.
DECLARE @ValidFrom DATE = (SELECT StartDate FROM Contracts WHERE ContractId = @ContractId);

INSERT INTO ContractLines
    (ContractId, ServiceId, VariantId, UnitPrice, Currency, MinBillableQuantity, RoundingRule,
     DayThresholdHours, DailyPrice, MobilizationFee, MaxQuantityPerRecord, ValidFrom, ValidTo, IsActive, CreatedAt)
SELECT @ContractId, @ServiceId, v.VariantId, p.UnitPrice, N'TRY', 2, N'UP_30',
       8, p.DailyPrice, p.MobilizationFee, 24, @ValidFrom, NULL, 1, SYSUTCDATETIME()
FROM (VALUES
        {rows}
     ) AS p(Code, UnitPrice, DailyPrice, MobilizationFee)
JOIN ServiceVariants v ON v.ServiceId = @ServiceId AND v.Code = p.Code
WHERE NOT EXISTS (
    SELECT 1 FROM ContractLines cl
     WHERE cl.ContractId = @ContractId AND cl.ServiceId = @ServiceId AND cl.VariantId = v.VariantId);

-- Not EKLENİR, mevcut not silinmez: ""Adım 17 demo başlangıç verisi"" hâlâ doğru.
-- CHARINDEX kontrolü migration'ın tekrar çalışmasında notu ikilemesin diye.
UPDATE Contracts
   SET Notes = LTRIM(RTRIM(ISNULL(Notes, N'') + N' ' + N'{NoteSql}'))
 WHERE ContractId = @ContractId
   AND CHARINDEX(N'{NoteSql}', ISNULL(Notes, N'')) = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            var codes = string.Join(", ", Prices.Select(p => $"N'{p.Code}'"));

            // Bir çalışma kaydı satırına bağlanmış fiyat satırı SİLİNMEZ: o satır
            // artık bir hesaplamanın kaynağıdır (kural 2 — snapshot satırda tutulur
            // ama satırın kendisi denetim izinin parçasıdır).
            migrationBuilder.Sql($@"
DECLARE @ContractId INT = (SELECT TOP 1 ContractId FROM Contracts WHERE ContractNo = N'{ContractNo}');
IF @ContractId IS NULL RETURN;

DELETE FROM ContractLines
 WHERE ContractId = @ContractId
   AND VariantId IN (SELECT VariantId FROM ServiceVariants WHERE Code IN ({codes}))
   AND ContractLineId NOT IN (SELECT ContractLineId FROM WorkRecordLines WHERE ContractLineId IS NOT NULL)
   AND ContractLineId NOT IN (SELECT ContractLineId FROM ContractLineSurcharges);

UPDATE Contracts
   SET Notes = NULLIF(LTRIM(RTRIM(REPLACE(ISNULL(Notes, N''), N'{NoteSql}', N''))), N'')
 WHERE ContractId = @ContractId;");
        }
    }
}
