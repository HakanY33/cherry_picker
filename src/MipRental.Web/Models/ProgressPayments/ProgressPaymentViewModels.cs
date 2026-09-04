using Microsoft.AspNetCore.Mvc.Rendering;
using MipRental.Domain.Enums;

namespace MipRental.Web.Models.ProgressPayments;

/// <summary>
/// ADIM 14 — hakediş ekranları.
///
/// Adım 9'daki "para alanı ayrı nullable nesnede" deseni burada UYGULANMAZ ve
/// bunun sebebi vardır: hakediş ekranları CanViewProgressPayments ile yalnızca
/// BUDGET ve BUDGET_MANAGER'a açıktır, ikisi de fiyat gören rollerdir. Tutarsız
/// bir hakediş ekranı diye bir şey yok — ekranın tamamı tutar hakkında.
/// </summary>
public class ProgressPaymentIndexViewModel
{
    public IReadOnlyList<ProgressPaymentRow> Items { get; init; } = Array.Empty<ProgressPaymentRow>();

    /// <summary>Oluşturma formu yalnızca Bütçe'ye çizilir; POST ayrıca policy ile kapalı.</summary>
    public bool CanCreate { get; init; }

    public IReadOnlyList<SelectListItem> PeriodOptions { get; init; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<SelectListItem> FirmOptions { get; init; } = Array.Empty<SelectListItem>();
}

public sealed class ProgressPaymentRow
{
    public required int ProgressPaymentId { get; init; }
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required string FirmTitle { get; init; }
    public required ProgressPaymentStatus Status { get; init; }
    public required decimal TotalAmount { get; init; }
    public required string Currency { get; init; }
    public required int RecordCount { get; init; }
    public DateTime? BudgetApprovedAt { get; init; }
    public DateTime? ManagerApprovedAt { get; init; }
}

public class ProgressPaymentDetailsViewModel
{
    public required ProgressPaymentRow Header { get; init; }
    public IReadOnlyList<ProgressPaymentRecordRow> Records { get; init; } = Array.Empty<ProgressPaymentRecordRow>();

    public string? BudgetNote { get; init; }
    public string? ManagerNote { get; init; }
    public string? RejectionReason { get; init; }
    public string? BudgetApprovedByName { get; init; }
    public string? ManagerApprovedByName { get; init; }

    /// <summary>Hakediş kurulurken onay bekleyen kayıt sayısı (dondurulmuş).</summary>
    public int PendingRecordCountAtCreation { get; init; }

    /// <summary>O dönemde ŞU AN onay bekleyen kayıt sayısı — uyarı için.</summary>
    public int PendingRecordCountNow { get; init; }

    public bool CanSendToManager { get; init; }

    /// <summary>B8 — yöneticiye gönderilmiş hakediş geri çekilebilir.</summary>
    public bool CanWithdraw { get; init; }
    public bool CanDecide { get; init; }
}

public sealed class ProgressPaymentRecordRow
{
    public required int WorkRecordId { get; init; }
    public required string DocumentNo { get; init; }
    public required DateOnly WorkDate { get; init; }
    public required WorkRecordStatus Status { get; init; }
    public decimal? TotalAmount { get; init; }
    public string? Currency { get; init; }
}
