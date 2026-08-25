using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MipRental.Web.Models.Departments;

public class DepartmentFormViewModel
{
    public int DepartmentId { get; set; }

    [Required(ErrorMessage = "Departman kodu zorunludur.")]
    [StringLength(20, ErrorMessage = "Departman kodu en fazla 20 karakter olabilir.")]
    [Display(Name = "Departman Kodu")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Departman adı zorunludur.")]
    [StringLength(150, ErrorMessage = "Departman adı en fazla 150 karakter olabilir.")]
    [Display(Name = "Departman Adı")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Üst Departman")]
    public int? ParentDepartmentId { get; set; }

    [Display(Name = "Aktif")]
    public bool IsActive { get; set; } = true;

    public List<SelectListItem> ParentOptions { get; set; } = new();
}
