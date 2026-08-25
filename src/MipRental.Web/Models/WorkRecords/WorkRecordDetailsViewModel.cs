using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Web.Models.WorkRecords;

public class WorkRecordDetailsViewModel
{
    public required WorkRecord WorkRecord { get; init; }
    public IReadOnlyList<AuditLog> AuditEntries { get; init; } = Array.Empty<AuditLog>();

    // RequestedByUser/WitnessedByUser navigation'ları YOK — MIP personeli User
    // entity'sinin firma izolasyon filtresine takılır (bkz. controller). İsimler
    // burada ayrıca (filtresiz) çözülüp taşınır.
    public string? RequestedByName { get; init; }
    public string? WitnessedByName { get; init; }

    // Onay geçmişi: karar verilmiş ve bekleyen tüm adımlar, sırayla.
    public IReadOnlyList<Approval> ApprovalHistory { get; init; } = Array.Empty<Approval>();

    // Bu kullanıcı kaydın AÇIK onay adımının rolünde mi (Onayla/Reddet/Revizyon
    // butonları buna göre gösterilir). Butonu gizlemek yetmez — action'lar
    // yetkiyi ayrıca doğrular.
    public bool CanDecide { get; init; }

    // Revizyon zinciri.
    public WorkRecordVersionLink? PreviousVersion { get; init; }
    public WorkRecordVersionLink? NextVersion { get; init; }
    public int VersionNumber { get; init; } = 1;
    public string RootDocumentNo { get; init; } = string.Empty;

    public bool IsRevision => PreviousVersion is not null;
}

public sealed class WorkRecordVersionLink
{
    public int WorkRecordId { get; init; }
    public string DocumentNo { get; init; } = string.Empty;
    public WorkRecordStatus Status { get; init; }
}
