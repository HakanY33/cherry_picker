using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MipRental.Web.Documents;

/// <summary>
/// PDF belgelerinin ortak görsel ayarları.
///
/// FONT KARARI — Türkçe karakterler için kritik:
/// Yazı tipi Lato'dur ve QuestPDF paketinin İÇİNDEN gelir; işletim sisteminin
/// yüklü fontlarından DEĞİL. Sunucuda hiçbir font kurulu olmasa da PDF aynı çıkar.
/// Lato'nun ç Ç ğ Ğ ı İ ö Ö ş Ş ü Ü ve ₺ glifleri tamdır — QuestPDF'te sık görülen
/// "Türkçe karakter kutu çıkıyor" sorunu, gliflerin olmadığı bir fonta düşülünce
/// yaşanır. Font ADI burada tek yerde tanımlı; şablonlar sabit metin yazmaz.
/// </summary>
public static class DocumentTheme
{
    /// <summary>
    /// Şablon sürümü. GeneratedDocuments.TemplateVersion'a yazılır: şablon
    /// değiştiğinde eski belgenin hangi düzenle üretildiği kayıtta kalır.
    /// Düzen değişirse BURASI ARTIRILMALI.
    /// </summary>
    public const string TemplateVersion = "1.0";

    /// <summary>QuestPDF ile birlikte gelen, Türkçe glif seti tam olan gömülü font.</summary>
    public const string FontFamily = Fonts.Lato;

    public const int BodySize = 9;
    public const int SmallSize = 8;
    public const int TitleSize = 15;
    public const int SectionSize = 10;

    public static readonly Color Ink = Colors.Grey.Darken4;
    public static readonly Color Muted = Colors.Grey.Darken1;
    public static readonly Color Line = Colors.Grey.Medium;
    public static readonly Color HeaderFill = Colors.Grey.Lighten3;
    public static readonly Color SubtotalFill = Colors.Grey.Lighten4;
}
