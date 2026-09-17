using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MipRental.Data.Migrations
{
    /// <summary>
    /// Devir öncesi temizlik — demo/test HAREKET verisini siler. Şema DEĞİŞMEZ.
    ///
    /// !!! BU MIGRATION VERİ SİLER VE Down() İLE GERİ ALINAMAZ. !!!
    /// Yalnızca HENÜZ CANLIYA ALINMAMIŞ bir veritabanında çalıştırılır. Canlıda
    /// asla `database update` ile değil, `migrations script` çıktısı gözden
    /// geçirilerek uygulanır (DEVIR-TESLIM bölüm 3).
    ///
    /// DOKUNULMAYAN İKİ TABLO — bilinçli:
    ///   AuditLog            : değişikliğin izi kaydın kendisinden bağımsız durur
    ///                         (CLAUDE.md kural 1). Silinen demo satırlarının izi
    ///                         kalır; bu bir tutarsızlık değil, denetim izinin
    ///                         tanımıdır.
    ///   GeneratedDocuments  : üretilmiş kâğıdın doğrulama kodu çalışmaya devam
    ///                         eder (ADR-020). Kaydı silinmiş bir belge artık
    ///                         DocumentNo/durum göstermez —
    ///                         DocumentVerificationService bu durumu zaten
    ///                         null-güvenli ele alır, sayfa patlamaz.
    ///   Bu yüzden DocumentSeries sayaçları da SIFIRLANMAZ: sıfırlansaydı gerçek
    ///   kayıtlar, arşivde duran demo belgeleriyle AYNI belge numarasını alırdı.
    ///
    /// Silme sırası FK bağımlılığıdır — tüm FK'lar Restrict'tir (ADR-010),
    /// cascade yoktur, çocuk önce gider. Migration kendi transaction'ında çalışır:
    /// bir DELETE düşerse hiçbiri uygulanmaz.
    ///
    /// Tekrar çalıştırılabilir: koşulsuz DELETE/UPDATE, boş tabloda da sorunsuz.
    /// </summary>
    public partial class RemoveDemoTransactionData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- 1) Hakediş dalı. ApprovalTokens -> ProgressPayments, ProgressPaymentRecords -> (ProgressPayments, WorkRecords)
DELETE FROM ApprovalTokens;
DELETE FROM ProgressPaymentRecords;
DELETE FROM ProgressPayments;

-- 2) Polimorfik bağlı hareket tabloları (DocumentType + DocumentId; FK yok,
--    bu yüzden sıradan bağımsızlar ama öksüz kalmasınlar diye burada silinirler).
DELETE FROM Approvals;
DELETE FROM Notifications;
DELETE FROM IntegrationQueue;

-- 3) Çalışma kaydı dalı. WorkRecords'un KENDİNE FK'si var (RevisionOfId);
--    tüm satırlar tek DELETE ile gittiği için revizyon zinciri kilitlenmez.
DELETE FROM WorkRecordLines;
DELETE FROM WorkRecords;

-- 4) Talep dalı. WorkRecords -> Requests olduğu için talepler EN SON gider.
DELETE FROM RequestLines;
DELETE FROM Requests;

-- 5) Eski/geçiş hesapları (DEVIR-TESLIM bölüm 7). Silinmez, pasife çekilir:
--    AuditLog ve GeneratedDocuments bu kullanıcılara FK ile bağlıdır.
UPDATE Users SET IsActive = 0 WHERE UserName IN (N'supervisor', N'testvinc') AND IsActive = 1;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Silinen hareket verisi geri getirilemez. Buraya bir INSERT yazmak,
            // geri alınabilir olduğu yalanını söylemek olurdu. Geri dönüş yolu
            // veritabanı yedeğidir (DEVIR-TESLIM bölüm 7).
            //
            // Hesapların pasifliği ise gerçekten geri alınabilir.
            migrationBuilder.Sql(
                "UPDATE Users SET IsActive = 1 WHERE UserName IN (N'supervisor', N'testvinc') AND IsActive = 0;");
        }
    }
}
