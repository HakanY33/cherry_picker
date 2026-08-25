using MipRental.Domain.Entities;

namespace MipRental.Domain.Pricing;

// PricingCalculator'a giden girdi. Saf veri taşıyıcısı; hesap mantığı içermez.
public sealed class PricingRequest
{
    public required ContractLine ContractLine { get; init; }

    // Bu çalışma kaydına uygulanacak ek ücretler (zaten seçilmiş/uygunluğu
    // belirlenmiş liste — hangi ek ücretin uygulanacağına bu sınıf karar vermez,
    // çağıran taraf verir. Bkz. PricingCalculator dosyasındaki not).
    public IReadOnlyList<ContractLineSurcharge> ApplicableSurcharges { get; init; } = Array.Empty<ContractLineSurcharge>();

    // HOUR birimi için: başlangıç/bitiş saati.
    public TimeOnly? StartTime { get; init; }
    public TimeOnly? EndTime { get; init; }
    public bool SpansMidnight { get; init; }

    // HOUR dışındaki birimler (METER, PIECE, DAY, SHIFT) için doğrudan miktar.
    public decimal? Quantity { get; init; }
}
