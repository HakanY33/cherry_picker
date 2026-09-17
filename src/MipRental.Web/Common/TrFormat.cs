using System.Globalization;

namespace MipRental.Web.Common;

/// <summary>
/// Ekran, PDF ve CSV'de kullanılan Türkçe biçimlerin TEK kaynağı.
///
/// Sunucunun kültürü ne olursa olsun (Docker'da çoğunlukla InvariantCulture'dır)
/// çıktı hep tr-TR olsun diye kültür açıkça veriliyor — CurrentCulture'a güvenilmiyor.
///   Para   : 1.250,00 TL
///   Tarih  : 19.08.2026
///   Miktar : 7,5
/// </summary>
public static class TrFormat
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>1250.5m -> "1.250,50"</summary>
    public static string Money(decimal value) => value.ToString("#,##0.00", Culture);

    /// <summary>1250.5m, "TRY" -> "1.250,50 TL"</summary>
    public static string MoneyWithCurrency(decimal value, string? currency) =>
        $"{Money(value)} {CurrencyLabel(currency)}";

    /// <summary>
    /// Miktar: gereksiz sıfır kuyruğu gösterilmez. 7.50 -> "7,5", 7.25 -> "7,25", 8 -> "8".
    /// Saat ve adet aynı sütunda görüneceği için sabit ondalık zorlamıyoruz.
    /// </summary>
    public static string Quantity(decimal value) => value.ToString("0.####", Culture);

    /// <summary>Birim fiyat her zaman iki ondalıkla; sözleşme fiyatı öyle okunur.</summary>
    public static string UnitPrice(decimal value) => value.ToString("#,##0.00", Culture);

    public static string Date(DateOnly value) => value.ToString("dd.MM.yyyy", Culture);

    public static string Date(DateTime value) => value.ToString("dd.MM.yyyy", Culture);

    /// <summary>
    /// Veritabanındaki UTC damgayı YEREL saate çevirip gösterir
    /// (CLAUDE.md: veritabanında UTC, ekranda yerel saat).
    /// </summary>
    public static string DateTimeLocal(DateTime utcValue) =>
        DateTime.SpecifyKind(utcValue, DateTimeKind.Utc).ToLocalTime().ToString("dd.MM.yyyy HH:mm", Culture);

    public static string Time(TimeOnly value) => value.ToString("HH\\:mm", Culture);

    /// <summary>
    /// Süre: "7 saat 30 dakika". Teyit ekranının okunması gereken tek sayısı bu
    /// olduğu için ondalık saat (7,5) değil konuşma dili kullanılıyor — sahada
    /// işi yaptıran kişi "yedi buçuk saat" diye hatırlar, "7,5" diye değil.
    /// </summary>
    public static string Duration(TimeSpan value)
    {
        var total = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        var hours = (int)total.TotalHours;
        var minutes = total.Minutes;

        if (hours == 0)
        {
            return $"{minutes} dakika";
        }

        return minutes == 0 ? $"{hours} saat" : $"{hours} saat {minutes} dakika";
    }

    /// <summary>
    /// ISO para kodunu Türkçe kısaltmaya çevirir. Faz 1'de TRY dışına çıkılmıyor
    /// ama sözleşme para birimi alanı serbest, o yüzden bilinmeyen kod aynen yazılır.
    /// </summary>
    public static string CurrencyLabel(string? currency) => currency switch
    {
        null or "" => "TL",
        "TRY" => "TL",
        "USD" => "USD",
        "EUR" => "EUR",
        _ => currency
    };

    public static string MonthName(int month) => PeriodStatusDisplay.GetMonthName(month);

    /// <summary>"Ağustos 2026" — dönem başlığı.</summary>
    public static string PeriodName(int year, int month) => $"{MonthName(month)} {year}";
}
