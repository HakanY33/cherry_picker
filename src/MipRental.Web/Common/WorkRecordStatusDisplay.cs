using MipRental.Domain.Enums;

namespace MipRental.Web.Common;

public static class WorkRecordStatusDisplay
{
    // Etiketler Domain'deki tek kaynaktan gelir: durum makinesinin hata mesajları
    // ile ekrandaki durum adı asla ayrışmasın.
    public static readonly IReadOnlyDictionary<WorkRecordStatus, string> Labels = WorkRecordStatusLabels.Labels;

    public static readonly IReadOnlyDictionary<WorkRecordStatus, string> BadgeClasses = new Dictionary<WorkRecordStatus, string>
    {
        [WorkRecordStatus.DRAFT] = "bg-secondary",
        [WorkRecordStatus.SUBMITTED] = "bg-info text-dark",
        [WorkRecordStatus.PENDING] = "bg-warning text-dark",
        [WorkRecordStatus.APPROVED] = "bg-success",
        [WorkRecordStatus.REJECTED] = "bg-danger",
        [WorkRecordStatus.REVISION_REQUESTED] = "bg-warning text-dark",
        [WorkRecordStatus.CANCELLED] = "bg-secondary",
        [WorkRecordStatus.LOCKED] = "bg-dark"
    };

    public static string GetLabel(WorkRecordStatus status) => Labels.TryGetValue(status, out var label) ? label : status.ToString();

    public static string GetBadgeClass(WorkRecordStatus status) => BadgeClasses.TryGetValue(status, out var css) ? css : "bg-secondary";
}
