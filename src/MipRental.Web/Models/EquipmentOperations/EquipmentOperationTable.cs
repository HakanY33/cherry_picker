using Microsoft.AspNetCore.Mvc.Rendering;

namespace MipRental.Web.Models.EquipmentOperations;

/// <summary>
/// ADIM 18 — Ekipman Müdürlüğü'nün aylık operasyon tablosu.
///
/// Hakediş (aylık icmal) ile KARIŞTIRILMAMALI: o Bütçe'nin mali belgesidir ve
/// tutar taşır, bu Ekipman Müdürlüğü'nün operasyon dökümüdür ve HİÇBİR para
/// alanı taşımaz. Bu sınıfta bilinçli olarak tek bir decimal yoktur — tutar
/// eklemek isteyen önce buraya bir alan açmak zorunda kalsın diye.
///
/// Ekran ve Excel AYNI nesneden beslenir; iki ayrı yerde "aynı tabloyu" kurmak
/// ikisinin zamanla ayrışması demekti.
/// </summary>
public sealed class EquipmentOperationTable
{
    public int PeriodId { get; init; }
    public int Year { get; init; }
    public int Month { get; init; }

    /// <summary>Formu HAZIRLAYAN, yani oturumdaki kullanıcı — Excel'in üst bloğu.</summary>
    public required EquipmentOperationFormHeader Header { get; init; }

    public List<EquipmentOperationRow> Rows { get; init; } = new();

    /// <summary>
    /// Bu dönemde henüz karara bağlanmamış çalışma kaydı sayısı. Tabloyu
    /// ENGELLEMEZ, yalnızca uyarır: "bu tablo bugünkü onaylı hâlidir".
    /// </summary>
    public int PendingCount { get; init; }

    public bool IsEmpty => Rows.Count == 0;
}

/// <summary>Excel'in tablodan ÖNCEKİ bloğu: formu kim, ne zaman hazırladı.</summary>
public sealed class EquipmentOperationFormHeader
{
    public required string FullName { get; init; }
    public string? Position { get; init; }
    public string? DepartmentName { get; init; }
    public DateOnly IssueDate { get; init; }
}

/// <summary>
/// Tablonun bir satırı — MIP'in Excel formundaki dokuz sütun, AYNI SIRADA.
/// Sütun sırası bu sınıfın alan sırasıyla aynıdır ve öyle kalmalıdır.
/// </summary>
public sealed class EquipmentOperationRow
{
    /// <summary>"2026 / 01" — yıl, boşluk, eğik çizgi, boşluk, iki haneli ay.</summary>
    public required string PeriodLabel { get; init; }

    public DateOnly WorkDate { get; init; }

    /// <summary>
    /// Lokasyonun KENDİ adı, FullPath değil: MIP'in formunda "11 Nolu Rıhtım"
    /// yazıyor, "Rıhtımlar &gt; 11 Nolu Rıhtım" değil. Lokasyon tanımsızsa
    /// kayda elle yazılan serbest metin (LocationText) kullanılır.
    /// </summary>
    public string? Location { get; init; }

    public string? WorkDescription { get; init; }

    /// <summary>Talebi açanın müdürlüğü. Talepsiz kayıtta boş kalabilir.</summary>
    public string? DepartmentName { get; init; }

    /// <summary>
    /// "Tonajı / Vinç Türü" — kaydın satırlarındaki varyant adları. Çok satırlı
    /// kayıtta hepsi virgülle yazılır; tek satıra indirgenmez, çünkü formu okuyan
    /// kişi o gün sahaya kaç vinç çıktığını bilmek zorunda.
    /// </summary>
    public string? VariantNames { get; init; }

    public TimeOnly? StartTime { get; init; }
    public TimeOnly? EndTime { get; init; }

    /// <summary>Firmanın kâğıt fişinin numarası. Kâğıt fiş kullanmayan firmada boştur.</summary>
    public string? ReceiptNo { get; init; }
}

/// <summary>Ekranın modeli: filtre kutuları + kurulmuş tablo.</summary>
public sealed class EquipmentOperationViewModel
{
    public int? PeriodId { get; set; }
    public int? DepartmentId { get; set; }
    public int? VariantId { get; set; }

    public List<SelectListItem> PeriodOptions { get; set; } = new();
    public List<SelectListItem> DepartmentOptions { get; set; } = new();
    public List<SelectListItem> VariantOptions { get; set; } = new();

    /// <summary>Dönem seçilene kadar null kalır.</summary>
    public EquipmentOperationTable? Table { get; set; }
}
