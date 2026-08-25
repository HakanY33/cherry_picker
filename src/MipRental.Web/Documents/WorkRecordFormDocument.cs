using MipRental.Web.Common;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MipRental.Web.Documents;

/// <summary>
/// Tek bir çalışma kaydının PDF formu. Düzen, sahada kullanılan KÂĞIT FORMA yakın
/// tutuldu: üstte MIP başlığı + belge no, altında yan yana iki blok (solda işi
/// talep eden MIP tarafı, sağda hizmeti veren firma), sonra işin bilgileri,
/// hizmet satırları, fiyat açıklaması, onay geçmişi, en altta imza kutuları ve
/// sağ altta doğrulama karekodu.
///
/// İmza kutuları BİLEREK BOŞ: ıslak imza alışkanlığından vazgeçmek zorunda
/// kalmayan kullanıcı çıktıyı alıp imzalar (bkz. spec.md §7).
/// </summary>
public sealed class WorkRecordFormDocument : IDocument
{
    private readonly WorkRecordFormModel _model;
    private readonly byte[] _qrPng;

    public WorkRecordFormDocument(WorkRecordFormModel model)
    {
        _model = model;
        _qrPng = VerificationQrCode.CreatePng(model.VerificationUrl);
    }

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Çalışma Kaydı Formu {_model.DocumentNo}",
        Author = "MIP — Mersin Uluslararası Limanı",
        Subject = $"{_model.FirmTitle} — {TrFormat.PeriodName(_model.Year, _model.Month)}",
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

