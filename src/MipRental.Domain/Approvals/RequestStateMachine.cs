using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;
using MipRental.Domain.Security;

namespace MipRental.Domain.Approvals;

/// <summary>
/// Talep durum geçişlerinin TEK KAYNAĞI. WorkRecordStateMachine ile aynı desen,
/// ayrı sınıf: talep ve çalışma kaydı ayrı yaşam döngüleridir (ADR-011) ve tek
/// bir makinede birleştirmek iki akışı birbirine kilitlerdi.
///
/// Kural: Request.Status'a hiçbir controller/servis DOĞRUDAN atama yapmaz.
/// Her geçiş buradaki bir metottan geçer ve o metot üç şeyi birden kontrol eder:
///   1. Geçiş izinli mi   (AllowedTransitions tablosu)
///   2. Kullanıcının rolü uygun mu
///   3. Dönem açık mı     (CLAUDE.md kural 4)
/// İhlal halinde Türkçe mesajlı exception fırlar.
///
/// Sınıf SAFTIR: veritabanı erişimi, DateTime.UtcNow ya da oturum bilgisi YOK.
/// Zaman damgası için gereken "şu an" değeri parametre gelir (nowUtc) — talep
/// akışında gerçekleşen saatler SUNUCU saatiyle damgalanır, istemciden gelen
/// saate güvenilmez; ama saati OKUMAK çağıranın işidir, makinenin değil.
/// Bu sayede tüm geçiş matrisi veritabanısız test edilebilir.
///
/// Dönem: Request'te PeriodId YOKTUR (talebin dönemi RequestedDate'ten türer);
/// bu yüzden dönem, çağıran tarafından çözülüp parametre olarak verilir.
/// </summary>
public static class RequestStateMachine
{
    private static readonly IReadOnlySet<RequestStatus> Terminal = new HashSet<RequestStatus>();

    /// <summary>
    /// İzin verilen geçişler. Burada olmayan HİÇBİR geçiş yapılamaz.
    /// CONFIRMED, REJECTED_BY_EQUIPMENT, REJECTED_BY_FIRM ve CANCELLED
    /// terminaldir: hiçbir yere gidilmez.
    ///
    /// ADIM 16 — COMPLETED ARTIK TERMİNAL DEĞİL. Operatörün "bitirdim" demesi
    /// işin bittiğini söyler, GERÇEKLEŞEN SÜRENİN doğru olduğunu söylemez.
    /// Süreyi teyit eden kişi bugün kâğıt fişi imzalayan kişidir: talebi açan.
    /// Teyit gelmeden çalışma kaydı türetilmez — teyit edilmemiş süreden kayıt
    /// üretip sonra itiraz gelmesi, kural 1 gereği revizyon demektir; önce
    /// doğruyu sabitlemek ucuzdur.
    /// </summary>
    public static readonly IReadOnlyDictionary<RequestStatus, IReadOnlySet<RequestStatus>> AllowedTransitions =
        new Dictionary<RequestStatus, IReadOnlySet<RequestStatus>>
        {
            [RequestStatus.DRAFT] = Set(RequestStatus.SUBMITTED, RequestStatus.CANCELLED),
            [RequestStatus.SUBMITTED] = Set(RequestStatus.PENDING_EQUIPMENT),
            [RequestStatus.PENDING_EQUIPMENT] = Set(RequestStatus.PENDING_FIRM, RequestStatus.REJECTED_BY_EQUIPMENT),
            [RequestStatus.PENDING_FIRM] = Set(RequestStatus.SCHEDULED, RequestStatus.REJECTED_BY_FIRM),

            // SCHEDULED -> CANCELLED: iş planlandı ama yapılmadan iptal edildi
            // (hava, liman durumu, talebin düşmesi). Başlamış işte iptal YOK.
            [RequestStatus.SCHEDULED] = Set(RequestStatus.IN_PROGRESS, RequestStatus.CANCELLED),
            [RequestStatus.IN_PROGRESS] = Set(RequestStatus.COMPLETED),

            // Talep açanın süre kararı: teyit ya da itiraz.
            [RequestStatus.COMPLETED] = Set(RequestStatus.CONFIRMED, RequestStatus.DISPUTED),

            // İtirazın hakemi Ekipman Müdürlüğü'dür: saatleri düzeltip onaylar
            // ya da "faturalanmayacak" deyip iptal eder. Üçüncü bir çıkış yok —
            // itiraz askıda kalamaz.
            [RequestStatus.DISPUTED] = Set(RequestStatus.CONFIRMED, RequestStatus.CANCELLED),

            [RequestStatus.CONFIRMED] = Terminal,
            [RequestStatus.REJECTED_BY_EQUIPMENT] = Terminal,
            [RequestStatus.REJECTED_BY_FIRM] = Terminal,
            [RequestStatus.CANCELLED] = Terminal
        };

