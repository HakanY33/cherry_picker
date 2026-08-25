using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using MipRental.Domain.Enums;

namespace MipRental.Web.Models.ServiceCategories;

public class ServiceCategoryFormViewModel
{
    public int ServiceId { get; set; }

    [Required(ErrorMessage = "Hizmet kodu zorunludur.")]
    [StringLength(40, ErrorMessage = "Hizmet kodu en fazla 40 karakter olabilir.")]
    [Display(Name = "Hizmet Kodu")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Hizmet adı zorunludur.")]
    [StringLength(150, ErrorMessage = "Hizmet adı en fazla 150 karakter olabilir.")]
    [Display(Name = "Hizmet Adı")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Birim seçimi zorunludur.")]
    [Display(Name = "Birim")]
    public ServiceUnit Unit { get; set; }

    [Display(Name = "Zaman Takibi Gerektirir")]
    public bool RequiresTimeTracking { get; set; }

    [Display(Name = "Araç Gerektirir")]
    public bool RequiresVehicle { get; set; }

    [Display(Name = "Aktif")]
    public bool IsActive { get; set; } = true;

    public List<SelectListItem> UnitOptions { get; set; } = new();
}
