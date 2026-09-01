using System.Text.Json;

namespace MipRental.Web.Common;

/// <summary>
/// WorkRecordLine.PricingRuleSnapshot içindeki Türkçe fiyat açıklamasını okur.
///
/// Açıklama yeniden HESAPLANMAZ: onay ekranında gösterilen metin, kaydın
/// gönderildiği anda dondurulmuş olan metnin ta kendisidir (CLAUDE.md kural 2).
/// Sözleşme sonradan değişse bile onaylayan, alt yüklenicinin gördüğü açıklamayı
/// görür — itirazda taraflar aynı belgeye bakar.
///
/// Adım 9'dan sonra snapshot açıklamayı İKİ ayrı dizide tutar:
///   quantityExplanation -> para geçmez, herkese gösterilir
///   amountExplanation   -> para geçer, sadece CanSeePricing olana gösterilir
///
/// Adım 9 ÖNCESİ kayıtlarda sadece birleşik "explanation" dizisi vardır ve içinde
/// para geçer. Bu yüzden eski kayıtlarda miktar açıklaması BOŞ döner: birleşik
/// metni ayırmak metin ayrıştırmayı gerektirirdi ve gizliliği kırılgan bir
/// regex'e bağlardı. Yetkisiz kullanıcıya eksik bilgi göstermek, yanlışlıkla
/// tutar sızdırmaktan iyidir.
/// </summary>
public static class PricingSnapshotReader
{
    /// <summary>Herkese gösterilebilir miktar açıklaması. Eski snapshot'ta boştur.</summary>
    public static IReadOnlyList<string> ReadQuantityExplanation(string? snapshotJson) =>
        ReadArray(snapshotJson, "quantityExplanation");

    /// <summary>
    /// Para içeren açıklama. YALNIZCA CanSeePricing yetkisi olana gösterilir.
    /// Eski snapshot'larda birleşik "explanation" dizisine düşer.
    /// </summary>
    public static IReadOnlyList<string> ReadAmountExplanation(string? snapshotJson)
    {
        var amounts = ReadArray(snapshotJson, "amountExplanation");
        return amounts.Count > 0 ? amounts : ReadArray(snapshotJson, "explanation");
    }

    private static IReadOnlyList<string> ReadArray(string? snapshotJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(snapshotJson);
            if (!document.RootElement.TryGetProperty(propertyName, out var array)
                || array.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            return array.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToList();
        }
        catch (JsonException)
        {
            // Bozuk/eski formatlı snapshot ekranı patlatmaz.
            return Array.Empty<string>();
        }
    }
}
