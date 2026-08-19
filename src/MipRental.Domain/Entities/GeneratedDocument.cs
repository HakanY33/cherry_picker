using MipRental.Domain.Enums;

namespace MipRental.Domain.Entities;

public class GeneratedDocument
{
    public int GeneratedDocumentId { get; set; }
    public DocumentType DocumentType { get; set; }
    public int DocumentId { get; set; }
    public GeneratedDocumentKind Kind { get; set; }
    public string FileName { get; set; } = null!;
    public string StoragePath { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
    public string? VerificationCode { get; set; }
    public string? TemplateVersion { get; set; }
    public DateTime GeneratedAt { get; set; }
    public int? GeneratedBy { get; set; }

    public User? GeneratedByUser { get; set; }
}
