using MipRental.Domain.Enums;

namespace MipRental.Domain.Entities;

public class ApprovalFlow
{
    public int FlowId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public DocumentType DocumentType { get; set; }
    public int? ServiceId { get; set; }
    public bool IsActive { get; set; } = true;

    public ServiceCategory? ServiceCategory { get; set; }
    public ICollection<ApprovalFlowStep> ApprovalFlowSteps { get; set; } = new List<ApprovalFlowStep>();
}
