using MipRental.Domain.Enums;
using MipRental.Domain.Pricing;

namespace MipRental.Web.Models.WorkRecords;

// B2: canli fiyat onizleme sonucu.
//
// ADIM 9 - FIYAT GIZLILIGI: bu onizleme SADECE firma kullanicisina gosterilir
// (PreviewLine action'i [Authorize(FirmUser)]), ve firma kullanicisi para
// goremez. Bu yuzden model artik PricingResult'i OLDUGU GIBI TASIMAZ - icinde
// UnitPriceApplied, LineAmount ve PricingRuleSnapshot vardi.
//
// Kalan: "kac saat faturalanacak" seffafligi. Giden: tutar.
public class LinePreviewViewModel
{
    public bool HasInput { get; private init; }
    public bool Success { get; private init; }
    public string? ErrorMessage { get; private init; }

    public decimal BillableQuantity { get; private init; }
    public ServiceUnit Unit { get; private init; }

    /// <summary>Para GECMEYEN aciklama satirlari: ham sure, yuvarlama, minimum.</summary>
    public IReadOnlyList<string> QuantityExplanation { get; private init; } = Array.Empty<string>();

    public static readonly LinePreviewViewModel Empty = new() { HasInput = false };

    public static LinePreviewViewModel Failed(string message) => new()
    {
        HasInput = true,
        Success = false,
        ErrorMessage = message
    };

    /// <summary>
    /// PricingResult'tan SADECE miktar tarafi alinir. Tutar alanlari bilincli
    /// olarak kopyalanmaz; modelde oyle bir alan yoktur.
    /// </summary>
    public static LinePreviewViewModel Succeeded(PricingResult result) => new()
    {
        HasInput = true,
        Success = true,
        BillableQuantity = result.BillableQuantity,
        Unit = result.Unit,
        QuantityExplanation = result.QuantityExplanation
    };
}
