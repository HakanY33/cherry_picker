using ClosedXML.Excel;
using MipRental.Web.Models.EquipmentOperations;

namespace MipRental.Web.Documents;

/// <summary>
/// ADIM 18 — Ekipman Müdürlüğü aylık operasyon tablosunun .xlsx çıktısı.
/// MIP'ten gelen formun birebir karşılığı.
///
/// MonthlySummaryExcelBuilder ile KARIŞTIRILMAMALI: o, Bütçe'nin üzerine formül
/// yazacağı hakediş dosyasıdır ve tutar taşır. Bu dosyada TEK BİR PARA HÜCRESİ
/// YOKTUR — ne sütun, ne toplam, ne dipnot. Zaten kaynak model
/// (EquipmentOperationTable) hiç decimal taşımıyor; ikisi birbirini tutuyor.
///
/// TOPLAM SATIRI YOKTUR ve bilinçlidir: MIP'in formunda yok. Süre toplamı
/// eklemek "şu ay kaç saat çalışıldı" sorusunun cevabını verirdi ve o soru
/// hakedişin sorusudur; bu form onay içindir.
///
/// HÜCRE TİPİ DOSYADA YAZILIDIR (CSV'den taşınırken öğrenilen ders):
///   İş tarihi          -> TARİH hücresi  (tarih aritmetiği çalışır)
///   Başlama/bitiş saati -> SAAT hücresi  (saat farkı alınabilir)
///   Dönem / fiş no      -> METİN         ("2026 / 01" sayı değildir)
///
/// SAYI BİÇİMİ NOTU: OOXML biçim kodu her zaman NOKTA ondalık söz dizimiyle
/// saklanır; Excel açan makinenin diline göre gösterir.
/// </summary>
public static class EquipmentOperationExcelBuilder
{
    private const string SheetName = "Operasyon Tablosu";

    private const string DateFormat = "dd.MM.yyyy";
    private const string TimeFormat = "HH:mm";

    /// <summary>Excel'in en üstündeki form başlığı — MIP'in formundaki yazımıyla.</summary>
    public const string FormTitle = "Cherrypicker Rental Approval Form / Çekici Kiralama Onay Formu";

    public const string PreparedByTitle = "Formu hazırlayanın bilgileri:";

    public static readonly string[] PreparedByLabels =
    [
        "Full Name / İsim Soyad",
        "Position / Görevi",
        "Department / Bölüm",
        "Date of Issue / Düzenleme Tarihi"
    ];

    /// <summary>
    /// Sütun başlıkları — SIRA VE YAZIM MIP'in formundan, DEĞİŞTİRİLEMEZ.
    /// Formu okuyan kişi sütunları göz kararı eşleştiriyor; "İş Tarihi" gibi
    /// kısaltılmış bir başlık bu eşleştirmeyi bozar. Testte birebir doğrulanır.
    /// </summary>
    public static readonly string[] Headers =
    [
        "İşin Dönemi/Period (Ay ve Yıl)",
        "Yapılan İşin Tarihi/Date of Work",
        "İşin Yapıldığı Yer/Location of Work",
        "Yapılan İşin Tanımı/Description of Work",
        "Talep Eden Müdürlük",
        "Tonajı / Vinç Türü",
        "İşin Başlama Saati/Time to Start Work",
        "İşin Bitiş Saati/Time to Finish Work",
        "Fiş No"
    ];

    // Sütunlar (1 tabanlı). Sıra Headers ile aynıdır ve öyle kalmalıdır.
    private const int ColPeriod = 1;
    private const int ColWorkDate = 2;
    private const int ColLocation = 3;
    private const int ColDescription = 4;
    private const int ColDepartment = 5;
    private const int ColVariant = 6;
    private const int ColStartTime = 7;
    private const int ColEndTime = 8;
    private const int ColReceiptNo = 9;

    /// <summary>Tablo başlıklarının bulunduğu satır. Üstünde form bilgi bloğu vardır.</summary>
    public const int HeaderRow = 7;

