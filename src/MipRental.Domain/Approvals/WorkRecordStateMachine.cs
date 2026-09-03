using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;
using MipRental.Domain.Security;

namespace MipRental.Domain.Approvals;

/// <summary>
/// Çalışma kaydı durum geçişlerinin TEK KAYNAĞI.
///
/// Kural: WorkRecord.Status'a hiçbir controller/servis DOĞRUDAN atama yapmaz.
/// Her geçiş buradaki bir metottan geçer ve o metot üç şeyi birden kontrol eder:
///   1. Geçiş izinli mi   (AllowedTransitions tablosu)
///   2. Kullanıcının yetkisi var mı
///   3. Dönem açık mı     (CLAUDE.md kural 4)
/// İhlal halinde Türkçe mesajlı exception fırlar.
///
/// Sınıf SAFTIR: veritabanı erişimi, DateTime.UtcNow ya da oturum bilgisi YOK.
/// Karar için gereken her şey (kayıt, dönem, aktör, adımın rolü) parametre gelir;
/// bu sayede tüm geçiş matrisi veritabanısız test edilebilir.
/// </summary>
public static class WorkRecordStateMachine
{
    private static readonly IReadOnlySet<WorkRecordStatus> Terminal = new HashSet<WorkRecordStatus>();

    // Kullanıcı eylemiyle DEĞİL, dönem kapanışıyla ulaşılan durumlar. Hata
    // mesajlarında "izin verilen geçişler" listesinde gösterilmezler.
    private static readonly IReadOnlySet<WorkRecordStatus> SystemOnlyTargets =
        new HashSet<WorkRecordStatus> { WorkRecordStatus.LOCKED };

    /// <summary>
    /// İzin verilen geçişler. Burada olmayan HİÇBİR geçiş yapılamaz.
    /// APPROVED / REJECTED / CANCELLED terminaldir: hiçbir yere gidilmez.
    /// </summary>
    public static readonly IReadOnlyDictionary<WorkRecordStatus, IReadOnlySet<WorkRecordStatus>> AllowedTransitions =
        new Dictionary<WorkRecordStatus, IReadOnlySet<WorkRecordStatus>>
        {
            [WorkRecordStatus.DRAFT] = Set(WorkRecordStatus.SUBMITTED, WorkRecordStatus.CANCELLED),
            [WorkRecordStatus.SUBMITTED] = Set(WorkRecordStatus.PENDING),

            // PENDING -> PENDING: çok adımlı akışta bir adım onaylanınca kayıt
            // bir sonraki adıma geçer ama onay beklemeye DEVAM eder.
            [WorkRecordStatus.PENDING] = Set(
                WorkRecordStatus.PENDING,
                WorkRecordStatus.APPROVED,
                WorkRecordStatus.REJECTED,
                WorkRecordStatus.REVISION_REQUESTED),

            // Revizyon: eski kaydın durumu DEĞİŞMEZ; ardılı DRAFT olarak doğar.
            [WorkRecordStatus.REVISION_REQUESTED] = Set(WorkRecordStatus.DRAFT),

            // APPROVED -> LOCKED: onay akışının değil, DÖNEM KAPANIŞININ sonucudur.
            // Kullanıcı eylemiyle tetiklenmez; sadece LockForPeriodClose uygular.
            [WorkRecordStatus.APPROVED] = Set(WorkRecordStatus.LOCKED),

            [WorkRecordStatus.REJECTED] = Terminal,
            [WorkRecordStatus.CANCELLED] = Terminal,

            // LOCKED TERMİNALDİR: buradan hiçbir duruma GEÇİLMEZ.
            //
            // Dönem yeniden açıldığında kayıtların APPROVED'a dönmesi bir geçiş
            // DEĞİL, kapanışın geri alınmasıdır — bu yüzden bilinçli olarak
            // tabloda yer almaz ve UnlockForPeriodReopen ile yapılır. Aradaki fark
            // önemli: tabloya LOCKED -> APPROVED yazsaydık, dönem hâlâ kapalıyken
            // herhangi bir onay ekranından kilidi açmak "izinli" görünürdü.
            [WorkRecordStatus.LOCKED] = Terminal
        };

    public static bool IsAllowed(WorkRecordStatus from, WorkRecordStatus to) =>
        AllowedTransitions.TryGetValue(from, out var targets) && targets.Contains(to);

    // ---------------------------------------------------------------
    // Geçiş metotları. Her biri: izin + yetki + dönem kontrolü yapar,
    // sonra Status'u değiştirir.
    // ---------------------------------------------------------------