            // Başlık her sayfada tekrar eder (belge birden fazla sayfaya taşarsa
            // ikinci sayfadaki tablo hangi belgeye ait, belli olsun).
            page.Header().Element(ComposeHeader);
            page.Content().PaddingVertical(10).Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("MERSİN ULUSLARARASI LİMAN İŞLETMECİLİĞİ")
                        .FontSize(DocumentTheme.TitleSize).Bold();
                    left.Item().Text("HİZMET / KİRALAMA ÇALIŞMA KAYDI FORMU")
                        .FontSize(DocumentTheme.SectionSize).FontColor(DocumentTheme.Muted);
                });

                row.ConstantItem(170).Column(right =>
                {
                    right.Item().AlignRight().Text(text =>
                    {
                        text.Span("Belge No: ").FontColor(DocumentTheme.Muted);
                        text.Span(_model.DocumentNo).Bold();
                    });
                    right.Item().AlignRight().Text(text =>
                    {
                        text.Span("Dönem: ").FontColor(DocumentTheme.Muted);
                        text.Span(TrFormat.PeriodName(_model.Year, _model.Month));
                    });
                    right.Item().AlignRight().Text(text =>
                    {
                        text.Span("Durum: ").FontColor(DocumentTheme.Muted);
                        text.Span(WorkRecordStatusDisplay.GetLabel(_model.Status)).Bold();
                    });
                });
            });

            column.Item().PaddingTop(6).LineHorizontal(1).LineColor(DocumentTheme.Ink);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(10);

            // Sol: hizmeti TALEP EDEN MIP tarafı — Sağ: hizmeti VEREN firma.
            column.Item().Row(row =>
            {
                row.RelativeItem().Element(c => Panel(c, "HİZMETİ TALEP EDEN (MIP)", inner =>
                {
                    Field(inner, "Talep Eden", _model.RequestedByName);
                    Field(inner, "İşi Gözleyen", _model.WitnessedByName);
                    Field(inner, "Departman", _model.DepartmentName);
                }));

                row.ConstantItem(12);

                row.RelativeItem().Element(c => Panel(c, "HİZMETİ VEREN FİRMA", inner =>
                {
                    Field(inner, "Firma", _model.FirmTitle);
                    Field(inner, "Sözleşme No", _model.ContractNo);
                    Field(inner, "Operatör", _model.OperatorName);
                    Field(inner, "Araç / Ekipman", _model.EquipmentDescription);
                    Field(inner, "Kapasite", _model.Capacity);
                    Field(inner, "Plaka", _model.LicensePlate);
                    Field(inner, "Personel Sayısı", _model.PersonnelCount?.ToString(TrFormat.Culture));
                }));
            });

            column.Item().Element(ComposeWorkInfo);
            column.Item().Element(ComposeLines);
            column.Item().Element(ComposeTotals);

            if (_model.PricingExplanation.Count > 0)
            {
                column.Item().Element(ComposePricingExplanation);
            }

            column.Item().Element(ComposeApprovalHistory);
            column.Item().PaddingTop(6).Element(ComposeSignatures);
        });
    }

    private void ComposeWorkInfo(IContainer container) => Panel(container, "İŞ BİLGİLERİ", inner =>
    {
        inner.Item().Row(row =>
        {
            row.RelativeItem().Element(c => Field(c, "İş Tarihi", TrFormat.Date(_model.WorkDate)));
            row.RelativeItem().Element(c => Field(c, "Başlangıç", _model.StartTime is { } s ? TrFormat.Time(s) : null));
            row.RelativeItem().Element(c => Field(c, "Bitiş",
                _model.EndTime is { } e ? TrFormat.Time(e) + (_model.SpansMidnight ? " (ertesi gün)" : "") : null));
        });

        Field(inner, "Yer", _model.Location);
        Field(inner, "İş Tanımı", _model.WorkDescription);

        if (!string.IsNullOrWhiteSpace(_model.ExternalReceiptNo))
        {
            Field(inner, "Dış Fiş No", _model.ExternalReceiptNo
                + (_model.ExternalReceiptDate is { } d ? $"  ({TrFormat.Date(d)})" : ""));
        }
    });

    private void ComposeLines(IContainer container) => container.Column(column =>
    {
        column.Item().Element(SectionTitle).Text("HİZMET SATIRLARI");

        column.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(24);   // Sıra
                columns.RelativeColumn(3);    // Hizmet
                columns.RelativeColumn(2);    // Varyant
                columns.ConstantColumn(52);   // Ham
                columns.ConstantColumn(58);   // Faturalanan
                columns.ConstantColumn(38);   // Birim
                columns.ConstantColumn(62);   // Birim fiyat
                columns.ConstantColumn(52);   // Fark
                columns.ConstantColumn(68);   // Tutar
            });

            // Tabloda başlık satırı her sayfada tekrar eder.
            table.Header(header =>
            {
                HeaderCell(header.Cell(), "#");
                HeaderCell(header.Cell(), "Hizmet");
                HeaderCell(header.Cell(), "Varyant");
                HeaderCell(header.Cell(), "Ham", TextAlign.Right);
                HeaderCell(header.Cell(), "Faturalanan", TextAlign.Right);
                HeaderCell(header.Cell(), "Birim");
                HeaderCell(header.Cell(), "Birim Fiyat", TextAlign.Right);
                HeaderCell(header.Cell(), "Fark", TextAlign.Right);
                HeaderCell(header.Cell(), "Tutar", TextAlign.Right);
            });

            foreach (var line in _model.Lines)
            {
                BodyCell(table.Cell(), line.LineNo.ToString(TrFormat.Culture));
                BodyCell(table.Cell(), line.ServiceName);
                BodyCell(table.Cell(), line.VariantName ?? "—");
                BodyCell(table.Cell(), TrFormat.Quantity(line.RawQuantity), TextAlign.Right);
                BodyCell(table.Cell(), TrFormat.Quantity(line.BillableQuantity), TextAlign.Right);
                BodyCell(table.Cell(), ServiceUnitDisplay.GetLabel(line.Unit));
                BodyCell(table.Cell(), TrFormat.UnitPrice(line.UnitPrice), TextAlign.Right);
                BodyCell(table.Cell(), line.SurchargeAmount == 0m ? "—" : TrFormat.Money(line.SurchargeAmount), TextAlign.Right);
                BodyCell(table.Cell(), TrFormat.Money(line.LineAmount), TextAlign.Right);
            }
        });
    });

    private void ComposeTotals(IContainer container) => container.AlignRight().Width(260).Column(column =>
    {
        TotalRow(column, "Satır Tutarları Toplamı", _model.LinesTotal, _model.Currency, bold: false);

        // Mobilizasyon AYRI KALEM: satır tutarlarına dahil değildir, sefer başına
        // bir kez uygulanır (bkz. RecordTotalCalculator).
        if (_model.MobilizationFee > 0m)
        {
            TotalRow(column, "Mobilizasyon Bedeli (sefer başı)", _model.MobilizationFee, _model.Currency, bold: false);
        }

        column.Item().PaddingVertical(3).LineHorizontal(1).LineColor(DocumentTheme.Ink);
        TotalRow(column, "GENEL TOPLAM", _model.TotalAmount, _model.Currency, bold: true);
    });

    private void ComposePricingExplanation(IContainer container) => container.Column(column =>
    {
        column.Item().Element(SectionTitle).Text("FİYAT AÇIKLAMASI");
        column.Item().Background(DocumentTheme.SubtotalFill).Padding(6).Column(inner =>
        {
            foreach (var explanation in _model.PricingExplanation)
            {
                inner.Item().Text($"• {explanation}").FontSize(DocumentTheme.SmallSize);
            }
        });
    });

    private void ComposeApprovalHistory(IContainer container) => container.Column(column =>
    {
        column.Item().Element(SectionTitle).Text("ONAY GEÇMİŞİ");

        if (_model.ApprovalHistory.Count == 0)
        {
            column.Item().Text("Bu kayıt için henüz onay adımı açılmamış.")
                .FontSize(DocumentTheme.SmallSize).FontColor(DocumentTheme.Muted);
            return;
        }

        column.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(32);  // Adım
                columns.RelativeColumn(2);   // Adım adı
                columns.RelativeColumn(2);   // Kim
                columns.ConstantColumn(78);  // Karar
                columns.ConstantColumn(88);  // Ne zaman
                columns.RelativeColumn(3);   // Açıklama
            });

            table.Header(header =>
            {
                HeaderCell(header.Cell(), "#");
                HeaderCell(header.Cell(), "Adım");
                HeaderCell(header.Cell(), "Karar Veren");
                HeaderCell(header.Cell(), "Karar");
                HeaderCell(header.Cell(), "Tarih");
                HeaderCell(header.Cell(), "Açıklama");
            });

            foreach (var approval in _model.ApprovalHistory)
            {
                BodyCell(table.Cell(), approval.StepNo.ToString(TrFormat.Culture));
                BodyCell(table.Cell(), approval.StepName);
                BodyCell(table.Cell(), approval.DecidedByName ?? "—");
                BodyCell(table.Cell(), approval.Decision is { } decision
                    ? ApprovalDecisionDisplay.GetLabel(decision)
                    : "Bekliyor");
                BodyCell(table.Cell(), approval.DecidedAtUtc is { } at ? TrFormat.DateTimeLocal(at) : "—");
                BodyCell(table.Cell(), approval.Comment ?? "—");
            }
        });
    });

    private void ComposeSignatures(IContainer container) => container.Column(column =>
    {
        column.Item().Element(SectionTitle).Text("İMZALAR");
        column.Item().Row(row =>
        {
            SignatureBox(row.RelativeItem(), "Hizmeti Veren Firma Yetkilisi");
            row.ConstantItem(10);
            SignatureBox(row.RelativeItem(), "MIP İşi Talep Eden");
            row.ConstantItem(10);
            SignatureBox(row.RelativeItem(), "MIP Onaylayan");
        });
    });

    private static void SignatureBox(IContainer container, string caption) =>
        container.Border(1).BorderColor(DocumentTheme.Line).Padding(6).Column(column =>
        {
            column.Item().Text(caption).FontSize(DocumentTheme.SmallSize).Bold();
            column.Item().Text("Ad Soyad / Tarih / İmza")
                .FontSize(DocumentTheme.SmallSize).FontColor(DocumentTheme.Muted);
            // İmza için boş alan — kullanıcı çıktıyı alıp elle imzalar.
            column.Item().Height(42);
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
                left.Item().Text($"Doğrulama kodu: {_model.VerificationCode}")
                    .FontSize(DocumentTheme.SmallSize);
                left.Item().Text(_model.VerificationUrl)
                    .FontSize(DocumentTheme.SmallSize).FontColor(DocumentTheme.Muted);
                left.Item().PaddingTop(2).Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(DocumentTheme.SmallSize).FontColor(DocumentTheme.Muted));
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });

            // Sağ alt köşe: doğrulama karekodu.
            row.ConstantItem(70).AlignRight().Height(70).Image(_qrPng);
        });
    });

    // ---------------------------------------------------------------
    // Küçük düzen yardımcıları
    // ---------------------------------------------------------------

    private static void Panel(IContainer container, string title, Action<ColumnDescriptor> body) =>
        container.Border(1).BorderColor(DocumentTheme.Line).Column(column =>
        {
            column.Item().Background(DocumentTheme.HeaderFill).Padding(4)
                .Text(title).Bold().FontSize(DocumentTheme.SmallSize);
            column.Item().Padding(6).Column(body);
        });

    private static void Field(ColumnDescriptor column, string label, string? value) =>
        column.Item().Element(c => Field(c, label, value));

    private static void Field(IContainer container, string label, string? value) =>
        container.PaddingBottom(2).Row(row =>
        {
            row.ConstantItem(96).Text($"{label}:").FontColor(DocumentTheme.Muted).FontSize(DocumentTheme.SmallSize);
            row.RelativeItem().Text(string.IsNullOrWhiteSpace(value) ? "—" : value).FontSize(DocumentTheme.SmallSize);
        });

    private static IContainer SectionTitle(IContainer container) =>
        container.PaddingBottom(3).BorderBottom(1).BorderColor(DocumentTheme.Line)
            .DefaultTextStyle(x => x.Bold().FontSize(DocumentTheme.SectionSize));

    private static void HeaderCell(IContainer container, string text, TextAlign align = TextAlign.Left) =>
        Cell(container, text, align, bold: true, background: DocumentTheme.HeaderFill);

    private static void BodyCell(IContainer container, string text, TextAlign align = TextAlign.Left) =>
        Cell(container, text, align, bold: false, background: null);

    private static void Cell(IContainer container, string text, TextAlign align, bool bold, Color? background)
    {
        var cell = container.BorderBottom(1).BorderColor(DocumentTheme.Line).Padding(3);
        if (background is { } fill)
        {
            cell = cell.Background(fill);
        }

        cell = align == TextAlign.Right ? cell.AlignRight() : cell;
        var span = cell.Text(text).FontSize(DocumentTheme.SmallSize);
        if (bold)
        {
            span.Bold();
        }
    }

    private static void TotalRow(ColumnDescriptor column, string label, decimal amount, string currency, bool bold) =>
        column.Item().PaddingVertical(1).Row(row =>
        {
            var labelText = row.RelativeItem().Text(label).FontSize(DocumentTheme.SmallSize);
            var valueText = row.ConstantItem(96).AlignRight()
                .Text(TrFormat.MoneyWithCurrency(amount, currency)).FontSize(DocumentTheme.SmallSize);

            if (bold)
            {
                labelText.Bold();
                valueText.Bold();
            }
        });

    private enum TextAlign
    {
        Left,
        Right
    }
}
