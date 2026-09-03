using MipRental.Domain.Enums;

namespace MipRental.Web.Models.Requests;

/// <summary>
/// ADIM 11 — FİRMA YETKİLİSİ EKRANLARI.
///
/// İKİ AYRI GİZLİLİK KURALI birden geçerlidir ve ikisi de MODEL SEVİYESİNDE
/// uygulanır (view'da gizlemek yetersizdir):
///
/// 1. Fiyat gizliliği — firma tutar görmez, para alanı hiç yok.
///    → [[Fiyat Gizliliği]]
/// 2. Talep edenin kimliği — firma yetkilisi MIP personelinin adını, görevini
///    ve departmanını GÖRMEZ (→ [[Aktörler ve Roller]]). Bu yüzden bu dosyadaki
///    modellerde RequesterName / Position / DepartmentName alanları
///    BULUNMAZ; null olarak da bulunmaz, hiç yoktur.
///
/// Firma gördüğü kadarıyla işi yapabilir: ne zaman, nerede, ne iş, hangi
/// kapasite. Kimin talep ettiği firmayı ilgilendirmez.
/// </summary>
public class FirmRequestsViewModel
{
    public IReadOnlyList<FirmRequestRow> Items { get; init; } = Array.Empty<FirmRequestRow>();
}

public sealed class FirmRequestRow
{
    public required int RequestId { get; init; }
    public required string DocumentNo { get; init; }
    public required RequestStatus Status { get; init; }
    public required DateOnly RequestedDate { get; init; }
    public TimeOnly? RequestedStartTime { get; init; }
    public TimeOnly? RequestedEndTime { get; init; }
    public string? LocationDisplay { get; init; }
    public string? WorkDescription { get; init; }
    public string? ServiceDisplay { get; init; }

    /// <summary>Planlanan işler listesinde dolu; bekleyenlerde henüz boş.</summary>
    public string? AssignedOperatorName { get; init; }
    public string? AssignedLicensePlate { get; init; }
}

/// <summary>
/// Kabul ekranı. Firma yetkilisi işi görür, operatör ve plaka girer.
/// Talep edenin kimlik alanları burada da YOKTUR.
/// </summary>
public class FirmRequestAcceptViewModel
{
    public required int RequestId { get; init; }
    public required string DocumentNo { get; init; }
    public required RequestStatus Status { get; init; }

    public required DateOnly RequestedDate { get; init; }
    public TimeOnly? RequestedStartTime { get; init; }
    public TimeOnly? RequestedEndTime { get; init; }
    public string? LocationDisplay { get; init; }
    public string? WorkDescription { get; init; }
    public string? ServiceDisplay { get; init; }

    /// <summary>Kabul için ZORUNLU; RequestStateMachine boş kabul etmez.</summary>
    public string? AssignedOperatorName { get; init; }
    public string? AssignedLicensePlate { get; init; }
}
