using MipRental.Domain.Enums;

namespace MipRental.Web.Models.ProgressPayments;

/// <summary>
/// /Onay/{token} özet sayfası. ADR-015: bu sayfa yalnızca GÖSTERİR, karar
/// vermez — mail tarayıcıları bağlantıları önceden açtığı için karar ayrı bir
/// POST isteğidir.
///
/// Sayfa oturumsuz açıldığı için içerik BİLİNÇLİ OLARAK dardır: dönem, firma,
/// kayıt sayısı, toplam tutar ve Bütçe'nin notu. Kayıt listesi, kişi adları ve
/// belge numaraları burada YOKTUR — bağlantıyı ele geçiren biri firmanın ay
/// dökümünü okuyamamalı.
/// </summary>
public class ApprovalLinkViewModel
{
    public required string Token { get; init; }
    public required string PeriodName { get; init; }
    public required string FirmTitle { get; init; }
    public required int RecordCount { get; init; }
    public required decimal TotalAmount { get; init; }
    public required string Currency { get; init; }
    public string? BudgetNote { get; init; }
    public DateTime ExpiresAt { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>Kullanılmış token: kararın NE olduğu ve NE ZAMAN verildiği gösterilir.</summary>
public class ApprovalLinkUsedViewModel
{
    public required string PeriodName { get; init; }
    public required string FirmTitle { get; init; }
    public required ProgressPaymentStatus Status { get; init; }
    public DateTime? DecidedAt { get; init; }
}

/// <summary>Hakediş artık onay beklemiyor: geri çekilmiş ya da karar verilmiş.</summary>
public class ApprovalLinkNotPendingViewModel
{
    public required string PeriodName { get; init; }
    public required string FirmTitle { get; init; }
    public required ProgressPaymentStatus Status { get; init; }
}

/// <summary>Karar kaydedildi.</summary>
public class ApprovalLinkDoneViewModel
{
    public required string PeriodName { get; init; }
    public required string FirmTitle { get; init; }
    public required ProgressPaymentStatus Status { get; init; }
}
