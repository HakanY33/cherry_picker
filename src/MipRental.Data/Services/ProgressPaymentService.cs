using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MipRental.Data.Approvals;
using MipRental.Data.Reporting;
using MipRental.Domain.Approvals;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;
using MipRental.Domain.Reporting;
using MipRental.Domain.Security;

namespace MipRental.Data.Services;

/// <summary>
/// ADIM 14 BÖLÜM A — hakediş kaydının oluşturulması ve durum geçişleri.
///
/// Hakediş bir ANLIK GÖRÜNTÜDÜR: oluşturulduğu andaki onaylı kayıt listesi ve
/// toplamı dondurulur. Aynı icmal ertesi gün açıldığında yeni onaylanmış bir
/// kayıt yüzünden büyüyebilir; hakediş büyümez. Yoksa "onaylanan tutar" ile
/// "ödenen tutar" ayrışırdı.
///
/// İş kuralı burada DEĞİL: durum geçişlerinin tek kaynağı
/// <see cref="ProgressPaymentStateMachine"/>. Bu sınıf makinenin istediği
/// parametreleri çözer, listeyi dondurur ve bildirimi kuyruğa yazar.
/// </summary>
public sealed class ProgressPaymentService
{
    private readonly AppDbContext _db;
    private readonly MonthlySummaryService _summaries;
    private readonly ApprovalService _approvals;
    private readonly ApprovalTokenService _tokens;
    private readonly NotificationQueue _notifications;

    public ProgressPaymentService(
        AppDbContext db,
        MonthlySummaryService summaries,
        ApprovalService approvals,
        ApprovalTokenService tokens,
        NotificationQueue notifications)
    {
        _db = db;
        _summaries = summaries;
        _approvals = approvals;
        _tokens = tokens;
        _notifications = notifications;
    }

    public Task<TransitionActor> GetActorAsync(CancellationToken cancellationToken = default) =>
        _approvals.GetActorAsync(cancellationToken);

    /// <summary>
    /// Dönem + firma için hakediş oluşturur ve kayıt listesini DONDURUR.
    ///
    /// İcmalin kendisi yeniden yazılmaz: hangi kaydın hakedişe gireceği sorusunun
    /// cevabı zaten <see cref="MonthlySummaryService"/>'te (APPROVED + LOCKED,
    /// superseded hariç). İki ayrı "hakedişe ne girer" tanımı olsaydı biri
    /// diğerinden sessizce ayrışırdı.
    /// </summary>
    public async Task<ProgressPayment> CreateAsync(int periodId, int firmId, CancellationToken cancellationToken = default)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (!actor.IsMipStaff || !actor.IsInRole(RoleCodes.Budget))
        {
            throw new ApprovalAuthorizationException("Hakedişi yalnızca Bütçe oluşturabilir.");
        }

        var summary = await _summaries.BuildAsync(periodId, firmId, serviceId: null, cancellationToken);

        if (summary.IsEmpty)
        {
            throw new ProgressPaymentStateTransitionException(
                $"{summary.FirmTitle} için {PeriodName(summary)} döneminde hakedişe girecek onaylı kayıt yok.");
        }

        // Karışık para birimi tek bir toplamda anlam taşımaz; hakediş ödemeye esas
        // belgedir, "1.000" yazıp hangi para biriminde olduğunu söylememek olmaz.
        if (summary.HasMixedCurrency)
        {
            throw new ProgressPaymentStateTransitionException(
                $"{PeriodName(summary)} döneminde birden fazla para birimi var; hakediş tek para biriminde düzenlenir.");
        }

        var payment = new ProgressPayment
        {
            PeriodId = periodId,
            FirmId = firmId,
            Status = ProgressPaymentStatus.DRAFT,
            TotalAmount = summary.GrandTotal ?? 0m,
            Currency = summary.Currency,
            RecordCount = summary.RecordCount,
            PendingRecordCountAtCreation = summary.PendingRecordCount,
            CreatedByUserId = actor.UserId,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var workRecordId in IncludedRecordIds(summary))
        {
            payment.Records.Add(new ProgressPaymentRecord { WorkRecordId = workRecordId });
        }

