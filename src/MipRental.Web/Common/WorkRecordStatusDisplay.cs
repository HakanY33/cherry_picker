using MipRental.Domain.Enums;

namespace MipRental.Web.Common;

public static class WorkRecordStatusDisplay
{
    // Etiketler Domain'deki tek kaynaktan gelir: durum makinesinin hata mesajları
    // ile ekrandaki durum adı asla ayrışmasın.
    public static readonly IReadOnlyDictionary<WorkRecordStatus, string> Labels = WorkRecordStatusLabels.Labels;

    public static string GetLabel(WorkRecordStatus status) => Labels.TryGetValue(status, out var label) ? label : status.ToString();

    // Renk TEK YERDE tanımlıdır: StatusBadge. Burası yalnızca ileri sarar.
    public static string GetBadgeClass(WorkRecordStatus status) => StatusBadge.Class(status);
}
