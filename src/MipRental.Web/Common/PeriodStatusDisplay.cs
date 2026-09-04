using MipRental.Domain.Enums;

namespace MipRental.Web.Common;

public static class PeriodStatusDisplay
{
    public static readonly IReadOnlyDictionary<PeriodStatus, string> Labels = new Dictionary<PeriodStatus, string>
    {
        [PeriodStatus.OPEN] = "Açık",
        [PeriodStatus.CLOSED] = "Kapalı",
        [PeriodStatus.REOPENED] = "Yeniden Açıldı"
    };

    public static readonly IReadOnlyDictionary<int, string> MonthNames = new Dictionary<int, string>
    {
        [1] = "Ocak",
        [2] = "Şubat",
        [3] = "Mart",
        [4] = "Nisan",
        [5] = "Mayıs",
        [6] = "Haziran",
        [7] = "Temmuz",
        [8] = "Ağustos",
        [9] = "Eylül",
        [10] = "Ekim",
        [11] = "Kasım",
        [12] = "Aralık"
    };

    public static string GetLabel(PeriodStatus status) => Labels.TryGetValue(status, out var label) ? label : status.ToString();

    public static string GetBadgeClass(PeriodStatus status) => StatusBadge.Class(status);

    public static string GetMonthName(int month) => MonthNames.TryGetValue(month, out var name) ? name : month.ToString();
}
