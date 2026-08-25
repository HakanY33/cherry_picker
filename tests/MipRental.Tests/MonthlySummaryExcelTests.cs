using ClosedXML.Excel;
using MipRental.Domain.Enums;
using MipRental.Domain.Reporting;
using MipRental.Web.Documents;

namespace MipRental.Tests;

/// <summary>
/// Aylık icmalin .xlsx çıktısı. Önceki CSV testlerinin yerini alır.
///
/// CSV'de hücre tipi diye bir şey yoktu; testler ancak Excel'in doğru YORUMLAMASI
/// için gereken metin biçimini doğrulayabiliyordu. Artık tip dosyada yazılı olduğu
/// için testler doğrudan onu okuyor: üretilen dosya geri açılır ve tutar hücresinin
/// SAYI, tarih hücresinin TARİH tipinde olduğu doğrulanır — Bütçe'nin üzerine
/// formül yazabilmesinin şartı budur.
/// </summary>
public class MonthlySummaryExcelTests
{
    // Sütunlar (1 tabanlı): 1 Belge No, 2 İş Tarihi, 3 Lokasyon, 4 Hizmet, 5 Varyant,
    // 6 Ham Miktar, 7 Faturalanan Miktar, 8 Birim, 9 Birim Fiyat, 10 Tutar,
    // 11 Durum, 12 Onaylayan, 13 Onay Tarihi
    private const int ColDocumentNo = 1;
    private const int ColWorkDate = 2;
    private const int ColLocation = 3;
    private const int ColService = 4;
    private const int ColRawQuantity = 6;
    private const int ColBillableQuantity = 7;
    private const int ColUnitPrice = 9;
    private const int ColAmount = 10;
    private const int ColApprovedBy = 12;
    private const int ColApprovedAt = 13;

    private static readonly DateTime ApprovedAtUtc = new(2026, 8, 20, 9, 30, 0, DateTimeKind.Utc);

    private static MonthlySummary BuildSummary(
        decimal lineAmount = 1250.75m,
        decimal unitPrice = 166.7667m,
        decimal billableQuantity = 7.5m,
        decimal mobilization = 250m,
        string location = "İskele 3 Güney Rıhtım")
    {
        var line = new MonthlySummaryLine
        {
            WorkRecordId = 1,
            DocumentNo = "WR-2026-00001",
            WorkDate = new DateOnly(2026, 8, 19),
            Location = location,
            ServiceId = 1,
            ServiceName = "Mobil Vinç",
            VariantName = "20 Ton",
            RawQuantity = 7.25m,
            BillableQuantity = billableQuantity,
            Unit = ServiceUnit.HOUR,
            UnitPrice = unitPrice,
            SurchargeAmount = 0m,
            LineAmount = lineAmount,
            Currency = "TRY",
            Status = WorkRecordStatus.APPROVED,
            ApprovedByName = "Şükrü Çağlayan",
            ApprovedAt = ApprovedAtUtc
        };

        return new MonthlySummary
        {
            PeriodId = 8,
            Year = 2026,
            Month = 8,
            PeriodStatus = PeriodStatus.OPEN,
            FirmId = 1,
            FirmCode = "TESTVINC",
            FirmTitle = "Şişli Vinç Ltd. Şti.",
            ContractNumbers = ["SÖZ-2026-001"],
            ServiceGroups =
            [
                new MonthlySummaryServiceGroup
                {
                    ServiceId = 1,
                    ServiceName = "Mobil Vinç",
                    Unit = ServiceUnit.HOUR,
                    Lines = [line],
                    SubtotalAmount = lineAmount,
                    SubtotalBillableQuantity = billableQuantity
                }
            ],
            Mobilizations = mobilization == 0m
                ? []
                : [
                    new MonthlySummaryMobilization
                    {
                        WorkRecordId = 1,
                        DocumentNo = "WR-2026-00001",
                        WorkDate = new DateOnly(2026, 8, 19),
                        Amount = mobilization,
                        Currency = "TRY"
                    }
                  ],
            LinesTotal = lineAmount,
            MobilizationTotal = mobilization,
            Currency = "TRY",
            HasMixedCurrency = false,
            RecordCount = 1,
            QuantityTotals = [new MonthlySummaryQuantityTotal { Unit = ServiceUnit.HOUR, TotalBillableQuantity = billableQuantity }],
            PendingRecordCount = 0
        };
    }

