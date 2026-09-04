using MipRental.Domain.Enums;

namespace MipRental.Web.Common;

public static class ApprovalDecisionDisplay
{
    private static readonly IReadOnlyDictionary<ApprovalDecision, string> LabelMap = new Dictionary<ApprovalDecision, string>
    {
        [ApprovalDecision.APPROVED] = "Onaylandı",
        [ApprovalDecision.REJECTED] = "Reddedildi",
        [ApprovalDecision.REVISION_REQUESTED] = "Revizyon İstendi"
    };

    public static string GetLabel(ApprovalDecision decision) =>
        LabelMap.TryGetValue(decision, out var label) ? label : decision.ToString();

    public static string GetBadgeClass(ApprovalDecision decision) => StatusBadge.Class(decision);
}
