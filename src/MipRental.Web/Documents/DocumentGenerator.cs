using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Services;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Reporting;
using MipRental.Web.Common;
using QuestPDF.Fluent;

namespace MipRental.Web.Documents;

/// <summary>
/// Belge üretiminin tek giriş noktası: veriyi toplar, PDF'i render eder, dosyayı
/// arşivler ve GeneratedDocuments kaydını atar.
///
/// Doğrulama kodu, PDF'in İÇİNE basıldığı için render'dan ÖNCE üretilir ve hem
/// belgeye hem kayda aynı değer gider.
/// </summary>
public sealed class DocumentGenerator
{
    private readonly AppDbContext _db;
    private readonly GeneratedDocumentService _archive;

    public DocumentGenerator(AppDbContext db, GeneratedDocumentService archive)
    {
        _db = db;
        _archive = archive;
    }

    /// <summary>
    /// Çalışma kaydı formu. verificationUrlFactory, kod verilince /Dogrula/{kod}
    /// adresinin tamamını üretir (controller LinkGenerator ile besler; şablon
    /// HttpContext bilmez).
    /// </summary>
    public async Task<GeneratedDocumentResult> GenerateWorkRecordFormAsync(
        int workRecordId, Func<string, string> verificationUrlFactory, CancellationToken cancellationToken = default)
    {
        var model = await BuildWorkRecordFormModelAsync(workRecordId, verificationUrlFactory, cancellationToken);
        var bytes = new WorkRecordFormDocument(model).GeneratePdf();

        var fileName = $"Calisma-Kaydi-{model.DocumentNo}.pdf";
        var firmId = await _db.WorkRecords.AsNoTracking()
            .Where(w => w.WorkRecordId == workRecordId)
            .Select(w => (int?)w.FirmId)
            .FirstOrDefaultAsync(cancellationToken);

        var document = await _archive.ArchiveAsync(new GeneratedDocumentRequest
        {
            DocumentType = DocumentType.WORK_RECORD,
            DocumentId = workRecordId,
            Kind = GeneratedDocumentKind.FORM_PDF,
            FirmId = firmId,
            FileName = fileName,
            Content = bytes,
            VerificationCode = model.VerificationCode,
            TemplateVersion = DocumentTheme.TemplateVersion,
            TotalAmount = model.TotalAmount,
            Currency = model.Currency
        }, cancellationToken);

        return new GeneratedDocumentResult(bytes, fileName, document);
    }

    /// <summary>Aylık icmal PDF'i. İcmal zaten hesaplanmış olarak gelir.</summary>
    public async Task<GeneratedDocumentResult> GenerateMonthlySummaryAsync(
        MonthlySummary summary, Func<string, string> verificationUrlFactory, CancellationToken cancellationToken = default)
    {
        var verificationCode = GeneratedDocumentService.NewVerificationCode();
        var bytes = new MonthlySummaryDocument(summary, verificationCode, verificationUrlFactory(verificationCode))
            .GeneratePdf();

        var fileName = $"Aylik-Icmal-{summary.FirmCode}-{summary.Year}-{summary.Month:00}.pdf";

        var document = await _archive.ArchiveAsync(new GeneratedDocumentRequest
        {
            DocumentType = DocumentType.PERIOD,
            DocumentId = summary.PeriodId,
            Kind = GeneratedDocumentKind.MONTHLY_SUMMARY_PDF,
            FirmId = summary.FirmId,
            FileName = fileName,
            Content = bytes,
            VerificationCode = verificationCode,
            TemplateVersion = DocumentTheme.TemplateVersion,
            // Farklı para birimi varsa tek bir tutar anlamlı değil; kayda da yazılmaz.
            TotalAmount = summary.HasMixedCurrency ? null : summary.GrandTotal,
            Currency = summary.HasMixedCurrency ? null : summary.Currency
        }, cancellationToken);

        return new GeneratedDocumentResult(bytes, fileName, document);
    }

    public async Task<WorkRecordFormModel> BuildWorkRecordFormModelAsync(
        int workRecordId, Func<string, string> verificationUrlFactory, CancellationToken cancellationToken = default)
    {
        var record = await _db.WorkRecords.AsNoTracking()
            .Include(w => w.Firm)
            .Include(w => w.Contract)
            .Include(w => w.Period)
            .Include(w => w.Location)
            .Include(w => w.Equipment).ThenInclude(e => e!.ServiceVariant)
            .Include(w => w.Department)
            .Include(w => w.WorkRecordLines).ThenInclude(l => l.ServiceCategory)
            .Include(w => w.WorkRecordLines).ThenInclude(l => l.ServiceVariant)
            .FirstOrDefaultAsync(w => w.WorkRecordId == workRecordId, cancellationToken)
            ?? throw new InvalidOperationException($"Çalışma kaydı bulunamadı (WorkRecordId = {workRecordId}).");

        // RequestedBy/WitnessedBy her zaman MIP personelidir; User entity'sindeki
        // firma izolasyon filtresi firma kullanıcısına bunları göstermez. Sadece
        // AD alanını, sadece FirmId == null olan kullanıcılar için çözüyoruz
        // (WorkRecordsController.Details ile aynı gerekçe).
        var staffIds = new[] { record.RequestedByUserId, record.WitnessedByUserId }
            .Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();
        var staffNames = staffIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.FirmId == null && staffIds.Contains(u.UserId))
                .Select(u => new { u.UserId, u.FullName })
                .ToDictionaryAsync(u => u.UserId, u => u.FullName, cancellationToken);

