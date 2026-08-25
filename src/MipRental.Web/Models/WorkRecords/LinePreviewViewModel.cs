using MipRental.Domain.Pricing;

namespace MipRental.Web.Models.WorkRecords;

// B2: canlı fiyat önizleme sonucu. Explanation satırları PricingResult'tan
// AYNEN (değiştirilmeden) gösterilir.
public class LinePreviewViewModel
{
    public bool HasInput { get; private init; }
    public bool Success { get; private init; }
    public string? ErrorMessage { get; private init; }
    public PricingResult? Result { get; private init; }

    public static readonly LinePreviewViewModel Empty = new() { HasInput = false };

    public static LinePreviewViewModel Failed(string message) => new()
    {
        HasInput = true,
        Success = false,
        ErrorMessage = message
    };

    public static LinePreviewViewModel Succeeded(PricingResult result) => new()
    {
        HasInput = true,
        Success = true,
        Result = result
    };
}
