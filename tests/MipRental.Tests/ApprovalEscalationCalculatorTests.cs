using MipRental.Domain.Approvals;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Tests;

// CLAUDE.md kural 5: otomatik onay YOKTUR; hatırlatma ve eskalasyon vardır.
// Tetikleyici sonraki adımda — burada sadece "ne zaman" hesabı test edilir.
public class ApprovalEscalationCalculatorTests
{
    private static readonly DateTime AssignedAt = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);

    private static Approval Pending() => new()
    {
        ApprovalId = 1,
        DocumentType = DocumentType.WORK_RECORD,
        DocumentId = 1,
        StepNo = 1,
        AssignedAt = AssignedAt
    };

    private static ApprovalFlowStep Step(int? reminderHours = 24, int? escalateHours = 48) => new()
    {
        FlowStepId = 1,
        FlowId = 1,
        StepNo = 1,
        RoleId = 2,
        Name = "Amir Onayı",
        ReminderAfterHours = reminderHours,
        EscalateAfterHours = escalateHours
    };

    [Fact]
    public void ReminderAndEscalationDueAt_AreComputedFromAssignedAt()
    {
        var approval = Pending();
        var step = Step();

        Assert.Equal(AssignedAt.AddHours(24), ApprovalEscalationCalculator.ReminderDueAt(approval, step));
        Assert.Equal(AssignedAt.AddHours(48), ApprovalEscalationCalculator.EscalationDueAt(approval, step));
    }

    [Fact]
    public void NoHoursConfigured_MeansNoReminderOrEscalation()
    {
        var approval = Pending();
        var step = Step(reminderHours: null, escalateHours: null);

        Assert.Null(ApprovalEscalationCalculator.ReminderDueAt(approval, step));
        Assert.Null(ApprovalEscalationCalculator.EscalationDueAt(approval, step));
        Assert.False(ApprovalEscalationCalculator.IsReminderDue(approval, step, AssignedAt.AddYears(1)));
        Assert.False(ApprovalEscalationCalculator.IsEscalationDue(approval, step, AssignedAt.AddYears(1)));
    }

    [Fact]
    public void Reminder_NotDueBeforeThreshold_DueAfter()
    {
        var approval = Pending();
        var step = Step();

        Assert.False(ApprovalEscalationCalculator.IsReminderDue(approval, step, AssignedAt.AddHours(23)));
        Assert.True(ApprovalEscalationCalculator.IsReminderDue(approval, step, AssignedAt.AddHours(24)));
        Assert.True(ApprovalEscalationCalculator.IsReminderDue(approval, step, AssignedAt.AddHours(30)));
    }

    [Fact]
    public void Reminder_NotRepeatedOnceSent()
    {
        var approval = Pending();
        approval.ReminderSentAt = AssignedAt.AddHours(25);

        Assert.False(ApprovalEscalationCalculator.IsReminderDue(approval, Step(), AssignedAt.AddHours(40)));
    }

    [Fact]
    public void DecidedApproval_NeedsNoReminderOrEscalation()
    {
        var approval = Pending();
        approval.Decision = ApprovalDecision.APPROVED;
        approval.DecidedAt = AssignedAt.AddHours(1);

        var farFuture = AssignedAt.AddHours(100);
        Assert.False(ApprovalEscalationCalculator.IsReminderDue(approval, Step(), farFuture));
        Assert.False(ApprovalEscalationCalculator.IsEscalationDue(approval, Step(), farFuture));
    }

    [Fact]
    public void Escalation_NotDueBeforeThreshold_DueAfter()
    {
        var approval = Pending();
        var step = Step();

        Assert.False(ApprovalEscalationCalculator.IsEscalationDue(approval, step, AssignedAt.AddHours(47)));
        Assert.True(ApprovalEscalationCalculator.IsEscalationDue(approval, step, AssignedAt.AddHours(48)));
    }

    [Fact]
    public void WaitingFor_NeverGoesNegative()
    {
        var approval = Pending();

        Assert.Equal(TimeSpan.Zero, ApprovalEscalationCalculator.WaitingFor(approval, AssignedAt.AddHours(-5)));
        Assert.Equal(TimeSpan.FromHours(3), ApprovalEscalationCalculator.WaitingFor(approval, AssignedAt.AddHours(3)));
    }
}