        _db.ProgressPayments.Add(payment);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // UQ_ProgressPayments_Period_Firm: aynı dönem+firma için ikinci hakediş.
            // Uygulama katmanı ön kontrolü yarışta yetmez, son sözü index söyler.
            _db.ChangeTracker.Clear();
            throw new ProgressPaymentStateTransitionException(
                $"{summary.FirmTitle} için {PeriodName(summary)} dönemi hakedişi zaten oluşturulmuş; " +
                "bir dönem ve firma için tek hakediş düzenlenir.");
        }

        return payment;
    }

    /// <summary>
    /// A4 — Bütçe onayı: hakediş Bütçe Yöneticisi'ne gider. Notu ve onaylayanı
    /// makine yazar; alan bazlı denetim izi AuditSaveChangesInterceptor'dan düşer.
    /// SaveChanges ÇAĞIRILMAZ: çağıran, mail token'ı gibi aynı işe ait diğer
    /// kayıtları aynı transaction'a katabilsin (Bölüm B).
    /// </summary>
    public async Task<int> SendToManagerAsync(
        ProgressPayment payment,
        string? budgetNote,
        Func<string, string> approvalUrlBuilder,
        CancellationToken cancellationToken = default)
    {
        var actor = await GetActorAsync(cancellationToken);
        var nowUtc = DateTime.UtcNow;

        ProgressPaymentStateMachine.SendToManager(payment, actor, budgetNote, nowUtc);

        // Alıcı ROL ile bulunur, elle seçilmez: aktif, MIP personeli olan tüm
        // BUDGET_MANAGER'lar. Rolde kimse yoksa mail düşmez ama hakediş yine
        // bekler — otomatik onay yok (kural 5).
        var managers = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.IsActive && u.FirmId == null
                     && u.UserRoles.Any(ur => ur.Role.Code == RoleCodes.BudgetManager))
            .Select(u => new { u.UserId, u.Email })
            .ToListAsync(cancellationToken);

        var (periodName, firmTitle) = await DescribeAsync(payment, cancellationToken);

        foreach (var manager in managers)
        {
            // Her yöneticiye AYRI token: biri kullanınca diğerininki de ölmez,
            // ama hakediş artık PENDING olmadığı için karar veremez — kim karar
            // verdi sorusunun cevabı token'ın sahibidir.
            var rawToken = _tokens.Issue(payment, manager.UserId, nowUtc);
            _notifications.QueueProgressPaymentApproval(
                payment, manager.UserId, manager.Email, periodName, firmTitle, approvalUrlBuilder(rawToken));
        }

        return managers.Count;
    }

    /// <summary>
    /// B8 — hakedişi geri çeker (PENDING_BUDGET_MANAGER -> DRAFT) ve AÇIK
    /// token'ların tamamını iptal eder. İptal aynı işin parçasıdır: mail
    /// kutusundaki bağlantı geri çekilmiş bir hakedişi onaylayamamalı.
    /// </summary>
    public async Task<int> WithdrawAsync(ProgressPayment payment, CancellationToken cancellationToken = default)
    {
        var actor = await GetActorAsync(cancellationToken);
        ProgressPaymentStateMachine.Withdraw(payment, actor);

        return await _tokens.RevokeOpenTokensAsync(payment.ProgressPaymentId, DateTime.UtcNow, cancellationToken);
    }

    /// <summary>
    /// Mailden gelen karar. Oturum YOK: aktör token'ın gönderildiği kullanıcıdır
    /// ve rolleri veritabanından okunur — rolü alınmış bir yönetici, elindeki eski
    /// bağlantıyla karar veremez.
    ///
    /// Karar durum makinesinden geçer; makine reddederse (yanlış durum, boş
    /// gerekçe) token TÜKENMEZ, kullanıcı düzeltip tekrar deneyebilir.
    /// </summary>
    public async Task DecideByTokenAsync(
        ApprovalToken token, bool approve, string? noteOrReason, string? ip, string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var actor = await BuildActorAsync(token.IssuedToUserId, cancellationToken);
        var nowUtc = DateTime.UtcNow;

        if (approve)
        {
            ProgressPaymentStateMachine.Approve(token.ProgressPayment, actor, noteOrReason, nowUtc);
        }
        else
        {
            ProgressPaymentStateMachine.Reject(token.ProgressPayment, actor, noteOrReason, nowUtc);
        }

        ApprovalTokenService.MarkUsed(token, nowUtc, ip, userAgent);
    }

    /// <summary>Token'ın sahibi için aktör; rolleri DB'den okunur.</summary>
    private async Task<TransitionActor> BuildActorAsync(int userId, CancellationToken cancellationToken)
    {
        var user = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.UserId == userId && u.IsActive)
            .Select(u => new { u.UserId, u.FirmId, Roles = u.UserRoles.Select(ur => ur.Role.Code).ToList() })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ApprovalAuthorizationException("Bağlantının sahibi kullanıcı bulunamadı ya da pasif.");

        return new TransitionActor
        {
            UserId = user.UserId,
            FirmId = user.FirmId,
            Roles = new HashSet<string>(user.Roles, StringComparer.Ordinal)
        };
    }

    private async Task<(string PeriodName, string FirmTitle)> DescribeAsync(
        ProgressPayment payment, CancellationToken cancellationToken)
    {
        var period = await _db.Periods.AsNoTracking()
            .Where(p => p.PeriodId == payment.PeriodId)
            .Select(p => new { p.Year, p.Month })
            .SingleAsync(cancellationToken);

        var firmTitle = await _db.Firms.AsNoTracking()
            .Where(f => f.FirmId == payment.FirmId)
            .Select(f => f.Title)
            .SingleAsync(cancellationToken);

        var periodName = $"{CultureInfo.GetCultureInfo("tr-TR").DateTimeFormat.GetMonthName(period.Month)} {period.Year}";
        return (periodName, firmTitle);
    }

    public async Task ApproveAsync(
        ProgressPayment payment, string? note, CancellationToken cancellationToken = default)
    {
        var actor = await GetActorAsync(cancellationToken);
        ProgressPaymentStateMachine.Approve(payment, actor, note, DateTime.UtcNow);
    }

    public async Task RejectAsync(
        ProgressPayment payment, string? reason, CancellationToken cancellationToken = default)
    {
        var actor = await GetActorAsync(cancellationToken);
        ProgressPaymentStateMachine.Reject(payment, actor, reason, DateTime.UtcNow);
    }

    /// <summary>
    /// Hakedişe giren çalışma kaydı id'leri. Satır bazlı icmalden kayıt bazına
    /// indirgenir: bir kaydın birden çok hizmet satırı olabilir.
    /// </summary>
    private static IEnumerable<int> IncludedRecordIds(MonthlySummary summary) =>
        summary.ServiceGroups
            .SelectMany(g => g.Lines)
            .Select(l => l.WorkRecordId)
            .Distinct()
            .OrderBy(id => id);

    // Dönem adı kodun her yerinde olduğu gibi tr-TR sözlüğünden gelir.
    private static string PeriodName(MonthlySummary summary) =>
        $"{CultureInfo.GetCultureInfo("tr-TR").DateTimeFormat.GetMonthName(summary.Month)} {summary.Year}";
}
