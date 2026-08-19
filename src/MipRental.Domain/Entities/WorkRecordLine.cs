using MipRental.Domain.Enums;

namespace MipRental.Domain.Entities;

public class WorkRecordLine
{
    public int WorkRecordLineId { get; set; }
    public int WorkRecordId { get; set; }
    public int LineNo { get; set; } = 1;

    public int ServiceId { get; set; }
    public int? VariantId { get; set; }

    public decimal RawQuantity { get; set; }
    public decimal BillableQuantity { get; set; }
    public ServiceUnit Unit { get; set; }

    public int? ContractLineId { get; set; }
    public decimal UnitPriceSnapshot { get; set; }
    public string? PricingRuleSnapshot { get; set; }
    public decimal SurchargeAmount { get; set; }
    public decimal LineAmount { get; set; }
    public string Currency { get; set; } = "TRY";

    public string? Description { get; set; }
    public bool IsManualOverride { get; set; }
    public string? OverrideReason { get; set; }

    public WorkRecord WorkRecord { get; set; } = null!;
    public ServiceCategory ServiceCategory { get; set; } = null!;
    public ServiceVariant? ServiceVariant { get; set; }
    public ContractLine? ContractLine { get; set; }
}
