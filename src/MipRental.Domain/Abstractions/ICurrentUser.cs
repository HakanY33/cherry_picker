namespace MipRental.Domain.Abstractions;

public interface ICurrentUser
{
    int UserId { get; }
    string FullName { get; }
    int? FirmId { get; }
    int? DepartmentId { get; }
    bool IsMipStaff { get; }
    bool IsFirmUser { get; }
    bool IsInRole(string role);
}