    /// <summary>
    /// DRAFT -> SUBMITTED. Kaydın sahibi firmanın YETKİLİSİ (FIRM_MANAGER /
    /// FIRM_USER). Operatör kaydı görür ama gönderemez (ADR-028).
    /// </summary>
    public static void Submit(WorkRecord record, Period period, TransitionActor actor)
    {
        EnsureTransitionAllowed(record, WorkRecordStatus.SUBMITTED);
        EnsureRecordOwner(record, actor, "gönderebilir");
        EnsureSubmitterRole(actor);
        EnsurePeriodOpen(period, "gönderilemez");

        record.Status = WorkRecordStatus.SUBMITTED;
    }

    /// <summary>
    /// SUBMITTED -> PENDING. Gönderimin hemen ardından ilk onay adımı açılınca
    /// uygulanır; kaydı gönderen firma kullanıcısı adına yapılır.
    /// </summary>
    public static void SendToApproval(WorkRecord record, Period period, TransitionActor actor)
    {
        EnsureTransitionAllowed(record, WorkRecordStatus.PENDING);
        EnsureRecordOwner(record, actor, "onaya gönderebilir");
        EnsurePeriodOpen(period, "onaya gönderilemez");

        record.Status = WorkRecordStatus.PENDING;
    }

    /// <summary>PENDING -> PENDING: adım onaylandı, sırada başka adım var.</summary>
    public static void AdvanceToNextStep(WorkRecord record, Period period, TransitionActor actor, string requiredRoleCode, string requiredRoleName)
    {
        EnsureTransitionAllowed(record, WorkRecordStatus.PENDING);
        EnsureApprover(record, actor, requiredRoleCode, requiredRoleName);
        EnsurePeriodOpen(period, "onaylanamaz");

        record.Status = WorkRecordStatus.PENDING;
    }

    /// <summary>PENDING -> APPROVED: son adım da onaylandı.</summary>
    public static void Approve(WorkRecord record, Period period, TransitionActor actor, string requiredRoleCode, string requiredRoleName)
    {
        EnsureTransitionAllowed(record, WorkRecordStatus.APPROVED);
        EnsureApprover(record, actor, requiredRoleCode, requiredRoleName);
        EnsurePeriodOpen(period, "onaylanamaz");

        record.Status = WorkRecordStatus.APPROVED;
    }

    /// <summary>PENDING -> REJECTED. Gerekçe ZORUNLU.</summary>
    public static void Reject(WorkRecord record, Period period, TransitionActor actor, string requiredRoleCode, string requiredRoleName, string? reason)
    {
        EnsureTransitionAllowed(record, WorkRecordStatus.REJECTED);
        EnsureApprover(record, actor, requiredRoleCode, requiredRoleName);
        EnsurePeriodOpen(period, "reddedilemez");
        EnsureReasonGiven(reason, "Red gerekçesi zorunludur; boş bırakılamaz.");

        record.Status = WorkRecordStatus.REJECTED;
    }

    /// <summary>PENDING -> REVISION_REQUESTED. Gerekçe ZORUNLU.</summary>
    public static void RequestRevision(WorkRecord record, Period period, TransitionActor actor, string requiredRoleCode, string requiredRoleName, string? reason)
    {
        EnsureTransitionAllowed(record, WorkRecordStatus.REVISION_REQUESTED);
        EnsureApprover(record, actor, requiredRoleCode, requiredRoleName);
        EnsurePeriodOpen(period, "revizyona gönderilemez");
        EnsureReasonGiven(reason, "Revizyon gerekçesi zorunludur; boş bırakılamaz.");

        record.Status = WorkRecordStatus.REVISION_REQUESTED;
    }

    /// <summary>DRAFT -> CANCELLED: iş yapılmadan iptal. Sadece kaydın sahibi firma.</summary>
    public static void Cancel(WorkRecord record, Period period, TransitionActor actor)
    {
        EnsureTransitionAllowed(record, WorkRecordStatus.CANCELLED);
        EnsureRecordOwner(record, actor, "iptal edebilir");
        EnsurePeriodOpen(period, "iptal edilemez");

        record.Status = WorkRecordStatus.CANCELLED;
    }

