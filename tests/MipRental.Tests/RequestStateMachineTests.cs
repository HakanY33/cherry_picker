using MipRental.Domain.Approvals;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;
using MipRental.Domain.Security;

namespace MipRental.Tests;

/// <summary>
/// ADIM 10 — TALEP DURUM MAKİNESİ.
///
/// Makine saf olduğu için 10 x 10 = 100 geçişin tamamı veritabanısız
/// doğrulanabiliyor. WorkRecordStateMachineTests ile aynı desen.
/// </summary>
public class RequestStateMachineTests
{
    private const int FirmId = 1;
    private const int OtherFirmId = 2;
    private const int RequesterUserId = 10;

    private static readonly DateTime Now = new(2026, 3, 10, 8, 0, 0, DateTimeKind.Utc);

    private static readonly RequestStatus[] AllStatuses = Enum.GetValues<RequestStatus>();

    private static readonly RequestStatus[] TerminalStatuses =
    {
        RequestStatus.COMPLETED,
        RequestStatus.REJECTED_BY_EQUIPMENT,
        RequestStatus.REJECTED_BY_FIRM,
        RequestStatus.CANCELLED
    };

    // Görevde verilen izin listesinin birebir kopyası. Kaynak tablo değişirse bu
    // beklenti de bilinçli olarak değişmeli — testin amacı tam olarak bu.
    private static readonly Dictionary<RequestStatus, RequestStatus[]> Expected = new()
    {
        [RequestStatus.DRAFT] = new[] { RequestStatus.SUBMITTED, RequestStatus.CANCELLED },
        [RequestStatus.SUBMITTED] = new[] { RequestStatus.PENDING_EQUIPMENT },
        [RequestStatus.PENDING_EQUIPMENT] = new[] { RequestStatus.PENDING_FIRM, RequestStatus.REJECTED_BY_EQUIPMENT },
        [RequestStatus.PENDING_FIRM] = new[] { RequestStatus.SCHEDULED, RequestStatus.REJECTED_BY_FIRM },
        [RequestStatus.SCHEDULED] = new[] { RequestStatus.IN_PROGRESS, RequestStatus.CANCELLED },
        [RequestStatus.IN_PROGRESS] = new[] { RequestStatus.COMPLETED },
        [RequestStatus.COMPLETED] = Array.Empty<RequestStatus>(),
        [RequestStatus.REJECTED_BY_EQUIPMENT] = Array.Empty<RequestStatus>(),
        [RequestStatus.REJECTED_BY_FIRM] = Array.Empty<RequestStatus>(),
        [RequestStatus.CANCELLED] = Array.Empty<RequestStatus>()
    };

    // ---------------------------------------------------------------
    // Yardımcılar
    // ---------------------------------------------------------------

    private static Request Req(RequestStatus status, int? firmId = FirmId) => new()
    {
        RequestId = 1,
        DocumentNo = "CPR-2026-00001",
        Status = status,
        RequestedByUserId = RequesterUserId,
        DepartmentId = 1,
        FirmId = firmId,
        IssueDate = new DateOnly(2026, 3, 9),
        RequestedDate = new DateOnly(2026, 3, 10),
        CreatedAt = Now
    };

    private static Period OpenPeriod() => new() { PeriodId = 1, Year = 2026, Month = 3, Status = PeriodStatus.OPEN };
    private static Period ClosedPeriod() => new() { PeriodId = 1, Year = 2026, Month = 3, Status = PeriodStatus.CLOSED };

    private static TransitionActor Actor(int userId, int? firmId, params string[] roles) => new()
    {
        UserId = userId,
        FirmId = firmId,
        Roles = new HashSet<string>(roles, StringComparer.Ordinal)
    };

