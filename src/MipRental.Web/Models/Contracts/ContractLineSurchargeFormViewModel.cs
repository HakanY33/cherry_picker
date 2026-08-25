using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using MipRental.Domain.Enums;

namespace MipRental.Web.Models.Contracts;

public class ContractLineSurchargeFormViewModel
{
    public int SurchargeId { get; set; }

    [Required]
    public int ContractLineId { get; set; }

    // Vazgeç bağlantısı sözleşme detayına dönebilsin diye taşınır; formda düzenlenmez.
    public int ContractId { get; set; }

    [Required(ErrorMessage = "Ek ücret tipi zorunludur.")]
    [Display(Name = "Tip")]
    public SurchargeType SurchargeType { get; set; }

    [Display(Name = "Çarpan")]
    public decimal? Multiplier { get; set; }

    [Display(Name = "Sabit Tutar")]
    public decimal? FixedAmount { get; set; }

    [Display(Name = "Başlangıç Saati")]
    public TimeOnly? AppliesFromHour { get; set; }

    [Display(Name = "Bitiş Saati")]
    public TimeOnly? AppliesToHour { get; set; }

    [Display(Name = "Aktif")]
    public bool IsActive { get; set; } = true;

    public List<SelectListItem> SurchargeTypeOptions { get; set; } = new();

    // Salt okunur gösterim: bu ek ücretin hangi fiyat satırına ait olduğu.
    public string? ContractLineDescription { get; set; }
}