    /// <summary>Üretilen baytları gerçek bir çalışma kitabı olarak geri açar.</summary>
    private static XLWorkbook Open(MonthlySummary summary) =>
        new(new MemoryStream(MonthlySummaryExcelBuilder.Build(summary)));

    /// <summary>
    /// Görevin ana maddesi: TUTAR hücresinin tipi SAYI. Metin olsaydı Bütçe'nin
    /// SUM'ı sessizce 0 döndürürdü — en tehlikeli hata biçimi.
    /// </summary>
    [Fact]
    public void Excel_AmountCellIsNumber()
    {
        using var workbook = Open(BuildSummary(lineAmount: 1250.75m));
        var cell = workbook.Worksheets.First().Cell(2, ColAmount);

        Assert.Equal(XLDataType.Number, cell.DataType);
        Assert.Equal(1250.75m, cell.GetValue<decimal>());
    }

    /// <summary>Görevin ana maddesi: TARİH hücresinin tipi TARİH.</summary>
    [Fact]
    public void Excel_WorkDateCellIsDate()
    {
        using var workbook = Open(BuildSummary());
        var cell = workbook.Worksheets.First().Cell(2, ColWorkDate);

        Assert.Equal(XLDataType.DateTime, cell.DataType);
        Assert.Equal(new DateTime(2026, 8, 19), cell.GetValue<DateTime>());
    }

    /// <summary>Miktar ve birim fiyat da sayı — icmalde çarpım/kontrol yapılabilmeli.</summary>
    [Fact]
    public void Excel_QuantityAndUnitPriceCellsAreNumbers()
    {
        using var workbook = Open(BuildSummary(unitPrice: 166.75m, billableQuantity: 7.5m));
        var sheet = workbook.Worksheets.First();

        Assert.Equal(XLDataType.Number, sheet.Cell(2, ColRawQuantity).DataType);
        Assert.Equal(7.25m, sheet.Cell(2, ColRawQuantity).GetValue<decimal>());

        Assert.Equal(XLDataType.Number, sheet.Cell(2, ColBillableQuantity).DataType);
        Assert.Equal(7.5m, sheet.Cell(2, ColBillableQuantity).GetValue<decimal>());

        Assert.Equal(XLDataType.Number, sheet.Cell(2, ColUnitPrice).DataType);
        Assert.Equal(166.75m, sheet.Cell(2, ColUnitPrice).GetValue<decimal>());
    }

    /// <summary>Onay damgası UTC saklanır, dosyaya YEREL saatle yazılır ve tarih tipindedir.</summary>
    [Fact]
    public void Excel_ApprovedAtCellIsDateInLocalTime()
    {
        using var workbook = Open(BuildSummary());
        var cell = workbook.Worksheets.First().Cell(2, ColApprovedAt);

        Assert.Equal(XLDataType.DateTime, cell.DataType);
        Assert.Equal(ApprovedAtUtc.ToLocalTime(), cell.GetValue<DateTime>());
    }

    /// <summary>
    /// Para sütunları #.##0,00 gösterilmeli. OOXML biçim kodu her zaman nokta
    /// ondalık / virgül binlik söz dizimiyle SAKLANIR; Türkçe Excel bunu
    /// "1.250,75" olarak gösterir. Saklanan kodu doğruluyoruz.
    /// </summary>
    [Fact]
    public void Excel_MoneyColumnsUseTwoDecimalThousandsFormat()
    {
        using var workbook = Open(BuildSummary());
        var sheet = workbook.Worksheets.First();

        Assert.Equal("#,##0.00", sheet.Cell(2, ColAmount).Style.NumberFormat.Format);
        Assert.Equal("#,##0.00", sheet.Cell(2, ColUnitPrice).Style.NumberFormat.Format);
        Assert.Equal("dd.MM.yyyy", sheet.Cell(2, ColWorkDate).Style.NumberFormat.Format);
    }

