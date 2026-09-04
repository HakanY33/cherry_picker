namespace MipRental.Domain.Enums;

/// <summary>
/// Hakediş durumları. Veritabanına STRING olarak yazılır (ADR-009).
///
/// DRAFT → PENDING_BUDGET_MANAGER → APPROVED
///                                → REJECTED (gerekçe zorunlu)
/// APPROVED ve REJECTED terminaldir.
/// </summary>
public enum ProgressPaymentStatus
{
    DRAFT,
    PENDING_BUDGET_MANAGER,
    APPROVED,
    REJECTED
}

public static class ProgressPaymentStatusLabels
{
    public static readonly IReadOnlyDictionary<ProgressPaymentStatus, string> Labels =
        new Dictionary<ProgressPaymentStatus, string>
        {
            [ProgressPaymentStatus.DRAFT] = "Taslak",
            [ProgressPaymentStatus.PENDING_BUDGET_MANAGER] = "Bütçe Yöneticisi Onayında",
            [ProgressPaymentStatus.APPROVED] = "Onaylandı",
            [ProgressPaymentStatus.REJECTED] = "Reddedildi"
        };

    public static string Get(ProgressPaymentStatus status) =>
        Labels.TryGetValue(status, out var label) ? label : status.ToString();
}
