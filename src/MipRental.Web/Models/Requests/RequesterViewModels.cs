using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using MipRental.Domain.Enums;

namespace MipRental.Web.Models.Requests;

/// <summary>
/// ADIM 11 — TALEP AÇANIN EKRANLARI.
///
/// Bu dosyadaki HİÇBİR modelde para alanı YOKTUR ve olmamalıdır. Adım 9'daki
/// desenin (nullable Pricing nesnesi) burada karşılığı bile yok: talep açan
/// hiçbir koşulda tutar görmediği için "yetkisi varsa dolsun" diyecek bir alan
/// da bulunmuyor. Gizlenen alan değil, var olmayan alan.
/// → [[Fiyat Gizliliği]]
/// </summary>
public class RequestFormViewModel
{
    public int RequestId { get; set; }

    // --- Oturumdan gelir, kullanıcı DEĞİŞTİREMEZ (salt okunur gösterilir) ---
    //
    // POST'ta geri gönderilseler bile controller bunları OKUMAZ: talep eden ve
    // departman her zaman oturumdan alınır. Modelde durmalarının tek sebebi
    // formu yeniden çizerken ekranda görünmeleri.
    public string RequesterName { get; set; } = string.Empty;
    public string? RequesterPosition { get; set; }
    public string? DepartmentName { get; set; }
    public DateOnly IssueDate { get; set; }

    // --- Talep açan girer ---

    [Display(Name = "Talep Edilen Tarih")]
    [Required(ErrorMessage = "Talep edilen tarih zorunludur.")]
    public DateOnly? RequestedDate { get; set; }

    [Display(Name = "Başlangıç Saati")]
    public TimeOnly? RequestedStartTime { get; set; }

    /// <summary>
    /// Tahmini süre (saat). Opsiyonel; girilirse başlangıç saatiyle birlikte
    /// bitiş saatine çevrilir. Gerçekleşen süre operatörün damgasından gelir,
    /// bu alan yalnızca planlama içindir.
    /// </summary>
    [Display(Name = "Tahmini Süre (saat)")]
    [Range(0.25, 24, ErrorMessage = "Tahmini süre 0,25 ile 24 saat arasında olmalıdır.")]
    public decimal? EstimatedHours { get; set; }

    [Display(Name = "Lokasyon")]
    public int? LocationId { get; set; }

    [Display(Name = "İş Tanımı")]
    [StringLength(1000, ErrorMessage = "İş tanımı en fazla 1000 karakter olabilir.")]
    public string? WorkDescription { get; set; }

    [Display(Name = "İstenen Hizmet")]
    public int? ServiceId { get; set; }

    [Display(Name = "Araç Kapasitesi / Varyant")]
    public int? VariantId { get; set; }

    public List<SelectListItem> LocationOptions { get; set; } = new();
    public List<SelectListItem> ServiceOptions { get; set; } = new();
    public List<SelectListItem> VariantOptions { get; set; } = new();
}

/// <summary>"Taleplerim" listesi. Yalnızca oturumdaki kullanıcının açtığı talepler.</summary>
public class MyRequestsViewModel
{
    public IReadOnlyList<MyRequestRow> Items { get; init; } = Array.Empty<MyRequestRow>();
    public int CurrentPage { get; init; } = 1;
    public int TotalPages { get; init; }

    // Filtre: sadeleştirilmiş durum etiketi + tarih aralığı.
    public string? Status { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public sealed class MyRequestRow
{
    public required int RequestId { get; init; }
    public required string DocumentNo { get; init; }

    /// <summary>
    /// GERÇEK durum. Ekranda doğrudan basılmaz — sadeleştirilmiş etikete
    /// çevrilir; buton görünürlüğü (İptal Et) için ham değere ihtiyaç var.
    /// </summary>
    public required RequestStatus Status { get; init; }

    public required DateOnly RequestedDate { get; init; }
    public TimeOnly? RequestedStartTime { get; init; }
    public string? LocationDisplay { get; init; }
    public string? ServiceDisplay { get; init; }
    public string? WorkDescription { get; init; }
}

/// <summary>
/// Talep detayı — talebi AÇAN kişi için. Tüm alanlar + durum geçmişi.
/// Red gerekçesi ayrı alan: ekranda belirgin gösterilmesi gerekiyor.
/// </summary>
public class RequestDetailsViewModel
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
    public string? LocationDisplay { get; init; }
    public string? WorkDescription { get; init; }
    public string? ServiceDisplay { get; init; }

    /// <summary>Firma yetkilisinin atadıkları; SCHEDULED'dan itibaren dolu.</summary>
    public string? FirmTitle { get; init; }
    public string? AssignedOperatorName { get; init; }
    public string? AssignedLicensePlate { get; init; }

    public string? RejectionReason { get; init; }
    public string? CancellationReason { get; init; }

    /// <summary>Durum geçmişi ayrı bir sorgudan gelir; controller doldurur.</summary>
    public IReadOnlyList<RequestStatusHistoryRow> History { get; set; } = Array.Empty<RequestStatusHistoryRow>();

    /// <summary>DRAFT ve SCHEDULED'da iptal edilebilir (RequestStateMachine'in izin verdiği geçişler).</summary>
    public bool CanCancel => Status is RequestStatus.DRAFT or RequestStatus.SCHEDULED;

    public bool CanSubmit => Status == RequestStatus.DRAFT;
}

/// <summary>
/// Durum geçmişinin bir satırı. Kaynak AuditLogs'tur: durum değişikliği zaten
/// alan bazlı denetim izine düşüyor (kim, ne zaman, eski/yeni değer), ayrıca
/// bir "RequestStatusHistory" tablosu açmak aynı bilgiyi ikinci kez saklardı.
/// </summary>
public sealed class RequestStatusHistoryRow
{
    public required DateTime OccurredAt { get; init; }
    public RequestStatus? From { get; init; }
    public required RequestStatus To { get; init; }
    public string? ByName { get; init; }
    public string? Reason { get; init; }
}
