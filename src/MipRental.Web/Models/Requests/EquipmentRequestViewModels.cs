using Microsoft.AspNetCore.Mvc.Rendering;
using MipRental.Domain.Enums;

namespace MipRental.Web.Models.Requests;

/// <summary>
/// ADIM 11 — EKİPMAN MÜDÜRLÜĞÜ EKRANLARI.
///
/// Burada da para alanı YOKTUR: Ekipman Müdürlüğü'nün İKİ rolü de (yönetici ve
/// salt okuyan kullanıcı) tutar görmez. Adım 9'un dersi — onaylama yetkisi
/// sessizce fiyat görme yetkisine dönüşmemeli. → [[Fiyat Gizliliği]]
/// </summary>
public class EquipmentRequestsViewModel
{
    public IReadOnlyList<EquipmentRequestRow> Items { get; init; } = Array.Empty<EquipmentRequestRow>();

    /// <summary>
    /// Karar butonları yalnızca EQUIPMENT_MANAGER'a çizilir. EQUIPMENT_VIEWER
    /// için ayrı ekran YOK; aynı ekran, butonsuz. Sunucu tarafında da
    /// CanDecideEquipmentRequest policy'si POST'u ayrıca engeller.
    /// </summary>
    public bool CanDecide { get; init; }

    public int? DepartmentId { get; init; }
    public int? LocationId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    public List<SelectListItem> DepartmentOptions { get; init; } = new();
    public List<SelectListItem> LocationOptions { get; init; } = new();
}

public sealed class EquipmentRequestRow
{
    public required int RequestId { get; init; }
    public required string DocumentNo { get; init; }
    public required DateOnly RequestedDate { get; init; }
    public TimeOnly? RequestedStartTime { get; init; }
    public string? DepartmentName { get; init; }
    public string? LocationDisplay { get; init; }
    public string? ServiceDisplay { get; init; }
    public required string RequesterName { get; init; }
    public string? WorkDescription { get; init; }

    /// <summary>
    /// Talep edilen tarih geçmişte kaldı. Onay gecikmiş demektir; listede
    /// görsel uyarı ile işaretlenir.
    /// </summary>
    public required bool IsPastDue { get; init; }
}

/// <summary>
/// Talep detayı ve karar ekranı. Ekipman Müdürlüğü talep edenin bilgilerini
/// GÖRÜR (kime hizmet verildiğini bilmesi gerekir).
///
/// Düzenlenebilen alanlar SADECE tarih/saat ve varyanttır; lokasyon, iş tanımı
/// ve talep eden bilgileri bu modelde salt gösterim içindir ve
/// <see cref="EquipmentApprovalModel"/> içinde karşılıkları YOKTUR — POST
/// gövdesine elle eklenseler bile bağlanacak bir alan bulunmaz.
/// </summary>
public class EquipmentRequestDetailsViewModel
{
    public required int RequestId { get; init; }
    public required string DocumentNo { get; init; }
    public required RequestStatus Status { get; init; }

    public required string RequesterName { get; init; }
    public string? RequesterPosition { get; init; }
    public string? DepartmentName { get; init; }

    public required DateOnly IssueDate { get; init; }
    public required DateOnly RequestedDate { get; init; }
    public TimeOnly? RequestedStartTime { get; init; }
    public TimeOnly? RequestedEndTime { get; init; }

    // Düzenlenemez: talep edenin ihtiyacını tanımlayan alanlar.
    public string? LocationDisplay { get; init; }
    public string? WorkDescription { get; init; }

    public int? ServiceId { get; init; }
    public string? ServiceName { get; init; }
    public int? VariantId { get; init; }
    public string? VariantName { get; init; }

    public bool CanDecide { get; init; }

    /// <summary>İstenen kapasite yoksa başka kapasite atanabilir.</summary>
    public List<SelectListItem> VariantOptions { get; init; } = new();

    /// <summary>
    /// Yönlendirilebilecek firmalar: talep edilen tarihte O HİZMET için AKTİF
    /// sözleşmesi olanlarla sınırlı. Sözleşmesi olmayan firmaya iş yönlendirmek,
    /// fiyatı olmayan bir çalışma kaydı doğururdu.
    /// </summary>
    public List<SelectListItem> FirmOptions { get; init; } = new();
}

/// <summary>
/// Onay POST gövdesi. Ekipman Müdürlüğü'nün değiştirebileceği alanların TAM
/// listesi budur — lokasyon, iş tanımı ve talep eden bilgileri BİLİNÇLİ OLARAK
/// yoktur; model binder onları bağlayamaz.
/// </summary>
public sealed class EquipmentApprovalModel
{
    public int RequestId { get; set; }
    public DateOnly? RequestedDate { get; set; }
    public TimeOnly? RequestedStartTime { get; set; }
    public int? VariantId { get; set; }

    /// <summary>Talebin yönlendirileceği alt yüklenici. Onay için zorunlu.</summary>
    public int? FirmId { get; set; }
}
