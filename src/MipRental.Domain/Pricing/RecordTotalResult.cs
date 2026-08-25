namespace MipRental.Domain.Pricing;

// Bir çalışma kaydının (WorkRecord) toplamı. RecordTotalCalculator üretir.
public sealed class RecordTotalResult
{
    // Satır tutarlarının toplamı (mobilizasyon bedeli hariç).
    public required decimal LinesAmount { get; init; }

    // Kayda BİR KEZ uygulanan sefer başı nakliye/mobilizasyon bedeli.
    public required decimal MobilizationFee { get; init; }

    public required decimal TotalAmount { get; init; }

    public required string Currency { get; init; }

    public required IReadOnlyList<string> Explanation { get; init; }
}