    private static TransitionActor Requester() => Actor(RequesterUserId, null, RoleCodes.Requester);
    private static TransitionActor OtherRequester() => Actor(99, null, RoleCodes.Requester);
    private static TransitionActor EquipmentManager() => Actor(20, null, RoleCodes.EquipmentManager);
    private static TransitionActor EquipmentViewer() => Actor(21, null, RoleCodes.EquipmentViewer);
    private static TransitionActor BudgetManager() => Actor(22, null, RoleCodes.BudgetManager);
    private static TransitionActor FirmManager(int firmId = FirmId) => Actor(30, firmId, RoleCodes.FirmManager);
    private static TransitionActor FirmOperator(int firmId = FirmId) => Actor(31, firmId, RoleCodes.FirmOperator);

    /// <summary>Bir geçiş metodu + onu çağırmanın geçerli olduğu kaynak durum.</summary>
    private sealed record Step(string Name, RequestStatus From, Action<Request, Period> Run);

    private static readonly Step[] Steps =
    {
        new("Submit", RequestStatus.DRAFT,
            (r, p) => RequestStateMachine.Submit(r, p, Requester(), Now)),
        new("SendToEquipment", RequestStatus.SUBMITTED,
            (r, p) => RequestStateMachine.SendToEquipment(r, p, Requester())),
        new("ApproveByEquipment", RequestStatus.PENDING_EQUIPMENT,
            (r, p) => RequestStateMachine.ApproveByEquipment(r, p, EquipmentManager(), Now)),
        new("RejectByEquipment", RequestStatus.PENDING_EQUIPMENT,
            (r, p) => RequestStateMachine.RejectByEquipment(r, p, EquipmentManager(), "uygun ekipman yok", Now)),
        new("AcceptByFirm", RequestStatus.PENDING_FIRM,
            (r, p) => RequestStateMachine.AcceptByFirm(r, p, FirmManager(), "Şükrü Çağlayan", "33 ABC 33", Now)),
        new("RejectByFirm", RequestStatus.PENDING_FIRM,
            (r, p) => RequestStateMachine.RejectByFirm(r, p, FirmManager(), "vinç arızalı", Now)),
        new("Start", RequestStatus.SCHEDULED,
            (r, p) => RequestStateMachine.Start(r, p, FirmOperator(), Now)),
        new("Complete", RequestStatus.IN_PROGRESS,
            (r, p) => RequestStateMachine.Complete(r, p, FirmOperator(), Now)),
        new("CancelFromDraft", RequestStatus.DRAFT,
            (r, p) => RequestStateMachine.Cancel(r, p, Requester(), "iş iptal oldu", Now)),
        new("CancelFromScheduled", RequestStatus.SCHEDULED,
            (r, p) => RequestStateMachine.Cancel(r, p, Requester(), "iş iptal oldu", Now))
    };

    private static Step StepBy(string name) => Steps.Single(s => s.Name == name);

    public static TheoryData<string> EveryStepName()
    {
        var data = new TheoryData<string>();
        foreach (var step in Steps)
        {
            data.Add(step.Name);
        }

        return data;
    }

    // ---------------------------------------------------------------
    // 1) İzin tablosu: 100 geçişin her biri
    // ---------------------------------------------------------------

    public static TheoryData<RequestStatus, RequestStatus> AllTransitions()
    {
        var data = new TheoryData<RequestStatus, RequestStatus>();
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
    public void IsAllowed_MatchesSpecifiedMatrix(RequestStatus from, RequestStatus to)
    {
        var shouldBeAllowed = Expected[from].Contains(to);

        Assert.Equal(shouldBeAllowed, RequestStateMachine.IsAllowed(from, to));
    }

    [Fact]
    public void AllowedTransitions_CoversEveryStatus()
    {
        // Enum'a yeni durum eklenip tabloya eklenmezse burada patlar.
        Assert.Equal(AllStatuses.Length, RequestStateMachine.AllowedTransitions.Count);
        Assert.All(AllStatuses, s => Assert.True(RequestStateMachine.AllowedTransitions.ContainsKey(s)));
    }

    // ---------------------------------------------------------------
    // 2) Terminal durumlar: hiçbir çıkış yok
    // ---------------------------------------------------------------

    public static TheoryData<RequestStatus> EveryTerminalStatus()
    {
        var data = new TheoryData<RequestStatus>();
        foreach (var status in TerminalStatuses)
        {
            data.Add(status);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryTerminalStatus))]
    public void TerminalStatus_HasNoOutgoingTransition(RequestStatus terminal)
    {
        Assert.Empty(RequestStateMachine.AllowedTransitions[terminal]);
        Assert.All(AllStatuses, to => Assert.False(RequestStateMachine.IsAllowed(terminal, to)));
    }

