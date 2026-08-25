using System.ComponentModel.DataAnnotations;

namespace MipRental.Web.Models.Firms;

public class FirmFormViewModel
{
    public int FirmId { get; set; }

    [Required(ErrorMessage = "Firma kodu zorunludur.")]
    [StringLength(20, ErrorMessage = "Firma kodu en fazla 20 karakter olabilir.")]
    [Display(Name = "Firma Kodu")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Unvan zorunludur.")]
    [StringLength(200, ErrorMessage = "Unvan en fazla 200 karakter olabilir.")]
    [Display(Name = "Unvan")]
    public string Title { get; set; } = string.Empty;

    [StringLength(20, ErrorMessage = "Vergi numarası en fazla 20 karakter olabilir.")]
    [Display(Name = "Vergi Numarası")]
    public string? TaxNumber { get; set; }

    [StringLength(100, ErrorMessage = "Vergi dairesi en fazla 100 karakter olabilir.")]
    [Display(Name = "Vergi Dairesi")]
    public string? TaxOffice { get; set; }

    [StringLength(34, ErrorMessage = "IBAN en fazla 34 karakter olabilir.")]
    [Display(Name = "IBAN")]
    public string? Iban { get; set; }

    [StringLength(30, ErrorMessage = "Telefon en fazla 30 karakter olabilir.")]
    [Display(Name = "Telefon")]
    public string? Phone { get; set; }

    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")]
    [StringLength(150, ErrorMessage = "E-posta en fazla 150 karakter olabilir.")]
    [Display(Name = "E-posta")]
    public string? Email { get; set; }

    [StringLength(500, ErrorMessage = "Adres en fazla 500 karakter olabilir.")]
    [Display(Name = "Adres")]
    public string? Address { get; set; }

    [Display(Name = "Aktif")]
    public bool IsActive { get; set; } = true;
}
