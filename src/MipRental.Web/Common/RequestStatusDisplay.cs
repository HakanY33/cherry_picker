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
    public static string GetLabel(RequestStatus status) => RequestStatusLabels.Get(status);

    public static string GetSummaryLabel(RequestStatus status) => RequestStatusLabels.GetSummary(status);

    // Renk TEK YERDE tanımlıdır: StatusBadge.
    public static string GetBadgeClass(RequestStatus status) => StatusBadge.Class(status);

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
