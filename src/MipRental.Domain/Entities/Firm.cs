namespace MipRental.Domain.Entities;

public class Firm
{
    public int FirmId { get; set; }
    public string Code { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? TaxNumber { get; set; }
    public string? TaxOffice { get; set; }
    public string? Iban { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Equipment> Equipment { get; set; } = new List<Equipment>();
    public ICollection<Contract> Contracts { get; set; } = new List<Contract>();
    public ICollection<Request> Requests { get; set; } = new List<Request>();
    public ICollection<WorkRecord> WorkRecords { get; set; } = new List<WorkRecord>();
}
