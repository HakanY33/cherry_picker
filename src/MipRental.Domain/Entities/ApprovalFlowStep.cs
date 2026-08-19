namespace MipRental.Domain.Entities;

public class ApprovalFlowStep
{
    public int FlowStepId { get; set; }
    public int FlowId { get; set; }
    public int StepNo { get; set; }
    public int RoleId { get; set; }
    public string Name { get; set; } = null!;
    public bool IsMandatory { get; set; } = true;
    public decimal? AmountThreshold { get; set; }
    public int? ReminderAfterHours { get; set; }
    public int? EscalateAfterHours { get; set; }

    public ApprovalFlow ApprovalFlow { get; set; } = null!;
    public Role Role { get; set; } = null!;
    public ICollection<Approval> Approvals { get; set; } = new List<Approval>();
}
