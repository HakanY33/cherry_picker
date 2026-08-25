using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using MipRental.Domain.Enums;

namespace MipRental.Web.Models.Contracts;

public class ContractFormViewModel
{
    public int ContractId { get; set; }

    [Required(ErrorMessage = "Firma seçimi zorunludur.")]
    [Display(Name = "Firma")]
    public int FirmId { get; set; }

    [Required(ErrorMessage = "Sözleşme numarası zorunludur.")]
    [StringLength(50, ErrorMessage = "Sözleşme numarası en fazla 50 karakter olabilir.")]
    [Display(Name = "Sözleşme No")]
    public string ContractNo { get; set; } = string.Empty;

    [StringLength(200, ErrorMessage = "Başlık en fazla 200 karakter olabilir.")]
    [Display(Name = "Başlık")]
    public string? Title { get; set; }

    [Required(ErrorMessage = "Başlangıç tarihi zorunludur.")]
    [Display(Name = "Başlangıç Tarihi")]
    public DateOnly StartDate { get; set; }

    [Required(ErrorMessage = "Bitiş tarihi zorunludur.")]
    [Display(Name = "Bitiş Tarihi")]
    public DateOnly EndDate { get; set; }

    [Required(ErrorMessage = "Para birimi zorunludur.")]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Para birimi 3 karakter olmalıdır (ör. TRY).")]
    [Display(Name = "Para Birimi")]
    public string Currency { get; set; } = "TRY";

    [StringLength(1000, ErrorMessage = "Notlar en fazla 1000 karakter olabilir.")]
    [Display(Name = "Notlar")]
    public string? Notes { get; set; }

    public ContractStatus Status { get; set; }

    public List<SelectListItem> FirmOptions { get; set; } = new();
}
