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

    public static readonly IReadOnlyDictionary<ContractStatus, string> BadgeClasses = new Dictionary<ContractStatus, string>
    {
        [ContractStatus.DRAFT] = "bg-secondary",
        [ContractStatus.ACTIVE] = "bg-success",
        [ContractStatus.EXPIRED] = "bg-warning text-dark",
        [ContractStatus.TERMINATED] = "bg-danger"
    };

    public static string GetLabel(ContractStatus status) => Labels.TryGetValue(status, out var label) ? label : status.ToString();

    public static string GetBadgeClass(ContractStatus status) => BadgeClasses.TryGetValue(status, out var css) ? css : "bg-secondary";
}
