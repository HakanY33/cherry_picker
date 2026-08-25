using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Approvals;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;

namespace MipRental.Data.Services;

/// <summary>
/// CLAUDE.md kural 1: düzeltme = YENİ VERSİYON, güncelleme değil.
///
/// REVISION_REQUESTED bir kayıt için alt yüklenici "Revize Et" dediğinde kaydın ve
/// satırlarının KOPYASI oluşturulur. Eski kayıt hiçbir şekilde değiştirilmez;
/// üzerinde sadece IsSuperseded = 1 işaretlenir — tutarı, satırları, durumu aynen kalır.
///
/// BELGE NUMARASI KARARI: yeni kayıt eskisinin numarasına sürüm eki alır
/// (WR-2026-00042 -> WR-2026-00042-R2), yepyeni bir seri numarası ALMAZ. Gerekçe:
///   - Sahadaki dış fiş ("0078 numaralı fiş") tek bir belge numarasıyla eşleşir;
///     revizyona bağımsız numara verilirse aynı iş iki ayrı belge gibi görünür ve
///     aylık icmalde mükerrer sanılır.
///   - Ek, soy bağını join'siz okunur kılar: "-R2" doğrudan "42'nin 2. versiyonu" der.
///   - Seri sayacı gerçek iş sayısını saymaya devam eder; düzeltmeler sayacı şişirmez.
/// </summary>
public sealed class WorkRecordRevisionService
{
    // "WR-2026-00042-R2" -> taban "WR-2026-00042", sürüm 2. Eki olmayan kayıt 1. sürümdür.
    private static readonly Regex RevisionSuffix = new(@"^(?<base>.+?)-R(?<version>\d+)$", RegexOptions.Compiled);

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public WorkRecordRevisionService(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Revizyon kaydını oluşturur ve change tracker'a ekler. SaveChanges ÇAĞIRMAZ —
    /// commit sınırını çağıran belirler.
    /// </summary>
    public async Task<WorkRecord> CreateRevisionAsync(int workRecordId, CancellationToken cancellationToken = default)
    {
        var original = await _db.WorkRecords
            .Include(w => w.WorkRecordLines)
            .FirstOrDefaultAsync(w => w.WorkRecordId == workRecordId, cancellationToken)
            ?? throw new ApprovalFlowException("Revize edilecek çalışma kaydı bulunamadı.");

        var period = await _db.Periods.AsNoTracking().SingleAsync(p => p.PeriodId == original.PeriodId, cancellationToken);

        var roleCodes = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == _currentUser.UserId)
            .Select(ur => ur.Role.Code)
            .ToListAsync(cancellationToken);
        var actor = TransitionActor.From(_currentUser, roleCodes);

        // İzin + yetki + dönem kontrolü tek yerden: durum makinesi.
        WorkRecordStateMachine.EnsureCanCreateRevision(original, period, actor);

        var revisionReason = await BuildRevisionReasonAsync(original, cancellationToken);

        var revision = new WorkRecord
        {
            DocumentNo = NextDocumentNo(original.DocumentNo),
            Status = WorkRecordStatus.DRAFT,
            IntegrationStatus = WorkRecordIntegrationStatus.NOT_SENT,

            RequestId = original.RequestId,
            FirmId = original.FirmId,
            ContractId = original.ContractId,
            PeriodId = original.PeriodId,

            // İş tarihi AYNEN taşınır. Yeni versiyon gönderildiğinde fiyat yeniden
            // hesaplanır ama sözleşme satırı yine BU tarihe göre bulunur (kural 3) —
            // revizyonun bugüne göre fiyatlanması geçmişi bozardı.
            WorkDate = original.WorkDate,
            StartTime = original.StartTime,
            EndTime = original.EndTime,
            SpansMidnight = original.SpansMidnight,

            LocationId = original.LocationId,
            LocationText = original.LocationText,
            WorkDescription = original.WorkDescription,

            RequestedByUserId = original.RequestedByUserId,
            WitnessedByUserId = original.WitnessedByUserId,
            DepartmentId = original.DepartmentId,

            OperatorName = original.OperatorName,
            EquipmentId = original.EquipmentId,
            LicensePlate = original.LicensePlate,
            PersonnelCount = original.PersonnelCount,

            ExternalReceiptNo = original.ExternalReceiptNo,
            ExternalReceiptDate = original.ExternalReceiptDate,

            // Tutarlar KOPYALANMAZ: yeni versiyon gönderilirken sıfırdan hesaplanır.
            MobilizationFee = null,
            TotalAmount = null,
            Currency = null,

            EnteredByUserId = _currentUser.UserId,
            SubmittedAt = null,
            ApprovedAt = null,

            RevisionOfId = original.WorkRecordId,
            RevisionReason = revisionReason,
            IsSuperseded = false
        };

        foreach (var line in original.WorkRecordLines.OrderBy(l => l.LineNo))
        {
            revision.WorkRecordLines.Add(new WorkRecordLine
            {
                LineNo = line.LineNo,
                ServiceId = line.ServiceId,
                VariantId = line.VariantId,

                // Miktar taşınır (alt yüklenici düzeltecek), fiyat snapshot'ları TAŞINMAZ:
                // yeni versiyonun fiyatı gönderimde yeniden hesaplanır.
                RawQuantity = line.RawQuantity,
                BillableQuantity = 0m,
                Unit = line.Unit,
                ContractLineId = null,
                UnitPriceSnapshot = 0m,
                PricingRuleSnapshot = null,
                SurchargeAmount = 0m,
                LineAmount = 0m,
                Currency = line.Currency,

                Description = line.Description,

                // İtiraz işaretleri yeni versiyona taşınmaz; itirazın kendisi eski
                // kayıtta duruyor ve revizyon zinciri üzerinden görülüyor.
                IsObjected = false
            });
        }

        // Eski kayıtta değişen TEK alan bu. Durumu REVISION_REQUESTED kalır.
        original.IsSuperseded = true;

        _db.WorkRecords.Add(revision);
        return revision;
    }

