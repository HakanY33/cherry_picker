namespace MipRental.Domain.Security;

/// <summary>
/// Rol KODLARININ tek kaynağı. `Roles.Code` sütununda ve `TransitionActor.Roles`
/// içinde birebir bu değerler durur.
///
/// Neden Domain'de: talep durum makinesi (<see cref="Approvals.RequestStateMachine"/>)
/// hangi adımı hangi rolün geçirebileceğini KENDİ bilir — çalışma kaydı onayının
/// aksine bu eşleme tabloya bağlı değildir (CLAUDE.md kural 6 yalnızca onay
/// zinciri içindir). Web katmanındaki `RoleNames` bu sabitleri ileri sarar;
/// iki ayrı liste tutulmaz.
/// </summary>
public static class RoleCodes
{
    public const string Requester = "REQUESTER";

    // Adım 10 yeniden adlandırması. RoleId DEĞİŞMEDİ (2 ve 3): UserRoles ve
    // ApprovalFlowSteps satırları RoleId ile bağlı olduğundan mevcut kullanıcı
    // yetkileri ve onay zinciri kendiliğinden korunur.
    public const string EquipmentManager = "EQUIPMENT_MANAGER";   // eski: SUPERVISOR
    public const string BudgetManager = "BUDGET_MANAGER";         // eski: DEPT_HEAD

    // Ekipman Müdürlüğü'nün salt okuyan kullanıcısı. Onaylamaz, fiyat görmez.
    public const string EquipmentViewer = "EQUIPMENT_VIEWER";

    public const string Budget = "BUDGET";
    public const string Accounting = "ACCOUNTING";

    // Firma tarafı ikiye ayrıldı: yetkili (talebi kabul eder, operatör/plaka
    // atar) ve operatör ("başladım"/"bitirdim").
    public const string FirmManager = "FIRM_MANAGER";
    public const string FirmOperator = "FIRM_OPERATOR";

    // Geçiş rolü: Adım 10 öncesi tüm firma kullanıcıları bu roldeydi. Yeni rol
    // dağıtımı tamamlanana kadar FIRM_MANAGER ile EŞDEĞER sayılır. Kaldırılmadı.
    public const string FirmUser = "FIRM_USER";

    public const string Admin = "ADMIN";
}