    public static byte[] Build(EquipmentOperationTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(SheetName);

        WriteFormHeader(sheet, table.Header);

        for (var i = 0; i < Headers.Length; i++)
        {
            sheet.Cell(HeaderRow, i + 1).Value = Headers[i];
        }

        sheet.Row(HeaderRow).Style.Font.Bold = true;

        var row = HeaderRow + 1;

        foreach (var line in table.Rows)
        {
            sheet.Cell(row, ColPeriod).Value = line.PeriodLabel;
            SetDate(sheet.Cell(row, ColWorkDate), line.WorkDate);
            SetText(sheet.Cell(row, ColLocation), line.Location);
            SetText(sheet.Cell(row, ColDescription), line.WorkDescription);
            SetText(sheet.Cell(row, ColDepartment), line.DepartmentName);
            SetText(sheet.Cell(row, ColVariant), line.VariantNames);
            SetTime(sheet.Cell(row, ColStartTime), line.StartTime);
            SetTime(sheet.Cell(row, ColEndTime), line.EndTime);
            SetText(sheet.Cell(row, ColReceiptNo), line.ReceiptNo);
            row++;
        }

        // Başlık satırı ve üstündeki form bloğu donduruluyor: tablo bir ayda
        // yüzlerce satır olabiliyor.
        sheet.SheetView.FreezeRows(HeaderRow);

        sheet.Columns().AdjustToContents();

        foreach (var column in sheet.ColumnsUsed())
        {
            // Üst blok tek hücrede uzun bir başlık taşıyor; genişlik hesabına
            // katılırsa A sütunu ekranı kaplar. Üst sınır onu keser.
            column.Width = Math.Clamp(column.Width + 1, 12, 42);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Aylik-Operasyon-2026-01.xlsx</summary>
    public static string BuildFileName(EquipmentOperationTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return $"Aylik-Operasyon-{table.Year}-{table.Month:00}.xlsx";
    }

    private static void WriteFormHeader(IXLWorksheet sheet, EquipmentOperationFormHeader header)
    {
        sheet.Cell(1, 1).Value = FormTitle;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Range(1, 1, 1, Headers.Length).Merge();

        sheet.Cell(2, 1).Value = PreparedByTitle;
        sheet.Cell(2, 1).Style.Font.Italic = true;

        // Etiket A sütununda, değer B'de. Etiketler sabit sırada (PreparedByLabels).
        sheet.Cell(3, 1).Value = PreparedByLabels[0];
        SetText(sheet.Cell(3, 2), header.FullName);

        sheet.Cell(4, 1).Value = PreparedByLabels[1];
        SetText(sheet.Cell(4, 2), header.Position);

        sheet.Cell(5, 1).Value = PreparedByLabels[2];
        SetText(sheet.Cell(5, 2), header.DepartmentName);

        sheet.Cell(6, 1).Value = PreparedByLabels[3];
        SetDate(sheet.Cell(6, 2), header.IssueDate);
    }

    /// <summary>
    /// Boş metin hücresine değer ATANMAZ. Fiş numarası olmayan kayıtta hücre
    /// BOŞ kalır — "-" yazılsaydı Excel'de dolu bir hücre olurdu ve "fişi
    /// olmayan işleri say" gibi basit bir filtre/COUNTA yanlış sayardı. Boş
    /// hücre "bilgi yok" demenin tek dürüst yoludur.
    /// </summary>
    private static void SetText(IXLCell cell, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            cell.Value = value;
        }
    }

    private static void SetDate(IXLCell cell, DateOnly value)
    {
        cell.Value = value.ToDateTime(TimeOnly.MinValue);
        cell.Style.NumberFormat.Format = DateFormat;
    }

    /// <summary>
    /// Saat, GERÇEK saat hücresi olarak yazılır (günün kesri). Metin yazılsaydı
    /// bitiş - başlama çıkarması Excel'de çalışmazdı.
    /// </summary>
    private static void SetTime(IXLCell cell, TimeOnly? value)
    {
        if (value is not { } time)
        {
            return;
        }

        cell.Value = time.ToTimeSpan();
        cell.Style.NumberFormat.Format = TimeFormat;
    }
}
