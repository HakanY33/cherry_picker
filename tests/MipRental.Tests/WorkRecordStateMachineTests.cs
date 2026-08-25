using MipRental.Domain.Approvals;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;

namespace MipRental.Tests;

/// <summary>
/// Durum makinesinin TAM geçiş matrisi. Veritabanı yok: makine saf olduğu için
/// 8 x 8 = 64 geçişin tamamı burada doğrulanabiliyor.
/// </summary>
public class WorkRecordStateMachineTests
{
    private const int FirmId = 1;
    private const string SupervisorRole = "SUPERVISOR";

    private static readonly WorkRecordStatus[] AllStatuses = Enum.GetValues<WorkRecordStatus>();

    // Görevde verilen izin listesinin birebir kopyası. Kaynak tablo değişirse bu
    // beklenti de bilinçli olarak değişmeli — testin amacı tam olarak bu.
    private static readonly Dictionary<WorkRecordStatus, WorkRecordStatus[]> Expected = new()
    {
        [WorkRecordStatus.DRAFT] = new[] { WorkRecordStatus.SUBMITTED, WorkRecordStatus.CANCELLED },
        [WorkRecordStatus.SUBMITTED] = new[] { WorkRecordStatus.PENDING },
        [WorkRecordStatus.PENDING] = new[]
        {
            WorkRecordStatus.PENDING, WorkRecordStatus.APPROVED, WorkRecordStatus.REJECTED, WorkRecordStatus.REVISION_REQUESTED
        },
        [WorkRecordStatus.REVISION_REQUESTED] = new[] { WorkRecordStatus.DRAFT },
        // APPROVED artık nihai değil: dönem kapanınca LOCKED'a gider. Bu geçiş
        // kullanıcı eylemiyle DEĞİL, PeriodLockService üzerinden yapılır.
        [WorkRecordStatus.APPROVED] = new[] { WorkRecordStatus.LOCKED },
        [WorkRecordStatus.REJECTED] = Array.Empty<WorkRecordStatus>(),
        [WorkRecordStatus.CANCELLED] = Array.Empty<WorkRecordStatus>(),
        [WorkRecordStatus.LOCKED] = Array.Empty<WorkRecordStatus>()
    };

    private static WorkRecord Record(WorkRecordStatus status) => new()
    {
        WorkRecordId = 1,
        DocumentNo = "WR-2026-00001",
        Status = status,
        FirmId = FirmId,
        ContractId = 1,
        PeriodId = 1,
        WorkDate = new DateOnly(2026, 3, 10),
        EnteredByUserId = 2
    };

    private static Period OpenPeriod() => new() { PeriodId = 1, Year = 2026, Month = 3, Status = PeriodStatus.OPEN };
    private static Period ClosedPeriod() => new() { PeriodId = 1, Year = 2026, Month = 3, Status = PeriodStatus.CLOSED };

    private static TransitionActor FirmActor() => new()
    {
        UserId = 2,
        FirmId = FirmId,
        Roles = new HashSet<string> { "FIRM_USER" }
    };

    private static TransitionActor SupervisorActor() => new()
    {
        UserId = 3,
        FirmId = null,
        Roles = new HashSet<string> { SupervisorRole }
    };

    // ---------------------------------------------------------------
    // 1) İzin tablosu: 64 geçişin her biri
    // ---------------------------------------------------------------

