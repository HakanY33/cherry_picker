using Microsoft.EntityFrameworkCore;
using MipRental.Data.Services;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Approvals;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;

namespace MipRental.Data.Approvals;

/// <summary>
/// Onay akışının yürütücüsü: Approvals kayıtlarını açar/kapatır, sıradaki adımı
/// bulur, bildirimleri kuyruğa yazar. Durum değişikliğini KENDİSİ yapmaz —
/// her Status değişimi WorkRecordStateMachine'den geçer.
///
/// SaveChanges ÇAĞIRMAZ. Değişiklikleri change tracker'a bırakır; commit sınırını
/// çağıran (controller) belirler. Böylece toplu onayda her kayıt kendi
/// transaction'ında commit edilebilir ve gönderim akışında durum değişikliği ile
/// belge numarası aynı transaction'da kalır.
/// </summary>
public sealed class ApprovalService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ApprovalFlowResolver _flowResolver;
    private readonly NotificationQueue _notifications;

    private TransitionActor? _actorCache;

    public ApprovalService(
        AppDbContext db, ICurrentUser currentUser, ApprovalFlowResolver flowResolver, NotificationQueue notifications)
    {
        _db = db;
        _currentUser = currentUser;
        _flowResolver = flowResolver;
        _notifications = notifications;
    }

    /// <summary>
    /// Rol kodları claim'den değil veritabanından okunur: onay yetkisi mali bir
    /// karardır, oturum açıldıktan sonra değişen rol hemen geçerli olmalıdır.
    /// </summary>
    public async Task<TransitionActor> GetActorAsync(CancellationToken cancellationToken = default)
    {
        if (_actorCache is not null)
        {
            return _actorCache;
        }

        var roleCodes = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == _currentUser.UserId)
            .Select(ur => ur.Role.Code)
            .ToListAsync(cancellationToken);

        _actorCache = TransitionActor.From(_currentUser, roleCodes);
        return _actorCache;
    }

    // ---------------------------------------------------------------
    // Gönderim: SUBMITTED -> PENDING + ilk adımın Approvals kaydı
    // ---------------------------------------------------------------

    /// <summary>
    /// Kayıt gönderildikten sonra ilk onay adımını açar ve kaydı PENDING yapar.
    /// Gönderimin parçasıdır; çağıranın transaction'ı içinde çalışır.
    /// </summary>
    public async Task<ApprovalFlowStep> SendToApprovalAsync(
        WorkRecord record, Period period, CancellationToken cancellationToken = default)
    {
        var actor = await GetActorAsync(cancellationToken);
        var chain = await _flowResolver.ResolveForWorkRecordAsync(record, cancellationToken);
        var firstStep = chain.First;

        WorkRecordStateMachine.SendToApproval(record, period, actor);

        OpenApproval(record, firstStep);
        await _notifications.QueueApprovalPendingAsync(record, firstStep, cancellationToken);

        return firstStep;
    }

    // ---------------------------------------------------------------
    // Kararlar
    // ---------------------------------------------------------------

    /// <summary>
    /// Açık adımı onaylar. Sıradaki adım varsa yeni Approvals kaydı açılır ve kayıt
    /// PENDING kalır; yoksa kayıt APPROVED olur.
    /// </summary>
    public async Task<ApprovalOutcome> ApproveAsync(int workRecordId, string? comment, CancellationToken cancellationToken = default)
    {
        var ctx = await LoadAsync(workRecordId, cancellationToken);
        var nextStep = ctx.Chain.StepAfter(ctx.OpenApproval.StepNo);

        if (nextStep is null)
        {
            WorkRecordStateMachine.Approve(ctx.Record, ctx.Period, ctx.Actor, ctx.StepRoleCode, ctx.StepRoleName);
            ctx.Record.ApprovedAt = DateTime.UtcNow;
        }
        else
        {
            WorkRecordStateMachine.AdvanceToNextStep(ctx.Record, ctx.Period, ctx.Actor, ctx.StepRoleCode, ctx.StepRoleName);
        }

        CloseApproval(ctx.OpenApproval, ApprovalDecision.APPROVED, comment);

        if (nextStep is not null)
        {
            OpenApproval(ctx.Record, nextStep);
            await _notifications.QueueApprovalPendingAsync(ctx.Record, nextStep, cancellationToken);
        }
        else
        {
            await _notifications.QueueDecisionAsync(ctx.Record, ApprovalDecision.APPROVED, comment, cancellationToken);
        }

        return new ApprovalOutcome
        {
            Record = ctx.Record,
            Status = ctx.Record.Status,
            CompletedStep = ctx.Step,
            NextStep = nextStep
        };
    }

    /// <summary>Kaydı reddeder. Gerekçe zorunlu (durum makinesi doğrular).</summary>
    public async Task<ApprovalOutcome> RejectAsync(int workRecordId, string? reason, CancellationToken cancellationToken = default)
    {
        var ctx = await LoadAsync(workRecordId, cancellationToken);

        WorkRecordStateMachine.Reject(ctx.Record, ctx.Period, ctx.Actor, ctx.StepRoleCode, ctx.StepRoleName, reason);

        CloseApproval(ctx.OpenApproval, ApprovalDecision.REJECTED, reason);
        await _notifications.QueueDecisionAsync(ctx.Record, ApprovalDecision.REJECTED, reason, cancellationToken);

        return new ApprovalOutcome { Record = ctx.Record, Status = ctx.Record.Status, CompletedStep = ctx.Step, NextStep = null };
    }

    /// <summary>Kaydın tamamı için revizyon ister. Gerekçe zorunlu.</summary>
    public async Task<ApprovalOutcome> RequestRevisionAsync(int workRecordId, string? reason, CancellationToken cancellationToken = default)
    {
        var ctx = await LoadAsync(workRecordId, cancellationToken);

        WorkRecordStateMachine.RequestRevision(ctx.Record, ctx.Period, ctx.Actor, ctx.StepRoleCode, ctx.StepRoleName, reason);

        CloseApproval(ctx.OpenApproval, ApprovalDecision.REVISION_REQUESTED, reason);
        await _notifications.QueueDecisionAsync(ctx.Record, ApprovalDecision.REVISION_REQUESTED, reason, cancellationToken);

        return new ApprovalOutcome { Record = ctx.Record, Status = ctx.Record.Status, CompletedStep = ctx.Step, NextStep = null };
    }

    /// <summary>
    /// SATIR BAZLI İTİRAZ: onaylayan, kaydın tamamını değil tek bir satırını
    /// reddeder. İtiraz edilen satır varsa kayıt REVISION_REQUESTED olur —
    /// 40 satırlık bir kayıtta 1 satır yüzünden tüm ay beklemesin diye, alt
    /// yüklenici yalnızca o satırı düzeltip yeni versiyonu gönderir.
    /// </summary>
    public async Task<ApprovalOutcome> ObjectToLineAsync(
        int workRecordId, int workRecordLineId, string? reason, CancellationToken cancellationToken = default)
    {
        var ctx = await LoadAsync(workRecordId, cancellationToken);

        var line = ctx.Record.WorkRecordLines.FirstOrDefault(l => l.WorkRecordLineId == workRecordLineId)
            ?? throw new ApprovalFlowException("İtiraz edilecek satır bu kayda ait değil.");

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new WorkRecordStateTransitionException("Satır itirazı için gerekçe zorunludur; boş bırakılamaz.");
        }

        // Durum geçişi önce doğrulanır: yetkisiz/izinsiz bir denemede satır
        // işaretlenmiş olarak kalmasın.
        var composedReason = $"{line.LineNo}. satıra itiraz: {reason.Trim()}";
        WorkRecordStateMachine.RequestRevision(ctx.Record, ctx.Period, ctx.Actor, ctx.StepRoleCode, ctx.StepRoleName, composedReason);

        line.IsObjected = true;
        line.ObjectionReason = reason.Trim();
        line.ObjectedByUserId = _currentUser.UserId;
        line.ObjectedAt = DateTime.UtcNow;

        CloseApproval(ctx.OpenApproval, ApprovalDecision.REVISION_REQUESTED, composedReason);

        var objectedLines = ctx.Record.WorkRecordLines.Where(l => l.IsObjected).ToList();
        await _notifications.QueueLineObjectionAsync(ctx.Record, objectedLines, cancellationToken);

        return new ApprovalOutcome { Record = ctx.Record, Status = ctx.Record.Status, CompletedStep = ctx.Step, NextStep = null };
    }

    // ---------------------------------------------------------------
    // Sorgular
    // ---------------------------------------------------------------

    /// <summary>
    /// "Onayımı Bekleyenler": karar verilmemiş, kullanıcının rolüne düşen adımlar.
    /// Rol eşleşmesi burada da uygulanır — sadece ekranda gizlemek yetmez.
    /// </summary>
    public async Task<IReadOnlyList<Approval>> GetPendingForCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        if (_currentUser.IsFirmUser)
        {
            // Alt yüklenici onay kuyruğu görmez.
            return Array.Empty<Approval>();
        }

        var roleIds = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == _currentUser.UserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(cancellationToken);

        if (roleIds.Count == 0)
        {
            return Array.Empty<Approval>();
        }

        return await _db.Approvals.AsNoTracking()
            .Include(a => a.ApprovalFlowStep)!.ThenInclude(s => s!.Role)
            .Where(a => a.Decision == null
                && a.DocumentType == DocumentType.WORK_RECORD
                && a.AssignedToRoleId != null
                && roleIds.Contains(a.AssignedToRoleId!.Value))
            .OrderBy(a => a.AssignedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Bir belgenin tüm onay geçmişi (kapalı + açık adımlar), sırayla.</summary>
    public async Task<IReadOnlyList<Approval>> GetHistoryAsync(int workRecordId, CancellationToken cancellationToken = default) =>
        await _db.Approvals.AsNoTracking()
            .Include(a => a.ApprovalFlowStep)!.ThenInclude(s => s!.Role)
            .Include(a => a.DecidedByUser)
            .Include(a => a.AssignedToRole)
            .Where(a => a.DocumentType == DocumentType.WORK_RECORD && a.DocumentId == workRecordId)
            .OrderBy(a => a.StepNo).ThenBy(a => a.ApprovalId)
            .ToListAsync(cancellationToken);

    /// <summary>Kullanıcı bu kaydın AÇIK adımında karar verebilir mi (buton gösterimi için).</summary>
    public async Task<bool> CanCurrentUserDecideAsync(int workRecordId, CancellationToken cancellationToken = default)
    {
        if (_currentUser.IsFirmUser)
        {
            return false;
        }

        var openApproval = await FindOpenApprovalAsync(workRecordId, cancellationToken);
        if (openApproval?.AssignedToRoleId is null)
        {
            return false;
        }

        return await _db.UserRoles.AsNoTracking()
            .AnyAsync(ur => ur.UserId == _currentUser.UserId && ur.RoleId == openApproval.AssignedToRoleId, cancellationToken);
    }

    // ---------------------------------------------------------------
    // İç yardımcılar
    // ---------------------------------------------------------------

    private void OpenApproval(WorkRecord record, ApprovalFlowStep step)
    {
        _db.Approvals.Add(new Approval
        {
            DocumentType = DocumentType.WORK_RECORD,
            DocumentId = record.WorkRecordId,
            FlowStepId = step.FlowStepId,
            StepNo = step.StepNo,
            // Atama kullanıcıya değil ROLE yapılır: kişi izinde olsa da adım
            // rolündeki başka biri onaylayabilir.
            AssignedToRoleId = step.RoleId,
            AssignedToUserId = null,
            AssignedAt = DateTime.UtcNow
        });
    }

    private void CloseApproval(Approval approval, ApprovalDecision decision, string? comment)
    {
        approval.Decision = decision;
        approval.DecidedByUserId = _currentUser.UserId;
        approval.DecidedAt = DateTime.UtcNow;
        approval.Comment = comment?.Trim();
    }

    private Task<Approval?> FindOpenApprovalAsync(int workRecordId, CancellationToken cancellationToken) =>
        _db.Approvals
            .Where(a => a.DocumentType == DocumentType.WORK_RECORD && a.DocumentId == workRecordId && a.Decision == null)
            .OrderBy(a => a.StepNo)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<DecisionContext> LoadAsync(int workRecordId, CancellationToken cancellationToken)
    {
        var record = await _db.WorkRecords
            .Include(w => w.WorkRecordLines)
            .FirstOrDefaultAsync(w => w.WorkRecordId == workRecordId, cancellationToken)
            ?? throw new ApprovalFlowException("Çalışma kaydı bulunamadı.");

        var openApproval = await FindOpenApprovalAsync(workRecordId, cancellationToken)
            ?? throw new WorkRecordStateTransitionException(
                $"{record.DocumentNo} numaralı kayıtta karar bekleyen bir onay adımı yok.");

        var period = await _db.Periods.AsNoTracking().SingleAsync(p => p.PeriodId == record.PeriodId, cancellationToken);
        var chain = await _flowResolver.ResolveForWorkRecordAsync(record, cancellationToken);

        var step = chain.StepByNo(openApproval.StepNo)
            ?? throw new ApprovalFlowException(
                $"Kaydın beklediği {openApproval.StepNo}. adım güncel onay akışında bulunamadı; akış tanımı kayıt gönderildikten sonra değiştirilmiş olabilir.");

        return new DecisionContext
        {
            Record = record,
            Period = period,
            Chain = chain,
            OpenApproval = openApproval,
            Step = step,
            Actor = await GetActorAsync(cancellationToken)
        };
    }

    private sealed class DecisionContext
    {
        public required WorkRecord Record { get; init; }
        public required Period Period { get; init; }
        public required ApprovalChain Chain { get; init; }
        public required Approval OpenApproval { get; init; }
        public required ApprovalFlowStep Step { get; init; }
        public required TransitionActor Actor { get; init; }

        public string StepRoleCode => Step.Role.Code;
        public string StepRoleName => Step.Role.Name;
    }
}

public sealed class ApprovalOutcome
{
    public required WorkRecord Record { get; init; }
    public required WorkRecordStatus Status { get; init; }
    public required ApprovalFlowStep CompletedStep { get; init; }
    public ApprovalFlowStep? NextStep { get; init; }
}
