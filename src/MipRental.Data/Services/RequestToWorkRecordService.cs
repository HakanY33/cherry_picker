using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MipRental.Data.Pricing;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;
using MipRental.Domain.Pricing;

namespace MipRental.Data.Services;

/// <summary>
/// ADIM 12 — talepten çalışma kaydı türetme.
///
/// Operatör "bitirdim" dedikten (talep COMPLETED olduktan) sonra çalışma kaydı
/// ELLE girilmez, talepten türer. Bu sınıf o türetmenin TEK giriş noktasıdır.
///
/// TEK KAYIT KURALI (A2): bir talepten yalnızca BİR çalışma kaydı doğar.
/// Çift türetme = çift faturalama. Garanti iki katmanlı:
///   1. Veritabanı — WorkRecords.RequestId üzerinde filtreli UNIQUE index
///      (UQ_WorkRecords_Request). Yarış durumunda ikinci INSERT reddedilir.
///   2. Bu servis — ihlali yakalar, kazananı okur ve onu döner (idempotent).
/// Uygulama katmanındaki kontrol tek başına YETERSİZDİR: iki paralel istek de
/// "yok" görüp ikisi de yazabilir. Son sözü veritabanı söyler.
///
/// Index filtresi RevisionOfId'yi de eler: revizyon (kural 1) selefinin
/// RequestId'sini taşır ve aynı işin YENİ VERSİYONUDUR, ikinci bir türetme değil.
///
/// SaveChanges'i BU SERVİS çağırır — ApprovalService/NotificationQueue'nun
/// aksine. Sebep: idempotentlik "yaz, çakışırsa oku" desenine dayanır ve bu desen
/// commit sınırının içeride olmasını gerektirir. Bu yüzden çağıran, talebin kendi
/// değişikliğini ÖNCE kaydeder: türetme başarısız olsa da talep COMPLETED kalır (B6).
/// </summary>
public sealed class RequestToWorkRecordService
{
    private readonly AppDbContext _db;
    private readonly ContractLineResolver _resolver;
    private readonly ICurrentUser _currentUser;
    private readonly NotificationQueue _notifications;

    public RequestToWorkRecordService(
        AppDbContext db, ContractLineResolver resolver, ICurrentUser currentUser, NotificationQueue notifications)
    {
        _db = db;
        _resolver = resolver;
        _currentUser = currentUser;
        _notifications = notifications;
    }

    /// <summary>
    /// Talepten çalışma kaydını türetir; zaten türetilmişse mevcut kaydı döner.
    ///
    /// Hata halinde İSTİSNA FIRLATIR ve hiçbir kayıt oluşmaz — sessizce sıfır
    /// tutarlı kayıt üretilmez (A4):
    ///   <see cref="PeriodGuardException"/> — dönem kapalı ya da tanımsız (A3)
    ///   <see cref="PricingException"/>     — sözleşme/fiyat bulunamadı (A4)
    ///   <see cref="RequestStateTransitionException"/> — talep türetmeye uygun değil
    /// </summary>
    public async Task<WorkRecord> DeriveAsync(int requestId, CancellationToken cancellationToken = default)
    {
        var existing = await FindDerivedAsync(requestId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var request = await _db.Requests
            .Include(r => r.RequestLines)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.RequestId == requestId, cancellationToken)
            ?? throw new RequestStateTransitionException($"{requestId} numaralı talep bulunamadı.");

        EnsureDerivable(request);

        // Gerçekleşen saatler veritabanında UTC durur; iş tarihi, dönem ve vardiya
        // saatleri YEREL saate göre anlam taşır (CLAUDE.md: veritabanında UTC,
        // ekranda yerel). Gece 01:00'de biten bir iş UTC'ye bakılırsa bir önceki
        // güne düşer ve yanlış döneme yazılırdı.
        // ponytail: yerel saat = sunucunun saat dilimi (uygulamanın her yerinde
        // olduğu gibi, bkz. TrFormat.DateTimeLocal). Sunucu Türkiye dışına
        // taşınırsa burada sabit bir TimeZoneInfo'ya geçilmeli.
        var start = ToLocal(request.ActualStartTime!.Value);
        var end = ToLocal(request.ActualEndTime!.Value);

        var workDate = DateOnly.FromDateTime(start);
        var spansMidnight = DateOnly.FromDateTime(end) > workDate;

        // A3 — dönem, talebin tarihine değil İŞİN GERÇEKLEŞTİĞİ tarihe göre
        // belirlenir. Talep 31 Ağustos'a açılıp iş 1 Eylül'e sarkmış olabilir;
        // hakediş işin yapıldığı aya yazılır (CLAUDE.md kural 3 ile aynı yön).
        var period = await _db.Periods.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Year == workDate.Year && p.Month == workDate.Month, cancellationToken)
            ?? throw new PeriodGuardException(
                $"{PeriodLabel(workDate.Year, workDate.Month)} dönemi tanımlı değil; " +
                $"{request.DocumentNo} numaralı talepten çalışma kaydı oluşturulamadı. Dönemin açılması gerekiyor.");