    public static TheoryData<string, RequestStatus> EveryStepFromEveryTerminal()
    {
        var data = new TheoryData<string, RequestStatus>();
        foreach (var step in Steps)
        {
            foreach (var terminal in TerminalStatuses)
            {
                data.Add(step.Name, terminal);
            }
        }

        return data;
    }

    /// <summary>
    /// Terminal durumdaki bir talepte HİÇBİR geçiş metodu çalışmaz — doğru rolle,
    /// açık dönemde, gerekçesiyle çağrılsa bile.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryStepFromEveryTerminal))]
    public void EveryTransition_FromTerminalStatus_IsRejected(string stepName, RequestStatus terminal)
    {
        var step = StepBy(stepName);
        var request = Req(terminal);

        var ex = Assert.Throws<RequestStateTransitionException>(() => step.Run(request, OpenPeriod()));

        Assert.Contains("nihaidir", ex.Message);
        Assert.Equal(terminal, request.Status); // durum kıpırdamadı
    }

    // ---------------------------------------------------------------
    // 3) Dönem kapalıysa hiçbir geçiş yapılamaz (CLAUDE.md kural 4)
    // ---------------------------------------------------------------

    [Theory]
    [MemberData(nameof(EveryStepName))]
    public void EveryTransition_InClosedPeriod_IsRejected(string stepName)
    {
        var step = StepBy(stepName);
        var request = Req(step.From);

        var ex = Assert.Throws<RequestStateTransitionException>(() => step.Run(request, ClosedPeriod()));

        Assert.Contains("kapalıdır", ex.Message);
        Assert.Equal(step.From, request.Status);
    }

    [Theory]
    [MemberData(nameof(EveryStepName))]
    public void EveryTransition_InOpenPeriod_Succeeds(string stepName)
    {
        var step = StepBy(stepName);
        var request = Req(step.From);

        step.Run(request, OpenPeriod());

        Assert.NotEqual(step.From, request.Status);
    }

    // ---------------------------------------------------------------
    // 4) Rol kontrolü — her adım için yanlış rol reddediliyor
    // ---------------------------------------------------------------

    [Fact]
    public void Submit_ByAnotherUser_IsRejected()
    {
        var request = Req(RequestStatus.DRAFT);

        var ex = Assert.Throws<ApprovalAuthorizationException>(
            () => RequestStateMachine.Submit(request, OpenPeriod(), OtherRequester(), Now));

        Assert.Contains("talebi açan kişi", ex.Message);
        Assert.Equal(RequestStatus.DRAFT, request.Status);
    }

    [Fact]
    public void SendToEquipment_ByAnotherUser_IsRejected()
    {
        var request = Req(RequestStatus.SUBMITTED);

        Assert.Throws<ApprovalAuthorizationException>(
            () => RequestStateMachine.SendToEquipment(request, OpenPeriod(), OtherRequester()));
    }

    public static TheoryData<string> NonEquipmentManagerActors() =>
        new() { "requester", "equipmentViewer", "budgetManager", "firmManager", "firmOperator" };

