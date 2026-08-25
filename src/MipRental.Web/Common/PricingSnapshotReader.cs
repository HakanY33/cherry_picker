using System.Text.Json;

namespace MipRental.Web.Common;

/// <summary>
/// WorkRecordLine.PricingRuleSnapshot içindeki Türkçe fiyat açıklamasını okur.
///
/// Açıklama yeniden HESAPLANMAZ: onay ekranında gösterilen metin, kaydın
/// gönderildiği anda dondurulmuş olan metnin ta kendisidir (CLAUDE.md kural 2).
/// Sözleşme sonradan değişse bile onaylayan, alt yüklenicinin gördüğü açıklamayı
/// görür — itirazda taraflar aynı belgeye bakar.
/// </summary>
public static class PricingSnapshotReader
{
    public static IReadOnlyList<string> ReadExplanation(string? snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(snapshotJson);
            if (!document.RootElement.TryGetProperty("explanation", out var explanation)
                || explanation.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            return explanation.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToList();
        }
        catch (JsonException)
        {
            // Bozuk/eski formatlı snapshot ekranı patlatmaz; ham JSON zaten
            // detay bölümünde ayrıca gösteriliyor.
            return Array.Empty<string>();
        }
    }
}
