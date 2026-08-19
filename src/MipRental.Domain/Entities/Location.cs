namespace MipRental.Domain.Entities;

public class Location
{
    public int LocationId { get; set; }
    public string? Code { get; set; }
    public string Name { get; set; } = null!;
    public int? ParentLocationId { get; set; }
    public string? FullPath { get; set; }
    public bool IsActive { get; set; } = true;

    public Location? ParentLocation { get; set; }
    public ICollection<Location> ChildLocations { get; set; } = new List<Location>();
    public ICollection<Request> Requests { get; set; } = new List<Request>();
    public ICollection<WorkRecord> WorkRecords { get; set; } = new List<WorkRecord>();
}
