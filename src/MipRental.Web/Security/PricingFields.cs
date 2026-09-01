namespace MipRental.Web.Security;

/// <summary>
/// Hangi kolonlar "para" sayılır — tek doğru kaynak (Adım 9).
///
/// Denetim izi ham kolon adı/değeri tuttuğu için maskeleme metin seviyesinde
/// yapılmak zorunda; bu liste o maskelemenin dayanağıdır. Yeni bir para kolonu
/// eklenirse BURAYA da eklenir.
///
/// Para SAYILMAYAN alanlar bilinçli olarak dışarıda: RawQuantity,
/// BillableQuantity, Unit, yuvarlama ve minimum bilgisi miktarla ilgilidir.
/// </summary>
public static class PricingFields
{
    public const string MaskedValue = "•••";

    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        // ContractLine
        "UnitPrice", "DailyPrice", "MobilizationFee",
        // ContractLineSurcharge
        "Multiplier", "FixedAmount",
        // WorkRecordLine
        "UnitPriceSnapshot", "LineAmount", "SurchargeAmount", "BaseAmount",
        // WorkRecord
        "TotalAmount", "LinesAmount",
        // İçinde birim fiyat geçer.
        "PricingRuleSnapshot"
    };

    public static bool IsMoney(string? fieldName) => fieldName is not null && Names.Contains(fieldName);

    /// <summary>Para alanıysa değeri maskeler, değilse aynen döner.</summary>
    public static string? Mask(string? fieldName, string? value) =>
        IsMoney(fieldName) && value is not null ? MaskedValue : value;
}
