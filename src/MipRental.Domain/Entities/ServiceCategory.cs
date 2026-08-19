using MipRental.Domain.Enums;

namespace MipRental.Domain.Entities;

public class ServiceCategory
{
    public int ServiceId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public ServiceUnit Unit { get; set; }
    public bool RequiresTimeTracking { get; set; }
    public bool RequiresVehicle { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<ServiceVariant> ServiceVariants { get; set; } = new List<ServiceVariant>();
    public ICollection<ContractLine> ContractLines { get; set; } = new List<ContractLine>();
    public ICollection<RequestLine> RequestLines { get; set; } = new List<RequestLine>();
    public ICollection<WorkRecordLine> WorkRecordLines { get; set; } = new List<WorkRecordLine>();
    public ICollection<ApprovalFlow> ApprovalFlows { get; set; } = new List<ApprovalFlow>();
}