    /// <summary>
    /// REVISION_REQUESTED -> DRAFT (yeni versiyon). Eski kaydın Status'u
    /// DEĞİŞMEZ — bu yüzden metot bir mutasyon yapmaz, sadece izin verir.
    /// Ardıl kayıt çağıran tarafından DRAFT olarak oluşturulur.
    /// </summary>
    public static void EnsureCanCreateRevision(WorkRecord record, Period period, TransitionActor actor)
    {
        EnsureTransitionAllowed(record, WorkRecordStatus.DRAFT);
        EnsureRecordOwner(record, actor, "revize edebilir");
        EnsurePeriodOpen(period, "revize edilemez");

        if (record.IsSuperseded)
        {
            throw new WorkRecordStateTransitionException(
                $"{record.DocumentNo} numaralı kaydın zaten bir sonraki versiyonu oluşturulmuş; aynı kayıttan ikinci kez revizyon üretilemez.");
        }
    }

    // ---------------------------------------------------------------
    // Dönem kapanışı / yeniden açılışı — SİSTEM geçişleri
    //
    // Bu ikisi bir kullanıcının belge üzerindeki kararı değildir; dönemin
    // kapanmasının/açılmasının kayıtlara yansımasıdır. Bu yüzden onaylayan rolü
    // aranmaz, kaydın sahibi firma aranmaz ve "dönem açık mı" kontrolü de
    // YAPILMAZ — zaten tam olarak dönem kapanırken/açılırken çağrılırlar.
    //
    // Çağıran (PeriodsController) tek transaction içinde tüm döneme uygular;
    // tek tek kayıt kilitlemek/açmak için bir giriş noktası YOKTUR.
    // ---------------------------------------------------------------

    /// <summary>
    /// APPROVED -> LOCKED. Dönem kapatılırken o döneme ait onaylı kayıtlara uygulanır.
    /// APPROVED olmayan kayıt (taslak, bekleyen, reddedilmiş) kilitlenmez: kilit
    /// "bu tutar artık icmale girmiştir" demektir, henüz onaylanmamış kayıt için
    /// söylenemez.
    /// </summary>
    public static void LockForPeriodClose(WorkRecord record, Period period)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(period);
        EnsureTransitionAllowed(record, WorkRecordStatus.LOCKED);