    private static TransitionActor ActorByName(string name) => name switch
    {
        "requester" => Requester(),
        "equipmentViewer" => EquipmentViewer(),
        "budgetManager" => BudgetManager(),
        "firmManager" => FirmManager(),
        "firmOperator" => FirmOperator(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Bilinmeyen aktör.")
    };

    /// <summary>
    /// Ekipman adımı SADECE EQUIPMENT_MANAGER'ındır. EQUIPMENT_VIEWER dahil —
    /// salt okuyan rol karar veremez.
    /// </summary>
    [Theory]
    [MemberData(nameof(NonEquipmentManagerActors))]
    public void ApproveByEquipment_WithWrongRole_IsRejected(string actorName)
    {
        var request = Req(RequestStatus.PENDING_EQUIPMENT);

        Assert.Throws<ApprovalAuthorizationException>(
            () => RequestStateMachine.ApproveByEquipment(request, OpenPeriod(), ActorByName(actorName), Now));

        Assert.Equal(RequestStatus.PENDING_EQUIPMENT, request.Status);
    }

    [Theory]
    [MemberData(nameof(NonEquipmentManagerActors))]
    public void RejectByEquipment_WithWrongRole_IsRejected(string actorName)
    {
        var request = Req(RequestStatus.PENDING_EQUIPMENT);

        Assert.Throws<ApprovalAuthorizationException>(
            () => RequestStateMachine.RejectByEquipment(request, OpenPeriod(), ActorByName(actorName), "gerekçe", Now));
    }

    public static TheoryData<string> NonFirmManagerActors() =>
        new() { "requester", "equipmentManager", "equipmentViewer", "firmOperator" };

    private static TransitionActor FirmSideActorByName(string name) => name switch
    {
        "requester" => Requester(),
        "equipmentManager" => EquipmentManager(),
        "equipmentViewer" => EquipmentViewer(),
        "firmOperator" => FirmOperator(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Bilinmeyen aktör.")
    };

    [Theory]
    [MemberData(nameof(NonFirmManagerActors))]
    public void AcceptByFirm_WithWrongRole_IsRejected(string actorName)
    {
        var request = Req(RequestStatus.PENDING_FIRM);

        Assert.Throws<ApprovalAuthorizationException>(
            () => RequestStateMachine.AcceptByFirm(
                request, OpenPeriod(), FirmSideActorByName(actorName), "Operatör", "33 ABC 33", Now));

        Assert.Equal(RequestStatus.PENDING_FIRM, request.Status);
    }

    [Theory]
    [MemberData(nameof(NonFirmManagerActors))]
    public void RejectByFirm_WithWrongRole_IsRejected(string actorName)
    {
        var request = Req(RequestStatus.PENDING_FIRM);

        Assert.Throws<ApprovalAuthorizationException>(
            () => RequestStateMachine.RejectByFirm(request, OpenPeriod(), FirmSideActorByName(actorName), "gerekçe", Now));
    }

    public static TheoryData<string> NonFirmOperatorActors() =>
        new() { "requester", "equipmentManager", "equipmentViewer", "firmManager" };

    private static TransitionActor OperatorStepActorByName(string name) => name switch
    {
        "requester" => Requester(),
        "equipmentManager" => EquipmentManager(),
        "equipmentViewer" => EquipmentViewer(),
        "firmManager" => FirmManager(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Bilinmeyen aktör.")
    };

    [Theory]
    [MemberData(nameof(NonFirmOperatorActors))]
    public void Start_WithWrongRole_IsRejected(string actorName)
    {
        var request = Req(RequestStatus.SCHEDULED);

        Assert.Throws<ApprovalAuthorizationException>(
            () => RequestStateMachine.Start(request, OpenPeriod(), OperatorStepActorByName(actorName), Now));

        Assert.Equal(RequestStatus.SCHEDULED, request.Status);
        Assert.Null(request.ActualStartTime);
    }

    [Theory]
    [MemberData(nameof(NonFirmOperatorActors))]
    public void Complete_WithWrongRole_IsRejected(string actorName)
    {
        var request = Req(RequestStatus.IN_PROGRESS);

        Assert.Throws<ApprovalAuthorizationException>(
            () => RequestStateMachine.Complete(request, OpenPeriod(), OperatorStepActorByName(actorName), Now));

        Assert.Null(request.ActualEndTime);
    }

    /// <summary>İptal: talebi açan VEYA Ekipman Müdürlüğü Yöneticisi.</summary>
    [Fact]
    public void Cancel_ByEquipmentManager_IsAllowed()
    {
        var request = Req(RequestStatus.SCHEDULED);

        RequestStateMachine.Cancel(request, OpenPeriod(), EquipmentManager(), "liman kapandı", Now);

        Assert.Equal(RequestStatus.CANCELLED, request.Status);
    }

    [Theory]
    [MemberData(nameof(NonFirmOperatorActors))]
    public void Cancel_ByUnrelatedActor_IsRejected(string actorName)
    {
        if (actorName == "requester" || actorName == "equipmentManager")
        {
            return; // bu ikisi iptal edebilir; ayrı testlerde doğrulanıyor
        }

        var request = Req(RequestStatus.SCHEDULED);

        var ex = Assert.Throws<ApprovalAuthorizationException>(
            () => RequestStateMachine.Cancel(request, OpenPeriod(), OperatorStepActorByName(actorName), "gerekçe", Now));

        Assert.Contains("iptal edebilir", ex.Message);
    }

    [Fact]
    public void Cancel_ByAnotherRequester_IsRejected()
    {
        var request = Req(RequestStatus.DRAFT);

        Assert.Throws<ApprovalAuthorizationException>(
            () => RequestStateMachine.Cancel(request, OpenPeriod(), OtherRequester(), "gerekçe", Now));
    }

    // ---------------------------------------------------------------
    // 5) Firma izolasyonu — makine seviyesinde (CLAUDE.md kural 7)
    // ---------------------------------------------------------------

    [Fact]
    public void AcceptByFirm_ByAnotherFirm_IsRejected()
    {
        var request = Req(RequestStatus.PENDING_FIRM);

        var ex = Assert.Throws<ApprovalAuthorizationException>(
            () => RequestStateMachine.AcceptByFirm(
                request, OpenPeriod(), FirmManager(OtherFirmId), "Operatör", "33 ABC 33", Now));

        Assert.Contains("Başka bir firmanın", ex.Message);
    }

    [Fact]
    public void Start_ByAnotherFirmsOperator_IsRejected()
    {
        var request = Req(RequestStatus.SCHEDULED);

        Assert.Throws<ApprovalAuthorizationException>(
            () => RequestStateMachine.Start(request, OpenPeriod(), FirmOperator(OtherFirmId), Now));
    }

    [Fact]
    public void FirmStep_OnRequestWithoutFirm_IsRejected()
    {
        var request = Req(RequestStatus.PENDING_FIRM, firmId: null);

        var ex = Assert.Throws<ApprovalAuthorizationException>(
            () => RequestStateMachine.AcceptByFirm(
                request, OpenPeriod(), FirmManager(), "Operatör", "33 ABC 33", Now));

        Assert.Contains("firma atanmamış", ex.Message);
    }

    /// <summary>
    /// FIRM_USER geçiş rolüdür: yeni rol dağıtımı tamamlanana kadar
    /// FIRM_MANAGER ile eşdeğerdir.
    /// </summary>
    [Fact]
    public void AcceptByFirm_ByLegacyFirmUserRole_IsAllowed()
    {
        var request = Req(RequestStatus.PENDING_FIRM);
        var legacy = Actor(30, FirmId, RoleCodes.FirmUser);

        RequestStateMachine.AcceptByFirm(request, OpenPeriod(), legacy, "Operatör", "33 ABC 33", Now);

        Assert.Equal(RequestStatus.SCHEDULED, request.Status);
    }

    // ---------------------------------------------------------------
    // 6) Gerekçe zorunluluğu — boşluk karakteri de reddedilir
    // ---------------------------------------------------------------

    public static TheoryData<string?> BlankReasons() => new() { null, "", "   ", "\t", "\n  \t " };

    [Theory]
    [MemberData(nameof(BlankReasons))]
    public void RejectByEquipment_WithoutReason_IsRejected(string? reason)
    {
        var request = Req(RequestStatus.PENDING_EQUIPMENT);

        var ex = Assert.Throws<RequestStateTransitionException>(
            () => RequestStateMachine.RejectByEquipment(request, OpenPeriod(), EquipmentManager(), reason, Now));

        Assert.Contains("Red gerekçesi zorunludur", ex.Message);
        Assert.Equal(RequestStatus.PENDING_EQUIPMENT, request.Status);
        Assert.Null(request.RejectionReason);
    }

    [Theory]
    [MemberData(nameof(BlankReasons))]
    public void RejectByFirm_WithoutReason_IsRejected(string? reason)
    {
        var request = Req(RequestStatus.PENDING_FIRM);

        Assert.Throws<RequestStateTransitionException>(
            () => RequestStateMachine.RejectByFirm(request, OpenPeriod(), FirmManager(), reason, Now));

        Assert.Equal(RequestStatus.PENDING_FIRM, request.Status);
    }

    [Theory]
    [MemberData(nameof(BlankReasons))]
    public void Cancel_WithoutReason_IsRejected(string? reason)
    {
        var request = Req(RequestStatus.DRAFT);

        var ex = Assert.Throws<RequestStateTransitionException>(
            () => RequestStateMachine.Cancel(request, OpenPeriod(), Requester(), reason, Now));

        Assert.Contains("İptal gerekçesi zorunludur", ex.Message);
        Assert.Null(request.CancellationReason);
    }

    [Theory]
    [MemberData(nameof(BlankReasons))]
    public void AcceptByFirm_WithoutOperatorOrPlate_IsRejected(string? blank)
    {
        var missingOperator = Req(RequestStatus.PENDING_FIRM);
        Assert.Throws<RequestStateTransitionException>(
            () => RequestStateMachine.AcceptByFirm(missingOperator, OpenPeriod(), FirmManager(), blank, "33 ABC 33", Now));

        var missingPlate = Req(RequestStatus.PENDING_FIRM);
        Assert.Throws<RequestStateTransitionException>(
            () => RequestStateMachine.AcceptByFirm(missingPlate, OpenPeriod(), FirmManager(), "Operatör", blank, Now));

        Assert.Equal(RequestStatus.PENDING_FIRM, missingOperator.Status);
        Assert.Equal(RequestStatus.PENDING_FIRM, missingPlate.Status);
    }

    // ---------------------------------------------------------------
    // 7) Mutlu yol: alanlar ve zaman damgaları doğru anda doluyor
    // ---------------------------------------------------------------

    [Fact]
    public void HappyPath_WalksDraftToCompleted_AndStampsEachDecision()
    {
        var request = Req(RequestStatus.DRAFT);
        var period = OpenPeriod();

        var submittedAt = Now;
        RequestStateMachine.Submit(request, period, Requester(), submittedAt);
        Assert.Equal(RequestStatus.SUBMITTED, request.Status);
        Assert.Equal(submittedAt, request.SubmittedAt);

        RequestStateMachine.SendToEquipment(request, period, Requester());
        Assert.Equal(RequestStatus.PENDING_EQUIPMENT, request.Status);

        var equipmentAt = Now.AddHours(1);
        RequestStateMachine.ApproveByEquipment(request, period, EquipmentManager(), equipmentAt);
        Assert.Equal(RequestStatus.PENDING_FIRM, request.Status);
        Assert.Equal(equipmentAt, request.EquipmentDecisionAt);

        var firmAt = Now.AddHours(2);
        RequestStateMachine.AcceptByFirm(request, period, FirmManager(), "Şükrü Çağlayan", "33 ABC 33", firmAt);
        Assert.Equal(RequestStatus.SCHEDULED, request.Status);
        Assert.Equal(firmAt, request.FirmDecisionAt);
        Assert.Equal("Şükrü Çağlayan", request.AssignedOperatorName);
        Assert.Equal("33 ABC 33", request.AssignedLicensePlate);

        var startedAt = Now.AddHours(3);
        RequestStateMachine.Start(request, period, FirmOperator(), startedAt);
        Assert.Equal(RequestStatus.IN_PROGRESS, request.Status);
        Assert.Equal(startedAt, request.ActualStartTime);

        var endedAt = Now.AddHours(10);
        RequestStateMachine.Complete(request, period, FirmOperator(), endedAt);
        Assert.Equal(RequestStatus.COMPLETED, request.Status);
        Assert.Equal(endedAt, request.ActualEndTime);

        // Reddedilmeyen/iptal edilmeyen bir talepte gerekçe alanları BOŞ kalır.
        Assert.Null(request.RejectionReason);
        Assert.Null(request.CancellationReason);
        Assert.Null(request.CancelledAt);
    }

    [Fact]
    public void RejectByEquipment_StoresReasonAndDecisionTime()
    {
        var request = Req(RequestStatus.PENDING_EQUIPMENT);

        RequestStateMachine.RejectByEquipment(request, OpenPeriod(), EquipmentManager(), "uygun kapasitede vinç yok", Now);

        Assert.Equal(RequestStatus.REJECTED_BY_EQUIPMENT, request.Status);
        Assert.Equal("uygun kapasitede vinç yok", request.RejectionReason);
        Assert.Equal(Now, request.EquipmentDecisionAt);
        Assert.Null(request.FirmDecisionAt);
    }

    [Fact]
    public void RejectByFirm_StoresReasonAndDecisionTime()
    {
        var request = Req(RequestStatus.PENDING_FIRM);

        RequestStateMachine.RejectByFirm(request, OpenPeriod(), FirmManager(), "vinç bakımda", Now);

        Assert.Equal(RequestStatus.REJECTED_BY_FIRM, request.Status);
        Assert.Equal("vinç bakımda", request.RejectionReason);
        Assert.Equal(Now, request.FirmDecisionAt);
    }

    [Fact]
    public void Cancel_StoresReasonAndCancelledAt()
    {
        var request = Req(RequestStatus.SCHEDULED);

        RequestStateMachine.Cancel(request, OpenPeriod(), Requester(), "iş ertelendi", Now);

        Assert.Equal(RequestStatus.CANCELLED, request.Status);
        Assert.Equal("iş ertelendi", request.CancellationReason);
        Assert.Equal(Now, request.CancelledAt);
    }

    // ---------------------------------------------------------------
    // 8) Talep açana gösterilen sadeleştirilmiş durum
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(RequestStatus.SUBMITTED, "Bekliyor")]
    [InlineData(RequestStatus.PENDING_EQUIPMENT, "Bekliyor")]
    [InlineData(RequestStatus.PENDING_FIRM, "Bekliyor")]
    [InlineData(RequestStatus.SCHEDULED, "Onaylandı")]
    [InlineData(RequestStatus.IN_PROGRESS, "Onaylandı")]
    [InlineData(RequestStatus.COMPLETED, "Tamamlandı")]
    [InlineData(RequestStatus.REJECTED_BY_EQUIPMENT, "Reddedildi")]
    [InlineData(RequestStatus.REJECTED_BY_FIRM, "Reddedildi")]
    [InlineData(RequestStatus.CANCELLED, "İptal edildi")]
    [InlineData(RequestStatus.DRAFT, "Taslak")]
    public void Summary_HidesInternalSteps(RequestStatus status, string expected)
    {
        Assert.Equal(expected, RequestStatusLabels.GetSummary(status));
    }

    [Fact]
    public void EveryStatus_HasBothLabels()
    {
        Assert.All(AllStatuses, s =>
        {
            Assert.True(RequestStatusLabels.Labels.ContainsKey(s), $"{s} için tam etiket yok.");
            Assert.True(RequestStatusLabels.Summary.ContainsKey(s), $"{s} için sade etiket yok.");
        });
    }

    /// <summary>
    /// Sadeleştirme, iki farklı bekleme adımını AYNI etikete indirir — talebi
    /// açan için ikisi arasında yapılacak bir şey yoktur.
    /// </summary>
    [Fact]
    public void Summary_CollapsesBothPendingSteps()
    {
        Assert.Equal(
            RequestStatusLabels.GetSummary(RequestStatus.PENDING_EQUIPMENT),
            RequestStatusLabels.GetSummary(RequestStatus.PENDING_FIRM));

        // ...ama gerçek etiketleri AYRI kalır: süreci yürüten kim bekletiyor görür.
        Assert.NotEqual(
            RequestStatusLabels.Get(RequestStatus.PENDING_EQUIPMENT),
            RequestStatusLabels.Get(RequestStatus.PENDING_FIRM));
    }
}
