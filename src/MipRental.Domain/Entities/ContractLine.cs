using MipRental.Domain.Enums;

namespace MipRental.Domain.Entities;

public class ContractLine
{
    public int ContractLineId { get; set; }
    public int ContractId { get; set; }
    public int ServiceId { get; set; }
    public int? VariantId { get; set; }

    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = "TRY";

    public decimal? MinBillableQuantity { get; set; }
    public RoundingRule RoundingRule { get; set; } = RoundingRule.NONE;
    public decimal? DayThresholdHours { get; set; }
    public decimal? DailyPrice { get; set; }
    public decimal? MobilizationFee { get; set; }
    public decimal? MaxQuantityPerRecord { get; set; }

    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }

    public Contract Contract { get; set; } = null!;
    public ServiceCategory ServiceCategory { get; set; } = null!;
    public ServiceVariant? ServiceVariant { get; set; }
    public User? CreatedByUser { get; set; }
    public ICollection<ContractLineSurcharge> ContractLineSurcharges { get; set; } = new List<ContractLineSurcharge>();
    public ICollection<WorkRecordLine> WorkRecordLines { get; set; } = new List<WorkRecordLine>();
}
