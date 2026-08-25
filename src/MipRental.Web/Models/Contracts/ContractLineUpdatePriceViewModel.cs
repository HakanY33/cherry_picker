using System.ComponentModel.DataAnnotations;

namespace MipRental.Web.Models.Contracts;

public class ContractLineUpdatePriceViewModel
{
    public int ContractLineId { get; set; }
    public int ContractId { get; set; }

    public string ServiceName { get; set; } = string.Empty;
    public string? VariantName { get; set; }
    public decimal CurrentUnitPrice { get; set; }
    public string Currency { get; set; } = "TRY";
    public DateOnly CurrentValidFrom { get; set; }

    [Required(ErrorMessage = "Yeni fiyat zorunludur.")]
    [Range(0.0001, double.MaxValue, ErrorMessage = "Yeni fiyat sıfırdan büyük olmalıdır.")]
    [Display(Name = "Yeni Fiyat")]
    public decimal NewUnitPrice { get; set; }

    [Required(ErrorMessage = "Yeni geçerlilik başlangıç tarihi zorunludur.")]
    [Display(Name = "Yeni Geçerlilik Başlangıcı")]
    public DateOnly NewValidFrom { get; set; }
}