        record.Status = WorkRecordStatus.LOCKED;
    }

    /// <summary>
    /// LOCKED -> APPROVED. Kapanışın geri alınmasıdır; sadece dönem yeniden
    /// açılırken uygulanır. Gerekçe ayrıca aranmaz çünkü dönemi yeniden açmanın
    /// gerekçesi zaten Periods.ReopenReason'a zorunlu olarak yazılmıştır
    /// (bkz. PeriodsController.Reopen).
    ///
    /// Dönemin GERÇEKTEN yeniden açılmış olduğunu burada doğruluyoruz: aksi halde
    /// metodun kendisi, tabloda kapalı olan LOCKED -> APPROVED geçişine arka
    /// kapı olurdu.
    /// </summary>
    public static void UnlockForPeriodReopen(WorkRecord record, Period period)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(period);

        if (record.Status != WorkRecordStatus.LOCKED)
        {
            throw new WorkRecordStateTransitionException(
                $"Yalnızca kilitli kayıtların kilidi açılabilir; {record.DocumentNo} numaralı kayıt " +
                $"\"{WorkRecordStatusLabels.Get(record.Status)}\" durumunda.");
        }

        if (period.Status != PeriodStatus.REOPENED)
        {
            throw new WorkRecordStateTransitionException(
                $"{PeriodLabel(period)} dönemi yeniden açılmadan kayıtların kilidi açılamaz.");
        }

        record.Status = WorkRecordStatus.APPROVED;
    }

    // ---------------------------------------------------------------
    // Ortak kontroller
    // ---------------------------------------------------------------

    private static void EnsureTransitionAllowed(WorkRecord record, WorkRecordStatus to)
    {
        var from = record.Status;
        if (IsAllowed(from, to))
        {
            return;
        }

        var fromLabel = WorkRecordStatusLabels.Get(from);
        var toLabel = WorkRecordStatusLabels.Get(to);

        // LOCKED yalnızca dönem kapanışıyla gelir; kullanıcıya "şuraya geçebilirsin"
        // diye önerilmez. Bu yüzden mesajı üretirken sistem geçişlerini eliyoruz —
        // aksi halde onaylı bir kaydı reddetmeye çalışan kullanıcı
        // "İzin verilen geçişler: Kilitli" gibi yanıltıcı bir metin görürdü.
        var allTargets = AllowedTransitions.TryGetValue(from, out var allowed) ? allowed : Terminal;
        var targets = allTargets.Where(t => !SystemOnlyTargets.Contains(t)).ToList();
        if (targets.Count == 0)
        {
            // LOCKED'da yönlendirme farklıdır: kayıt kapalı bir döneme aittir,
            // yeni versiyon da açılamaz. Kullanıcıya yapılabilecek TEK şeyi söylüyoruz.
            var guidance = from == WorkRecordStatus.LOCKED
                ? "Kayıt kapalı bir döneme aittir; düzeltme için önce dönemin yeniden açılması gerekir."
                : "Düzeltme gerekiyorsa yeni versiyon oluşturulmalıdır.";

            throw new WorkRecordStateTransitionException(
                $"\"{fromLabel}\" durumundaki bir kayıt nihaidir; \"{toLabel}\" dahil hiçbir duruma geçirilemez. " +
                guidance);
        }

        var allowedLabels = string.Join(", ", targets.Select(WorkRecordStatusLabels.Get));
        throw new WorkRecordStateTransitionException(
            $"\"{fromLabel}\" durumundan \"{toLabel}\" durumuna geçilemez. İzin verilen geçişler: {allowedLabels}.");
    }

    /// <summary>
    /// Gönderim rolü. Firma eşleşmesi TEK BAŞINA yetmez: FIRM_OPERATOR de kendi
    /// firmasının kaydının sahibidir ve yalnızca sahiplik aransaydı, işi yapan
    /// kişi mali talebi de zincire sokabilirdi — gerçekleşen süreyi teyit eden
    /// kimse kalmazdı (ADR-028). Policy controller'ın kapısını tutar, bu kontrol
    /// kaydın kendisini: hangi yoldan gelinirse gelinsin geçiş düşer.
    ///
    /// FIRM_USER, RequestStateMachine.EnsureFirmManager'da olduğu gibi
    /// FIRM_MANAGER'a eşdeğer geçiş rolüdür.
    /// </summary>
    private static void EnsureSubmitterRole(TransitionActor actor)
    {
        if (!actor.IsInRole(RoleCodes.FirmManager) && !actor.IsInRole(RoleCodes.FirmUser))
        {
            throw new ApprovalAuthorizationException(
                "Çalışma kaydını yalnızca firma yetkilisi onaya gönderebilir; " +
                "operatör kaydı görür ama gönderemez.");
        }
    }

    private static void EnsureRecordOwner(WorkRecord record, TransitionActor actor, string verb)
    {
        if (actor.FirmId is null)
        {
            throw new ApprovalAuthorizationException(
                $"Bu işlemi ({verb}) yalnızca kaydın sahibi firma kullanıcısı yapabilir; MIP personeli alt yüklenici adına işlem yapamaz.");
        }

        if (actor.FirmId != record.FirmId)
        {
            throw new ApprovalAuthorizationException("Başka bir firmanın kaydı üzerinde işlem yapılamaz.");
        }
    }

    private static void EnsureApprover(WorkRecord record, TransitionActor actor, string requiredRoleCode, string requiredRoleName)
    {
        // CLAUDE.md kural 7 + görev tanımı: alt yüklenici kendi kaydını onaylayamaz.
        if (!actor.IsMipStaff)
        {
            throw new ApprovalAuthorizationException(
                "Onay işlemleri yalnızca MIP personeli tarafından yapılabilir; alt yüklenici kendi kaydını onaylayamaz.");
        }

        // Sadece politika (CanApprove) yetmez: kullanıcı GERÇEKTEN o adımın rolünde olmalı.
        if (!actor.IsInRole(requiredRoleCode))
        {
            throw new ApprovalAuthorizationException(
                $"Bu onay adımı \"{requiredRoleName}\" rolündedir; bu rolde olmadığınız için işlem yapamazsınız.");
        }
    }

    private static void EnsurePeriodOpen(Period period, string verb)
    {
        if (period.Status == PeriodStatus.CLOSED)
        {
            throw new WorkRecordStateTransitionException(
                $"{PeriodLabel(period)} dönemi kapalıdır; bu dönemdeki kayıt {verb}.");
        }
    }

    private static void EnsureReasonGiven(string? reason, string message)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new WorkRecordStateTransitionException(message);
        }
    }

    private static string PeriodLabel(Period period) =>
        $"{System.Globalization.CultureInfo.GetCultureInfo("tr-TR").DateTimeFormat.GetMonthName(period.Month)} {period.Year}";

    private static IReadOnlySet<WorkRecordStatus> Set(params WorkRecordStatus[] statuses) => new HashSet<WorkRecordStatus>(statuses);
}