    public static bool IsAllowed(RequestStatus from, RequestStatus to) =>
        AllowedTransitions.TryGetValue(from, out var targets) && targets.Contains(to);

    // ---------------------------------------------------------------
    // Geçiş metotları. Her biri: izin + rol + dönem kontrolü yapar,
    // sonra Status'u ve karar noktasının zaman damgasını yazar.
    // ---------------------------------------------------------------

    /// <summary>DRAFT -> SUBMITTED. Sadece talebi AÇAN kişi.</summary>
    public static void Submit(Request request, Period period, TransitionActor actor, DateTime nowUtc)
    {
        EnsureTransitionAllowed(request, RequestStatus.SUBMITTED);
        EnsureRequester(request, actor, "gönderebilir");
        EnsurePeriodOpen(period, "gönderilemez");

        request.Status = RequestStatus.SUBMITTED;
        request.SubmittedAt = nowUtc;
    }

    /// <summary>
    /// SUBMITTED -> PENDING_EQUIPMENT. Gönderimin hemen ardından ilk onay adımı
    /// açılınca uygulanır; talebi açan kişi adına yapılır (kendi başına bir
    /// karar değil, gönderimin devamıdır — bu yüzden ayrı zaman damgası yok).
    /// </summary>
    public static void SendToEquipment(Request request, Period period, TransitionActor actor)
    {
        EnsureTransitionAllowed(request, RequestStatus.PENDING_EQUIPMENT);
        EnsureRequester(request, actor, "ekipman onayına gönderebilir");
        EnsurePeriodOpen(period, "ekipman onayına gönderilemez");

        request.Status = RequestStatus.PENDING_EQUIPMENT;
    }

    /// <summary>PENDING_EQUIPMENT -> PENDING_FIRM. Ekipman Müdürlüğü Yöneticisi.</summary>
    public static void ApproveByEquipment(Request request, Period period, TransitionActor actor, DateTime nowUtc)
    {
        EnsureTransitionAllowed(request, RequestStatus.PENDING_FIRM);
        EnsureEquipmentManager(actor, "onaylayabilir");
        EnsurePeriodOpen(period, "onaylanamaz");

        request.Status = RequestStatus.PENDING_FIRM;
        request.EquipmentDecisionAt = nowUtc;
    }

    /// <summary>PENDING_EQUIPMENT -> REJECTED_BY_EQUIPMENT. Gerekçe ZORUNLU.</summary>
    public static void RejectByEquipment(Request request, Period period, TransitionActor actor, string? reason, DateTime nowUtc)
    {
        EnsureTransitionAllowed(request, RequestStatus.REJECTED_BY_EQUIPMENT);
        EnsureEquipmentManager(actor, "reddedebilir");
        EnsurePeriodOpen(period, "reddedilemez");
        EnsureReasonGiven(reason, "Red gerekçesi zorunludur; boş bırakılamaz.");

        request.Status = RequestStatus.REJECTED_BY_EQUIPMENT;
        request.RejectionReason = reason;
        request.EquipmentDecisionAt = nowUtc;
    }

    /// <summary>
    /// PENDING_FIRM -> SCHEDULED. Firma Yetkilisi kabul eder ve AYNI ANDA
    /// operatör ile plakayı atar.
    ///
    /// Operatör adı ve plaka boş bırakılamaz: bir sonraki adım "operatör işe
    /// başladı"dır, atanmamış operatörle planlanmış bir iş yarım kalır ve saha
    /// tarafında kimin geleceği bilinmez.
    /// </summary>
    public static void AcceptByFirm(
        Request request, Period period, TransitionActor actor,
        string? operatorName, string? licensePlate, DateTime nowUtc)
    {
        EnsureTransitionAllowed(request, RequestStatus.SCHEDULED);
        EnsureFirmManager(request, actor, "kabul edebilir");
        EnsurePeriodOpen(period, "kabul edilemez");
        EnsureAssignmentGiven(operatorName, licensePlate);

        request.Status = RequestStatus.SCHEDULED;
        request.AssignedOperatorName = operatorName;
        request.AssignedLicensePlate = licensePlate;
        request.FirmDecisionAt = nowUtc;
    }