    /// <summary>Zincirdeki sürüm numarası: eki olmayan kayıt 1. sürümdür.</summary>
    public static int VersionOf(string documentNo)
    {
        var match = RevisionSuffix.Match(documentNo);
        return match.Success ? int.Parse(match.Groups["version"].Value, CultureInfo.InvariantCulture) : 1;
    }

    /// <summary>Sürüm ekinden arındırılmış kök belge numarası.</summary>
    public static string BaseDocumentNo(string documentNo)
    {
        var match = RevisionSuffix.Match(documentNo);
        return match.Success ? match.Groups["base"].Value : documentNo;
    }

    private static string NextDocumentNo(string originalDocumentNo) =>
        $"{BaseDocumentNo(originalDocumentNo)}-R{VersionOf(originalDocumentNo) + 1}";

    /// <summary>
    /// Yeni versiyonun gerekçesi: onaylayanın revizyon/itiraz gerekçesi. Kayıt
    /// düzeltmesinin NEDEN yapıldığı yeni versiyonun üzerinde de dursun diye.
    /// </summary>
    private async Task<string?> BuildRevisionReasonAsync(WorkRecord original, CancellationToken cancellationToken)
    {
        var objectionReasons = original.WorkRecordLines
            .Where(l => l.IsObjected && !string.IsNullOrWhiteSpace(l.ObjectionReason))
            .OrderBy(l => l.LineNo)
            .Select(l => $"{l.LineNo}. satır: {l.ObjectionReason}")
            .ToList();

        if (objectionReasons.Count > 0)
        {
            return Truncate(string.Join(" | ", objectionReasons));
        }

        var lastDecisionComment = await _db.Approvals.AsNoTracking()
            .Where(a => a.DocumentType == DocumentType.WORK_RECORD
                && a.DocumentId == original.WorkRecordId
                && a.Decision == ApprovalDecision.REVISION_REQUESTED)
            .OrderByDescending(a => a.DecidedAt)
            .Select(a => a.Comment)
            .FirstOrDefaultAsync(cancellationToken);

        return lastDecisionComment is null ? null : Truncate(lastDecisionComment);
    }

    // RevisionReason kolonu nvarchar(500).
    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500];
}