    public static TheoryData<WorkRecordStatus, WorkRecordStatus> AllTransitions()
    {
        var data = new TheoryData<WorkRecordStatus, WorkRecordStatus>();
        foreach (var from in AllStatuses)
        {
            foreach (var to in AllStatuses)
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllTransitions))]
    public void IsAllowed_MatchesSpecifiedMatrix(WorkRecordStatus from, WorkRecordStatus to)
    {
        var shouldBeAllowed = Expected[from].Contains(to);

        Assert.Equal(shouldBeAllowed, WorkRecordStateMachine.IsAllowed(from, to));
    }

    // ---------------------------------------------------------------
    // 2) Terminal durumlar: APPROVED / REJECTED / CANCELLED
    // ---------------------------------------------------------------

    public static TheoryData<WorkRecordStatus> EveryStatus()
    {
        var data = new TheoryData<WorkRecordStatus>();
        foreach (var status in AllStatuses)
        {
            data.Add(status);
        }

        return data;
    }

    // APPROVED'dan çıkan TEK yol dönem kapanışıdır (LOCKED). Onay akışına ait
    // hiçbir duruma geri dönülemez.
    [Theory]
    [MemberData(nameof(EveryStatus))]
    public void Approved_CanOnlyGoToLocked(WorkRecordStatus target)
    {
        var expected = target == WorkRecordStatus.LOCKED;
        Assert.Equal(expected, WorkRecordStateMachine.IsAllowed(WorkRecordStatus.APPROVED, target));
    }

    [Theory]
    [MemberData(nameof(EveryStatus))]
    public void Rejected_CannotGoAnywhere(WorkRecordStatus target)
    {
        Assert.False(WorkRecordStateMachine.IsAllowed(WorkRecordStatus.REJECTED, target));
    }

    [Theory]
    [MemberData(nameof(EveryStatus))]
    public void Cancelled_CannotGoAnywhere(WorkRecordStatus target)
    {
        Assert.False(WorkRecordStateMachine.IsAllowed(WorkRecordStatus.CANCELLED, target));
    }

    // APPROVED kaydın her bir geçiş METODU ayrı ayrı reddetmeli — sadece tabloya
    // bakmak yetmez, metotların da tabloyu kullandığını doğruluyoruz.
    [Fact]
    public void ApprovedRecord_Submit_IsRejected()
    {
        var record = Record(WorkRecordStatus.APPROVED);
        var ex = Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.Submit(record, OpenPeriod(), FirmActor()));

        Assert.Contains("nihaidir", ex.Message);
        Assert.Equal(WorkRecordStatus.APPROVED, record.Status);
    }

    [Fact]
    public void ApprovedRecord_SendToApproval_IsRejected()
    {
        var record = Record(WorkRecordStatus.APPROVED);
        Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.SendToApproval(record, OpenPeriod(), FirmActor()));
        Assert.Equal(WorkRecordStatus.APPROVED, record.Status);
    }

    [Fact]
    public void ApprovedRecord_Approve_IsRejected()
    {
        var record = Record(WorkRecordStatus.APPROVED);
        Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.Approve(record, OpenPeriod(), SupervisorActor(), SupervisorRole, "Amir"));
        Assert.Equal(WorkRecordStatus.APPROVED, record.Status);
    }

    [Fact]
    public void ApprovedRecord_Reject_IsRejected()
    {
        var record = Record(WorkRecordStatus.APPROVED);
        Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.Reject(record, OpenPeriod(), SupervisorActor(), SupervisorRole, "Amir", "gerekçe"));
        Assert.Equal(WorkRecordStatus.APPROVED, record.Status);
    }

    [Fact]
    public void ApprovedRecord_RequestRevision_IsRejected()
    {
        var record = Record(WorkRecordStatus.APPROVED);
        Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.RequestRevision(record, OpenPeriod(), SupervisorActor(), SupervisorRole, "Amir", "gerekçe"));
        Assert.Equal(WorkRecordStatus.APPROVED, record.Status);
    }

    [Fact]
    public void ApprovedRecord_Cancel_IsRejected()
    {
        var record = Record(WorkRecordStatus.APPROVED);
        Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.Cancel(record, OpenPeriod(), FirmActor()));
        Assert.Equal(WorkRecordStatus.APPROVED, record.Status);
    }

    [Fact]
    public void ApprovedRecord_CannotBeRevised()
    {
        var record = Record(WorkRecordStatus.APPROVED);
        Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.EnsureCanCreateRevision(record, OpenPeriod(), FirmActor()));
    }

    // ---------------------------------------------------------------
    // 3) İzin verilmeyen tekil geçişler (her biri ayrı test)
    // ---------------------------------------------------------------

    [Fact]
    public void Draft_CannotGoStraightToApproved()
    {
        var record = Record(WorkRecordStatus.DRAFT);
        Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.Approve(record, OpenPeriod(), SupervisorActor(), SupervisorRole, "Amir"));
        Assert.Equal(WorkRecordStatus.DRAFT, record.Status);
    }

    [Fact]
    public void Draft_CannotGoStraightToPending()
    {
        var record = Record(WorkRecordStatus.DRAFT);
        Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.SendToApproval(record, OpenPeriod(), FirmActor()));
    }

    [Fact]
    public void Draft_CannotBeRejected()
    {
        var record = Record(WorkRecordStatus.DRAFT);
        Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.Reject(record, OpenPeriod(), SupervisorActor(), SupervisorRole, "Amir", "gerekçe"));
    }

    [Fact]
    public void Draft_CannotBeRevised()
    {
        var record = Record(WorkRecordStatus.DRAFT);
        Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.EnsureCanCreateRevision(record, OpenPeriod(), FirmActor()));
    }

    [Fact]
    public void Submitted_CannotBeApprovedDirectly()
    {
        var record = Record(WorkRecordStatus.SUBMITTED);
        Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.Approve(record, OpenPeriod(), SupervisorActor(), SupervisorRole, "Amir"));
    }

    [Fact]
    public void Submitted_CannotBeCancelled()
    {
        var record = Record(WorkRecordStatus.SUBMITTED);
        Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.Cancel(record, OpenPeriod(), FirmActor()));
    }

    [Fact]
    public void Submitted_CannotBeSubmittedAgain()
    {
        var record = Record(WorkRecordStatus.SUBMITTED);
        Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.Submit(record, OpenPeriod(), FirmActor()));
    }

    [Fact]
    public void Pending_CannotBeSubmittedAgain()
    {
        var record = Record(WorkRecordStatus.PENDING);
        Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.Submit(record, OpenPeriod(), FirmActor()));
    }

    [Fact]
    public void Pending_CannotBeCancelled()
    {
        var record = Record(WorkRecordStatus.PENDING);
        Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.Cancel(record, OpenPeriod(), FirmActor()));
    }

    [Fact]
    public void Pending_CannotCreateRevisionBeforeRevisionIsRequested()
    {
        var record = Record(WorkRecordStatus.PENDING);
        Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.EnsureCanCreateRevision(record, OpenPeriod(), FirmActor()));
    }

    [Fact]
    public void RevisionRequested_CannotBeApproved()
    {
        var record = Record(WorkRecordStatus.REVISION_REQUESTED);
        Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.Approve(record, OpenPeriod(), SupervisorActor(), SupervisorRole, "Amir"));
    }

    [Fact]
    public void RevisionRequested_CannotBeSubmittedDirectly()
    {
        // Eski kayıt yeniden gönderilemez; yeni versiyon oluşturulur.
        var record = Record(WorkRecordStatus.REVISION_REQUESTED);
        Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.Submit(record, OpenPeriod(), FirmActor()));
    }

    [Fact]
    public void RevisionRequested_CannotBeCancelled()
    {
        var record = Record(WorkRecordStatus.REVISION_REQUESTED);
        Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.Cancel(record, OpenPeriod(), FirmActor()));
    }

    [Fact]
    public void Locked_CannotGoAnywhereAtAll()
    {
        var record = Record(WorkRecordStatus.LOCKED);
        Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.Submit(record, OpenPeriod(), FirmActor()));
        Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.Approve(record, OpenPeriod(), SupervisorActor(), SupervisorRole, "Amir"));
        Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.Cancel(record, OpenPeriod(), FirmActor()));
    }

    // ---------------------------------------------------------------
    // 4) İzinli geçişler gerçekten çalışıyor
    // ---------------------------------------------------------------

    [Fact]
    public void Draft_CanBeSubmitted()
    {
        var record = Record(WorkRecordStatus.DRAFT);
        WorkRecordStateMachine.Submit(record, OpenPeriod(), FirmActor());
        Assert.Equal(WorkRecordStatus.SUBMITTED, record.Status);
    }

    [Fact]
    public void Draft_CanBeCancelled()
    {
        var record = Record(WorkRecordStatus.DRAFT);
        WorkRecordStateMachine.Cancel(record, OpenPeriod(), FirmActor());
        Assert.Equal(WorkRecordStatus.CANCELLED, record.Status);
    }

    [Fact]
    public void Submitted_CanGoToPending()
    {
        var record = Record(WorkRecordStatus.SUBMITTED);
        WorkRecordStateMachine.SendToApproval(record, OpenPeriod(), FirmActor());
        Assert.Equal(WorkRecordStatus.PENDING, record.Status);
    }

    [Fact]
    public void Pending_CanAdvanceToNextStepAndStayPending()
    {
        var record = Record(WorkRecordStatus.PENDING);
        WorkRecordStateMachine.AdvanceToNextStep(record, OpenPeriod(), SupervisorActor(), SupervisorRole, "Amir");
        Assert.Equal(WorkRecordStatus.PENDING, record.Status);
    }

    [Fact]
    public void Pending_CanBeApprovedRejectedOrSentToRevision()
    {
        var approved = Record(WorkRecordStatus.PENDING);
        WorkRecordStateMachine.Approve(approved, OpenPeriod(), SupervisorActor(), SupervisorRole, "Amir");
        Assert.Equal(WorkRecordStatus.APPROVED, approved.Status);

        var rejected = Record(WorkRecordStatus.PENDING);
        WorkRecordStateMachine.Reject(rejected, OpenPeriod(), SupervisorActor(), SupervisorRole, "Amir", "eksik belge");
        Assert.Equal(WorkRecordStatus.REJECTED, rejected.Status);

        var revision = Record(WorkRecordStatus.PENDING);
        WorkRecordStateMachine.RequestRevision(revision, OpenPeriod(), SupervisorActor(), SupervisorRole, "Amir", "miktar hatalı");
        Assert.Equal(WorkRecordStatus.REVISION_REQUESTED, revision.Status);
    }

    // ---------------------------------------------------------------
    // 5) Gerekçe zorunluluğu
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_WithoutReason_IsRejected(string? reason)
    {
        var record = Record(WorkRecordStatus.PENDING);

        var ex = Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.Reject(record, OpenPeriod(), SupervisorActor(), SupervisorRole, "Amir", reason));

        Assert.Contains("gerekçesi zorunludur", ex.Message);
        Assert.Equal(WorkRecordStatus.PENDING, record.Status); // durum değişmedi
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequestRevision_WithoutReason_IsRejected(string? reason)
    {
        var record = Record(WorkRecordStatus.PENDING);

        var ex = Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.RequestRevision(record, OpenPeriod(), SupervisorActor(), SupervisorRole, "Amir", reason));

        Assert.Contains("gerekçesi zorunludur", ex.Message);
        Assert.Equal(WorkRecordStatus.PENDING, record.Status);
    }

    // ---------------------------------------------------------------
    // 6) Yetki
    // ---------------------------------------------------------------

    [Fact]
    public void WrongRole_CannotApproveStep()
    {
        // Adım SUPERVISOR'a ait ama kullanıcı DEPT_HEAD.
        var record = Record(WorkRecordStatus.PENDING);
        var deptHead = new TransitionActor { UserId = 4, FirmId = null, Roles = new HashSet<string> { "DEPT_HEAD" } };

        var ex = Assert.Throws<ApprovalAuthorizationException>(
            () => WorkRecordStateMachine.Approve(record, OpenPeriod(), deptHead, SupervisorRole, "Amir"));

        Assert.Contains("Amir", ex.Message);
        Assert.Equal(WorkRecordStatus.PENDING, record.Status);
    }

    [Fact]
    public void Subcontractor_CannotApproveOwnRecord()
    {
        var record = Record(WorkRecordStatus.PENDING);

        // Firma kullanıcısı SUPERVISOR rolü taşısa bile onaylayamaz.
        var firmUserWithApproverRole = new TransitionActor
        {
            UserId = 2,
            FirmId = FirmId,
            Roles = new HashSet<string> { SupervisorRole }
        };

        var ex = Assert.Throws<ApprovalAuthorizationException>(
            () => WorkRecordStateMachine.Approve(record, OpenPeriod(), firmUserWithApproverRole, SupervisorRole, "Amir"));

        Assert.Contains("alt yüklenici", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(WorkRecordStatus.PENDING, record.Status);
    }

    [Fact]
    public void OtherFirmsUser_CannotSubmitRecord()
    {
        var record = Record(WorkRecordStatus.DRAFT);
        var otherFirmUser = new TransitionActor { UserId = 9, FirmId = 2, Roles = new HashSet<string> { "FIRM_USER" } };

        Assert.Throws<ApprovalAuthorizationException>(
            () => WorkRecordStateMachine.Submit(record, OpenPeriod(), otherFirmUser));
    }

    [Fact]
    public void MipStaff_CannotSubmitOnBehalfOfSubcontractor()
    {
        var record = Record(WorkRecordStatus.DRAFT);

        Assert.Throws<ApprovalAuthorizationException>(
            () => WorkRecordStateMachine.Submit(record, OpenPeriod(), SupervisorActor()));
    }

    // ---------------------------------------------------------------
    // 7) Kapalı dönem
    // ---------------------------------------------------------------

    [Fact]
    public void ClosedPeriod_ApprovalIsRejected()
    {
        var record = Record(WorkRecordStatus.PENDING);

        var ex = Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.Approve(record, ClosedPeriod(), SupervisorActor(), SupervisorRole, "Amir"));

        Assert.Contains("kapalıdır", ex.Message);
        Assert.Equal(WorkRecordStatus.PENDING, record.Status);
    }

    [Fact]
    public void ClosedPeriod_SubmitIsRejected()
    {
        var record = Record(WorkRecordStatus.DRAFT);

        Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.Submit(record, ClosedPeriod(), FirmActor()));
        Assert.Equal(WorkRecordStatus.DRAFT, record.Status);
    }

    [Fact]
    public void ClosedPeriod_RevisionIsRejected()
    {
        var record = Record(WorkRecordStatus.REVISION_REQUESTED);

        Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.EnsureCanCreateRevision(record, ClosedPeriod(), FirmActor()));
    }

    // ---------------------------------------------------------------
    // 8) Aynı kayıttan iki kez revizyon üretilemez
    // ---------------------------------------------------------------

    [Fact]
    public void SupersededRecord_CannotBeRevisedAgain()
    {
        var record = Record(WorkRecordStatus.REVISION_REQUESTED);
        record.IsSuperseded = true;

        var ex = Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.EnsureCanCreateRevision(record, OpenPeriod(), FirmActor()));

        Assert.Contains("ikinci kez", ex.Message);
    }
}
