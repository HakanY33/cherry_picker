using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MipRental.Data.Migrations
{
    /// <summary>
    /// Windows-1254 (Türkçe ANSI) ile URL-kodlanmış metinleri onarır.
    ///
    /// SEBEP — uygulama kaynaklı DEĞİLDİR, DIŞ TEST İSTEMCİSİNDENDİR:
    /// Veritabanında "Sistem Y%F6neticisi", "Duman Testi Kullan%FDc%FD" ve
    /// "Mobil Vin%E7 Kiralama S%F6zle%FEmesi" gibi değerler vardı. AuditLog üçünün
    /// de uygulamanın kendi HTTP uçlarından, ::1 (localhost) üzerinden, oturum
    /// açmış bir istemciyle geldiğini gösteriyor.
    ///
    /// Kodlar Windows-1254 (Türkçe ANSI) baytlarıdır: %E7=ç, %F6=ö, %FD=ı, %FE=ş.
    /// %FE belirleyici: ş harfi Latin-1'de yoktur, yani kaynak kesinlikle Türkçe
    /// kod sayfasıdır.
    ///
    /// Zincirin tamamı bu makinede birebir yeniden üretildi:
    ///   1) Bu makinenin ANSI kod sayfası 1254'tür.
    ///   2) Windows'taki curl, KOMUT SATIRI ARGÜMANI olarak verilen metni bu kod
    ///      sayfasından geçirir. Ölçüm:
    ///        curl --data-urlencode $'FullName=Yöneticisi'  -> FullName=Y%F6neticisi
    ///        curl --data-urlencode  'FullName@dosya.txt'   -> FullName=Y%C3%B6neticisi
    ///      Yani hata argümandan okumakta; dosyadan okunan gerçek UTF-8 doğru gidiyor.
    ///   3) ASP.NET Core "%F6" kaçışını UTF-8 olarak ÇÖZEMEDİĞİ için olduğu gibi
    ///      bırakır ("+" işaretini boşluğa çevirir). Sonuç, veritabanında
    ///      bulduğumuz değerin ta kendisidir.
    ///      (bkz. TurkishTextRoundTripTests.FormBinding_LeavesNonUtf8PercentEscapesUntouched...)
    ///
    /// Yani uygulama hiçbir dönüşüm yapmadı; gelen bozuk metni sadakatle sakladı.
    /// Kaynak, Türkçe metni komut satırı argümanı olarak curl'e veren eski bir
    /// duman testi betiğidir.
    ///
    /// Uygulamanın kendisinde hiçbir URL kodlama/çözme çağrısı yok, sayfalar
    /// UTF-8, form binding ve htmx UTF-8 çalışıyor. Bu yüzden düzeltilecek bir
    /// uygulama hatası bulunmadı; yalnızca eski test artığı veri onarılıyor.
    ///
    /// İLERİSİ İÇİN NOT: bu depoda Türkçe metinle HTTP denemesi yapan herkes,
    /// değeri komut satırına yazmak yerine dosyadan okutmalıdır
    /// (curl --data-urlencode "Alan@dosya.txt").
    ///
    /// KAPSAM — bilinçli olarak dar:
    /// Sadece Türkçe HARF karşılıkları değiştirilir. %20 (boşluk) ve %25 (yüzde
    /// işareti) ÇEVRİLMEZ: Türkçe iş metinlerinde "%20 indirim" gibi ifadeler
    /// gerçektir ve bunları boşluğa çevirmek veriyi bozardı. Aşağıdaki on iki
    /// dizinin hiçbiri doğal metinde yüzde ifadesi olarak geçemez.
    ///
    /// İşlem idempotenttir: düzeltilmiş satır ikinci çalıştırmada eşleşmez.
    /// </summary>
    public partial class FixCp1254UrlEncodedText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tüm metin kolonlarını tarayan dinamik SQL; hangi kolonların
            // etkilendiği zamanla değişebileceği için kolon listesi elle yazılmadı.
            // REPLACE zinciri yalnızca yukarıda gerekçesi verilen 12 diziyi çevirir.
            migrationBuilder.Sql(@"
SET NOCOUNT ON;

DECLARE @Fix nvarchar(max) = N'
    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        {col},
        N''%C7'', N''Ç''), N''%D0'', N''Ğ''), N''%DD'', N''İ''),
        N''%D6'', N''Ö''), N''%DE'', N''Ş''), N''%DC'', N''Ü''),
        N''%E7'', N''ç''), N''%F0'', N''ğ''), N''%FD'', N''ı''),
        N''%F6'', N''ö''), N''%FE'', N''ş''), N''%FC'', N''ü'')';

DECLARE @Sql nvarchar(max) = N'';

SELECT @Sql = @Sql + N'UPDATE ' + QUOTENAME(t.name) +
       N' SET ' + QUOTENAME(c.name) + N' = ' + REPLACE(@Fix, N'{col}', QUOTENAME(c.name)) +
       N' WHERE ' + QUOTENAME(c.name) +
       N' LIKE N''%[%][CDEF][0-9A-F]%'' COLLATE Latin1_General_CI_AS;' + CHAR(10)
FROM sys.tables t
JOIN sys.columns c ON c.object_id = t.object_id
JOIN sys.types ty ON c.user_type_id = ty.user_type_id
WHERE ty.name IN ('nvarchar', 'nchar', 'varchar', 'char')
  AND c.max_length <> -1          -- (max) kolonlar hedeflenmiyor; etkilenen alanlar kısa metin
  AND t.is_ms_shipped = 0
  AND t.name NOT IN (
        '__EFMigrationsHistory',
        -- AuditLog KASTEN DIŞARIDA: o tablo ""o an ne yazıldığının"" kaydıdır.
        -- Geçmişteki bozuk değeri düzeltmek, olayın kanıtını silmek olurdu;
        -- bozuk verinin nereden geldiği ancak orada okunabiliyor.
        'AuditLog');

EXEC sp_executesql @Sql;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Geri alınmaz ve alınmamalıdır: bu bir veri ONARIMIDIR. Doğru yazılmış
            // Türkçe metinleri tekrar bozacak bir Down() yazmak, ileride yapılacak
            // bir rollback'te veriyi kasten bozmak olurdu.
        }
    }
}
