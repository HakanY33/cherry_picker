namespace MipRental.Domain.Entities;

public class Department
{
    public int DepartmentId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public int? ParentDepartmentId { get; set; }
    public bool IsActive { get; set; } = true;

    public Department? ParentDepartment { get; set; }
    public ICollection<Department> ChildDepartments { get; set; } = new List<Department>();
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<Request> Requests { get; set; } = new List<Request>();
    public ICollection<WorkRecord> WorkRecords { get; set; } = new List<WorkRecord>();
}
