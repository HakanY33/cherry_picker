using System.Globalization;

namespace MipRental.Domain.Pricing;

// Satır tutarlarını çalışma kaydı (WorkRecord) toplamına çevirir.
//
// Var oluş sebebi: mobilizasyon (sefer başı nakliye) bedeli SATIR değil KAYIT
// seviyesinde bir bedeldir. Bir çalışma kaydı = araç/ekibin sahaya bir kez gelmesi
// demektir; kayıtta 1 satır da olsa 5 satır da olsa nakliye bir kez yapılmıştır ve
// bir kez faturalanır. PricingCalculator bu yüzden bedeli satır tutarına eklemez,
// buraya BİR KEZ eklenir.
//
// PricingCalculator gibi bu sınıf da saftır: veritabanı erişimi, DateTime.Now veya
// başka dış bağımlılık YOK.
public static class RecordTotalCalculator
{
    private static readonly CultureInfo TrCulture = CultureInfo.GetCultureInfo("tr-TR");

    public static RecordTotalResult Calculate(IReadOnlyList<PricingResult> lineResults)
    {
        ArgumentNullException.ThrowIfNull(lineResults);

        if (lineResults.Count == 0)
        {
            throw new PricingException("Tutar hesaplanacak hizmet satırı yok.");
        }

        var currency = lineResults[0].Currency;
        if (lineResults.Any(r => !string.Equals(r.Currency, currency, StringComparison.Ordinal)))
        {
            // Farklı para birimleri toplanamaz; sessizce yanlış bir toplam üretmek yerine
            // kaydı reddediyoruz (sözleşme fiyat satırlarının düzeltilmesi gerekir).
            throw new PricingException(
                "Kaydın satırları farklı para birimlerinde fiyatlandırılmış; tek bir toplam hesaplanamaz. Sözleşme fiyat satırlarını kontrol edin.");
        }

        var linesAmount = lineResults.Sum(r => r.LineAmount);

        // Satırlar farklı sözleşme satırlarına düşebilir ve her birinin kendi
        // mobilizasyon bedeli olabilir. Sefer bir tane olduğu için bedel de bir tanedir:
        // aynı seferde sahaya çıkan en yüksek bedelli hizmet esas alınır (toplanmaz).
        var mobilizationFee = lineResults.Max(r => r.MobilizationFee);

        var explanation = new List<string>
        {
            $"Satır tutarları toplamı ({lineResults.Count} satır): {FormatMoney(linesAmount)} {currency}"
        };

        if (mobilizationFee > 0m)
        {
            explanation.Add($"Mobilizasyon bedeli (sefer başına bir kez): {FormatMoney(mobilizationFee)} {currency}");
        }

        var totalAmount = linesAmount + mobilizationFee;
        explanation.Add($"Kayıt toplamı: {FormatMoney(totalAmount)} {currency}");

        return new RecordTotalResult
        {
            LinesAmount = linesAmount,
            MobilizationFee = mobilizationFee,
            TotalAmount = totalAmount,
            Currency = currency,
            Explanation = explanation
        };
    }

    private static string FormatMoney(decimal value) => value.ToString("N2", TrCulture);
}
