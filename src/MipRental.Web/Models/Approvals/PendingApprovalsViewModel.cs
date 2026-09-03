using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Web.Models.Approvals;

// "Onayımı Bekleyenler" listesi.
public class PendingApprovalsViewModel
{
    public IReadOnlyList<PendingApprovalItem> Items { get; init; } = Array.Empty<PendingApprovalItem>();

    /// <summary>
    /// ADIM 9: Tutar sütunu yalnızca CanSeePricing olana çizilir. CanApprove
    /// policy'si Ekipman Müdürlüğü'nü (EQUIPMENT_MANAGER) de kapsar; onaylayabilmek
    /// tutarı görebilmek DEMEK DEĞİLDİR — iki ayrı eksen.
    /// </summary>
    public bool ShowPricing { get; init; }
}

public sealed class PendingApprovalItem
{
    public required int ApprovalId { get; init; }
    public required int WorkRecordId { get; init; }
    public required string DocumentNo { get; init; }
    public required string FirmTitle { get; init; }
    public required DateOnly WorkDate { get; init; }
    public required WorkRecordStatus Status { get; init; }

    /// <summary>Para bilgisi. Yetkisiz kullanıcıda null — alan hiç bulunmaz.</summary>
    public PendingApprovalPricing? Pricing { get; init; }

    public required int StepNo { get; init; }
    public required string StepName { get; init; }
    public required DateTime AssignedAt { get; init; }
    public int LineCount { get; init; }

    // Hatırlatma/eskalasyon zamanları ApprovalEscalationCalculator ile hesaplanır.
    // Tetikleyici sonraki adımda; burada sadece gösterilir.
    public DateTime? ReminderDueAt { get; init; }
    public DateTime? EscalationDueAt { get; init; }
    public bool IsEscalationDue { get; init; }
}

public sealed class PendingApprovalPricing
{
    public decimal? TotalAmount { get; init; }
    public string? Currency { get; init; }
}

// Onay/red/revizyon POST gövdesi.
public sealed class ApprovalDecisionModel
{
    public int WorkRecordId { get; set; }
    public string? Reason { get; set; }
}

// Toplu onay sonucunun ekrana dönen özeti.
public sealed class BulkApprovalResult
{
    public List<string> Approved { get; } = new();
    public List<string> Failed { get; } = new();
}

public sealed class ApprovalHistoryRow
{
    public required Approval Approval { get; init; }
    public required string RoleName { get; init; }
    public string? DecidedByName { get; init; }
}
