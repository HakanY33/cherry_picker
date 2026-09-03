using MipRental.Domain.Enums;

namespace MipRental.Web.Common;

/// <summary>
/// Talep durumlarının ekran karşılığı. WorkRecordStatusDisplay ile aynı desen:
/// etiketler Domain'deki tek kaynaktan (<see cref="RequestStatusLabels"/>) gelir,
/// burada yalnızca RENK eklenir.
///
/// İki ayrı takım vardır ve karıştırılmamalıdır:
///   Get*        — GERÇEK durum. Süreci yürütenler görür (Ekipman, firma).
///   GetSummary* — SADELEŞTİRİLMİŞ durum. Talebi açan görür; iki ayrı bekleme
///                 adımı onun için tek bir "Bekliyor"dur.
/// </summary>
public static class RequestStatusDisplay
{
    private static readonly IReadOnlyDictionary<RequestStatus, string> BadgeClasses = new Dictionary<RequestStatus, string>
    {
        [RequestStatus.DRAFT] = "bg-secondary",
        [RequestStatus.SUBMITTED] = "bg-info text-dark",
        [RequestStatus.PENDING_EQUIPMENT] = "bg-warning text-dark",
        [RequestStatus.PENDING_FIRM] = "bg-warning text-dark",
        [RequestStatus.SCHEDULED] = "bg-primary",
        [RequestStatus.IN_PROGRESS] = "bg-primary",
        [RequestStatus.COMPLETED] = "bg-success",
        [RequestStatus.REJECTED_BY_EQUIPMENT] = "bg-danger",
        [RequestStatus.REJECTED_BY_FIRM] = "bg-danger",
        [RequestStatus.CANCELLED] = "bg-secondary"
    };

    public static string GetLabel(RequestStatus status) => RequestStatusLabels.Get(status);

    public static string GetSummaryLabel(RequestStatus status) => RequestStatusLabels.GetSummary(status);

    public static string GetBadgeClass(RequestStatus status) =>
        BadgeClasses.TryGetValue(status, out var css) ? css : "bg-secondary";

    /// <summary>
    /// Talep açanın listesindeki filtre seçenekleri. Sadeleştirilmiş etiketler
    /// tekrarsız listelenir ("Bekliyor" üç gerçek durumu birden karşılar).
    /// </summary>
    public static IReadOnlyList<string> SummaryFilterOptions { get; } =
        Enum.GetValues<RequestStatus>().Select(RequestStatusLabels.GetSummary).Distinct().ToList();

    /// <summary>Sadeleştirilmiş etiketin arkasındaki GERÇEK durumlar.</summary>
    public static IReadOnlyList<RequestStatus> StatusesFor(string? summaryLabel) =>
        string.IsNullOrWhiteSpace(summaryLabel)
            ? Array.Empty<RequestStatus>()
            : Enum.GetValues<RequestStatus>()
                .Where(s => RequestStatusLabels.GetSummary(s) == summaryLabel)
                .ToList();
}
