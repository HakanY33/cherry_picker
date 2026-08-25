using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MipRental.Web.Models.Equipment;

public class EquipmentFormViewModel
{
    public int EquipmentId { get; set; }

    [Required(ErrorMessage = "Firma seçimi zorunludur.")]
    [Display(Name = "Firma")]
    public int FirmId { get; set; }

    [Display(Name = "Hizmet Varyantı")]
    public int? VariantId { get; set; }

    [StringLength(20, ErrorMessage = "Plaka en fazla 20 karakter olabilir.")]
    [Display(Name = "Plaka")]
    public string? LicensePlate { get; set; }

    [StringLength(200, ErrorMessage = "Açıklama en fazla 200 karakter olabilir.")]
    [Display(Name = "Açıklama")]
    public string? Description { get; set; }

    [Display(Name = "Aktif")]
    public bool IsActive { get; set; } = true;

    public List<SelectListItem> FirmOptions { get; set; } = new();
    public List<SelectListItem> VariantOptions { get; set; } = new();
}
