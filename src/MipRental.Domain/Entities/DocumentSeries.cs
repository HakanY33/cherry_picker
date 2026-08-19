using MipRental.Domain.Enums;

namespace MipRental.Domain.Entities;

public class DocumentSeries
{
    public int SeriesId { get; set; }
    public DocumentType DocumentType { get; set; }
    public string Prefix { get; set; } = null!;
    public int Year { get; set; }
    public int LastNumber { get; set; }
    public int Padding { get; set; } = 5;
}
