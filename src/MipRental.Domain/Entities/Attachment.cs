using MipRental.Domain.Enums;

namespace MipRental.Domain.Entities;

public class Attachment
{
    public int AttachmentId { get; set; }
    public DocumentType DocumentType { get; set; }
    public int DocumentId { get; set; }
    public string FileName { get; set; } = null!;
    public string StoragePath { get; set; } = null!;
    public string? ContentType { get; set; }
    public long? FileSizeBytes { get; set; }
    public int? UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; }

    public User? UploadedByUser { get; set; }
}
