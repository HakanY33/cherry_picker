namespace MipRental.Domain.Entities;

public class RequestLine
{
    public int RequestLineId { get; set; }
    public int RequestId { get; set; }
    public int ServiceId { get; set; }
    public int? VariantId { get; set; }
    public decimal? EstimatedQuantity { get; set; }
    public int LineNo { get; set; } = 1;

    public Request Request { get; set; } = null!;
    public ServiceCategory ServiceCategory { get; set; } = null!;
    public ServiceVariant? ServiceVariant { get; set; }
}
