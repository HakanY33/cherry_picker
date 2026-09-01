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

    // Açıklama satırları İKİYE AYRILIR (Adım 9 — fiyat gizliliği).
    //
    // QuantityExplanation: "neden 7,5 saat" — ham süre, yuvarlama, minimum, gün
    // eşiği. İçinde para GEÇMEZ, HERKESE gösterilir. Firma "kaç saat faturalanacak"
    // şeffaflığını burada bulur.
    //
    // AmountExplanation: "7,5 × 1.250,00 = 9.375,00 TL" — birim fiyat, ek ücret,
    // satır tutarı, mobilizasyon bedeli. SADECE CanSeePricing yetkisi olana verilir.
    //
    // İkisini tek listede birleştirmeyin: birleşik listeyi sonradan ayırmak metin
    // ayrıştırmayı gerektirir ve güvenliği kırılgan bir regex'e bağlar.
    public required IReadOnlyList<string> QuantityExplanation { get; init; }
    public required IReadOnlyList<string> AmountExplanation { get; init; }
}
