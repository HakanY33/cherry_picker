using MipRental.Domain.Enums;

namespace MipRental.Domain.Pricing;

public sealed class PricingResult
{
    public required decimal RawQuantity { get; init; }
    public required decimal BillableQuantity { get; init; }
    public required ServiceUnit Unit { get; init; }

    public required decimal UnitPriceApplied { get; init; }
    public required AppliedTariff AppliedTariff { get; init; }

    public required decimal BaseAmount { get; init; }
    public required decimal SurchargeAmount { get; init; }

    // Sözleşme satırında tanımlı sefer başı nakliye/mobilizasyon bedeli.
    // DİKKAT: LineAmount'a DAHİL DEĞİLDİR ve satır bazında toplanmamalıdır.
    // Bir çalışma kaydı = bir sefer olduğu için bu bedel kaydın tamamına BİR KEZ
    // uygulanır; bunu RecordTotalCalculator yapar. Buradaki değer "bu satırın
    // sözleşme satırı şu bedeli taşıyor" bilgisidir, ödenecek tutar değildir.
    public required decimal MobilizationFee { get; init; }

    // Satır tutarı = BaseAmount + SurchargeAmount. Mobilizasyon bedeli HARİÇ.
    public required decimal LineAmount { get; init; }

    public required string Currency { get; init; }

    // Uygulanan tüm parametrelerin JSON gösterimi. WorkRecordLine.PricingRuleSnapshot'a
    // aynen yazılır; sözleşme sonradan değişse bile bu satır asla değişmez.
    public required string PricingRuleSnapshot { get; init; }

    // Kullanıcıya gösterilecek / itirazda kanıt olacak Türkçe açıklama satırları.
    public required IReadOnlyList<string> Explanation { get; init; }
}
