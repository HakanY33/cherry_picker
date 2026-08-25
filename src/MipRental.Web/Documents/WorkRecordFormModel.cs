using MipRental.Domain.Enums;

namespace MipRental.Web.Documents;

/// <summary>
/// Çalışma kaydı formu PDF'inin girdisi. EF entity'si yerine düz bir model
/// kullanılıyor: şablon veritabanı bilmez, testte elle kurulabilir ve hangi
/// alanların kâğıda çıktığı tek bakışta görülür.
/// </summary>
public sealed class WorkRecordFormModel
{
    public required string DocumentNo { get; init; }
    public required WorkRecordStatus Status { get; init; }
    public required int Year { get; init; }
    public required int Month { get; init; }

    // İşi talep eden MIP tarafı (sol blok)
    public string? RequestedByName { get; init; }
    public string? WitnessedByName { get; init; }
    public string? DepartmentName { get; init; }

    // Hizmeti veren firma (sağ blok)
    public required string FirmTitle { get; init; }
    public required string FirmCode { get; init; }
    public string? ContractNo { get; init; }
    public string? OperatorName { get; init; }
    public string? EquipmentDescription { get; init; }
    public string? Capacity { get; init; }
    public string? LicensePlate { get; init; }
    public int? PersonnelCount { get; init; }

    // İşin kendisi
    public required DateOnly WorkDate { get; init; }
    public TimeOnly? StartTime { get; init; }
    public TimeOnly? EndTime { get; init; }
    public bool SpansMidnight { get; init; }
    public string? Location { get; init; }
    public string? WorkDescription { get; init; }
    public string? ExternalReceiptNo { get; init; }
    public DateOnly? ExternalReceiptDate { get; init; }

    public required IReadOnlyList<WorkRecordFormLine> Lines { get; init; }

    /// <summary>
    /// Fiyat açıklaması: PricingCalculator'ın ürettiği, satıra kopyalanmış
    /// (PricingRuleSnapshot) insan okunur gerekçe satırları. "Neden bu tutar"
    /// sorusunun cevabı kâğıdın üzerinde dursun diye basılır.
    /// </summary>
    public required IReadOnlyList<string> PricingExplanation { get; init; }

    public decimal LinesTotal { get; init; }
    public decimal MobilizationFee { get; init; }
    public decimal TotalAmount { get; init; }
    public required string Currency { get; init; }

    public required IReadOnlyList<WorkRecordFormApproval> ApprovalHistory { get; init; }

    /// <summary>Doğrulama sayfasının tam adresi; karekod bunu taşır.</summary>
    public required string VerificationUrl { get; init; }
    public required string VerificationCode { get; init; }
}

public sealed class WorkRecordFormLine
{
    public required int LineNo { get; init; }
    public required string ServiceName { get; init; }
    public string? VariantName { get; init; }
    public required decimal RawQuantity { get; init; }
    public required decimal BillableQuantity { get; init; }
    public required ServiceUnit Unit { get; init; }
    public required decimal UnitPrice { get; init; }
    public required decimal SurchargeAmount { get; init; }
    public required decimal LineAmount { get; init; }
    public string? Description { get; init; }
}

public sealed class WorkRecordFormApproval
{
    public required int StepNo { get; init; }
    public required string StepName { get; init; }
    public string? DecidedByName { get; init; }
    public ApprovalDecision? Decision { get; init; }
    public DateTime? DecidedAtUtc { get; init; }
    public string? Comment { get; init; }
}
