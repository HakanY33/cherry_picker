using MipRental.Data.Services;

namespace MipRental.Web.Models.Verification;

/// <summary>
/// Doğrulama sayfasının modeli. Result null ise kod bulunamadı (ya da hiç girilmedi).
/// Modelde bilinçli olarak kişisel veri alanı yok; bkz. DocumentVerificationService.
/// </summary>
public class VerificationViewModel
{
    public string? Code { get; set; }
    public DocumentVerificationResult? Result { get; set; }

    public bool IsFound => Result is not null;
    public bool HasQuery => !string.IsNullOrWhiteSpace(Code);
}