        var approvals = await _db.Approvals.IgnoreQueryFilters().AsNoTracking()
            .Include(a => a.ApprovalFlowStep)
            .Include(a => a.DecidedByUser)
            .Include(a => a.AssignedToRole)
            .Where(a => a.DocumentType == DocumentType.WORK_RECORD && a.DocumentId == workRecordId)
            .OrderBy(a => a.StepNo).ThenBy(a => a.ApprovalId)
            .ToListAsync(cancellationToken);

        var lines = record.WorkRecordLines.OrderBy(l => l.LineNo).ToList();

        // Fiyat açıklaması satırların DONDURULMUŞ snapshot'ından okunur; yeniden
        // hesaplanmaz (CLAUDE.md kural 2).
        var explanation = new List<string>();
        foreach (var line in lines)
        {
            var lineExplanation = PricingSnapshotReader.ReadExplanation(line.PricingRuleSnapshot);
            if (lineExplanation.Count == 0)
            {
                continue;
            }

            explanation.Add($"{line.LineNo}. satır — {line.ServiceCategory.Name}:");
            explanation.AddRange(lineExplanation.Select(e => $"   {e}"));
        }

        var currency = record.Currency ?? lines.FirstOrDefault()?.Currency ?? "TRY";
        var linesTotal = lines.Sum(l => l.LineAmount);
        var mobilizationFee = record.MobilizationFee ?? 0m;

        var verificationCode = GeneratedDocumentService.NewVerificationCode();

        return new WorkRecordFormModel
        {
            DocumentNo = record.DocumentNo,
            Status = record.Status,
            Year = record.Period.Year,
            Month = record.Period.Month,

            RequestedByName = record.RequestedByUserId is int r ? staffNames.GetValueOrDefault(r) : null,
            WitnessedByName = record.WitnessedByUserId is int w ? staffNames.GetValueOrDefault(w) : null,
            DepartmentName = record.Department?.Name,

            FirmTitle = record.Firm.Title,
            FirmCode = record.Firm.Code,
            ContractNo = record.Contract.ContractNo,
            OperatorName = record.OperatorName,
            EquipmentDescription = record.Equipment?.Description,
            Capacity = record.Equipment?.ServiceVariant?.Capacity,
            LicensePlate = record.LicensePlate ?? record.Equipment?.LicensePlate,
            PersonnelCount = record.PersonnelCount,

            WorkDate = record.WorkDate,
            StartTime = record.StartTime,
            EndTime = record.EndTime,
            SpansMidnight = record.SpansMidnight,
            Location = record.Location?.Name ?? record.LocationText,
            WorkDescription = record.WorkDescription,
            ExternalReceiptNo = record.ExternalReceiptNo,
            ExternalReceiptDate = record.ExternalReceiptDate,

            Lines = lines.Select(l => new WorkRecordFormLine
            {
                LineNo = l.LineNo,
                ServiceName = l.ServiceCategory.Name,
                VariantName = l.ServiceVariant?.Name,
                RawQuantity = l.RawQuantity,
                BillableQuantity = l.BillableQuantity,
                Unit = l.Unit,
                UnitPrice = l.UnitPriceSnapshot,
                SurchargeAmount = l.SurchargeAmount,
                LineAmount = l.LineAmount,
                Description = l.Description
            }).ToList(),

            PricingExplanation = explanation,
            LinesTotal = linesTotal,
            MobilizationFee = mobilizationFee,
            // Kayıtta hesaplanmış toplam varsa o basılır; yoksa (henüz
            // fiyatlanmamış taslak) satırlardan türetilir.
            TotalAmount = record.TotalAmount ?? linesTotal + mobilizationFee,
            Currency = currency,

            ApprovalHistory = approvals.Select(a => new WorkRecordFormApproval
            {
                StepNo = a.StepNo,
                StepName = a.ApprovalFlowStep?.Name ?? a.AssignedToRole?.Name ?? $"{a.StepNo}. adım",
                DecidedByName = a.DecidedByUser?.FullName,
                Decision = a.Decision,
                DecidedAtUtc = a.DecidedAt,
                Comment = a.Comment
            }).ToList(),

            VerificationCode = verificationCode,
            VerificationUrl = verificationUrlFactory(verificationCode)
        };
    }
}

public sealed record GeneratedDocumentResult(byte[] Content, string FileName, GeneratedDocument Document);
