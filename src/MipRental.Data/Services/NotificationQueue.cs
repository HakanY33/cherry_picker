using Microsoft.EntityFrameworkCore;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Security;

namespace MipRental.Data.Services;

/// <summary>
/// Bildirimleri SADECE Notifications tablosuna yazar. GERÇEK E-POSTA GÖNDERMEZ:
/// SMTP yapılandırması yok, arka plan zamanlayıcı yok — kayıtlar QUEUED durumunda
/// bekler, gönderim sonraki adımın işi.
///
/// Kayıtlar çağıranın SaveChanges'ine dahil edilir (ayrı SaveChanges YOK) ki
/// bildirim ile durum değişikliği aynı transaction'da atomik olsun: onay
/// commit olmadıysa bildirim de düşmez.
/// </summary>
public sealed class NotificationQueue
{
    private readonly AppDbContext _db;

    public NotificationQueue(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Konu satırının TEK biçimi: "[CPR-2026-00001] Talebiniz onaylandı".
    ///
    /// Belge numarası ÖNDE ve köşeli parantez içinde. Mail istemcisi konuyu
    /// kısalttığında bile hangi belge olduğu okunur; gelen kutusunda belge
    /// numarasıyla arayan kişi de bulur. Konu üretilen her yer buradan geçer ki
    /// biçim ekranlara ve şablonlara göre ayrışmasın.
    /// </summary>
    public static string Subject(string documentNo, string text) => $"[{documentNo}] {text}";

    public static class Templates
    {
        public const string ApprovalPending = "WR_APPROVAL_PENDING";
        public const string Approved = "WR_APPROVED";
        public const string Rejected = "WR_REJECTED";
        public const string RevisionRequested = "WR_REVISION_REQUESTED";
        public const string LineObjected = "WR_LINE_OBJECTED";

        // Adım 11 — talep akışı. WR_ ile karışmasın diye ayrı önek: aynı
        // kuyrukta iki farklı belge tipinin bildirimi durur.
        public const string RequestSubmitted = "REQ_SUBMITTED";
        public const string RequestEquipmentApproved = "REQ_EQUIPMENT_APPROVED";
        public const string RequestEquipmentRejected = "REQ_EQUIPMENT_REJECTED";
        public const string RequestEquipmentEdited = "REQ_EQUIPMENT_EDITED";
        public const string RequestFirmAccepted = "REQ_FIRM_ACCEPTED";
        public const string RequestFirmRejected = "REQ_FIRM_REJECTED";
        public const string RequestCancelled = "REQ_CANCELLED";
        public const string RequestAssignmentChanged = "REQ_ASSIGNMENT_CHANGED";

        // Adım 12 — türetme. İlki firma yetkilisine (gönderim bekliyor), ikincisi
        // Ekipman Müdürlüğü'ne (türetme yapılamadı; sebebi çözecek taraf onlar).
        public const string WorkRecordDerived = "WR_DERIVED_PENDING_SUBMIT";
        public const string RequestDerivationFailed = "REQ_DERIVE_FAILED";

        // Adım 16 Bölüm B — envanterde bildirimsiz kalan geçişler.
        public const string RevisionDrafted = "WR_REVISION_DRAFTED";
        public const string DraftCancelled = "WR_DRAFT_CANCELLED";
        public const string PeriodLocked = "WR_PERIOD_LOCKED";
        public const string PeriodReopened = "WR_PERIOD_REOPENED";
        public const string ProgressPaymentApproved = "PP_APPROVED";
        public const string ProgressPaymentRejected = "PP_REJECTED";
        public const string ProgressPaymentWithdrawn = "PP_WITHDRAWN";

        // Adım 16 — süre teyidi. Teyit MAİLDEN VERİLMEZ (magic link yayılmaz,
        // ADR-030): mail yalnızca haber verir, karar uygulamada verilir.
        public const string RequestStarted = "REQ_STARTED";
        public const string RequestConfirmPending = "REQ_CONFIRM_PENDING";
        public const string RequestConfirmed = "REQ_CONFIRMED";
        public const string RequestDisputed = "REQ_DISPUTED";
        public const string RequestDisputeResolved = "REQ_DISPUTE_RESOLVED";
        public const string RequestDisputeCancelled = "REQ_DISPUTE_CANCELLED";

        // Adım 14 — hakedişin Bütçe Yöneticisi'ne mail onayı (ADR-015).
        public const string ProgressPaymentApproval = "PP_APPROVAL_LINK";
    }

    /// <summary>
    /// Hakediş onay bağlantısını TEK BİR Bütçe Yöneticisi'ne kuyruğa yazar.
    ///
    /// HAM TOKEN YALNIZCA BURADA görünür: bağlantının içinde, mail gövdesinde.
    /// Veritabanında token'ın SHA-256 hash'i durur; bu satırdan geri üretilemez.
    /// Gerçek mail GÖNDERİLMEZ (Adım 15) — satır QUEUED bekler.
    ///
    /// Çağıranın SaveChanges'ine dahil edilir: hakediş yöneticiye geçmediyse
    /// bağlantı da düşmez.
    /// </summary>
    public void QueueProgressPaymentApproval(
        ProgressPayment payment, int userId, string? email, string periodName, string firmTitle, string approvalUrl)
    {
        ArgumentNullException.ThrowIfNull(payment);

        var note = string.IsNullOrWhiteSpace(payment.BudgetNote)
            ? string.Empty
            : $"Bütçe notu: {payment.BudgetNote}";

        var body =
            $"""
            {periodName} dönemi hakedişi onayınızı bekliyor.

            Firma: {firmTitle}
            Kayıt sayısı: {payment.RecordCount}
            Toplam tutar: {payment.TotalAmount:N2} {payment.Currency}
            {note}
            Özeti görmek ve karar vermek için: {approvalUrl}

            Bağlantı 7 gün geçerlidir ve tek kullanımlıktır. Bağlantıya girmek
            onay VERMEZ; özet sayfasındaki butonla karar verirsiniz.
            """;

        _db.Notifications.Add(new Notification
        {
            UserId = userId,
            Email = email,
            Channel = NotificationChannel.EMAIL,
            TemplateCode = Templates.ProgressPaymentApproval,
            Subject = Subject(ProgressPaymentReference(payment), $"Hakediş onayınızı bekliyor — {periodName}, {firmTitle}"),
            Body = body,
            // DocumentType hakediş için ayrı bir değer taşımaz; bildirim satırı
            // belge tipiyle değil, hakedişin kendi id'siyle izlenir (B9 ile aynı
            // yön: mail onayı tek bir yere kısıtlı bir istisnadır).
            DocumentType = null,
            DocumentId = payment.ProgressPaymentId,
            Status = NotificationStatus.QUEUED,
            CreatedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Talepten çalışma kaydı türedi: firmanın YETKİLİLERİNE "gönderim bekliyor".
    ///
    /// Alıcıdan FIRM_OPERATOR HARİÇTİR. Gönderim operatörün işi değil (ADR-028);
    /// ona kaydın mali tarafı hiç yansımaz — "işi bitirdim" der, gerisi firma
    /// yetkilisinin işidir.
    ///
    /// Bildirim TALEBİ işaret eder, taslağı değil: kayıt henüz INSERT edilmediği
    /// için WorkRecordId yoktur. Aynı SaveChanges'e girmesi — kayıt oluşmadıysa
    /// bildirim de düşmesin — bu bağın önüne geçiyor; taslağın numarası zaten
    /// geçicidir, yetkili Çalışma Kayıtları listesinden ulaşır.
    ///
    /// Eklenen satırlar DÖNER: türetme yarışı kaybederse çağıran bunları da
    /// change tracker'dan düşürür, yoksa kaydı oluşmayan bir bildirim kalırdı.
    /// </summary>
    public async Task<IReadOnlyList<Notification>> QueueWorkRecordDerivedAsync(
        Request request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Firma izolasyon filtresi alıcı bulmak için bilinçli olarak bypass edilir
        // (aynı gerekçe: QueueRequestEventAsync). Koşullar burada açıkça yazılı.
        var recipients = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.IsActive
                && u.FirmId != null && u.FirmId == request.FirmId
                && u.UserRoles.Any(ur => ur.Role.Code == RoleCodes.FirmManager
                                      || ur.Role.Code == RoleCodes.FirmUser))
            .Select(u => new { u.UserId, u.Email })
            .ToListAsync(cancellationToken);

        var subject = Subject(request.DocumentNo, "Çalışma kaydı oluştu, gönderim bekliyor");
        var body =
            $"{request.DocumentNo} talebinden çalışma kaydı oluştu, gönderim bekliyor. " +
            "Kayıt Çalışma Kayıtları ekranında taslak olarak duruyor; eksik alanları " +
            "tamamlayıp gönderdikten sonra onay zincirine girer.";

        var queued = new List<Notification>(recipients.Count);
        foreach (var recipient in recipients)
        {
            var notification = new Notification
            {
                UserId = recipient.UserId,
                Email = recipient.Email,
                Channel = NotificationChannel.EMAIL,
                TemplateCode = Templates.WorkRecordDerived,
                Subject = subject,
                Body = body,
                DocumentType = DocumentType.REQUEST,
                DocumentId = request.RequestId,
                Status = NotificationStatus.QUEUED,
                CreatedAt = DateTime.UtcNow
            };

            _db.Notifications.Add(notification);
            queued.Add(notification);
        }

        return queued;
    }

    /// <summary>
    /// Talep akışındaki bir olayı ilgili taraflara kuyruğa yazar (Adım 11).
    ///
    /// Alıcılar ROL/İLİŞKİ ile bulunur, kullanıcı elle seçilmez:
    ///   toRequester — talebi açan kişi,
    ///   toEquipment — EQUIPMENT_MANAGER rolündeki tüm aktif MIP personeli,
    ///   toFirm      — talebin atandığı firmanın aktif kullanıcıları.
    /// Rolde/firmada kimse yoksa o taraf sessizce atlanır; bildirim düşmemesi
    /// akışı durdurmaz (CLAUDE.md kural 5: otomatik onay yok, otomatik ilerleme
    /// de yok — kayıt yerinde bekler).
    ///
    /// GERÇEK MAİL GÖNDERİLMEZ; kayıtlar QUEUED durumunda bekler.
    /// Çağıranın SaveChanges'ine dahil edilir: durum değişmediyse bildirim de düşmez.
    /// </summary>
    public async Task<int> QueueRequestEventAsync(
        Request request, string template, string subject, string body,
        bool toRequester = false, bool toEquipment = false, bool toFirm = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Firma izolasyon filtresi (kural 7) burada bilinçli olarak bypass edilir:
        // bildirim alıcısını bulmak için kimin hangi firmada olduğunu bilmek
        // gerekiyor. Sızan tek şey UserId/Email; koşullar aşağıda açıkça yazılı.
        var users = _db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => u.IsActive);

        var recipients = await users
            .Where(u =>
                (toRequester && u.UserId == request.RequestedByUserId) ||
                (toEquipment && u.FirmId == null && u.UserRoles.Any(ur => ur.Role.Code == RoleCodes.EquipmentManager)) ||
                (toFirm && request.FirmId != null && u.FirmId == request.FirmId))
            .Select(u => new { u.UserId, u.Email })
            .ToListAsync(cancellationToken);

        foreach (var recipient in recipients)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = recipient.UserId,
                Email = recipient.Email,
                Channel = NotificationChannel.EMAIL,
                TemplateCode = template,
                Subject = subject,
                Body = body,
                DocumentType = DocumentType.REQUEST,
                DocumentId = request.RequestId,
                Status = NotificationStatus.QUEUED,
                CreatedAt = DateTime.UtcNow
            });
        }