        if (period.Status == PeriodStatus.CLOSED)
        {
            throw new PeriodGuardException(
                $"{PeriodLabel(period.Year, period.Month)} dönemi kapalıdır; " +
                $"{request.DocumentNo} numaralı talepten çalışma kaydı oluşturulamadı. " +
                "İş bu dönemde gerçekleşti, kaydın girilebilmesi için dönemin yeniden açılması gerekiyor.");
        }

        var record = new WorkRecord
        {
            // Gerçek belge numarası SUBMITTED'da verilir (→ Belge Numarası notu);
            // taslak benzersiz-ama-geçici numara taşır, Create ekranıyla aynı desen.
            DocumentNo = $"DRAFT-{Guid.NewGuid():N}"[..30],
            Status = WorkRecordStatus.DRAFT,

            RequestId = request.RequestId,
            FirmId = request.FirmId!.Value,
            PeriodId = period.PeriodId,

            WorkDate = workDate,
            StartTime = TimeOnly.FromDateTime(start),
            EndTime = TimeOnly.FromDateTime(end),
            SpansMidnight = spansMidnight,

            LocationId = request.LocationId,
            LocationText = request.LocationText,
            WorkDescription = request.WorkDescription,

            RequestedByUserId = request.RequestedByUserId,
            DepartmentId = request.DepartmentId,

            // Saha yetkilisi FİRMA TARAFINDAN SEÇİLMEZ, talepten gelir. İki sebep:
            // alt yüklenici kendi doğrulayıcısını seçerse imzanın kanıt değeri
            // kalmaz; ayrıca firma yetkilisi MIP personelinin kimlik bilgilerini
            // görmez (Adım 11). Talebi açan kişi zaten sahada işi yaptıran,
            // bugün kâğıt fişi imzalayan kişidir.
            WitnessedByUserId = request.RequestedByUserId,

            OperatorName = request.AssignedOperatorName,
            LicensePlate = request.AssignedLicensePlate,

            // Kaydı "giren" kişi türetmeyi tetikleyendir: normal akışta işi
            // bitiren operatör. Denetim izi de aynı kullanıcıyı yazar.
            EnteredByUserId = _currentUser.UserId
        };

        // A4 — fiyat TÜRETME ANINDA çözülür ve satıra kopyalanır (kural 2).
        // Sözleşme bitmiş ya da fiyat tanımsızsa ResolveAsync/Calculate
        // PricingException fırlatır; kayıt oluşmaz, talep COMPLETED kalır.
        var lineResults = new List<PricingResult>();
        int? contractId = null;

        foreach (var requestLine in request.RequestLines.OrderBy(l => l.LineNo))
        {
            var contractLine = await _resolver.ResolveAsync(
                record.FirmId, requestLine.ServiceId, requestLine.VariantId, workDate, cancellationToken);

            var result = PricingCalculator.Calculate(new PricingRequest
            {
                ContractLine = contractLine,
                ApplicableSurcharges = Array.Empty<ContractLineSurcharge>(),

                // Miktar GERÇEKLEŞEN süreden hesaplanır (bitiş - başlangıç);
                // yuvarlama ve minimum dahil hesabı PricingCalculator yapar.
                // Saatlik olmayan hizmette talepteki tahmini miktar kullanılır:
                // sahada ölçülen bir miktar yok, operatör hiçbir şey yazmıyor.
                StartTime = record.StartTime,
                EndTime = record.EndTime,
                SpansMidnight = record.SpansMidnight,
                Quantity = contractLine.ServiceCategory.Unit == ServiceUnit.HOUR ? null : requestLine.EstimatedQuantity
            });

            contractId ??= contractLine.ContractId;

            record.WorkRecordLines.Add(new WorkRecordLine
            {
                LineNo = record.WorkRecordLines.Count + 1,
                ServiceId = requestLine.ServiceId,
                VariantId = requestLine.VariantId,
                RawQuantity = result.RawQuantity,
                BillableQuantity = result.BillableQuantity,
                Unit = result.Unit,
                ContractLineId = contractLine.ContractLineId,
                UnitPriceSnapshot = result.UnitPriceApplied,
                PricingRuleSnapshot = result.PricingRuleSnapshot,
                SurchargeAmount = result.SurchargeAmount,
                // Mobilizasyon bedeli satıra YAZILMAZ; kayıt seviyesinde bir kez.
                LineAmount = result.LineAmount,
                Currency = result.Currency
            });

            lineResults.Add(result);
        }