    /// <summary>
    /// SCHEDULED'da operatör/plaka değişikliği. DURUM DEĞİŞMEZ — bu bir geçiş
    /// değil, planlanmış işin sahadaki karşılığının güncellenmesidir: araç
    /// arızalanır, operatör izne çıkar, iş yine aynı iştir.
    ///
    /// Bu yüzden AllowedTransitions'a SCHEDULED -> SCHEDULED diye bir satır
    /// EKLENMEDİ; öyle bir satır "durum kendine geçebilir" anlamına gelir ve
    /// makinenin bütün geçiş matrisini bulanıklaştırırdı.
    ///
    /// Gerekçe istenmez: değişiklik zaten alan bazlı denetim izine düşer
    /// (AuditSaveChangesInterceptor eski/yeni değeri kimin ne zaman yazdığıyla
    /// birlikte kaydeder). Boş operatör/plaka yine kabul edilmez — kabulde
    /// zorunlu olan bir alan, sonradan boşaltılarak da kaybedilememeli.
    /// </summary>
    public static void UpdateAssignment(
        Request request, Period period, TransitionActor actor, string? operatorName, string? licensePlate)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Status != RequestStatus.SCHEDULED)
        {
            throw new RequestStateTransitionException(
                $"Operatör ve plaka yalnızca \"{RequestStatusLabels.Get(RequestStatus.SCHEDULED)}\" durumundaki talepte " +
                $"değiştirilebilir; bu talep \"{RequestStatusLabels.Get(request.Status)}\" durumunda.");
        }

        EnsureFirmManager(request, actor, "güncelleyebilir");
        EnsurePeriodOpen(period, "güncellenemez");
        EnsureAssignmentGiven(operatorName, licensePlate);

        request.AssignedOperatorName = operatorName;
        request.AssignedLicensePlate = licensePlate;
    }

    /// <summary>PENDING_FIRM -> REJECTED_BY_FIRM. Gerekçe ZORUNLU.</summary>
    public static void RejectByFirm(Request request, Period period, TransitionActor actor, string? reason, DateTime nowUtc)
    {
        EnsureTransitionAllowed(request, RequestStatus.REJECTED_BY_FIRM);
        EnsureFirmManager(request, actor, "reddedebilir");
        EnsurePeriodOpen(period, "reddedilemez");
        EnsureReasonGiven(reason, "Red gerekçesi zorunludur; boş bırakılamaz.");

        request.Status = RequestStatus.REJECTED_BY_FIRM;
        request.RejectionReason = reason;
        request.FirmDecisionAt = nowUtc;
    }

    /// <summary>
    /// SCHEDULED -> IN_PROGRESS. Firma Operatörü "başladım" der.
    /// Başlangıç saati SUNUCU saatiyle damgalanır.
    /// </summary>
    public static void Start(Request request, Period period, TransitionActor actor, DateTime nowUtc)
    {
        EnsureTransitionAllowed(request, RequestStatus.IN_PROGRESS);
        EnsureFirmOperator(request, actor, "başlatabilir");
        EnsurePeriodOpen(period, "başlatılamaz");

        request.Status = RequestStatus.IN_PROGRESS;
        request.ActualStartTime = nowUtc;
    }

    /// <summary>
    /// IN_PROGRESS -> COMPLETED. Firma Operatörü "bitirdim" der.
    /// Bitiş saati SUNUCU saatiyle damgalanır.
    /// </summary>
    public static void Complete(Request request, Period period, TransitionActor actor, DateTime nowUtc)
    {
        EnsureTransitionAllowed(request, RequestStatus.COMPLETED);
        EnsureFirmOperator(request, actor, "bitirebilir");
        EnsurePeriodOpen(period, "bitirilemez");

        request.Status = RequestStatus.COMPLETED;
        request.ActualEndTime = nowUtc;

        // Kimin bitirdiği KİMLİK olarak saklanır: türeyen çalışma kaydının
        // "kaydı giren" alanı buradan gelir (Adım 16).
        request.CompletedByUserId = actor.UserId;
    }

    // ---------------------------------------------------------------
    // ADIM 16 — süre teyidi
    // ---------------------------------------------------------------

    /// <summary>
    /// COMPLETED -> CONFIRMED. Talebi AÇAN kişi gerçekleşen süreyi teyit eder.
    ///
    /// Rol değil KİMLİK kontrolü: teyidi sahada işi yaptıran kişi verir,
    /// "REQUESTER rolündeki herhangi biri" değil. Otomatik teyit YOKTUR
    /// (CLAUDE.md kural 5) — teyit gelmezse süreç ilerlemez; hatırlatma ve
    /// eskalasyon devreye girer.
    /// </summary>
    public static void ConfirmDuration(Request request, Period period, TransitionActor actor, DateTime nowUtc)
    {
        EnsureTransitionAllowed(request, RequestStatus.CONFIRMED);
        EnsureRequester(request, actor, "teyit edebilir");
        EnsurePeriodOpen(period, "teyit edilemez");

        request.Status = RequestStatus.CONFIRMED;
        request.ConfirmationDecisionAt = nowUtc;
    }

    /// <summary>COMPLETED -> DISPUTED. Talebi açan süreye itiraz eder; gerekçe ZORUNLU.</summary>
    public static void DisputeDuration(
        Request request, Period period, TransitionActor actor, string? reason, DateTime nowUtc)
    {
        EnsureTransitionAllowed(request, RequestStatus.DISPUTED);
        EnsureRequester(request, actor, "itiraz edebilir");
        EnsurePeriodOpen(period, "itiraz edilemez");
        EnsureReasonGiven(reason, "İtiraz gerekçesi zorunludur; boş bırakılamaz.");

        request.Status = RequestStatus.DISPUTED;
        request.DisputeReason = reason;
        request.ConfirmationDecisionAt = nowUtc;
    }

    /// <summary>
    /// DISPUTED -> CONFIRMED. Ekipman Müdürlüğü hakemlik eder.
    ///
    /// Saatler AYNI GEÇİŞTE düzeltilir, ayrı bir "saat güncelle" adımı yoktur:
    /// düzeltme kararın kendisidir; karardan bağımsız yapılabilseydi düzeltilmiş
    /// ama karara bağlanmamış bir talep ortada kalırdı. Boş bırakılan saat
    /// DEĞİŞMEZ — hakem yalnızca yanlış olanı düzeltir.
    ///
    /// Düzeltme gerekçe istemez: alan bazlı denetim izi eski/yeni değeri kimin
    /// yazdığıyla birlikte zaten kaydeder (AuditSaveChangesInterceptor).
    /// </summary>
    public static void ResolveDispute(
        Request request, Period period, TransitionActor actor,
        DateTime? correctedStartUtc, DateTime? correctedEndUtc, DateTime nowUtc)
    {
        EnsureTransitionAllowed(request, RequestStatus.CONFIRMED);
        EnsureEquipmentManager(actor, "itirazını karara bağlayabilir");
        EnsurePeriodOpen(period, "karara bağlanamaz");

        var start = correctedStartUtc ?? request.ActualStartTime;
        var end = correctedEndUtc ?? request.ActualEndTime;

        if (start is null || end is null)
        {
            throw new RequestStateTransitionException(
                "Gerçekleşen başlangıç ve bitiş saati olmadan itiraz karara bağlanamaz.");
        }

        if (end <= start)
        {
            throw new RequestStateTransitionException(
                "Bitiş saati başlangıç saatinden sonra olmalıdır.");
        }

        request.ActualStartTime = start;
        request.ActualEndTime = end;
        request.Status = RequestStatus.CONFIRMED;
        request.DisputeResolvedAt = nowUtc;
    }

    /// <summary>
    /// DISPUTED -> CANCELLED. Ekipman Müdürlüğü "bu iş faturalanmayacak" der.
    /// Gerekçe ZORUNLU. Bu geçişten çalışma kaydı TÜRETİLMEZ.
    ///
    /// <see cref="Cancel"/>'dan ayrı metot: oradaki yetki "talebi açan VEYA
    /// Ekipman Müdürlüğü"dür ve DISPUTED'ı oraya eklemek, itiraz eden kişiye
    /// kendi itirazını faturalanmaz ilan etme yetkisi verirdi. Hakem karar verir.
    /// </summary>
    public static void CancelDisputed(
        Request request, Period period, TransitionActor actor, string? reason, DateTime nowUtc)
    {
        EnsureTransitionAllowed(request, RequestStatus.CANCELLED);
        EnsureEquipmentManager(actor, "faturalanmayacak olarak kapatabilir");
        EnsurePeriodOpen(period, "kapatılamaz");
        EnsureReasonGiven(reason, "İptal gerekçesi zorunludur; boş bırakılamaz.");

        request.Status = RequestStatus.CANCELLED;
        request.CancellationReason = reason;
        request.CancelledAt = nowUtc;
        request.DisputeResolvedAt = nowUtc;
    }

    /// <summary>
    /// DRAFT / SCHEDULED -> CANCELLED. Gerekçe ZORUNLU.
    /// Talebi açan kişi VEYA Ekipman Müdürlüğü Yöneticisi iptal edebilir.
    /// </summary>
    public static void Cancel(Request request, Period period, TransitionActor actor, string? reason, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(request);

        // DISPUTED -> CANCELLED buraya DÜŞMEZ: o karar hakemindir
        // (CancelDisputed). Aksi halde itiraz eden kişi kendi itirazını
        // "faturalanmayacak" ilan edebilirdi.
        if (request.Status == RequestStatus.DISPUTED)
        {
            throw new ApprovalAuthorizationException(
                "İtiraz edilmiş bir talebi yalnızca Ekipman Müdürlüğü Yöneticisi " +
                "\"faturalanmayacak\" olarak kapatabilir.");
        }

        EnsureTransitionAllowed(request, RequestStatus.CANCELLED);
        EnsureRequesterOrEquipmentManager(request, actor);
        EnsurePeriodOpen(period, "iptal edilemez");
        EnsureReasonGiven(reason, "İptal gerekçesi zorunludur; boş bırakılamaz.");

        request.Status = RequestStatus.CANCELLED;
        request.CancellationReason = reason;
        request.CancelledAt = nowUtc;
    }

    // ---------------------------------------------------------------
    // Ortak kontroller
    // ---------------------------------------------------------------

    private static void EnsureTransitionAllowed(Request request, RequestStatus to)
    {
        ArgumentNullException.ThrowIfNull(request);

        var from = request.Status;
        if (IsAllowed(from, to))
        {
            return;
        }

        var fromLabel = RequestStatusLabels.Get(from);
        var toLabel = RequestStatusLabels.Get(to);

        var targets = AllowedTransitions.TryGetValue(from, out var allowed) ? allowed : Terminal;
        if (targets.Count == 0)
        {
            throw new RequestStateTransitionException(
                $"\"{fromLabel}\" durumundaki bir talep nihaidir; \"{toLabel}\" dahil hiçbir duruma geçirilemez. " +
                "Yeni bir ihtiyaç varsa yeni talep açılmalıdır.");
        }

        var allowedLabels = string.Join(", ", targets.Select(RequestStatusLabels.Get));
        throw new RequestStateTransitionException(
            $"\"{fromLabel}\" durumundan \"{toLabel}\" durumuna geçilemez. İzin verilen geçişler: {allowedLabels}.");
    }

    /// <summary>Talebi AÇAN kişi. Rol değil KİMLİK kontrolü: başkasının talebi gönderilemez.</summary>
    private static void EnsureRequester(Request request, TransitionActor actor, string verb)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (actor.UserId != request.RequestedByUserId)
        {
            throw new ApprovalAuthorizationException(
                $"Bu işlemi ({verb}) yalnızca talebi açan kişi yapabilir.");
        }
    }

    private static void EnsureEquipmentManager(TransitionActor actor, string verb)
    {
        ArgumentNullException.ThrowIfNull(actor);

        // CLAUDE.md kural 7: alt yüklenici MIP adına karar veremez.
        if (!actor.IsMipStaff)
        {
            throw new ApprovalAuthorizationException(
                $"Talebi yalnızca MIP personeli {verb}; alt yüklenici bu adımda karar veremez.");
        }

        // EQUIPMENT_VIEWER bilinçli olarak DIŞARIDA: salt okur, karar vermez.
        if (!actor.IsInRole(RoleCodes.EquipmentManager))
        {
            throw new ApprovalAuthorizationException(
                $"Bu adım \"Ekipman Müdürlüğü Yöneticisi\" rolündedir; bu rolde olmadığınız için talebi {verb} değilsiniz.");
        }
    }

    private static void EnsureFirmManager(Request request, TransitionActor actor, string verb)
    {
        EnsureRequestFirm(request, actor, verb);

        // FIRM_USER geçiş rolüdür: Adım 10 öncesi tüm firma kullanıcıları bu
        // roldeydi ve rol dağıtımı tamamlanana kadar FIRM_MANAGER'a eşdeğerdir.
        if (!actor.IsInRole(RoleCodes.FirmManager) && !actor.IsInRole(RoleCodes.FirmUser))
        {
            throw new ApprovalAuthorizationException(
                $"Bu adım \"Firma Yetkilisi\" rolündedir; bu rolde olmadığınız için talebi {verb} değilsiniz.");
        }
    }

    private static void EnsureFirmOperator(Request request, TransitionActor actor, string verb)
    {
        EnsureRequestFirm(request, actor, verb);

        if (!actor.IsInRole(RoleCodes.FirmOperator))
        {
            throw new ApprovalAuthorizationException(
                $"Bu adım \"Firma Operatörü\" rolündedir; bu rolde olmadığınız için işi {verb} değilsiniz.");
        }
    }

    /// <summary>
    /// Firma tarafındaki adımlar: aktör, talebin atandığı firmanın kullanıcısı olmalı.
    /// CLAUDE.md kural 7'nin durum makinesi tarafındaki karşılığı — query filter
    /// kaydı gizler, bu kontrol kayıt eline geçse bile işlem yapılmasını engeller.
    /// </summary>
    private static void EnsureRequestFirm(Request request, TransitionActor actor, string verb)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);

        if (actor.FirmId is null)
        {
            throw new ApprovalAuthorizationException(
                $"Bu işlemi yalnızca talebin atandığı firmanın kullanıcısı yapabilir; MIP personeli alt yüklenici adına talebi {verb} değil.");
        }

        if (request.FirmId is null)
        {
            throw new ApprovalAuthorizationException(
                "Talebe henüz firma atanmamış; firma tarafındaki adımlar işletilemez.");
        }

        if (actor.FirmId != request.FirmId)
        {
            throw new ApprovalAuthorizationException("Başka bir firmanın talebi üzerinde işlem yapılamaz.");
        }
    }

    private static void EnsureRequesterOrEquipmentManager(Request request, TransitionActor actor)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);

        if (actor.UserId == request.RequestedByUserId)
        {
            return;
        }

        if (actor.IsMipStaff && actor.IsInRole(RoleCodes.EquipmentManager))
        {
            return;
        }

        throw new ApprovalAuthorizationException(
            "Talebi yalnızca talebi açan kişi veya Ekipman Müdürlüğü Yöneticisi iptal edebilir.");
    }

    private static void EnsurePeriodOpen(Period period, string verb)
    {
        ArgumentNullException.ThrowIfNull(period);

        if (period.Status == PeriodStatus.CLOSED)
        {
            throw new RequestStateTransitionException(
                $"{PeriodLabel(period)} dönemi kapalıdır; bu dönemdeki talep {verb}.");
        }
    }

    /// <summary>
    /// Operatör adı ve plaka boş bırakılamaz: bir sonraki adım "operatör işe
    /// başladı"dır, atanmamış operatörle planlanmış bir iş yarım kalır ve saha
    /// tarafında kimin geleceği bilinmez.
    /// </summary>
    private static void EnsureAssignmentGiven(string? operatorName, string? licensePlate)
    {
        if (string.IsNullOrWhiteSpace(operatorName))
        {
            throw new RequestStateTransitionException("Operatör adı zorunludur; boş bırakılamaz.");
        }

        if (string.IsNullOrWhiteSpace(licensePlate))
        {
            throw new RequestStateTransitionException("Araç plakası zorunludur; boş bırakılamaz.");
        }
    }

    private static void EnsureReasonGiven(string? reason, string message)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new RequestStateTransitionException(message);
        }
    }

    private static string PeriodLabel(Period period) =>
        $"{System.Globalization.CultureInfo.GetCultureInfo("tr-TR").DateTimeFormat.GetMonthName(period.Month)} {period.Year}";

    private static IReadOnlySet<RequestStatus> Set(params RequestStatus[] statuses) => new HashSet<RequestStatus>(statuses);
}
