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

    // Para bilgisini gorebilir mi? Servis/sorgu katmani bu bayraga bakarak
    // para alanlarini HIC CEKMEZ (view'da gizlemek yeterli degildir).
    bool CanSeePricing { get; }
}
