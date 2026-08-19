using MipRental.Domain.Enums;

namespace MipRental.Domain.Entities;

public class ContractLineSurcharge
{
    public int SurchargeId { get; set; }
    public int ContractLineId { get; set; }
    public SurchargeType SurchargeType { get; set; }
    public decimal? Multiplier { get; set; }
    public decimal? FixedAmount { get; set; }
    public TimeOnly? AppliesFromHour { get; set; }
    public TimeOnly? AppliesToHour { get; set; }
    public bool IsActive { get; set; } = true;

    public ContractLine ContractLine { get; set; } = null!;
}
