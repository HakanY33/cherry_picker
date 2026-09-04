using MipRental.Domain.Enums;

namespace MipRental.Web.Common;

public static class ContractStatusDisplay
{
    public static readonly IReadOnlyDictionary<ContractStatus, string> Labels = new Dictionary<ContractStatus, string>
    {
        [ContractStatus.DRAFT] = "Taslak",
        [ContractStatus.ACTIVE] = "Aktif",
        [ContractStatus.EXPIRED] = "Süresi Doldu",
        [ContractStatus.TERMINATED] = "Feshedildi"
    };

    public static string GetLabel(ContractStatus status) => Labels.TryGetValue(status, out var label) ? label : status.ToString();

    public static string GetBadgeClass(ContractStatus status) => StatusBadge.Class(status);
}
