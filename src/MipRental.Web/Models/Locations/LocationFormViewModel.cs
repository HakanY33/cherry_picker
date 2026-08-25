using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MipRental.Web.Models.Locations;

public class LocationFormViewModel
{
    public int LocationId { get; set; }

    [StringLength(30, ErrorMessage = "Lokasyon kodu en fazla 30 karakter olabilir.")]
    [Display(Name = "Lokasyon Kodu")]
    public string? Code { get; set; }

    [Required(ErrorMessage = "Lokasyon adı zorunludur.")]
    [StringLength(150, ErrorMessage = "Lokasyon adı en fazla 150 karakter olabilir.")]
    [Display(Name = "Lokasyon Adı")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Üst Lokasyon")]
    public int? ParentLocationId { get; set; }

    [Display(Name = "Aktif")]
    public bool IsActive { get; set; } = true;

    public List<SelectListItem> ParentOptions { get; set; } = new();
}
