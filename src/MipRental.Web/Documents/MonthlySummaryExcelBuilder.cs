using ClosedXML.Excel;
using MipRental.Domain.Reporting;
using MipRental.Web.Common;

namespace MipRental.Web.Documents;

/// <summary>
/// Aylık icmalin Excel (.xlsx) çıktısı — Bütçe'nin ÜZERİNE FORMÜL YAZACAĞI dosya.
///
/// Önceki CSV çıktısının yerini alır. CSV'de hücre TİPİ diye bir kavram yoktu;
/// tip, Excel'in açarken yaptığı yoruma kalıyordu ve o yorum kullanıcının Windows
/// bölge ayarına göre değişiyordu. Burada tip dosyanın kendisinde yazılı:
///   - Tutar / miktar / birim fiyat -> SAYI hücresi   (SUM, çarpma çalışır)
///   - İş tarihi / onay tarihi      -> TARİH hücresi  (tarih aritmetiği çalışır)
///
/// SAYI BİÇİMİ NOTU: OOXML'de biçim kodu her zaman NOKTA ondalık / VİRGÜL binlik
/// söz dizimiyle saklanır; Excel bunu açan makinenin diline göre gösterir. Yani
/// "#,##0.00" kodu Türkçe Excel'de "1.250,75" olarak görünür — istenen biçim budur.
/// Koda doğrudan "#.##0,00" yazmak bozuk bir biçim üretirdi.
/// </summary>
public static class MonthlySummaryExcelBuilder
{
    private const string SheetName = "Aylık İcmal";

    /// <summary>Para sütunları: binlik ayıraçlı, iki ondalıklı (tr-TR'de 1.250,75).</summary>
    private const string MoneyFormat = "#,##0.00";

    /// <summary>Miktar: saat ile adet aynı sütunda, gereksiz sıfır kuyruğu gösterilmez.</summary>
    private const string QuantityFormat = "#,##0.####";

    private const string DateFormat = "dd.MM.yyyy";
    private const string DateTimeFormat = "dd.MM.yyyy hh:mm";

    private static readonly string[] Headers =
    [
        "Belge No", "İş Tarihi", "Lokasyon", "Hizmet", "Varyant",
        "Ham Miktar", "Faturalanan Miktar", "Birim", "Birim Fiyat", "Tutar",
        "Durum", "Onaylayan", "Onay Tarihi"
    ];

    // Sütun numaraları (1 tabanlı) — biçimlendirme tek yerden verilsin diye adlandırıldı.
    private const int ColDocumentNo = 1;
    private const int ColWorkDate = 2;
    private const int ColLocation = 3;
    private const int ColService = 4;
    private const int ColVariant = 5;
    private const int ColRawQuantity = 6;
    private const int ColBillableQuantity = 7;
    private const int ColUnit = 8;
    private const int ColUnitPrice = 9;
    private const int ColAmount = 10;
    private const int ColStatus = 11;
    private const int ColApprovedBy = 12;
    private const int ColApprovedAt = 13;

    public static byte[] Build(MonthlySummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(SheetName);

        for (var i = 0; i < Headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = Headers[i];
        }

        sheet.Row(1).Style.Font.Bold = true;

        var row = 2;

        foreach (var group in summary.ServiceGroups)
        {
            foreach (var line in group.Lines)
            {
                sheet.Cell(row, ColDocumentNo).Value = line.DocumentNo;
                SetDate(sheet.Cell(row, ColWorkDate), line.WorkDate);
                SetText(sheet.Cell(row, ColLocation), line.Location);
                sheet.Cell(row, ColService).Value = line.ServiceName;
                SetText(sheet.Cell(row, ColVariant), line.VariantName);
                SetQuantity(sheet.Cell(row, ColRawQuantity), line.RawQuantity);
                SetQuantity(sheet.Cell(row, ColBillableQuantity), line.BillableQuantity);
                sheet.Cell(row, ColUnit).Value = ServiceUnitDisplay.GetLabel(line.Unit);
                SetMoney(sheet.Cell(row, ColUnitPrice), line.UnitPrice);
                SetMoney(sheet.Cell(row, ColAmount), line.LineAmount);
                sheet.Cell(row, ColStatus).Value = WorkRecordStatusDisplay.GetLabel(line.Status);
                SetText(sheet.Cell(row, ColApprovedBy), line.ApprovedByName);

                if (line.ApprovedAt is { } approvedAt)
                {
                    SetDateTimeLocal(sheet.Cell(row, ColApprovedAt), approvedAt);
                }

                row++;
            }
        }

        // Mobilizasyon AYRI SATIR olarak, kayıt başına BİR KEZ. Satır tutarlarına
        // dahil olmadığı için "Hizmet" sütununda ayrı bir kalem adıyla görünür;
        // Bütçe pivotladığında hizmet tutarlarıyla karışmaz. Miktar ve birim fiyat
        // hücreleri BOŞ bırakılır — 0 yazılsaydı ortalamaları bozardı.
        foreach (var mobilization in summary.Mobilizations)
        {
            sheet.Cell(row, ColDocumentNo).Value = mobilization.DocumentNo;
            SetDate(sheet.Cell(row, ColWorkDate), mobilization.WorkDate);
            sheet.Cell(row, ColService).Value = "Mobilizasyon Bedeli";
            SetMoney(sheet.Cell(row, ColAmount), mobilization.Amount);
            row++;
        }

        // Başlık satırı donduruluyor: icmal yüzlerce satır olabiliyor, Bütçe
        // aşağı indiğinde hangi sütunda olduğunu görmeli.
        sheet.SheetView.FreezeRows(1);

        // Sütun genişlikleri içeriğe göre. Lokasyon serbest metin olduğu için
        // üst sınır konuyor; yoksa tek uzun satır sütunu ekrandan taşırıyor.
        sheet.Columns().AdjustToContents();

        foreach (var column in sheet.ColumnsUsed())
        {
            column.Width = Math.Clamp(column.Width + 1, 10, 40);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Excel dosya adı: Aylik-Icmal-FIRMAKODU-2026-08.xlsx</summary>
    public static string BuildFileName(MonthlySummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return $"Aylik-Icmal-{summary.FirmCode}-{summary.Year}-{summary.Month:00}.xlsx";
    }

    /// <summary>
    /// Boş metin hücresine değer ATANMAZ. string.Empty atansaydı hücre "metin
    /// içeren dolu hücre" olurdu ve Bütçe'nin COUNTA / boşluk sayımları şaşardı.
    /// </summary>
    private static void SetText(IXLCell cell, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            cell.Value = value;
        }
    }

    private static void SetMoney(IXLCell cell, decimal value)
    {
        cell.Value = value;
        cell.Style.NumberFormat.Format = MoneyFormat;
    }

    private static void SetQuantity(IXLCell cell, decimal value)
    {
        cell.Value = value;
        cell.Style.NumberFormat.Format = QuantityFormat;
    }

    private static void SetDate(IXLCell cell, DateOnly value)
    {
        cell.Value = value.ToDateTime(TimeOnly.MinValue);
        cell.Style.NumberFormat.Format = DateFormat;
    }

    /// <summary>UTC damga yerel saate çevrilip yazılır (CLAUDE.md: ekranda yerel saat).</summary>
    private static void SetDateTimeLocal(IXLCell cell, DateTime utcValue)
    {
        cell.Value = DateTime.SpecifyKind(utcValue, DateTimeKind.Utc).ToLocalTime();
        cell.Style.NumberFormat.Format = DateTimeFormat;
    }
}
