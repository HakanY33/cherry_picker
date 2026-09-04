using MipRental.Domain.Enums;

namespace MipRental.Domain.Entities;

public class Approval
{
    public int ApprovalId { get; set; }
    public DocumentType DocumentType { get; set; }
    public int DocumentId { get; set; }
    public int? FlowStepId { get; set; }
    public int StepNo { get; set; }

    public int? AssignedToUserId { get; set; }
    public int? AssignedToRoleId { get; set; }
    public int? DecidedByUserId { get; set; }

    public ApprovalDecision? Decision { get; set; }
    public string? Comment { get; set; }
    public DateTime AssignedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public DateTime? ReminderSentAt { get; set; }

    /// <summary>
    /// Eskalasyon bildirimi üretildi mi? Hatırlatma gibi eskalasyon da adım
    /// başına BİR KEZ üretilir; zamanlayıcı her turda yeniden üretmesin.
    /// </summary>
    public DateTime? EscalationSentAt { get; set; }

    public ApprovalFlowStep? ApprovalFlowStep { get; set; }
    public User? AssignedToUser { get; set; }
    public Role? AssignedToRole { get; set; }
    public User? DecidedByUser { get; set; }
}
