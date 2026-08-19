namespace MipRental.Domain.Entities;

public class Equipment
{
    public int EquipmentId { get; set; }
    public int FirmId { get; set; }
    public int? VariantId { get; set; }
    public string? LicensePlate { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public Firm Firm { get; set; } = null!;
    public ServiceVariant? ServiceVariant { get; set; }
    public ICollection<WorkRecord> WorkRecords { get; set; } = new List<WorkRecord>();
}
