using Microsoft.EntityFrameworkCore;
using MipRental.Domain.Enums;

namespace MipRental.Data.Services;

/// <summary>
/// /Dogrula/{kod} sayfasının veri kaynağı.
///
/// Ayrı bir servis olmasının sebebi tek bir güvenlik kuralını tek yerde
/// tutmaktır: BU SORGU KİŞİSEL VERİ VE PARA BİLGİSİ DÖNDÜRMEZ.
///
/// ADIM 9 — FİYAT GİZLİLİĞİ: sayfa anonim erişilebilir olduğu için karekodu
/// gören HERKES döneni görür. Tutar bu yüzden ne çekilir ne de döndürülür;
/// DocumentVerificationResult'ta öyle bir alan YOKTUR. Aşağıdaki Select listesi bilinçli
/// olarak dardır — operatör adı, telefon, e-posta, onaylayan/üreten kullanıcı
/// adları, iş tanımı ve lokasyon HİÇ ÇEKİLMEZ. Böylece controller'da yanlışlıkla
/// fazladan bir alan ekranlamak mümkün olmaz; entity'nin tamamı ortada dolaşmaz.
///
/// Firma izolasyon filtresi bilinçli olarak atlanır: sayfa oturum açmamış birine
/// de açıktır. Güvenliği sağlayan şey tahmin edilemez doğrulama kodudur, kimlik
/// değil.
/// </summary>
public sealed class DocumentVerificationService
{
    private readonly AppDbContext _db;

    public DocumentVerificationService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<DocumentVerificationResult?> VerifyAsync(string? code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var document = await _db.GeneratedDocuments.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.VerificationCode == code)
            .Select(d => new
            {
                d.GeneratedDocumentId,
                d.DocumentType,
                d.DocumentId,
                d.Kind,
                d.ContentHash,
                d.TemplateVersion,
                d.GeneratedAt,
                // TotalAmount / Currency BİLİNÇLİ OLARAK ÇEKİLMEZ (Adım 9).
                FirmTitle = d.Firm != null ? d.Firm.Title : null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (document is null)
        {
            return null;
        }

        string? documentNo = null;
        WorkRecordStatus? recordStatus = null;
        int? year = null;
        int? month = null;

        if (document.DocumentType == DocumentType.WORK_RECORD)
        {
            var record = await _db.WorkRecords.IgnoreQueryFilters().AsNoTracking()
                .Where(w => w.WorkRecordId == document.DocumentId)
                .Select(w => new { w.DocumentNo, w.Status, w.Period.Year, w.Period.Month })
                .FirstOrDefaultAsync(cancellationToken);

            documentNo = record?.DocumentNo;
            recordStatus = record?.Status;
            year = record?.Year;
            month = record?.Month;
        }
        else if (document.DocumentType == DocumentType.PERIOD)
        {
            var period = await _db.Periods.AsNoTracking()
                .Where(p => p.PeriodId == document.DocumentId)
                .Select(p => new { p.Year, p.Month })
                .FirstOrDefaultAsync(cancellationToken);

            year = period?.Year;
            month = period?.Month;
        }

        return new DocumentVerificationResult
        {
            Kind = document.Kind,
            DocumentNo = documentNo,
            FirmTitle = document.FirmTitle,
            Year = year,
            Month = month,
            RecordStatus = recordStatus,
            GeneratedAtUtc = document.GeneratedAt,
            ContentHash = document.ContentHash,
            TemplateVersion = document.TemplateVersion,

            // Aynı kayıt için sonradan yeni bir sürüm üretilmiş mi? Eldeki kâğıt
            // hâlâ geçerli bir belgedir ama "daha yenisi var" bilgisi doğrulayan
            // kişi için önemlidir.
            HasNewerVersion = await _db.GeneratedDocuments.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(d => d.DocumentType == document.DocumentType
                            && d.DocumentId == document.DocumentId
                            && d.Kind == document.Kind
                            && d.GeneratedDocumentId != document.GeneratedDocumentId
                            && d.GeneratedAt > document.GeneratedAt, cancellationToken)
        };
    }
}

/// <summary>
/// Doğrulama sayfasına dönen veri. Buraya KİŞİSEL VERİ veya PARA ALANI
/// EKLENMEZ — sayfa açık erişimlidir. Tutar alanı Adım 9'da kaldırıldı:
/// karekodu gören herkes tutarı görebiliyordu.
/// </summary>
public sealed class DocumentVerificationResult
{
    public required GeneratedDocumentKind Kind { get; init; }
    public string? DocumentNo { get; init; }
    public string? FirmTitle { get; init; }
    public int? Year { get; init; }
    public int? Month { get; init; }
    public WorkRecordStatus? RecordStatus { get; init; }
    public required DateTime GeneratedAtUtc { get; init; }
    public required string ContentHash { get; init; }
    public string? TemplateVersion { get; init; }
    public required bool HasNewerVersion { get; init; }
}