        return recipients.Count;
    }

    /// <summary>
    /// Sıradaki onay adımının rolündeki MIP kullanıcılarına "onayınızı bekliyor".
    /// Rolde kimse yoksa bildirim düşmez — onay yine de bekler (otomatik onay YOK).
    /// </summary>
    public async Task<int> QueueApprovalPendingAsync(
        WorkRecord record, ApprovalFlowStep step, CancellationToken cancellationToken = default)
    {
        // Rol ataması kullanıcı bazlı değil rol bazlı: adımın rolündeki tüm aktif
        // MIP personeline (FirmId = null) haber verilir.
        var recipients = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.IsActive && u.FirmId == null && u.UserRoles.Any(ur => ur.RoleId == step.RoleId))
            .Select(u => new { u.UserId, u.Email })
            .ToListAsync(cancellationToken);

        var subject = Subject(record.DocumentNo, $"\"{step.Name}\" adımında onayınızı bekliyor");

        // FİYAT GİZLİLİĞİ (ADR-016): mail gövdesine TUTAR YAZILMAZ. Bu bildirimin
        // alıcısı adımın rolündeki kişidir ve o rol (ör. EQUIPMENT_MANAGER) tutarı
        // görmez — "onaylama yetkisi" sessizce "fiyat görme yetkisine" dönüşmesin.
        // Tutar uygulamada, yetkisi olana gösterilir. Tek istisna hakediş onay
        // mailidir; alıcısı zaten Bütçe Yöneticisi'dir.
        var body =
            $"{record.DocumentNo} numaralı çalışma kaydı \"{step.Name}\" adımında onayınızı bekliyor. " +
            $"İş tarihi: {record.WorkDate:dd.MM.yyyy}. Ayrıntı için uygulamadaki kaydı açın.";

        foreach (var recipient in recipients)
        {
            Enqueue(recipient.UserId, recipient.Email, Templates.ApprovalPending, subject, body, record.WorkRecordId);
        }

        return recipients.Count;
    }

    /// <summary>Karar sonucu alt yükleniciye (kaydı giren kullanıcıya) bildirilir.</summary>
    public async Task QueueDecisionAsync(
        WorkRecord record, ApprovalDecision decision, string? reason, CancellationToken cancellationToken = default)
    {
        var (template, subject, headline, todo) = decision switch
        {
            ApprovalDecision.APPROVED => (Templates.Approved,
                Subject(record.DocumentNo, "Çalışma kaydınız onaylandı"),
                $"{record.DocumentNo} numaralı çalışma kaydınız onaylandı.",
                "Yapmanız gereken bir şey yok; kayıt dönem hakedişine girecek."),
            ApprovalDecision.REJECTED => (Templates.Rejected,
                Subject(record.DocumentNo, "Çalışma kaydınız reddedildi"),
                $"{record.DocumentNo} numaralı çalışma kaydınız reddedildi.",
                "Reddedilen kayıt hakedişe girmez."),
            ApprovalDecision.REVISION_REQUESTED => (Templates.RevisionRequested,
                Subject(record.DocumentNo, "Çalışma kaydınız için revizyon istendi"),
                $"{record.DocumentNo} numaralı çalışma kaydınız için revizyon istendi.",
                "Uygulamada kaydı açıp yeni versiyon oluşturun, düzeltip tekrar gönderin."),
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Bilinmeyen onay kararı.")
        };

        var body = string.IsNullOrWhiteSpace(reason)
            ? $"{headline} {todo}"
            : $"{headline} Gerekçe: {reason} {todo}";

        await QueueForFirmAsync(record, template, subject, body, cancellationToken);
    }

    /// <summary>Satır bazlı itiraz: alt yüklenici HANGİ satıra NEDEN itiraz edildiğini görsün.</summary>
    public async Task QueueLineObjectionAsync(
        WorkRecord record, IReadOnlyCollection<WorkRecordLine> objectedLines, CancellationToken cancellationToken = default)
    {
        var lineList = string.Join("; ", objectedLines.Select(l => $"{l.LineNo}. satır: {l.ObjectionReason}"));
        var subject = Subject(record.DocumentNo, $"{objectedLines.Count} satıra itiraz edildi");
        var body =
            $"{record.DocumentNo} numaralı çalışma kaydınızın {objectedLines.Count} satırına itiraz edildi. " +
            $"{lineList} Uygulamada kaydı açıp yeni versiyon oluşturun, itiraz edilen satırı düzeltip " +
            "tekrar gönderin.";

        await QueueForFirmAsync(record, Templates.LineObjected, subject, body, cancellationToken);
    }

    /// <summary>
    /// Revizyon taslağı oluştu: firma yetkilisine "düzelt ve gönder" (Adım 16 B).
    /// Kayıt DRAFT doğar ve kendiliğinden zincire girmez; haber düşmezse kayıt
    /// kimsenin beklemediği bir taslak olarak asılı kalırdı.
    /// </summary>
    public Task QueueRevisionDraftedAsync(WorkRecord revision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revision);

        return QueueForFirmAsync(revision, Templates.RevisionDrafted,
            Subject(revision.DocumentNo, "Revizyon taslağı oluştu, gönderim bekliyor"),
            $"{revision.DocumentNo} numaralı yeni versiyon oluşturuldu ve taslak olarak bekliyor. " +
            "Uygulamada kaydı açıp düzeltmeyi yapın ve gönderin; gönderilmeden onay zincirine girmez.",
            cancellationToken);
    }

    /// <summary>
    /// Taslak çalışma kaydı iptal edildi — haber MIP'e (Ekipman Müdürlüğü) gider.
    ///
    /// Firma tarafına gitmez: iptali yapan zaten firma yetkilisidir. MIP'in
    /// bilmesi gerekir çünkü talebin süresi TEYİT EDİLMİŞTİR; teyit edilmiş bir
    /// işin kaydı sessizce ortadan kalkarsa kimse fark etmez.
    /// </summary>
    public async Task QueueDraftCancelledAsync(WorkRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var recipients = await EquipmentManagersAsync(cancellationToken);
        foreach (var recipient in recipients)
        {
            Enqueue(recipient.UserId, recipient.Email, Templates.DraftCancelled,
                Subject(record.DocumentNo, "Çalışma kaydı taslağı iptal edildi"),
                $"{record.DocumentNo} numaralı çalışma kaydı taslağı alt yüklenici tarafından iptal edildi. " +
                $"İş tarihi: {record.WorkDate:dd.MM.yyyy}. Bu iş hakedişe girmeyecek; teyit edilmiş bir " +
                "işin kaydı bekleniyorduysa alt yükleniciyle görüşülmesi gerekir.",
                record.WorkRecordId);
        }
    }

    /// <summary>
    /// Dönem kapandı / yeniden açıldı: FİRMA BAŞINA TEK bildirim.
    ///
    /// Kayıt başına bildirim üretmek yığılmanın ders kitabı örneğidir: 40 kayıtlı
    /// bir dönem kapanışı 40 mail demektir ve hiçbiri tek tek okunmaz. Kilit
    /// zaten kayıt bazlı değil DÖNEM bazlı bir olaydır.
    /// </summary>
    public async Task<int> QueuePeriodLockAsync(
        Period period, IReadOnlyCollection<int> affectedFirmIds, bool locked, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(period);

        if (affectedFirmIds.Count == 0)
        {
            return 0;
        }

        var recipients = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.IsActive
                && u.FirmId != null && affectedFirmIds.Contains(u.FirmId.Value)
                && u.UserRoles.Any(ur => ur.Role.Code == RoleCodes.FirmManager
                                      || ur.Role.Code == RoleCodes.FirmUser))
            .Select(u => new { u.UserId, u.Email })
            .ToListAsync(cancellationToken);

        var periodName = PeriodName(period);
        var (template, subject, body) = locked
            ? (Templates.PeriodLocked,
               Subject(periodName, "Dönem kapatıldı, kayıtlarınız kilitlendi"),
               $"{periodName} dönemi kapatıldı. Bu döneme ait onaylı çalışma kayıtlarınız kilitlendi ve " +
               "artık değiştirilemez. Yapmanız gereken bir şey yok; bir hata varsa MIP ile görüşün.")
            : (Templates.PeriodReopened,
               Subject(periodName, "Dönem yeniden açıldı"),
               $"{periodName} dönemi yeniden açıldı ve bu döneme ait kayıtlarınızın kilidi kaldırıldı. " +
               "Düzeltme gerekiyorsa uygulamadan işlem yapabilirsiniz.");

        foreach (var recipient in recipients)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = recipient.UserId,
                Email = recipient.Email,
                Channel = NotificationChannel.EMAIL,
                TemplateCode = template,
                Subject = subject,
                Body = body,
                // Belge tipi YOK: olay tek bir belgeye değil DÖNEME ait.
                DocumentType = null,
                DocumentId = period.PeriodId,
                Status = NotificationStatus.QUEUED,
                CreatedAt = DateTime.UtcNow
            });
        }

        return recipients.Count;
    }

    /// <summary>
    /// Hakediş kararı — alıcı hakedişi KURAN Bütçe kullanıcısıdır (süreci
    /// başlatan), red halinde gerekçesiyle. Geri çekmede alıcı Bütçe
    /// Yöneticisi'dir: mail kutusundaki bağlantısı artık ölüdür, bilmesi gerekir.
    ///
    /// Firma bu zincire HİÇ girmez: hakediş MIP'in iç mali sürecidir ve
    /// bildirimde tutar geçer — alt yükleniciye gitseydi fiyat sızardı.
    /// </summary>
    public async Task<int> QueueProgressPaymentDecisionAsync(
        ProgressPayment payment, string template, string headline, string? note,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payment);

        var toWithdrawnManagers = template == Templates.ProgressPaymentWithdrawn;

        var recipients = toWithdrawnManagers
            ? await _db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.IsActive && u.FirmId == null
                         && u.UserRoles.Any(ur => ur.Role.Code == RoleCodes.BudgetManager))
                .Select(u => new { u.UserId, u.Email })
                .ToListAsync(cancellationToken)
            : await _db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.IsActive && u.UserId == payment.CreatedByUserId)
                .Select(u => new { u.UserId, u.Email })
                .ToListAsync(cancellationToken);

        var reference = ProgressPaymentReference(payment);
        var body = string.IsNullOrWhiteSpace(note) ? headline : $"{headline} Not/gerekçe: {note}";

        foreach (var recipient in recipients)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = recipient.UserId,
                Email = recipient.Email,
                Channel = NotificationChannel.EMAIL,
                TemplateCode = template,
                Subject = Subject(reference, headline),
                Body = body,
                DocumentType = null,
                DocumentId = payment.ProgressPaymentId,
                Status = NotificationStatus.QUEUED,
                CreatedAt = DateTime.UtcNow
            });
        }

        return recipients.Count;
    }

    /// <summary>
    /// Çalışma kaydı bildirimlerinin alıcısı: kaydın sahibi firmanın KARAR
    /// VEREBİLEN kullanıcıları (FIRM_MANAGER / FIRM_USER).
    ///
    /// Adım 16'ya kadar alıcı EnteredByUserId idi. Türetme teyide taşınınca o
    /// alan operatörü göstermeye başladı ve operatör kaydı ne gönderebiliyor ne
    /// revize edebiliyor (ADR-028): "revizyon istendi" maili yapabileceği bir şey
    /// olmayan kişiye gidiyordu. Bildirim, EYLEMİ YAPACAK kişiye gider.
    /// </summary>
    private async Task QueueForFirmAsync(
        WorkRecord record, string template, string subject, string body, CancellationToken cancellationToken)
    {
        var recipients = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.IsActive
                && u.FirmId != null && u.FirmId == record.FirmId
                && u.UserRoles.Any(ur => ur.Role.Code == RoleCodes.FirmManager
                                      || ur.Role.Code == RoleCodes.FirmUser))
            .Select(u => new { u.UserId, u.Email })
            .ToListAsync(cancellationToken);

        foreach (var recipient in recipients)
        {
            Enqueue(recipient.UserId, recipient.Email, template, subject, body, record.WorkRecordId);
        }
    }

    private Task<List<UserContact>> EquipmentManagersAsync(CancellationToken cancellationToken) =>
        _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.IsActive && u.FirmId == null
                     && u.UserRoles.Any(ur => ur.Role.Code == RoleCodes.EquipmentManager))
            .Select(u => new UserContact(u.UserId, u.Email))
            .ToListAsync(cancellationToken);

    private sealed record UserContact(int UserId, string? Email);

    /// <summary>Hakedişin belge referansı: kendi numarası yok, dönem+firma ile anılır.</summary>
    private static string ProgressPaymentReference(ProgressPayment payment) =>
        $"HAK-{payment.PeriodId:0000}-{payment.FirmId:0000}";

    private static string PeriodName(Period period) =>
        $"{System.Globalization.CultureInfo.GetCultureInfo("tr-TR").DateTimeFormat.GetMonthName(period.Month)} {period.Year}";

    private void Enqueue(int userId, string? email, string template, string subject, string body, int workRecordId)
    {
        _db.Notifications.Add(new Notification
        {
            UserId = userId,
            Email = email,
            Channel = NotificationChannel.EMAIL,
            TemplateCode = template,
            Subject = subject,
            Body = body,
            DocumentType = DocumentType.WORK_RECORD,
            DocumentId = workRecordId,
            Status = NotificationStatus.QUEUED,
            CreatedAt = DateTime.UtcNow
        });
    }
}
