using MipRental.Domain.Reporting;
using MipRental.Web.Common;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MipRental.Web.Documents;

/// <summary>
/// Aylık icmal PDF'i: firma + dönem + sözleşme no başlığı, kayıt bazında tablo,
/// hizmet bazında ara toplam, mobilizasyon kalemleri, genel toplam ve altta
/// imza/mühür alanı.
///
/// Toplamları KENDİ HESAPLAMAZ; ekranla birebir aynı olsun diye MonthlySummary'den
/// okur (tek kaynak, bkz. MonthlySummaryService).
/// </summary>
public sealed class MonthlySummaryDocument : IDocument
{
    private readonly MonthlySummary _summary;
    private readonly byte[] _qrPng;
    private readonly string _verificationCode;
    private readonly string _verificationUrl;

    public MonthlySummaryDocument(MonthlySummary summary, string verificationCode, string verificationUrl)
    {
        _summary = summary;
        _verificationCode = verificationCode;
        _verificationUrl = verificationUrl;
        _qrPng = VerificationQrCode.CreatePng(verificationUrl);
    }

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Aylık İcmal — {_summary.FirmTitle} — {TrFormat.PeriodName(_summary.Year, _summary.Month)}",
        Author = "MIP — Mersin Uluslararası Limanı",
        Creator = "MipRental"
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.4f, Unit.Centimetre);
            page.DefaultTextStyle(x => x
                .FontFamily(DocumentTheme.FontFamily)
                .FontSize(DocumentTheme.BodySize)
                .FontColor(DocumentTheme.Ink));

            page.Header().Element(ComposeHeader);
            page.Content().PaddingVertical(10).Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container) => container.Column(column =>
    {
        column.Item().Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text("MERSİN ULUSLARARASI LİMAN İŞLETMECİLİĞİ")
                    .FontSize(DocumentTheme.TitleSize).Bold();
                left.Item().Text("AYLIK HİZMET İCMALİ")
                    .FontSize(DocumentTheme.SectionSize).FontColor(DocumentTheme.Muted);
            });

            row.ConstantItem(210).Column(right =>
            {
                LabelValue(right, "Firma", $"{_summary.FirmTitle} ({_summary.FirmCode})");
                LabelValue(right, "Dönem", TrFormat.PeriodName(_summary.Year, _summary.Month));
                LabelValue(right, "Sözleşme No",
                    _summary.ContractNumbers.Count > 0 ? string.Join(", ", _summary.ContractNumbers) : "—");

                if (_summary.FilteredServiceName is not null)
                {
                    LabelValue(right, "Hizmet Tipi", _summary.FilteredServiceName);
                }
            });
        });

        column.Item().PaddingTop(6).LineHorizontal(1).LineColor(DocumentTheme.Ink);
    });

    private void ComposeContent(IContainer container) => container.Column(column =>
    {
        column.Spacing(10);

        column.Item().Element(ComposeSummaryBox);

        if (_summary.IsEmpty)
        {
            column.Item().PaddingTop(20).AlignCenter()
                .Text("Bu dönemde icmale girecek onaylanmış kayıt bulunmuyor.")
                .FontColor(DocumentTheme.Muted);
            return;
        }

        foreach (var group in _summary.ServiceGroups)
        {
            column.Item().Element(c => ComposeServiceGroup(c, group));
        }

        if (_summary.Mobilizations.Count > 0)
        {
            column.Item().Element(ComposeMobilizations);
        }

        column.Item().Element(ComposeGrandTotal);
        column.Item().PaddingTop(10).Element(ComposeSignatures);
    });

    private void ComposeSummaryBox(IContainer container) =>
        container.Background(DocumentTheme.SubtotalFill).Border(1).BorderColor(DocumentTheme.Line)
            .Padding(8).Row(row =>
            {
                Stat(row.RelativeItem(), "Kayıt Sayısı", _summary.RecordCount.ToString(TrFormat.Culture));

                var quantities = _summary.QuantityTotals.Count == 0
                    ? "—"
                    : string.Join("  ", _summary.QuantityTotals.Select(q =>
                        $"{TrFormat.Quantity(q.TotalBillableQuantity)} {ServiceUnitDisplay.GetLabel(q.Unit)}"));
                Stat(row.RelativeItem(), "Toplam Miktar", quantities);

                Stat(row.RelativeItem(), "Toplam Tutar", _summary.HasMixedCurrency
                    ? "— (farklı para birimleri)"
                    : TrFormat.MoneyWithCurrency(_summary.GrandTotal, _summary.Currency));
            });

    private void ComposeServiceGroup(IContainer container, MonthlySummaryServiceGroup group) =>
        container.Column(column =>
        {
            column.Item().Element(SectionTitle).Text(group.ServiceName);

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(88);  // Belge no
                    columns.ConstantColumn(56);  // İş tarihi
                    columns.RelativeColumn(2);   // Lokasyon
                    columns.RelativeColumn(2);   // Varyant
                    columns.ConstantColumn(46);  // Ham
                    columns.ConstantColumn(56);  // Faturalanan
                    columns.ConstantColumn(34);  // Birim
                    columns.ConstantColumn(58);  // Birim fiyat
                    columns.ConstantColumn(64);  // Tutar
                });

                // Sayfa taşarsa başlık satırı tekrar eder.
                table.Header(header =>
                {
                    HeaderCell(header.Cell(), "Belge No");
                    HeaderCell(header.Cell(), "İş Tarihi");
                    HeaderCell(header.Cell(), "Lokasyon");
                    HeaderCell(header.Cell(), "Varyant");
                    HeaderCell(header.Cell(), "Ham", right: true);
                    HeaderCell(header.Cell(), "Faturalanan", right: true);
                    HeaderCell(header.Cell(), "Birim");
                    HeaderCell(header.Cell(), "Birim Fiyat", right: true);
                    HeaderCell(header.Cell(), "Tutar", right: true);
                });

                foreach (var line in group.Lines)
                {
                    BodyCell(table.Cell(), line.DocumentNo);
                    BodyCell(table.Cell(), TrFormat.Date(line.WorkDate));
                    BodyCell(table.Cell(), line.Location ?? "—");
                    BodyCell(table.Cell(), line.VariantName ?? "—");
                    BodyCell(table.Cell(), TrFormat.Quantity(line.RawQuantity), right: true);
                    BodyCell(table.Cell(), TrFormat.Quantity(line.BillableQuantity), right: true);
                    BodyCell(table.Cell(), ServiceUnitDisplay.GetLabel(line.Unit));
                    BodyCell(table.Cell(), TrFormat.UnitPrice(line.UnitPrice), right: true);
                    BodyCell(table.Cell(), TrFormat.Money(line.LineAmount), right: true);
                }

                // Hizmet bazında ara toplam.
                table.Cell().ColumnSpan(5).Background(DocumentTheme.SubtotalFill).Padding(3)
                    .Text($"{group.ServiceName} ara toplamı").Bold().FontSize(DocumentTheme.SmallSize);
                table.Cell().Background(DocumentTheme.SubtotalFill).Padding(3).AlignRight()
                    .Text(TrFormat.Quantity(group.SubtotalBillableQuantity)).Bold().FontSize(DocumentTheme.SmallSize);
                table.Cell().Background(DocumentTheme.SubtotalFill).Padding(3)
                    .Text(ServiceUnitDisplay.GetLabel(group.Unit)).Bold().FontSize(DocumentTheme.SmallSize);
                table.Cell().Background(DocumentTheme.SubtotalFill).Padding(3).Text("");
                table.Cell().Background(DocumentTheme.SubtotalFill).Padding(3).AlignRight()
                    .Text(TrFormat.Money(group.SubtotalAmount)).Bold().FontSize(DocumentTheme.SmallSize);
            });
        });

    private void ComposeMobilizations(IContainer container) => container.Column(column =>
    {
        column.Item().Element(SectionTitle).Text("MOBİLİZASYON BEDELLERİ");
        column.Item().PaddingBottom(3)
            .Text("Sefer başına bir kez uygulanır; yukarıdaki satır tutarlarına DAHİL DEĞİLDİR.")
            .FontSize(DocumentTheme.SmallSize).FontColor(DocumentTheme.Muted);

        column.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(100);
                columns.ConstantColumn(70);
                columns.RelativeColumn();
                columns.ConstantColumn(80);
            });

            table.Header(header =>
            {
                HeaderCell(header.Cell(), "Belge No");
                HeaderCell(header.Cell(), "İş Tarihi");
                HeaderCell(header.Cell(), "");
                HeaderCell(header.Cell(), "Tutar", right: true);
            });

            foreach (var mobilization in _summary.Mobilizations)
            {
                BodyCell(table.Cell(), mobilization.DocumentNo);
                BodyCell(table.Cell(), TrFormat.Date(mobilization.WorkDate));
                BodyCell(table.Cell(), "");
                BodyCell(table.Cell(), TrFormat.Money(mobilization.Amount), right: true);
            }

            table.Cell().ColumnSpan(3).Background(DocumentTheme.SubtotalFill).Padding(3)
                .Text("Mobilizasyon ara toplamı").Bold().FontSize(DocumentTheme.SmallSize);
            table.Cell().Background(DocumentTheme.SubtotalFill).Padding(3).AlignRight()
                .Text(TrFormat.Money(_summary.MobilizationTotal)).Bold().FontSize(DocumentTheme.SmallSize);
        });
    });

    private void ComposeGrandTotal(IContainer container) => container.AlignRight().Width(280).Column(column =>
    {
        TotalRow(column, "Satır tutarları toplamı", _summary.LinesTotal, bold: false);
        TotalRow(column, "Mobilizasyon toplamı", _summary.MobilizationTotal, bold: false);
        column.Item().PaddingVertical(3).LineHorizontal(1).LineColor(DocumentTheme.Ink);

        if (_summary.HasMixedCurrency)
        {
            column.Item().Text("Kayıtlar farklı para birimlerinde; tek bir genel toplam üretilemez.")
                .FontSize(DocumentTheme.SmallSize).Bold().FontColor(Colors.Red.Darken2);
        }
        else
        {
            TotalRow(column, "GENEL TOPLAM", _summary.GrandTotal, bold: true);
        }
    });

    private void ComposeSignatures(IContainer container) => container.Row(row =>
    {
        SignatureBox(row.RelativeItem(), "Hizmeti Veren Firma — Kaşe / İmza");
        row.ConstantItem(14);
        SignatureBox(row.RelativeItem(), "MIP — Kaşe / İmza");
    });

    private static void SignatureBox(IContainer container, string caption) =>
        container.Border(1).BorderColor(DocumentTheme.Line).Padding(6).Column(column =>
        {
            column.Item().Text(caption).FontSize(DocumentTheme.SmallSize).Bold();
            column.Item().Text("Ad Soyad / Tarih").FontSize(DocumentTheme.SmallSize).FontColor(DocumentTheme.Muted);
            column.Item().Height(56);
        });

    private void ComposeFooter(IContainer container) => container.Column(column =>
    {
        column.Item().PaddingBottom(4).LineHorizontal(1).LineColor(DocumentTheme.Line);
        column.Item().Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text("Bu belge MipRental sisteminde üretilmiştir.")
                    .FontSize(DocumentTheme.SmallSize).FontColor(DocumentTheme.Muted);
                left.Item().Text($"Doğrulama kodu: {_verificationCode}").FontSize(DocumentTheme.SmallSize);
                left.Item().Text(_verificationUrl).FontSize(DocumentTheme.SmallSize).FontColor(DocumentTheme.Muted);
                left.Item().PaddingTop(2).Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(DocumentTheme.SmallSize).FontColor(DocumentTheme.Muted));
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });

            row.ConstantItem(70).AlignRight().Height(70).Image(_qrPng);
        });
    });

    // ---------------------------------------------------------------

    private static void Stat(IContainer container, string label, string value) => container.Column(column =>
    {
        column.Item().Text(label).FontSize(DocumentTheme.SmallSize).FontColor(DocumentTheme.Muted);
        column.Item().Text(value).FontSize(DocumentTheme.SectionSize).Bold();
    });

    private static void LabelValue(ColumnDescriptor column, string label, string value) =>
        column.Item().AlignRight().Text(text =>
        {
            text.Span($"{label}: ").FontColor(DocumentTheme.Muted);
            text.Span(value).Bold();
        });

    private static IContainer SectionTitle(IContainer container) =>
        container.PaddingBottom(3).BorderBottom(1).BorderColor(DocumentTheme.Line)
            .DefaultTextStyle(x => x.Bold().FontSize(DocumentTheme.SectionSize));

    private static void HeaderCell(IContainer container, string text, bool right = false)
    {
        var cell = container.Background(DocumentTheme.HeaderFill)
            .BorderBottom(1).BorderColor(DocumentTheme.Line).Padding(3);
        (right ? cell.AlignRight() : cell).Text(text).Bold().FontSize(DocumentTheme.SmallSize);
    }

    private static void BodyCell(IContainer container, string text, bool right = false)
    {
        var cell = container.BorderBottom(1).BorderColor(DocumentTheme.Line).Padding(3);
        (right ? cell.AlignRight() : cell).Text(text).FontSize(DocumentTheme.SmallSize);
    }

    private void TotalRow(ColumnDescriptor column, string label, decimal amount, bool bold) =>
        column.Item().PaddingVertical(1).Row(row =>
        {
            var labelText = row.RelativeItem().Text(label).FontSize(DocumentTheme.SmallSize);
            var valueText = row.ConstantItem(110).AlignRight()
                .Text(TrFormat.MoneyWithCurrency(amount, _summary.Currency)).FontSize(DocumentTheme.SmallSize);

            if (bold)
            {
                labelText.Bold();
                valueText.Bold();
            }
        });
}
