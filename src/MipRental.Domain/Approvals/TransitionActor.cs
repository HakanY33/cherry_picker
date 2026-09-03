using MipRental.Domain.Abstractions;

namespace MipRental.Domain.Approvals;

// Durum geçişini YAPAN kişinin, geçiş kararı için gereken asgari bilgisi.
// WorkRecordStateMachine'i saf tutmak için ICurrentUser/HttpContext yerine bu
// veri taşıyıcısı kullanılır: makine veritabanına da oturuma da dokunmaz.
public sealed class TransitionActor
{
    public required int UserId { get; init; }

    // null = MIP personeli. Dolu = o firmanın alt yüklenici kullanıcısı.
    public required int? FirmId { get; init; }

    // Rol KODLARI (EQUIPMENT_MANAGER, BUDGET_MANAGER, ...). Rol adı değil kod tutulur;
    // ApprovalFlowSteps.RoleId -> Role.Code ile karşılaştırılır.
    public IReadOnlySet<string> Roles { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    public bool IsMipStaff => FirmId is null;

    public bool IsInRole(string roleCode) => Roles.Contains(roleCode);

    public static TransitionActor From(ICurrentUser currentUser, IEnumerable<string> roleCodes) => new()
    {
        UserId = currentUser.UserId,
        FirmId = currentUser.FirmId,
        Roles = new HashSet<string>(roleCodes, StringComparer.Ordinal)
    };
}
