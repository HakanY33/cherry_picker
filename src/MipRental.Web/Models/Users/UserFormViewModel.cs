using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MipRental.Web.Models.Users;

public class RoleCheckboxItem
{
    public int RoleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Selected { get; set; }
}

public class UserFormViewModel
{
    public int UserId { get; set; }

    [Required(ErrorMessage = "Kullanıcı adı zorunludur.")]
    [StringLength(100, ErrorMessage = "Kullanıcı adı en fazla 100 karakter olabilir.")]
    [Display(Name = "Kullanıcı Adı")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ad soyad zorunludur.")]
    [StringLength(150, ErrorMessage = "Ad soyad en fazla 150 karakter olabilir.")]
    [Display(Name = "Ad Soyad")]
    public string FullName { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")]
    [StringLength(150, ErrorMessage = "E-posta en fazla 150 karakter olabilir.")]
    [Display(Name = "E-posta")]
    public string? Email { get; set; }

    [StringLength(30, ErrorMessage = "Telefon en fazla 30 karakter olabilir.")]
    [Display(Name = "Telefon")]
    public string? Phone { get; set; }

    [StringLength(100, ErrorMessage = "Unvan en fazla 100 karakter olabilir.")]
    [Display(Name = "Unvan")]
    public string? Position { get; set; }

    [Display(Name = "Firma")]
    public int? FirmId { get; set; }

    [Display(Name = "Departman")]
    public int? DepartmentId { get; set; }

    [Display(Name = "Firma Admini")]
    public bool IsFirmAdmin { get; set; }

    [Display(Name = "Aktif")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "Roller")]
    public List<int> SelectedRoleIds { get; set; } = new();

    public List<SelectListItem> FirmOptions { get; set; } = new();
    public List<SelectListItem> DepartmentOptions { get; set; } = new();
    public List<RoleCheckboxItem> RoleOptions { get; set; } = new();

    // MIP admini firmasını seçebilir (dropdown); firma admini kendi firmasına sabittir.
    public bool CanChooseFirm { get; set; }
    public bool IsEdit { get; set; }
}

public class UserCreatedViewModel
{
    public string UserName { get; set; } = string.Empty;
    public string GeneratedPassword { get; set; } = string.Empty;
}
