using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Web.Models.WorkRecords;

/// <summary>
/// Çalışma kaydı detay ekranının modeli.
///
/// ADIM 9 — FİYAT GİZLİLİĞİ: burada WorkRecord ENTITY'si TAŞINMAZ. Entity
/// taşımak, view'da @if ile gizlense bile para kolonlarının modele girmesi
/// demekti. Bunun yerine para alanları ayrı bir nesnede toplandı:
/// yetkisiz kullanıcıda <see cref="Pricing"/> ve <see cref="WorkRecordLineView.Pricing"/>
/// NULL'dır — alanlar "boş" değil, HİÇ YOKTUR. Controller o kolonları SQL'de
/// de seçmez.
/// </summary>
public class WorkRecordDetailsViewModel
{
    public required WorkRecordHeaderView Record { get; init; }
    public IReadOnlyList<WorkRecordLineView> Lines { get; init; } = Array.Empty<WorkRecordLineView>();

    /// <summary>Para bilgisi. Yetkisiz kullanıcıda null — alan hiç bulunmaz.</summary>
    public WorkRecordPricingView? Pricing { get; init; }

    /// <summary>Denetim izi. Para alanlarının değerleri yetkisiz kullanıcıda maskelidir.</summary>
    public IReadOnlyList<AuditEntryView> AuditEntries { get; init; } = Array.Empty<AuditEntryView>();

    // RequestedByUser/WitnessedByUser navigation'ları YOK — MIP personeli User
    // entity'sinin firma izolasyon filtresine takılır (bkz. controller). İsimler
    // burada ayrıca (filtresiz) çözülüp taşınır.
    public string? RequestedByName { get; init; }
    public string? WitnessedByName { get; init; }

    // Onay geçmişi: karar verilmiş ve bekleyen tüm adımlar, sırayla.
    public IReadOnlyList<Approval> ApprovalHistory { get; init; } = Array.Empty<Approval>();

    // Bu kullanıcı kaydın AÇIK onay adımının rolünde mi (Onayla/Reddet/Revizyon
    // butonları buna göre gösterilir). Butonu gizlemek yetmez — action'lar
    // yetkiyi ayrıca doğrular.
    public bool CanDecide { get; init; }

    // Talepten türemiş TASLAK: eksik alan formu (B7) yalnızca bunda çizilir.
    // Elle açılmış taslakta bu form YOKTUR — orada alanlar giriş ekranında
    // zaten doldurulur.
    public bool IsDerivedDraft { get; init; }

    // Revizyon zinciri.
    public WorkRecordVersionLink? PreviousVersion { get; init; }
    public WorkRecordVersionLink? NextVersion { get; init; }
    public int VersionNumber { get; init; } = 1;
    public string RootDocumentNo { get; init; } = string.Empty;

    public bool IsRevision => PreviousVersion is not null;
}

/// <summary>Kaydın para İÇERMEYEN başlık bilgisi — herkese görünür.</summary>
public sealed class WorkRecordHeaderView
{
    public required int WorkRecordId { get; init; }
    public required string DocumentNo { get; init; }
    public required WorkRecordStatus Status { get; init; }
    public required string FirmTitle { get; init; }
    public required DateOnly WorkDate { get; init; }
    public required int PeriodYear { get; init; }
    public required int PeriodMonth { get; init; }
    public bool IsSuperseded { get; init; }
    public string? RevisionReason { get; init; }
    public string? LocationDisplay { get; init; }
    public TimeOnly? StartTime { get; init; }
    public TimeOnly? EndTime { get; init; }
    public bool SpansMidnight { get; init; }
    public int? PersonnelCount { get; init; }
    public string? OperatorName { get; init; }
    public string? LicensePlate { get; init; }
    public string? ExternalReceiptNo { get; init; }
    public DateOnly? ExternalReceiptDate { get; init; }
    public required string EnteredByName { get; init; }
    public string? WorkDescription { get; init; }
}

/// <summary>Kayıt seviyesindeki para bilgisi. Yalnızca CanSeePricing olana kurulur.</summary>
public sealed class WorkRecordPricingView
{
    // Mobilizasyon bedeli satır tutarlarına dahil değildir; kayıt başına bir kez.
    public decimal? MobilizationFee { get; init; }
    public decimal? TotalAmount { get; init; }
    public string? Currency { get; init; }
}

/// <summary>Satırın para İÇERMEYEN kısmı — miktar herkese görünür.</summary>
public sealed class WorkRecordLineView
{
    public required int WorkRecordLineId { get; init; }
    public required int LineNo { get; init; }
    public required string ServiceName { get; init; }
    public string? VariantName { get; init; }
    public required decimal RawQuantity { get; init; }
    public required decimal BillableQuantity { get; init; }
    public required ServiceUnit Unit { get; init; }
    public bool IsObjected { get; init; }
    public string? ObjectionReason { get; init; }

    /// <summary>"Neden 7,5 saat" — para geçmez, herkese gösterilir.</summary>
    public IReadOnlyList<string> QuantityExplanation { get; init; } = Array.Empty<string>();

    /// <summary>Para bilgisi. Yetkisiz kullanıcıda null — alan hiç bulunmaz.</summary>
    public WorkRecordLinePricingView? Pricing { get; init; }
}

public sealed class WorkRecordLinePricingView
{
    public required decimal UnitPrice { get; init; }
    public required decimal SurchargeAmount { get; init; }
    public required decimal LineAmount { get; init; }
    public required string Currency { get; init; }

    /// <summary>"7,5 × 1.250,00 = 9.375,00 TL" — sadece yetkiliye.</summary>
    public IReadOnlyList<string> AmountExplanation { get; init; } = Array.Empty<string>();

    /// <summary>Ham snapshot JSON'u: içinde birim fiyat geçer, para sayılır.</summary>
    public string? RawSnapshot { get; init; }
}

/// <summary>Denetim izi satırı. Para alanlarının değeri yetkisizde maskelenmiştir.</summary>
public sealed class AuditEntryView
{
    public required DateTime OccurredAt { get; init; }
    public required AuditAction Action { get; init; }
    public string? FieldName { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public string? Reason { get; init; }
}

public sealed class WorkRecordVersionLink
{
    public int WorkRecordId { get; init; }
    public string DocumentNo { get; init; } = string.Empty;
    public WorkRecordStatus Status { get; init; }
}
