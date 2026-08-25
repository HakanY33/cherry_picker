namespace MipRental.Web.Models.WorkRecords;

// B2: canlı fiyat önizleme. Formdan (başlık + tek satır) htmx ile POST edilir.
public class LinePreviewRequest
{
    public DateOnly? WorkDate { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public bool SpansMidnight { get; set; }

    public int? ServiceId { get; set; }
    public int? VariantId { get; set; }
    public decimal? Quantity { get; set; }
}
