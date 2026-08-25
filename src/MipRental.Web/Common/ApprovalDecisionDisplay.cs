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

    private static readonly IReadOnlyDictionary<ApprovalDecision, string> BadgeMap = new Dictionary<ApprovalDecision, string>
    {
        [ApprovalDecision.APPROVED] = "bg-success",
        [ApprovalDecision.REJECTED] = "bg-danger",
        [ApprovalDecision.REVISION_REQUESTED] = "bg-warning text-dark"
    };

    public static string GetLabel(ApprovalDecision decision) =>
        LabelMap.TryGetValue(decision, out var label) ? label : decision.ToString();

    public static string GetBadgeClass(ApprovalDecision decision) =>
        BadgeMap.TryGetValue(decision, out var css) ? css : "bg-secondary";
}
