using MipRental.Domain.Enums;

namespace MipRental.Domain.Entities;

public class GeneratedDocument
{
    public int GeneratedDocumentId { get; set; }
    public DocumentType DocumentType { get; set; }
    public int DocumentId { get; set; }
    public GeneratedDocumentKind Kind { get; set; }

    // Aylık icmalde DocumentId dönemi gösterir; hangi firmanın icmali olduğu
    // ancak bu alanla belli olur. Çalışma kaydı formunda da doldurulur, böylece
    // doğrulama sayfası kaydın kendisine hiç gitmeden firmayı gösterebilir.
    public int? FirmId { get; set; }
    public string FileName { get; set; } = null!;
    public string StoragePath { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
    public string? VerificationCode { get; set; }
    public string? TemplateVersion { get; set; }

    // Belgenin ÜZERİNDE YAZAN tutar. Kaydın güncel tutarına bakılmaz: belge
    // üretildikten sonra kayıt revize edilirse doğrulama sayfası hâlâ elindeki
    // kâğıtta yazan tutarı göstermelidir (CLAUDE.md kural 2'nin aynı mantığı).
    public decimal? TotalAmount { get; set; }
    public string? Currency { get; set; }

    public DateTime GeneratedAt { get; set; }
    public int? GeneratedBy { get; set; }

    public Firm? Firm { get; set; }
    public User? GeneratedByUser { get; set; }
}