        var recordTotal = RecordTotalCalculator.Calculate(lineResults);
        record.ContractId = contractId!.Value;
        record.MobilizationFee = recordTotal.MobilizationFee;
        record.TotalAmount = recordTotal.TotalAmount;
        record.Currency = recordTotal.Currency;

        _db.WorkRecords.Add(record);

        // B6 — firma YETKİLİSİNE "gönderim bekliyor" bildirimi. Kaydın kendisiyle
        // AYNI SaveChanges'te yazılır: kayıt oluşmadıysa bildirim de düşmez.
        // Operatör bu bildirimi almaz — gönderim onun işi değil (ADR-028).
        var notifications = await _notifications.QueueWorkRecordDerivedAsync(request, cancellationToken);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Paralel bir türetme bizden önce davrandı: UNIQUE index ikinci
            // INSERT'i reddetti. Yeni kayıt üretmeyip kazananı döneriz.
            Detach(record, notifications);

            var winner = await FindDerivedAsync(requestId, cancellationToken);
            if (winner is null)
            {
                throw; // Tek kayıt kuralıyla ilgisi olmayan bir hata; yutulmaz.
            }

            return winner;
        }

        return record;
    }

    /// <summary>
    /// Bu talepten türemiş kayıt. Revizyonlar (RevisionOfId dolu) hariç: onlar
    /// aynı türetmenin sonraki versiyonlarıdır, ayrı bir türetme değil.
    /// UNIQUE index'in filtresiyle BİREBİR aynı koşul — ikisi ayrışamaz.
    /// </summary>
    private Task<WorkRecord?> FindDerivedAsync(int requestId, CancellationToken cancellationToken) =>
        _db.WorkRecords.AsNoTracking()
            .FirstOrDefaultAsync(w => w.RequestId == requestId && w.RevisionOfId == null, cancellationToken);

    private static void EnsureDerivable(Request request)
    {
        if (request.Status != RequestStatus.COMPLETED)
        {
            throw new RequestStateTransitionException(
                $"Çalışma kaydı yalnızca \"{RequestStatusLabels.Get(RequestStatus.COMPLETED)}\" durumundaki talepten türetilir; " +
                $"{request.DocumentNo} numaralı talep \"{RequestStatusLabels.Get(request.Status)}\" durumunda.");
        }

        if (request.ActualStartTime is null || request.ActualEndTime is null)
        {
            throw new RequestStateTransitionException(
                $"{request.DocumentNo} numaralı talepte gerçekleşen başlangıç/bitiş saati yok; çalışma kaydı türetilemez.");
        }

        if (request.FirmId is null)
        {
            throw new RequestStateTransitionException(
                $"{request.DocumentNo} numaralı talebe firma atanmamış; çalışma kaydı türetilemez.");
        }

        if (request.RequestLines.Count == 0)
        {
            throw new PricingException(
                $"{request.DocumentNo} numaralı talepte hizmet satırı yok; fiyatlandırılacak bir şey bulunamadı.");
        }
    }

    private void Detach(WorkRecord record, IReadOnlyList<Notification> notifications)
    {
        // Kopya üzerinde dönülür: satırı detach etmek EF'in navigation
        // düzeltmesiyle koleksiyonun kendisini değiştirir.
        foreach (var line in record.WorkRecordLines.ToList())
        {
            _db.Entry(line).State = EntityState.Detached;
        }

        _db.Entry(record).State = EntityState.Detached;

        // Bildirimler de düşürülür: kaydı oluşmayan bir "gönderim bekliyor"
        // satırı, çağıranın sonraki SaveChanges'iyle sessizce yazılırdı.
        foreach (var notification in notifications)
        {
            _db.Entry(notification).State = EntityState.Detached;
        }
    }

    private static DateTime ToLocal(DateTime utcValue) =>
        DateTime.SpecifyKind(utcValue, DateTimeKind.Utc).ToLocalTime();

    private static string PeriodLabel(int year, int month) =>
        $"{CultureInfo.GetCultureInfo("tr-TR").DateTimeFormat.GetMonthName(month)} {year}";
}
