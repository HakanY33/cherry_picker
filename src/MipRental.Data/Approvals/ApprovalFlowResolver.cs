using Microsoft.EntityFrameworkCore;
using MipRental.Domain.Approvals;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;

namespace MipRental.Data.Approvals;

/// <summary>
/// CLAUDE.md kural 6: onay zinciri ApprovalFlowSteps tablosundan okunur, kodda
/// sabit değildir. Bu sınıf "hangi akış, hangi adımlar" sorusunu YALNIZCA veriye
/// bakarak cevaplar. Toplantıdan farklı bir karar çıkarsa veri değişir, kod değişmez.
///
/// Veritabanı erişimi gerektirdiği için (ContractLineResolver gibi) Data katmanında.
/// </summary>
public sealed class ApprovalFlowResolver
{
    private readonly AppDbContext _db;

    public ApprovalFlowResolver(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Bir çalışma kaydı için geçerli onay zincirini çözer.
    /// Akış seçimi: kaydın hizmetlerine özel akış varsa o, yoksa ServiceId = null
    /// olan varsayılan akış. Adımlar StepNo sırasıyla döner; AmountThreshold dolu
    /// olan adımlar yalnızca tutar eşiği AŞILIYORSA listeye girer.
    /// </summary>
    public async Task<ApprovalChain> ResolveForWorkRecordAsync(WorkRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var serviceIds = record.WorkRecordLines.Count > 0
            ? record.WorkRecordLines.Select(l => l.ServiceId).Distinct().ToList()
            : await _db.WorkRecordLines.AsNoTracking()
                .Where(l => l.WorkRecordId == record.WorkRecordId)
                .Select(l => l.ServiceId)
                .Distinct()
                .ToListAsync(cancellationToken);

        // Tutar henüz hesaplanmadıysa (gönderim öncesi) eşik karşılaştırması 0
        // üzerinden yapılır: eşikli adımlar devreye girmez. Gönderim sırasında
        // TotalAmount dolu olduğu için pratikte bu yol tetiklenmez.
        var amount = record.TotalAmount ?? 0m;

        return await ResolveAsync(DocumentType.WORK_RECORD, serviceIds, amount, cancellationToken);
    }

    public async Task<ApprovalChain> ResolveAsync(
        DocumentType documentType, IReadOnlyCollection<int> serviceIds, decimal amount, CancellationToken cancellationToken = default)
    {
        var flow = await ResolveFlowAsync(documentType, serviceIds, cancellationToken);

        var allSteps = await _db.ApprovalFlowSteps.AsNoTracking()
            .Include(s => s.Role)
            .Where(s => s.FlowId == flow.FlowId)
            .OrderBy(s => s.StepNo)
            .ToListAsync(cancellationToken);

        if (allSteps.Count == 0)
        {
            throw new ApprovalFlowException(
                $"\"{flow.Name}\" onay akışında hiç adım tanımlı değil. Onay adımları tanımlanmadan kayıt onaya gönderilemez.");
        }

        // AmountThreshold: "eşiği AŞIYORSA devreye girer" — eşit tutar eşiği aşmaz.
        var applicable = allSteps.Where(s => s.AmountThreshold is null || amount > s.AmountThreshold.Value).ToList();

        if (applicable.Count == 0)
        {
            // CLAUDE.md kural 5: otomatik onay YOKTUR. Hiç adım kalmadıysa kaydı
            // sessizce onaylamak yerine akış verisinin eksik olduğunu söylüyoruz.
            throw new ApprovalFlowException(
                $"\"{flow.Name}\" akışındaki tüm adımlar tutar eşiğinin üzerinde tanımlı; {amount:N2} tutarlı kayıt için onaylayacak adım kalmıyor. " +
                "Eşiksiz en az bir adım tanımlanmalıdır.");
        }

        return new ApprovalChain { Flow = flow, Steps = applicable };
    }

    private async Task<ApprovalFlow> ResolveFlowAsync(
        DocumentType documentType, IReadOnlyCollection<int> serviceIds, CancellationToken cancellationToken)
    {
        // 1) Hizmete özel akış.
        if (serviceIds.Count > 0)
        {
            var serviceFlows = await _db.ApprovalFlows.AsNoTracking()
                .Where(f => f.DocumentType == documentType && f.IsActive && f.ServiceId != null && serviceIds.Contains(f.ServiceId!.Value))
                .OrderBy(f => f.FlowId)
                .ToListAsync(cancellationToken);

            if (serviceFlows.Count > 1)
            {
                // Kaydın satırları farklı hizmetlere ait ve her biri farklı bir akışa
                // işaret ediyor. Birini keyfî seçmek yerine durumu bildiriyoruz.
                var names = string.Join(", ", serviceFlows.Select(f => $"\"{f.Name}\""));
                throw new ApprovalFlowException(
                    $"Bu kaydın hizmetleri birden fazla onay akışına işaret ediyor ({names}). " +
                    "Kaydı tek akışa düşen hizmetlerle ayırın veya akış tanımlarını düzeltin.");
            }

            if (serviceFlows.Count == 1)
            {
                return serviceFlows[0];
            }
        }

        // 2) Varsayılan akış (ServiceId = null).
        var defaultFlow = await _db.ApprovalFlows.AsNoTracking()
            .Where(f => f.DocumentType == documentType && f.IsActive && f.ServiceId == null)
            .OrderBy(f => f.FlowId)
            .FirstOrDefaultAsync(cancellationToken);

        if (defaultFlow is null)
        {
            throw new ApprovalFlowException(
                "Bu belge tipi için tanımlı bir onay akışı yok. Varsayılan onay akışı tanımlanmadan kayıt onaya gönderilemez.");
        }

        return defaultFlow;
    }
}
