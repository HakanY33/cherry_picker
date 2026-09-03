using MipRental.Domain.Abstractions;
using MipRental.Web.Security;

namespace MipRental.Tests;

internal sealed class FakeCurrentUser : ICurrentUser
{
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public int? FirmId { get; set; }
    public int? DepartmentId { get; set; }
    public HashSet<string> Roles { get; set; } = new();
    public bool IsMipStaff => FirmId is null;
    public bool IsFirmUser => FirmId is not null;
    public bool IsInRole(string role) => Roles.Contains(role);

    public bool CanSeePricing =>
        IsInRole(RoleNames.Budget) || IsInRole(RoleNames.BudgetManager) || IsInRole(RoleNames.Admin) ||
        IsInRole(RoleNames.Accounting);
}
