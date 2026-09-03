using MipRental.Domain.Security;

namespace MipRental.Web.Security;

/// <summary>
/// Web katmanının rol sabitleri. Değerlerin tek kaynağı
/// <see cref="RoleCodes"/>; burası yalnızca ileri sarar — talep durum makinesi
/// Domain'deki listeyi, policy'ler buradakini kullanır ve ikisi ayrışamaz.
/// </summary>
public static class RoleNames
{
    public const string Requester = RoleCodes.Requester;
    public const string EquipmentManager = RoleCodes.EquipmentManager;
    public const string BudgetManager = RoleCodes.BudgetManager;
    public const string EquipmentViewer = RoleCodes.EquipmentViewer;
    public const string Budget = RoleCodes.Budget;
    public const string Accounting = RoleCodes.Accounting;
    public const string FirmManager = RoleCodes.FirmManager;
    public const string FirmOperator = RoleCodes.FirmOperator;
    public const string FirmUser = RoleCodes.FirmUser;
    public const string Admin = RoleCodes.Admin;
}
