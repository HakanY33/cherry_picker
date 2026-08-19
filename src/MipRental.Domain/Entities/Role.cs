using MipRental.Domain.Enums;

namespace MipRental.Domain.Entities;

public class Role
{
    public int RoleId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public RoleScope Scope { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<ApprovalFlowStep> ApprovalFlowSteps { get; set; } = new List<ApprovalFlowStep>();
    public ICollection<Approval> ApprovalsAssigned { get; set; } = new List<Approval>();
}