    [Fact]
    public void Excel_HeaderRowIsBoldAndHasExpectedColumns()
    {
        using var workbook = Open(BuildSummary());
        var sheet = workbook.Worksheets.First();

        var headers = Enumerable.Range(1, 13).Select(c => sheet.Cell(1, c).GetString()).ToArray();

        Assert.Equal(
            new[]
            {
                "Belge No", "İş Tarihi", "Lokasyon", "Hizmet", "Varyant",
                "Ham Miktar", "Faturalanan Miktar", "Birim", "Birim Fiyat", "Tutar",
                "Durum", "Onaylayan", "Onay Tarihi"
            },
            headers);

        foreach (var column in Enumerable.Range(1, 13))
        {
            Assert.True(sheet.Cell(1, column).Style.Font.Bold, $"{column}. baslik hucresi kalin degil");
        }
    }

    /// <summary>Sütun genişlikleri içeriğe göre ayarlanır — varsayılanda kalmaz.</summary>
    [Fact]
    public void Excel_ColumnWidthsAreAdjustedToContents()
    {
        using var workbook = Open(BuildSummary(location: "İskele 3 Güney Rıhtım Uzun Lokasyon Adı"));
        var sheet = workbook.Worksheets.First();

        var locationWidth = sheet.Column(ColLocation).Width;
        var documentWidth = sheet.Column(ColDocumentNo).Width;

        // Uzun lokasyon metni, kısa belge no sütunundan geniş olmalı.
        Assert.True(locationWidth > documentWidth,
            $"Lokasyon sutunu ({locationWidth}) belge no sutunundan ({documentWidth}) genis degil");

        // Serbest metin sütunu üst sınırı aşmamalı.
        Assert.True(locationWidth <= 40, $"Lokasyon sutunu cok genis: {locationWidth}");
    }

    [Fact]
    public void Excel_PreservesTurkishCharacters()
    {
        using var workbook = Open(BuildSummary());
        var sheet = workbook.Worksheets.First();

        Assert.Equal("İş Tarihi", sheet.Cell(1, ColWorkDate).GetString());
        Assert.Equal("Mobil Vinç", sheet.Cell(2, ColService).GetString());
        Assert.Equal("Şükrü Çağlayan", sheet.Cell(2, ColApprovedBy).GetString());
        Assert.Equal("İskele 3 Güney Rıhtım", sheet.Cell(2, ColLocation).GetString());
    }

    /// <summary>Mobilizasyon .xlsx'te de yalnızca BİR kez, ayrı kalem olarak yer alır.</summary>
    [Fact]
    public void Excel_ListsMobilizationOnceAsSeparateRowWithBlankQuantity()
    {
        using var workbook = Open(BuildSummary(mobilization: 250m));
        var sheet = workbook.Worksheets.First();

        // başlık + hizmet satırı + mobilizasyon satırı
        Assert.Equal(3, sheet.LastRowUsed()?.RowNumber());
        Assert.Equal("Mobilizasyon Bedeli", sheet.Cell(3, ColService).GetString());

        // Tutar sayı; miktar ve birim fiyat BOŞ — 0 yazılsaydı ortalamaları bozardı.
        Assert.Equal(XLDataType.Number, sheet.Cell(3, ColAmount).DataType);
        Assert.Equal(250m, sheet.Cell(3, ColAmount).GetValue<decimal>());
        Assert.True(sheet.Cell(3, ColBillableQuantity).IsEmpty());
        Assert.True(sheet.Cell(3, ColUnitPrice).IsEmpty());
    }

    [Fact]
    public void Excel_OmitsMobilizationRowWhenThereIsNone()
    {
        using var workbook = Open(BuildSummary(mobilization: 0m));
        var sheet = workbook.Worksheets.First();

        Assert.Equal(2, sheet.LastRowUsed()?.RowNumber());
        Assert.NotEqual("Mobilizasyon Bedeli", sheet.Cell(2, ColService).GetString());
    }
}
