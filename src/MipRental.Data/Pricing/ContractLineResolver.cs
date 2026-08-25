using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MipRental.Domain.Enums;
using MipRental.Domain.Entities;
using MipRental.Domain.Pricing;

namespace MipRental.Data.Pricing;

// CLAUDE.md kural 3: doğru sözleşme satırı İŞİN YAPILDIĞI tarihe (WorkDate) göre
// seçilir, kaydın girildiği tarihe göre değil. Veritabanı erişimi gerektirdiği için
// PricingCalculator'dan (saf hesaplama çekirdeği) ayrı tutulur.
public sealed class ContractLineResolver
{
    private readonly AppDbContext _db;

    public ContractLineResolver(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ContractLine> ResolveAsync(int firmId, int serviceId, int? variantId, DateOnly workDate, CancellationToken cancellationToken = default)
    {
        var firm = await _db.Firms.AsNoTracking().FirstOrDefaultAsync(f => f.FirmId == firmId, cancellationToken)
            ?? throw new PricingException($"{firmId} numaralı firma bulunamadı.");

        var contractsOnDate = await _db.Contracts.AsNoTracking()
            .Where(c => c.FirmId == firmId && c.StartDate <= workDate && c.EndDate >= workDate)
            .ToListAsync(cancellationToken);

        var activeContractIds = contractsOnDate
            .Where(c => c.Status == ContractStatus.ACTIVE)
            .Select(c => c.ContractId)
            .ToList();

        if (activeContractIds.Count == 0)
        {
            if (contractsOnDate.Count > 0)
            {
                throw new PricingException($"{FormatDate(workDate)} tarihinde \"{firm.Title}\" firmasının sözleşmesi aktif değil.");
            }

            throw new PricingException($"{FormatDate(workDate)} tarihinde \"{firm.Title}\" firmasının geçerli bir sözleşmesi bulunamadı.");
        }

        var candidates = await _db.ContractLines.AsNoTracking()
            .Include(l => l.ServiceCategory)
            .Include(l => l.ServiceVariant)
            .Where(l => activeContractIds.Contains(l.ContractId)
                && l.ServiceId == serviceId
                && l.VariantId == variantId
                && l.IsActive
                && l.ValidFrom <= workDate
                && (l.ValidTo == null || l.ValidTo >= workDate))
            .ToListAsync(cancellationToken);

        if (candidates.Count > 1)
        {
            throw new PricingException(
                $"{FormatDate(workDate)} tarihi için birden fazla sözleşme fiyat satırı eşleşti; veri tutarsızlığı var, sözleşme fiyat satırlarını kontrol edin.");
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        var service = await _db.ServiceCategories.AsNoTracking().FirstOrDefaultAsync(s => s.ServiceId == serviceId, cancellationToken);
        var variant = variantId is null
            ? null
            : await _db.ServiceVariants.AsNoTracking().FirstOrDefaultAsync(v => v.VariantId == variantId, cancellationToken);

        var serviceDescription = variant is null
            ? service?.Name ?? "hizmet"
            : $"{variant.Name} {service?.Name ?? "hizmet"}";

        throw new PricingException($"{FormatDate(workDate)} tarihi için {firm.Title} firmasının {serviceDescription} fiyatı tanımlı değil.");
    }

    private static string FormatDate(DateOnly date) =>
        date.ToDateTime(TimeOnly.MinValue).ToString("d MMMM yyyy", new CultureInfo("tr-TR"));
}
