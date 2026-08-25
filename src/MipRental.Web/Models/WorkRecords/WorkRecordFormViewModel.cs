using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MipRental.Web.Models.WorkRecords;

// CLAUDE.md/Adım 6 B3: DRAFT aşamasında çoğu alan boş bırakılabilir; zorunlu alan
// kontrolü gönderim (Submit) anında yapılır, burada değil. Bu yüzden DataAnnotations
// sadece taslağı bile anlamsız kılacak asgari alanlarla sınırlı tutulmuştur.
public class WorkRecordFormViewModel
{
    public int WorkRecordId { get; set; }

    [Required(ErrorMessage = "Dönem seçilmelidir.")]
    public int PeriodId { get; set; }

    [Required(ErrorMessage = "İş tarihi girilmelidir.")]
    [Display(Name = "İş Tarihi")]
    public DateOnly WorkDate { get; set; }

    [Display(Name = "Lokasyon")]
    public int? LocationId { get; set; }

    [Display(Name = "Lokasyon (serbest metin)")]
    [MaxLength(300)]
    public string? LocationText { get; set; }

    [Display(Name = "İş Tanımı")]
    [MaxLength(1000)]
    public string? WorkDescription { get; set; }

    [Display(Name = "Talep Eden MIP Personeli")]
    public int? RequestedByUserId { get; set; }

    [Display(Name = "Saha Yetkilisi")]
    public int? WitnessedByUserId { get; set; }

    [Display(Name = "Operatör Adı")]
    [MaxLength(150)]
    public string? OperatorName { get; set; }

    [Display(Name = "Araç")]
    public int? EquipmentId { get; set; }

    [Display(Name = "Plaka")]
    [MaxLength(20)]
    public string? LicensePlate { get; set; }

    [Display(Name = "Personel Sayısı")]
    public int? PersonnelCount { get; set; }

    [Display(Name = "Dış Fiş No")]
    [MaxLength(40)]
    public string? ExternalReceiptNo { get; set; }

    [Display(Name = "Dış Fiş Tarihi")]
    public DateOnly? ExternalReceiptDate { get; set; }

    [Display(Name = "Başlangıç Saati")]
    public TimeOnly? StartTime { get; set; }

    [Display(Name = "Bitiş Saati")]
    public TimeOnly? EndTime { get; set; }

    [Display(Name = "Gece vardiyası (bitiş, ertesi güne taşıyor)")]
    public bool SpansMidnight { get; set; }

    public List<WorkRecordLineFormViewModel> Lines { get; set; } = new();

    public List<SelectListItem> PeriodOptions { get; set; } = new();
    public List<SelectListItem> LocationOptions { get; set; } = new();
    public List<SelectListItem> RequestedByOptions { get; set; } = new();
    public List<SelectListItem> WitnessedByOptions { get; set; } = new();
    public List<SelectListItem> EquipmentOptions { get; set; } = new();
}
