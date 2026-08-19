namespace MipRental.Domain.Entities;

public class ServiceVariant
{
    public int VariantId { get; set; }
    public int ServiceId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Capacity { get; set; }
    public bool IsActive { get; set; } = true;

    public ServiceCategory ServiceCategory { get; set; } = null!;
    public ICollection<Equipment> Equipment { get; set; } = new List<Equipment>();
    public ICollection<ContractLine> ContractLines { get; set; } = new List<ContractLine>();
    public ICollection<RequestLine> RequestLines { get; set; } = new List<RequestLine>();
    public ICollection<WorkRecordLine> WorkRecordLines { get; set; } = new List<WorkRecordLine>();
}
