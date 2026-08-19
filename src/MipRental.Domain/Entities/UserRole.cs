namespace MipRental.Domain.Entities;

public class UserRole
{
    public int UserId { get; set; }
    public int RoleId { get; set; }
    public int? DepartmentId { get; set; }

    public User User { get; set; } = null!;
    public Role Role { get; set; } = null!;
    public Department? Department { get; set; }
}
