using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MipRental.Web.Models.ServiceVariants;

public class ServiceVariantFormViewModel
{
    public int VariantId { get; set; }

    [Required(ErrorMessage = "Hizmet tanımı seçimi zorunludur.")]
    [Display(Name = "Hizmet Tanımı")]
    public int ServiceId { get; set; }

    [Required(ErrorMessage = "Varyant kodu zorunludur.")]
    [StringLength(40, ErrorMessage = "Varyant kodu en fazla 40 karakter olabilir.")]
    [Display(Name = "Varyant Kodu")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Varyant adı zorunludur.")]
    [StringLength(150, ErrorMessage = "Varyant adı en fazla 150 karakter olabilir.")]
    [Display(Name = "Varyant Adı")]
    public string Name { get; set; } = string.Empty;

    [StringLength(50, ErrorMessage = "Kapasite en fazla 50 karakter olabilir.")]
    [Display(Name = "Kapasite")]
    public string? Capacity { get; set; }

    [Display(Name = "Aktif")]
    public bool IsActive { get; set; } = true;

    public List<SelectListItem> ServiceOptions { get; set; } = new();

    // Edit ekranında salt okunur gösterim için; ServiceId formdan değiştirilemez.
    public string? ServiceName { get; set; }
}
